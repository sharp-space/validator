using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Sharp.Validator;

internal static partial class InternalExtensions
{
    extension(LambdaExpression lambdaExpression)
    {
        // Every error is reported under a property, so a selector that returns its parameter has nothing to name. Rules
        // on the model itself and a collection as the root model are unsupported by design, and the message says what
        // to write instead. A property or a field may end the chain.
        internal MemberInfo GetSelectedMember() =>
            PeelUnaries(lambdaExpression.Body, out _) switch
            {
                MemberExpression memberExpression => memberExpression.Member,

                NewExpression => throw new InvalidOperationException(
                    "Validator's property expression does not support new expression, such as 'x => new { x.Id }'."),

                ParameterExpression => throw new InvalidOperationException(
                    $"Validator's property expression('{lambdaExpression}') selects the model itself, which isn't supported: a rule " +
                    "reports its errors under a property. Put a check across properties on one of them and read the others from " +
                    "context.Model, validate a collection through a model that holds it, and put rules for each element itself on " +
                    "its Elements chain."),

                _ => throw new InvalidOperationException(
                    $"Validator's property expression('{lambdaExpression}') is invalid: a selector must be a member chain, such as " +
                    "'x => x.Address.City'. To check a value computed from the model, put a Must on the property it reads.")
            };

        /// <summary>
        /// Rejects, where a chain or a partial-validation selection is declared, a selector without a path from the model: one that reads through an indexer, a
        /// call or a cast on the way to its last member, or that starts at a captured or static value rather than its parameter. Its path would stop at the last
        /// member, so its errors would be reported, and a selection matched, under a name that isn't its own. The member chain may sit under conversions and one
        /// other unary operator, such as <c>!</c>, <c>-</c>, <c>as</c> or an array's <c>Length</c>, whose operand names the value.
        /// </summary>
        internal void EnsureChainSelector()
        {
            var node = PeelUnaries(lambdaExpression.Body, out var arrayLength);

            // A body that isn't a member chain throws first, with the message that says what to write instead.
            // A conversion left under an array's Length is a cast on the way to that hop, which the cast's
            // message below names.
            if (!(arrayLength && node is UnaryExpression)) lambdaExpression.GetSelectedMember();
            while (node is MemberExpression member) node = member.Expression;
            if (node == lambdaExpression.Parameters[0]) return;

            const string indexer = "reads through an indexer; validate each element with Elements(...), and a member of each " +
                "with Elements(...).Property(...)";

            var reason = node switch
            {
                null => "reads a static member rather than the model",
                MethodCallExpression { Method.Name: "get_Item" } => indexer,
                BinaryExpression { NodeType: ExpressionType.ArrayIndex } => indexer,
                MethodCallExpression => "reads through a method call; select the member the call reads, and check the computed " +
                    "value with a Must on it",
                UnaryExpression { Operand: var operand } when operand == lambdaExpression.Parameters[0] =>
                    "casts the model; check a derived type's members in a Must that tests context.Model, or validate that " +
                    "type with a validator of its own",
                UnaryExpression => "reads through a cast; select the member the cast reads, and validate it as the derived " +
                    "type with Include(...)",
                _ => "doesn't start at its parameter, so it reads another value than the model"
            };

            throw new InvalidOperationException(
                $"Validator's property expression('{lambdaExpression}') {reason}. It has no path of its own to report errors " +
                "under or to select by, so select a member chain of the model, such as 'x => x.Address.City'.");
        }

        /// <summary>
        /// Root-first dotted path of the member-access chain, e.g. <c>x => x.Policy.Max</c> → <c>"Policy.Max"</c>. <c>Nullable&lt;T&gt;.Value</c> hops are
        /// elided so the reported name stays clean. Falls back to <see cref="GetSelectedMember" /> (and its validation) when the body is not a member chain.
        /// </summary>
        internal string GetPropertyPath(bool allowEmptyNullableValuePath = false)
        {
            var node = PeelUnaries(lambdaExpression.Body, out var arrayLength);
            var nullableValueOnly = node is MemberExpression nullableValue && nullableValue.IsNullableValueAccess();

            // An array's Length is an ArrayLength node rather than a member. Keeping the hop makes the path read like
            // List<T>.Count and match the key the generator derives from the source text.
            var segments = new List<string>();
            if (arrayLength) segments.Add(nameof(Array.Length));

            while (node is MemberExpression member)
            {
                if (!member.IsNullableValueAccess()) segments.Add(member.Member.Name);

                node = member.Expression!;
            }

            if (segments.Count == 0)
            {
                return allowEmptyNullableValuePath && nullableValueOnly
                    ? string.Empty
                    : lambdaExpression.GetSelectedMember().Name;
            }

            segments.Reverse();
            return string.Join('.', segments);
        }

        /// <summary>
        /// The member the selector ends at, past <c>Nullable&lt;T&gt;.Value</c> as the path skips it, for the display-name resolver; <c>null</c> when the body
        /// is not a member access, or ends at an array's Length.
        /// </summary>
        internal MemberInfo? GetLeafMember()
        {
            var node = PeelUnaries(lambdaExpression.Body, out var arrayLength);
            if (arrayLength) return null;

            while (node is MemberExpression member)
            {
                if (!member.IsNullableValueAccess()) return member.Member;

                node = member.Expression;
            }

            return null;
        }

        /// <summary>
        /// True when the body, past its conversions and one other unary, is a member access, so it has a path to name. C# writes a widening into a nullable
        /// type, such as an <c>int</c> member read as <c>long?</c>, as two conversions, and the target is still that member.
        /// </summary>
        internal bool IsMemberChain() => PeelUnaries(lambdaExpression.Body, out _) is MemberExpression;

