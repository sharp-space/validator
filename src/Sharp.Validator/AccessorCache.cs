using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using AccessorKey = (string Path, System.Linq.Expressions.ExpressionType Body, System.Type? Operand, System.Type? Root);

namespace Sharp.Validator;

internal static class PropertyAccessorCache<TModel, TProperty>
{
    private static readonly ConcurrentDictionary<AccessorKey, Func<TModel, TProperty>> Cache = new();

    // Source-generated accessors for this closed generic, path-keyed. Null until a [ModuleInitializer] seeds one
    // (assemblies built without the generator never allocate it); consulted before Expression.Compile() so that under
    // Native AOT a directly-compiled delegate replaces the interpreted one. See AccessorRegistry.
    private static ConcurrentDictionary<string, Func<TModel, TProperty>>? _generated;

    internal static void Seed(string path, Func<TModel, TProperty> accessor)
    {
        if (_generated is null)
            Interlocked.CompareExchange(ref _generated, new ConcurrentDictionary<string, Func<TModel, TProperty>>(), null);

        _generated.TryAdd(path, accessor);
    }

    internal static bool HasGenerated(string path) => _generated is { } generated && generated.ContainsKey(path);

    internal static Func<TModel, TProperty> GetOrAdd(PropertyValidatorBuilder<TModel> builder)
    {
        // _generated is path-keyed, but the dotted path is only a faithful key for a pure member chain — a cast (e.g.
        // ((I)x.Item).Id) truncates to the same path as a sibling x.Id rule (see AccessorCastCollisionTests).
        // So use it only for path-faithful selectors that a registration can serve (see GeneratedAccessors); everything
        // else falls through to the compiled accessors below.
        if (GeneratedAccessors.TryGet(
                _generated, builder.PropertyPath, builder.IsPathFaithful,
                builder.PropertyExpression, out var precompiled))
            return precompiled;

        // Non-faithful selectors must not enter the static cache either: a captured this or closure is not part of
        // the key, so two validator instances with different captures would share one key and silently reuse the
        // first compiled accessor. Compile per rule instead — the evaluator memoizes the delegate, so this costs one
        // compile per registration.
        return !builder.IsPathFaithful
            ? Unsafe.As<Func<TModel, TProperty>>(builder.PropertyExpression.Compile())
            : GetOrAddFaithful(builder.PropertyPath, builder.PropertyExpression);
    }

    internal static Func<TModel, TProperty> GetOrAdd(string path, bool faithful, LambdaExpression expression)
    {
        if (GeneratedAccessors.TryGet(_generated, path, faithful, expression, out var precompiled))
            return precompiled;

        return !faithful ? Unsafe.As<Func<TModel, TProperty>>(expression.Compile()) : GetOrAddFaithful(path, expression);
    }

    private static Func<TModel, TProperty> GetOrAddFaithful(string path, LambdaExpression expression) =>
        Cache.GetOrAdd(FaithfulKey.Of(path, expression),
            static (_, expression) => Unsafe.As<Func<TModel, TProperty>>(expression.Compile()), expression);
}