        /// <summary>
        /// True when the selector's member chain starts at a captured value or a static member rather than at the model, which a cast, an indexer, or a call on
        /// the way still leaves it reading.
        /// </summary>
        internal bool StartsOutsideTheModel()
        {
            var node = PeelUnaries(lambdaExpression.Body, out _);
            while (node is MemberExpression member) node = member.Expression;

            return node is null or ConstantExpression;
        }

        /// <summary>
        /// True when the chain dereferences at least one intermediate member (e.g. <c>x => x.A.B</c>), so a null intermediate would throw unless the accessor
        /// is compiled null-safe.
        /// </summary>
        internal bool IsNestedProperty()
        {
            var node = PeelUnaries(lambdaExpression.Body, out var arrayLength);
            var hops = arrayLength ? 1 : 0;
            while (node is MemberExpression member)
            {
                hops++;
                node = member.Expression!;
            }

            return hops >= 2;
        }

        /// <summary>
        /// True, when the selector is a pure member-access chain rooted at the parameter (no cast, index, or call in the middle), so its dotted path is a
        /// faithful, collision-free cache key. A cast such as <c>((I)x.Item).Id</c> is not faithful — it truncates to the same path as a sibling <c>x.Id</c>
        /// rule. Neither is a computing unary such as <c>!x.Active</c>: its path names the operand, not the value. Of stacked conversions, only the pair that
        /// reads a member as another nullable type is: its value follows from the member, the type and the checking, which the static caches' key holds. Any
        /// other stack can pass through a type in between that the key doesn't show.
        /// </summary>
        internal bool IsPathFaithfulMemberChain()
        {
            var node = lambdaExpression.Body;
            if (node is UnaryExpression unary)
            {
                if (unary.NodeType is not (ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.ArrayLength))
                    return false;

                node = unary.IsConversionIntoNullable(out var read) ? read : unary.Operand;
            }

            while (node is MemberExpression member) node = member.Expression!;

            return node is ParameterExpression;
        }
    }

    // The member chain under a selector's top: any number of conversions, since C# writes a widening into a nullable
    // type as two, and at most one other unary operator, such as !, -, as or an array's Length. A second other unary is
    // left in place, so the caller sees no member and treats the body as computed. An array's Length is a hop of the
    // path, so it sits on the chain itself: a conversion under it, as in ((int[]) x.Items).Length, is a cast in the
    // middle of the path and is left in place as well.
    internal static Expression PeelUnaries(Expression body, out bool arrayLength)
    {
        arrayLength = false;
        var other = false;
        while (body is UnaryExpression unary)
        {
            if (unary.NodeType is not (ExpressionType.Convert or ExpressionType.ConvertChecked))
            {
                if (other) break;

                other = true;
                arrayLength = unary.NodeType == ExpressionType.ArrayLength;
            }
            else if (arrayLength) break;

            body = unary.Operand;
        }

        return body;
    }

    extension(UnaryExpression conversion)
    {
        /// <summary>
        /// True when the conversion is the pair C# writes to read a member whose type isn't nullable as a nullable type other than its own, such as an
        /// <c>int</c> member read as <c>long?</c>: <c>Convert(Convert(x, long), long?)</c>, both checked or both not. The inner one may call an operator, as
        /// <c>int</c> to <c>decimal</c> does. Its value follows from the member, the type and the checking alone, as a single conversion's does. A nullable
        /// member or its <c>Value</c> doesn't count: the path leaves the <c>Value</c> out, so <c>(long) x.Other</c>, which throws on a null <c>Other</c>, would
        /// share a key with <c>x.Other.Value</c>, which skips.
        /// </summary>
        internal bool IsConversionIntoNullable([NotNullWhen(true)] out MemberExpression? member)
        {
            member = conversion is
                {
                    NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked, Method: null,
                    Operand: UnaryExpression { Operand: MemberExpression read } inner
                } && inner.NodeType == conversion.NodeType && Nullable.GetUnderlyingType(conversion.Type) == inner.Type &&
                Nullable.GetUnderlyingType(read.Type) is null && !read.IsNullableValueAccess()
                    ? read
                    : null;

            return member is not null;
        }
    }

    extension(MemberExpression memberExpression)
    {
        /// <summary>True when this node accesses <c>Nullable&lt;T&gt;.Value</c>.</summary>
        internal bool IsNullableValueAccess() =>
            memberExpression.Member is { Name: "Value", DeclaringType: { IsGenericType: true } declaring } &&
            declaring.GetGenericTypeDefinition() == typeof(Nullable<>);
    }

    extension<T>(T value)
    {
        /// <summary>
        /// Culture-stable stringification for error-message values: <see cref="IFormattable" /> formats with
        /// <see cref="System.Globalization.CultureInfo.InvariantCulture" /> (a singleton validator baked under one request's culture must not leak it into every
        /// later message), and <c>null</c> renders as <c>"null"</c> so placeholders are always substituted.
        /// </summary>
        internal string ToInvariantString() =>
            value is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value?.ToString() ?? "null";
    }

    extension(string input)
    {
        internal string PascalCase() =>
            PascalCaseRegex().Replace(input, x => x.Groups[1].Value.ToUpperInvariant());
    }

    [GeneratedRegex("(?:^[_ ]*|[_ ]+)(.)")]
    // ^[_ ]* consumes any leading separators in a single match, so "foo" becomes "Foo" instead of "Foo". The original
    // pattern (^|_| +)(.) treated each leading run of separators as a capture point but only consumed one character per
    // match, leaving the remainder verbatim.
    private static partial Regex PascalCaseRegex();
}