// The generator registers its accessors by path alone and refuses a cast, so a selector whose body converts its value
// matches a registration only through a conversion the compiler inserted, such as a boxing one. A Nullable<T>.Value
// hop, which the path leaves out, makes the end of such a chain uncertain: (int) x.Age and x.Age.Value share a path and
// a type, yet the first throws on a null age where the second skips its chain, and x.Age.Value and x.Age, boxed to
// object, differ the same way. So no selector that converts a Nullable<T> or its Value has a generated accessor: the
// generator doesn't register one, and a registration made before it stopped, or found for a cast, isn't used. Such a
// selector reads through its own compiled accessor, as it does in a build without the generator.
file static class GeneratedAccessors
{
    internal static bool TryGet<TAccessor>(
        ConcurrentDictionary<string, TAccessor>? generated,
        string path,
        bool faithful,
        LambdaExpression expression,
        [MaybeNullWhen(false)] out TAccessor accessor
    )
    {
        accessor = default;
        return generated is not null && faithful && CanServe(expression) && generated.TryGetValue(path, out accessor);
    }

    // A faithful selector's one unary is a Convert, a ConvertChecked or an ArrayLength, or its two read a member as
    // a nullable type other than its own. The generator registers only what C# converts implicitly, so the
    // registration for such a path and type holds the same conversion, and none exists for a narrowing one.
    // ARCHITECTURE.md says so too.
    private static bool CanServe(LambdaExpression expression)
    {
        // The generator registers a chain rooted at the parameter itself. A chain rooted at a conversion of the model
        // reads the members of the type it converts to, which can be an explicit implementation or a member that one of
        // the model's own hides, so the registration for its path may read another member.
        if (expression.GetFaithfulChainRoot() is not ParameterExpression) return false;

        if (expression.Body is not UnaryExpression { NodeType: not ExpressionType.ArrayLength } conversion) return true;

        // A faithful selector converts a member chain: one that selects its parameter fails at the builder or
        // IsMemberChain first. A failing cast would expose a break in that, where a type test would quietly
        // serve a registration.
        var operand = conversion.IsConversionIntoNullable(out var read) ? read : conversion.Operand;
        return Nullable.GetUnderlyingType(operand.Type) is null &&
            !((MemberExpression)operand).IsNullableValueAccess();
    }
}

// The key of a path-faithful selector, the only kind the static caches take: a member chain rooted at the parameter or
// at its conversion to a type it already is, under at most one Convert, ConvertChecked or ArrayLength, or under the
// pair of conversions that reads a member whose type isn't nullable as another nullable type. Its dotted path names the
// members but leaves out a Nullable<T>.Value hop, so the body's node type and the type under its outer unary tell apart
// what shares a path: (long) x.Big and checked((long) x.Big), x.Age.Value and (int) x.Age, and (object) x.Child.Age,
// which reads a null age as null, and (object) x.Child.Age.Value, which reads it as no value. Only the last hop can be
// a left-out .Value, since one in the middle of a chain must be written out, and whether it is shows in that type: S?
// or S. Under a pair, that type is the one the member converts to before it is wrapped; both conversions check or
// neither does, and the member is neither nullable nor a .Value, so no single conversion of the same member reaches
// that type. A body with no unary is the TProperty the cache is for. The type a converted root converts to is in the
// key too: x => x.Name and x => ((Base)x).Name read different members when the model's Name hides the base one, as an
// interface's member does when the model implements it explicitly. expression.ToString() keyed them before, and
// rendered the whole selector for each validator built, which a Scoped validator is in every scope.
file static class FaithfulKey
{
    internal static AccessorKey Of(string path, LambdaExpression expression) =>
        (path, expression.Body.NodeType, (expression.Body as UnaryExpression)?.Operand.Type,
            (expression.GetFaithfulChainRoot() as UnaryExpression)?.Type);
}

/// <summary>
/// Compiles and caches <em>null-safe</em> accessors for member chains such as <c>x => x.Policy.Max</c>. The returned delegate reports
/// <c>HasValue = false</c> when any intermediate member is null, instead of throwing a <see cref="NullReferenceException" /> — so callers can skip the
/// rule, matching the library's "null short-circuits" convention. A leaf <c>null</c> (the final member) is still returned with <c>HasValue = true</c>,
/// so leaf-level rules (<c>NotNull</c>, comparisons) behave as before.
/// </summary>
internal static class NullSafeAccessorCache<TModel, TProperty>
{
    private static readonly ConcurrentDictionary<AccessorKey, Func<TModel, (bool HasValue, TProperty Value)>> Cache = new();

    // Source-generated null-safe accessors, path-keyed (see PropertyAccessorCache for the faithfulness rationale).
    private static ConcurrentDictionary<string, Func<TModel, (bool HasValue, TProperty Value)>>? _generated;

    internal static void Seed(string path, Func<TModel, (bool HasValue, TProperty Value)> accessor)
    {
        if (_generated is null)
        {
            Interlocked.CompareExchange(ref _generated,
                new ConcurrentDictionary<string, Func<TModel, (bool HasValue, TProperty Value)>>(), null);
        }

        _generated.TryAdd(path, accessor);
    }

    internal static bool HasGenerated(string path) => _generated is { } generated && generated.ContainsKey(path);

    // Also used for cross-property target selectors (e.g. GreaterThanOrEqual(x => x.Min)), which arrive as raw
    // expressions with no builder, so the caller passes the path. A path-faithful caller short-circuits to a
    // source-generated accessor keyed by its dotted path.
    internal static Func<TModel, (bool HasValue, TProperty Value)> GetOrAdd(string path, bool faithful, LambdaExpression expression)
    {
        if (GeneratedAccessors.TryGet(_generated, path, faithful, expression, out var precompiled))
            return precompiled;

        // Non-faithful selectors (closure/static/cast roots) bypass the static cache: a capture is not part of the
        // key, so different captured values would collide on one key and silently reuse the wrong accessor. Callers
        // memoize the delegate (comparison validators at construction, evaluators via ??=), so this costs one
        // compile per registration.
        if (!faithful) return Build(expression);

        return Cache.GetOrAdd(FaithfulKey.Of(path, expression), static (_, expression) => Build(expression),
            expression);
    }

    private static Func<TModel, (bool HasValue, TProperty Value)> Build(LambdaExpression expression)
    {
        // Collect the member chain root-first: x.A.B.C -> [A, B, C]. Every member except the leaf produces an object we
        // dereference further, so each of those results must be null-checked.
        // The unaries over the chain (conversions, of which C# writes two for a widening into a nullable type, and one
        // cast, !, -, as or array length) wrap it and are re-applied to the leaf below, innermost first.
        var unaries = AccessorExpressions.UnariesOver(expression.Body, out var node);
        var members = new Stack<MemberInfo>();
        while (node is MemberExpression member)
        {
            members.Push(member.Member);
            node = member.Expression!;
        }

        if (members.Count == 0)
        {
            throw new InvalidOperationException(
                $"A null-safe accessor requires a member-access expression, but '{expression}' is not one.");
        }

        var resultType = typeof((bool, TProperty));
        var ctor = resultType.GetConstructor([typeof(bool), typeof(TProperty)])!;
        var absent = Expression.New(ctor, Expression.Constant(false), Expression.Default(typeof(TProperty)));
        var returnTarget = Expression.Label(resultType);

        // Reuse the original lambda's parameter: the chain root may be a subtree (closure constant, cast, method call)
        // that still references it — a fresh parameter would leave those references unbound.
        var parameter = expression.Parameters[0];
        var locals = new List<ParameterExpression>();
        var body = new List<Expression>();

        // `node` is the chain's root: the parameter itself, null for a static member, or an arbitrary expression
        // (closure constant, cast, call) evaluated once and null-guarded like any intermediate.
        var current = AnchorRoot(node, absent, returnTarget, locals, body);
        var chain = members.ToArray(); // root-first

        // An array's length dereferences the last member as well, so that member is guarded like an intermediate.
        var isArrayLength = unaries.Count > 0 && unaries[^1].NodeType == ExpressionType.ArrayLength;
        var guarded = isArrayLength ? chain.Length : chain.Length - 1;
        for (var index = 0; index < guarded; index++)
        {
            var access = Expression.MakeMemberAccess(current, chain[index]);
            var local = Expression.Variable(access.Type, "t" + index);
            locals.Add(local);
            body.Add(Expression.Assign(local, access));

            // Only reference types and Nullable<T> can be null; skip the check for plain structs.
            if (!access.Type.IsValueType || Nullable.GetUnderlyingType(access.Type) != null)
            {
                body.Add(Expression.IfThen(
                    NullCheck.IsNull(local),
                    Expression.Return(returnTarget, absent)));
            }

            current = local;
        }

        var leaf = isArrayLength ? current! : Expression.MakeMemberAccess(current, chain[^1]);
        for (var index = unaries.Count - 1; index >= 0; index--) leaf = unaries[index].Update(leaf);
        if (leaf.Type != typeof(TProperty)) leaf = Expression.Convert(leaf, typeof(TProperty));

        body.Add(Expression.Return(returnTarget, Expression.New(ctor, Expression.Constant(true), leaf)));
        body.Add(Expression.Label(returnTarget, absent));

        var lambda = Expression.Lambda<Func<TModel, (bool HasValue, TProperty Value)>>(
            Expression.Block(locals, body), parameter);

        return lambda.Compile();
    }

    private static Expression? AnchorRoot(
        Expression? root,
        Expression absent,
        LabelTarget returnTarget,
        List<ParameterExpression> locals,
        List<Expression> body
    )
    {
        // Parameter root: the model is non-null by ValidationContext contract. Null root: the first chain member is
        // static and MakeMemberAccess accepts a null instance.
        if (root is null or ParameterExpression) return root;

        // Closure constant, cast, or call root, which only a comparison target can have: evaluate once into a local and
        // null-guard it when the type permits null (a TypeAs cast such as (x.Item as Child) yields null for a mismatch
        // → skip the rule).
        var local = Expression.Variable(root.Type, "root");
        locals.Add(local);
        body.Add(Expression.Assign(local, root));
        if (!root.Type.IsValueType || Nullable.GetUnderlyingType(root.Type) != null)
        {
            body.Add(Expression.IfThen(
                NullCheck.IsNull(local),
                Expression.Return(returnTarget, absent)));
        }

        return local;
    }
}

internal static class ElementsAccessorCache<TModel, TProperty>
{
    private static readonly ConcurrentDictionary<AccessorKey, Func<TModel, IEnumerable<TProperty>?>> Cache = new();

    // Source-generated elements accessors, path-keyed (see PropertyAccessorCache for the faithfulness rationale).
    private static ConcurrentDictionary<string, Func<TModel, IEnumerable<TProperty>?>>? _generated;

    internal static void Seed(string path, Func<TModel, IEnumerable<TProperty>?> accessor)
    {
        if (_generated is null)
        {
            Interlocked.CompareExchange(ref _generated,
                new ConcurrentDictionary<string, Func<TModel, IEnumerable<TProperty>?>>(), null);
        }

        _generated.TryAdd(path, accessor);
    }

    internal static bool HasGenerated(string path) => _generated is { } generated && generated.ContainsKey(path);

    internal static Func<TModel, IEnumerable<TProperty>?> GetOrAdd(PropertyValidatorBuilder<TModel> builder)
    {
        // A path-faithful selector short-circuits to a source-generated accessor keyed by its dotted path. A cast
        // inside the selector truncates the path and would collide with a same-named sibling rule (see
        // AccessorCastCollisionTests), so such a selector isn't faithful and stays out of both caches.
        var expression = builder.PropertyExpression;
        if (GeneratedAccessors.TryGet(
                _generated, builder.PropertyPath,
                builder.IsPathFaithful, expression, out var precompiled))
            return precompiled;

        // Non-faithful selectors bypass the static cache (capture identity — see PropertyAccessorCache.GetOrAdd); the
        // evaluator memoizes the delegate, so this costs one compile per registration.
        if (!builder.IsPathFaithful) return Build(expression);

        return Cache.GetOrAdd(
            FaithfulKey.Of(builder.PropertyPath, expression), static (_, expression) => Build(expression), expression);
    }

    // Null-safe collection accessor: a null intermediate (x => x.Order.Items where Order is null) yields a null
    // collection instead of throwing, which the evaluator already treats as "skip".
    // Unlike the property accessor a tuple is unnecessary — null is a representable "absent" here. Its selector passed
    // EnsureChainSelector where the chain was declared, so the chain starts at the lambda's parameter, or at that
    // parameter converted to a type it already is, as a generic model reads its constraint's members.
    private static Func<TModel, IEnumerable<TProperty>?> Build(LambdaExpression expression)
    {
        var unaries = AccessorExpressions.UnariesOver(expression.Body, out var node);
        var members = new Stack<MemberInfo>();
        while (node is MemberExpression member)
        {
            members.Push(member.Member);
            node = member.Expression!;
        }

        var returnType = typeof(IEnumerable<TProperty>);
        var nullResult = Expression.Constant(null, returnType);
        var returnTarget = Expression.Label(returnType);

        // Reuse the original lambda's parameter — see NullSafeAccessorCache.Build for the root rationale.
        var parameter = expression.Parameters[0];
        var locals = new List<ParameterExpression>();
        var body = new List<Expression>();

        // The root is the parameter or its widening conversion, neither of which can be null where the parameter isn't.
        var current = node;
        var chain = members.ToArray();
        for (var index = 0; index < chain.Length - 1; index++)
        {
            var access = Expression.MakeMemberAccess(current, chain[index]);
            var local = Expression.Variable(access.Type, "t" + index);
            locals.Add(local);
            body.Add(Expression.Assign(local, access));
            if (!access.Type.IsValueType || Nullable.GetUnderlyingType(access.Type) != null)
            {
                body.Add(Expression.IfThen(
                    NullCheck.IsNull(local),
                    Expression.Return(returnTarget, nullResult)));
            }

            current = local;
        }

        Expression leaf = Expression.MakeMemberAccess(current, chain[^1]);
        for (var index = unaries.Count - 1; index >= 0; index--) leaf = unaries[index].Update(leaf);
        if (leaf.Type != returnType) leaf = Expression.Convert(leaf, returnType);

        body.Add(Expression.Return(returnTarget, leaf));
        body.Add(Expression.Label(returnTarget, nullResult));

        var lambda = Expression.Lambda<Func<TModel, IEnumerable<TProperty>?>>(
            Expression.Block(locals, body), parameter);

        return lambda.Compile();
    }
}

// Shared by the null-safe and collection accessor builders.
file static class AccessorExpressions
{
    // The unary nodes over a selector's member chain, outermost first, and the chain itself in node.
    internal static List<UnaryExpression> UnariesOver(Expression body, out Expression node)
    {
        var unaries = new List<UnaryExpression>();
        node = body;
        while (node is UnaryExpression unary)
        {
            unaries.Add(unary);
            node = unary.Operand;
        }

        return unaries;
    }
}

internal static class CrossPropertyTarget<TModel, TProperty>
{
    // A member chain reads null-safely through the path-keyed, possibly source-generated accessor. One that reads the
    // model, through a cast, an indexer, or a call included, is another property, named by its path and the member it
    // ends at; one that starts at a captured or static value is printed by its value, as anything else is, such as
    // x => x.Minimum * 2, which is compiled as written.
    internal static (Func<TModel, (bool HasValue, TProperty Value)> Accessor, string? Name, MemberInfo? Member) Of(
        Expression<Func<TModel, TProperty>> expression
    )
    {
        if (expression.IsMemberChain())
        {
            var path = expression.GetPropertyPath();
            var accessor = NullSafeAccessorCache<TModel, TProperty>.GetOrAdd(path, expression.IsPathFaithfulMemberChain(), expression);
            return expression.StartsOutsideTheModel() ? (accessor, null, null) : (accessor, path, expression.GetLeafMember());
        }

        var compiled = expression.Compile();
        return (model => (true, compiled(model)), null, null);
    }
}

// Absent means a null reference or an empty Nullable<T>, which is what the generated accessors' `is null` tests.
// Expression.Equal binds to the == a reference type declares, which can call a present member absent or throw on the
// null operand, so a reference is compared by identity. On Nullable<T> it stays lifted: against a null constant it
// compares HasValue and never calls an operator of T.
file static class NullCheck
{
    internal static Expression IsNull(Expression value) =>
        value.Type.IsValueType
            ? Expression.Equal(value, Expression.Constant(null, value.Type))
            : Expression.ReferenceEqual(value, Expression.Constant(null, value.Type));
}