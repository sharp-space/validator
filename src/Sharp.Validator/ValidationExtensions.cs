using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Sharp.Validator.Result;
using Sharp.Validator.Validators;

namespace Sharp.Validator;

/// <summary>
/// Built-in validator rules. Most rules short-circuit on a <c>null</c> value; combine with <c>NotNull</c>/<c>NotEmpty</c> to require presence.
/// </summary>
public static class ValidationExtensions
{
    extension<TModel, TProperty>(PropertyValidatorBuilder<TModel, TProperty> builder)
    {
        /// <summary>Value must be <c>null</c>.</summary>
        public PropertyValidatorBuilder<TModel, TProperty> Null(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add<NullValidator<TModel, TProperty>>(configure);

        /// <summary>Value must not be <c>null</c>.</summary>
        public PropertyValidatorBuilder<TModel, TProperty> NotNull(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add<NotNullValidator<TModel, TProperty>>(configure);

        /// <summary>Value must be <c>null</c>, an empty/whitespace string, an empty collection, or the type's default.</summary>
        public PropertyValidatorBuilder<TModel, TProperty> Empty(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add<EmptyValidator<TModel, TProperty>>(configure);

        /// <summary>Value must not be <c>null</c>, empty/whitespace, an empty collection, or the type's default.</summary>
        public PropertyValidatorBuilder<TModel, TProperty> NotEmpty(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add<NotEmptyValidator<TModel, TProperty>>(configure);

        #region Include Validation

        /// <summary>
        /// Runs another <see cref="Validator{TChild}" /> against this property and merges its errors. Child errors report as <c>"Parent.Child"</c> by default;
        /// set <see cref="IncludeValidatorOptions.IncludeParentPropertyName" /> to <c>false</c> to report the child name only. A <typeparamref name="TChild" />
        /// narrower than the property runs only against values of that type. Composing the child freezes its validator definition. Parent and child validation
        /// contexts share only <see cref="ValidationContext{TModel}.Parameters" />; a partial-validation selection narrows into the child.
        /// </summary>
        // Generic over the child rather than taking Validator<TProperty>: a Validator<Address> must fit an Address?
        // property without a nullability warning, and two overloads can't differ by annotation alone.
        public PropertyValidatorBuilder<TModel, TProperty> Include<TChild>(
            Validator<TChild> validator,
            Action<IncludeValidatorOptions>? configure = null
        ) where TChild : TProperty
        {
            ArgumentNullException.ThrowIfNull(validator);
            builder.PrimaryBuilder.EnsureMutable();
            IncludeValidatorComposition.EnsureCanCompose(builder.PrimaryBuilder, validator);

            var options = new IncludeValidatorOptions();
            configure?.Invoke(options);
            options.MakeReadOnly();
            if (IncludeValidatorComposition.Prepare(builder.PrimaryBuilder, validator, true))
                builder.Add(new AsyncIncludeDerivedValidator<TModel, TProperty, TChild>(validator, options));
            else builder.Add(new IncludeDerivedValidator<TModel, TProperty, TChild>(validator, options));

            return builder;
        }

        #endregion


        #region Equality Validation

        /// <summary>Value must equal <paramref name="value" /> by the default equality comparer.</summary>
        public PropertyValidatorBuilder<TModel, TProperty> Equal(TProperty? value, Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new EqualValidator<TModel, TProperty>(value), configure);

        /// <summary>Value must equal <paramref name="value" />. Uses <paramref name="comparer" />, or the default comparer when it is <c>null</c>.</summary>
        public PropertyValidatorBuilder<TModel, TProperty> Equal(
            TProperty? value,
            IEqualityComparer<TProperty>? comparer,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Add(new EqualValidator<TModel, TProperty>(value, comparer), configure);

        /// <summary>Value must equal the result of <paramref name="expression" /> applied to the model.</summary>
        public PropertyValidatorBuilder<TModel, TProperty> Equal(
            Expression<Func<TModel, TProperty?>> expression,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Equal(expression, null, configure);

        /// <summary>
        /// Value must equal the result of <paramref name="expression" /> applied to the model. Uses <paramref name="comparer" />, or the default comparer when
        /// it is <c>null</c>.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TProperty> Equal(
            Expression<Func<TModel, TProperty?>> expression,
            IEqualityComparer<TProperty>? comparer,
            Action<PropertyValidatorOptions>? configure = null
        )
        {
            ArgumentNullException.ThrowIfNull(expression);
            var (accessor, name, member) = CrossPropertyTarget<TModel, TProperty?>.Of(expression);
            return builder.Add(new EqualValidator<TModel, TProperty>(accessor, name, comparer) { TargetMember = member }, configure);
        }

        /// <summary>Value must not equal <paramref name="value" />.</summary>
        public PropertyValidatorBuilder<TModel, TProperty> NotEqual(TProperty? value, Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new NotEqualValidator<TModel, TProperty>(value), configure);

        /// <summary>Value must not equal <paramref name="value" />. Uses <paramref name="comparer" />, or the default comparer when it is <c>null</c>.</summary>
        public PropertyValidatorBuilder<TModel, TProperty> NotEqual(
            TProperty? value,
            IEqualityComparer<TProperty>? comparer,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Add(new NotEqualValidator<TModel, TProperty>(value, comparer), configure);

        /// <summary>Value must not equal the result of <paramref name="expression" /> applied to the model.</summary>
        public PropertyValidatorBuilder<TModel, TProperty> NotEqual(
            Expression<Func<TModel, TProperty?>> expression,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.NotEqual(expression, null, configure);

        /// <summary>
        /// Value must not equal the result of <paramref name="expression" /> applied to the model. Uses <paramref name="comparer" />, or the default comparer
        /// when it is <c>null</c>.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TProperty> NotEqual(
            Expression<Func<TModel, TProperty?>> expression,
            IEqualityComparer<TProperty>? comparer,
            Action<PropertyValidatorOptions>? configure = null
        )
        {
            ArgumentNullException.ThrowIfNull(expression);
            var (accessor, name, member) = CrossPropertyTarget<TModel, TProperty?>.Of(expression);
            return builder.Add(new NotEqualValidator<TModel, TProperty>(accessor, name, comparer) { TargetMember = member }, configure);
        }

        #endregion

        #region Comparison Validation

        /// <summary>Value must be strictly greater than <paramref name="value" />.</summary>
        public PropertyValidatorBuilder<TModel, TProperty> GreaterThan(
            [DisallowNull] TProperty value,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Add(new GreaterThanValidator<TModel, TProperty>(value), configure);

        /// <summary>
        /// Value must be strictly greater than <paramref name="value" />. Uses <paramref name="comparer" />, or the default comparer when it is <c>null</c>.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TProperty> GreaterThan(
            [DisallowNull] TProperty value,
            IComparer<TProperty>? comparer,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Add(new GreaterThanValidator<TModel, TProperty>(value, comparer), configure);

        /// <summary>Value must be strictly greater than the result of <paramref name="expression" />.</summary>
        public PropertyValidatorBuilder<TModel, TProperty> GreaterThan(
            Expression<Func<TModel, TProperty?>> expression,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.GreaterThan(expression, null, configure);

        /// <summary>
        /// Value must be strictly greater than the result of <paramref name="expression" />. Uses <paramref name="comparer" />, or the default comparer when
        /// it is <c>null</c>.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TProperty> GreaterThan(
            Expression<Func<TModel, TProperty?>> expression,
            IComparer<TProperty>? comparer,
            Action<PropertyValidatorOptions>? configure = null
        )
        {
            ArgumentNullException.ThrowIfNull(expression);
            var (accessor, name, member) = CrossPropertyTarget<TModel, TProperty?>.Of(expression);
            return builder.Add(new GreaterThanValidator<TModel, TProperty>(accessor, name, comparer) { TargetMember = member }, configure);
        }

        /// <summary>Value must be greater than or equal to <paramref name="value" />.</summary>
        public PropertyValidatorBuilder<TModel, TProperty> GreaterThanOrEqual(
            [DisallowNull] TProperty value,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Add(new GreaterThanOrEqualValidator<TModel, TProperty>(value), configure);

        /// <summary>
        /// Value must be greater than or equal to <paramref name="value" />. Uses <paramref name="comparer" />, or the default comparer when it is <c>null</c>.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TProperty> GreaterThanOrEqual(
            [DisallowNull] TProperty value,
            IComparer<TProperty>? comparer,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Add(new GreaterThanOrEqualValidator<TModel, TProperty>(value, comparer), configure);

        /// <summary>Value must be greater than or equal to the result of <paramref name="expression" />.</summary>
        public PropertyValidatorBuilder<TModel, TProperty> GreaterThanOrEqual(
            Expression<Func<TModel, TProperty?>> expression,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.GreaterThanOrEqual(expression, null, configure);

        /// <summary>
        /// Value must be greater than or equal to the result of <paramref name="expression" />. Uses <paramref name="comparer" />, or the default comparer when
        /// it is <c>null</c>.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TProperty> GreaterThanOrEqual(
            Expression<Func<TModel, TProperty?>> expression,
            IComparer<TProperty>? comparer,
            Action<PropertyValidatorOptions>? configure = null
        )
        {
            ArgumentNullException.ThrowIfNull(expression);
            var (accessor, name, member) = CrossPropertyTarget<TModel, TProperty?>.Of(expression);
            return builder.Add(new GreaterThanOrEqualValidator<TModel, TProperty>(accessor, name, comparer) { TargetMember = member }, configure);
        }

        /// <summary>Value must be strictly less than <paramref name="value" />.</summary>
        public PropertyValidatorBuilder<TModel, TProperty> LessThan(
            [DisallowNull] TProperty value,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Add(new LessThanValidator<TModel, TProperty>(value), configure);

        /// <summary>
        /// Value must be strictly less than <paramref name="value" />. Uses <paramref name="comparer" />, or the default comparer when it is <c>null</c>.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TProperty> LessThan(
            [DisallowNull] TProperty value,
            IComparer<TProperty>? comparer,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Add(new LessThanValidator<TModel, TProperty>(value, comparer), configure);

        /// <summary>Value must be strictly less than the result of <paramref name="expression" />.</summary>
        public PropertyValidatorBuilder<TModel, TProperty> LessThan(
            Expression<Func<TModel, TProperty?>> expression,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.LessThan(expression, null, configure);

        /// <summary>
        /// Value must be strictly less than the result of <paramref name="expression" />. Uses <paramref name="comparer" />, or the default comparer when
        /// it is <c>null</c>.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TProperty> LessThan(
            Expression<Func<TModel, TProperty?>> expression,
            IComparer<TProperty>? comparer,
            Action<PropertyValidatorOptions>? configure = null
        )
        {
            ArgumentNullException.ThrowIfNull(expression);
            var (accessor, name, member) = CrossPropertyTarget<TModel, TProperty?>.Of(expression);
            return builder.Add(new LessThanValidator<TModel, TProperty>(accessor, name, comparer) { TargetMember = member }, configure);
        }

        /// <summary>Value must be less than or equal to <paramref name="value" />.</summary>
        public PropertyValidatorBuilder<TModel, TProperty> LessThanOrEqual(
            [DisallowNull] TProperty value,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Add(new LessThanOrEqualValidator<TModel, TProperty>(value), configure);

        /// <summary>
        /// Value must be less than or equal to <paramref name="value" />. Uses <paramref name="comparer" />, or the default comparer when it is <c>null</c>.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TProperty> LessThanOrEqual(
            [DisallowNull] TProperty value,
            IComparer<TProperty>? comparer,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Add(new LessThanOrEqualValidator<TModel, TProperty>(value, comparer), configure);

        /// <summary>Value must be less than or equal to the result of <paramref name="expression" />.</summary>
        public PropertyValidatorBuilder<TModel, TProperty> LessThanOrEqual(
            Expression<Func<TModel, TProperty?>> expression,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.LessThanOrEqual(expression, null, configure);

        /// <summary>
        /// Value must be less than or equal to the result of <paramref name="expression" />. Uses <paramref name="comparer" />, or the default comparer when
        /// it is <c>null</c>.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TProperty> LessThanOrEqual(
            Expression<Func<TModel, TProperty?>> expression,
            IComparer<TProperty>? comparer,
            Action<PropertyValidatorOptions>? configure = null
        )
        {
            ArgumentNullException.ThrowIfNull(expression);
            var (accessor, name, member) = CrossPropertyTarget<TModel, TProperty?>.Of(expression);
            return builder.Add(new LessThanOrEqualValidator<TModel, TProperty>(accessor, name, comparer) { TargetMember = member }, configure);
        }

        /// <summary>Value must satisfy <c>from &lt;= value &lt;= to</c>.</summary>
        public PropertyValidatorBuilder<TModel, TProperty> InclusiveBetween(
            [DisallowNull] TProperty from,
            [DisallowNull] TProperty to,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Add(new InclusiveBetweenValidator<TModel, TProperty>(from, to), configure);

        /// <summary>
        /// Value must satisfy <c>from &lt;= value &lt;= to</c>. Uses <paramref name="comparer" />, or the default comparer when it is <c>null</c>.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TProperty> InclusiveBetween(
            [DisallowNull] TProperty from,
            [DisallowNull] TProperty to,
            IComparer<TProperty>? comparer,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Add(new InclusiveBetweenValidator<TModel, TProperty>(from, to, comparer), configure);

        /// <summary>Value must satisfy <c>from &lt; value &lt; to</c>.</summary>
        public PropertyValidatorBuilder<TModel, TProperty> ExclusiveBetween(
            [DisallowNull] TProperty from,
            [DisallowNull] TProperty to,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Add(new ExclusiveBetweenValidator<TModel, TProperty>(from, to), configure);

        /// <summary>
        /// Value must satisfy <c>from &lt; value &lt; to</c>. Uses <paramref name="comparer" />, or the default comparer when it is <c>null</c>.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TProperty> ExclusiveBetween(
            [DisallowNull] TProperty from,
            [DisallowNull] TProperty to,
            IComparer<TProperty>? comparer,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Add(new ExclusiveBetweenValidator<TModel, TProperty>(from, to, comparer), configure);

        /// <summary>
        /// Value must satisfy <c>from &lt;= value &lt;= to</c>, with both bounds read from the model, such as two other properties. A bound behind a <c>null</c>
        /// member skips the check, and an inverted range admits no value.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TProperty> InclusiveBetween(
            Expression<Func<TModel, TProperty?>> from,
            Expression<Func<TModel, TProperty?>> to,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.InclusiveBetween(from, to, null, configure);

        /// <summary>
        /// Value must satisfy <c>from &lt;= value &lt;= to</c>, with both bounds read from the model, such as two other properties. A bound behind a <c>null</c>
        /// member skips the check, and an inverted range admits no value. Uses <paramref name="comparer" />, or the default comparer when it is <c>null</c>.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TProperty> InclusiveBetween(
            Expression<Func<TModel, TProperty?>> from,
            Expression<Func<TModel, TProperty?>> to,
            IComparer<TProperty>? comparer,
            Action<PropertyValidatorOptions>? configure = null
        )
        {
            ArgumentNullException.ThrowIfNull(from);
            ArgumentNullException.ThrowIfNull(to);

            var (fromAccessor, fromName, fromMember) = CrossPropertyTarget<TModel, TProperty?>.Of(from);
            var (toAccessor, toName, toMember) = CrossPropertyTarget<TModel, TProperty?>.Of(to);
            return builder.Add(
                new InclusiveBetweenValidator<TModel, TProperty>(fromAccessor, fromName, toAccessor, toName, comparer)
                {
                    FromMember = fromMember,
                    ToMember = toMember
                },
                configure);
        }

        /// <summary>
        /// Value must satisfy <c>from &lt; value &lt; to</c>, with both bounds read from the model, such as two other properties. A bound behind a <c>null</c>
        /// member skips the check, and equal or inverted bounds admit no value.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TProperty> ExclusiveBetween(
            Expression<Func<TModel, TProperty?>> from,
            Expression<Func<TModel, TProperty?>> to,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.ExclusiveBetween(from, to, null, configure);

        /// <summary>
        /// Value must satisfy <c>from &lt; value &lt; to</c>, with both bounds read from the model, such as two other properties. A bound behind a
        /// <c>null</c> member skips the check, and equal or inverted bounds admit no value. Uses <paramref name="comparer" />, or the default comparer when
        /// it is <c>null</c>.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TProperty> ExclusiveBetween(
            Expression<Func<TModel, TProperty?>> from,
            Expression<Func<TModel, TProperty?>> to,
            IComparer<TProperty>? comparer,
            Action<PropertyValidatorOptions>? configure = null
        )
        {
            ArgumentNullException.ThrowIfNull(from);
            ArgumentNullException.ThrowIfNull(to);

            var (fromAccessor, fromName, fromMember) = CrossPropertyTarget<TModel, TProperty?>.Of(from);
            var (toAccessor, toName, toMember) = CrossPropertyTarget<TModel, TProperty?>.Of(to);
            return builder.Add(
                new ExclusiveBetweenValidator<TModel, TProperty>(fromAccessor, fromName, toAccessor, toName, comparer)
                {
                    FromMember = fromMember,
                    ToMember = toMember
                },
                configure);
        }

        #endregion

        #region Must Validation

        /// <summary>Value must satisfy <paramref name="predicate" />, which receives every value, <c>null</c> included.</summary>
        public PropertyValidatorBuilder<TModel, TProperty> Must(Func<TProperty, bool> predicate, Action<PropertyValidatorOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            return builder.Add(PredicateValidator<TModel, TProperty>.ForValue(predicate), configure);
        }

        /// <summary>Value must satisfy <paramref name="predicate" />, which also receives the validation context.</summary>
        public PropertyValidatorBuilder<TModel, TProperty> Must(
            Func<TProperty, ValidationContext<TModel>, bool> predicate,
            Action<PropertyValidatorOptions>? configure = null
        )
        {
            ArgumentNullException.ThrowIfNull(predicate);
            return builder.Add(new PredicateValidator<TModel, TProperty>(predicate), configure);
        }

        /// <summary>Value must satisfy the async <paramref name="predicate" />. Marks the parent builder as async.</summary>
        public PropertyValidatorBuilder<TModel, TProperty> MustAsync(
            Func<TProperty, Task<bool>> predicate,
            Action<PropertyValidatorOptions>? configure = null
        )
        {
            ArgumentNullException.ThrowIfNull(predicate);
            return builder.Add(AsyncPredicateValidator<TModel, TProperty>.ForValue(predicate), configure);
        }

        /// <summary>
        /// Value must satisfy the async <paramref name="predicate" />, which observes a <see cref="CancellationToken" />. Marks the parent builder as async.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TProperty> MustAsync(
            Func<TProperty, CancellationToken, Task<bool>> predicate,
            Action<PropertyValidatorOptions>? configure = null
        )
        {
            ArgumentNullException.ThrowIfNull(predicate);
            return builder.Add(AsyncPredicateValidator<TModel, TProperty>.ForValueAndToken(predicate), configure);
        }

        /// <summary>
        /// Value must satisfy the async <paramref name="predicate" />, which receives the validation context and a <see cref="CancellationToken" />. Marks the
        /// parent builder as async.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TProperty> MustAsync(
            Func<TProperty, ValidationContext<TModel>, CancellationToken, Task<bool>> predicate,
            Action<PropertyValidatorOptions>? configure = null
        )
        {
            ArgumentNullException.ThrowIfNull(predicate);
            return builder.Add(new AsyncPredicateValidator<TModel, TProperty>(predicate), configure);
        }

        #endregion
    }

    // A string chain is string or string? by the property's declaration, so the string rules take either: the
    // constraint admits both, and a string? rule fits both by the interfaces' variance. It also admits a type of an
    // app's that implements IEquatable<string>, which no string rule can check, so the block's Add turns that away when
    // the rule is declared. That Add takes a string? rule where the chain's own Add can't: in code generic over the
    // chain's type, as these rules and an app's string rule extensions are. On a chain of string or string?, the
    // instance Add wins, as an applicable instance method does over any extension.
    extension<TModel, TString>(PropertyValidatorBuilder<TModel, TString> builder) where TString : class?, IEquatable<string>?
    {
        /// <summary>
        /// Adds a <c>string?</c> rule to a chain whose type is generic over <c>string</c> and <c>string?</c>, as in an extension method written over both
        /// string chains. Throws when the chain's type is not <see cref="string" />.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TString> Add(
            IPropertyValidator<TModel, string?> validator,
            Action<PropertyValidatorOptions>? configure = null
        )
        {
            EnsureStringChain<TString>();
            return builder.Add((IPropertyValidator<TModel, TString>)validator, configure);
        }

        /// <summary>Adds an async <c>string?</c> rule to a chain whose type is generic over <c>string</c> and <c>string?</c>, as the sync form does.</summary>
        public PropertyValidatorBuilder<TModel, TString> Add(
            IAsyncPropertyValidator<TModel, string?> validator,
            Action<PropertyValidatorOptions>? configure = null
        )
        {
            EnsureStringChain<TString>();
            return builder.Add((IAsyncPropertyValidator<TModel, TString>)validator, configure);
        }

        /// <summary>String must be a valid email address (case-insensitive domain).</summary>
        public PropertyValidatorBuilder<TModel, TString> Email(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new EmailValidator<TModel>(), configure);

        /// <summary>Adds the password rules with their default requirements.</summary>
        public PropertyValidatorBuilder<TModel, TString> Password() => builder.Password(_ => { });

        /// <summary>
        /// Adds a configurable bundle of password rules (length, digits, symbols, case, uniqueness): <paramref name="requirements" /> sets them, and
        /// <paramref name="configure" />, applied to each rule, its code, message, or severity.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TString> Password(
            Action<PasswordValidatorOptions> requirements,
            Action<PropertyValidatorOptions>? configure = null
        )
        {
            ArgumentNullException.ThrowIfNull(requirements);

            var options = new PasswordValidatorOptions();
            requirements(options);
            options.MakeReadOnly();
            if (options.RequiredLength > 0)
            {
                var validator = new PasswordLengthValidator<TModel>(options.RequiredLength);
                builder.Add(validator, configure);
            }

            if (options.RequiredDigits > 0)
            {
                var validator = new PasswordDigitCountValidator<TModel>(options.RequiredDigits);
                builder.Add(validator, configure);
            }

            if (options.RequiredNonAlphanumerics > 0)
            {
                var validator = new PasswordNonAlphanumericCountValidator<TModel>(options.RequiredNonAlphanumerics);
                builder.Add(validator, configure);
            }

            if (options.RequiredUppercaseLetters > 0)
            {
                var validator = new PasswordUppercaseCountValidator<TModel>(options.RequiredUppercaseLetters);
                builder.Add(validator, configure);
            }

            if (options.RequiredLowercaseLetters > 0)
            {
                var validator = new PasswordLowercaseCountValidator<TModel>(options.RequiredLowercaseLetters);
                builder.Add(validator, configure);
            }

            if (options.RequiredUniqueCharacters > 0)
            {
                var validator = new PasswordUniqueCharacterCountValidator<TModel>(options.RequiredUniqueCharacters);
                builder.Add(validator, configure);
            }

            return builder;
        }

        /// <summary>String must be a Luhn-valid card number (12–19 digits, with optional spaces or dashes).</summary>
        public PropertyValidatorBuilder<TModel, TString> CreditCard(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new CreditCardValidator<TModel>(), configure);

        /// <summary>String must look like a phone number (digits with optional separators, plus, and extension).</summary>
        public PropertyValidatorBuilder<TModel, TString> PhoneNumber(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new PhoneNumberValidator<TModel>(), configure);

        /// <summary>String must equal the name of a member of <paramref name="enumType" />, compared ordinally.</summary>
        public PropertyValidatorBuilder<TModel, TString> InEnum(Type enumType, Action<PropertyValidatorOptions>? configure = null) =>
            builder.InEnum(enumType, StringComparison.Ordinal, configure);

        /// <summary>String must equal the name of a member of <paramref name="enumType" /> under <paramref name="stringComparison" />.</summary>
        public PropertyValidatorBuilder<TModel, TString> InEnum(
            Type enumType,
            StringComparison stringComparison,
            Action<PropertyValidatorOptions>? configure = null
        )
        {
            ArgumentNullException.ThrowIfNull(enumType);
            return builder.Add(new EnumStringValidator<TModel>(enumType, stringComparison), configure);
        }

        #region Length Validation

        /// <summary>
        /// String length must satisfy <c>min &lt;= length &lt;= max</c>. Throws when a bound is negative or <paramref name="max" /> is below
        /// <paramref name="min" />; for no upper bound, use <c>MinimumLength</c>.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TString> Length(
            int min,
            int max,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Add(new LengthValidator<TModel>(min, max), configure);

        /// <summary>
        /// String length must satisfy <c>min &lt;= length &lt;= max</c>, with bounds resolved from the model at validation time. A validation that reads a
        /// negative bound, or a maximum below the minimum, throws <see cref="InvalidOperationException" />.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TString> Length(
            Func<TModel, int> min,
            Func<TModel, int> max,
            Action<PropertyValidatorOptions>? configure = null
        )
        {
            ArgumentNullException.ThrowIfNull(min);
            ArgumentNullException.ThrowIfNull(max);
            return builder.Add(new LengthValidator<TModel>(min, max), configure);
        }

        /// <summary>String length must equal <paramref name="length" />.</summary>
        public PropertyValidatorBuilder<TModel, TString> Length(int length, Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new ExactLengthValidator<TModel>(length), configure);

        /// <summary>String length must equal the value returned by <paramref name="func" /> at validation time.</summary>
        public PropertyValidatorBuilder<TModel, TString> Length(Func<TModel, int> func, Action<PropertyValidatorOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(func);
            return builder.Add(new ExactLengthValidator<TModel>(func), configure);
        }

        /// <summary>String length must be at most <paramref name="length" />.</summary>
        public PropertyValidatorBuilder<TModel, TString> MaximumLength(int length, Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new MaximumLengthValidator<TModel>(length), configure);

        /// <summary>String length must be at most the value returned by <paramref name="func" /> at validation time.</summary>
        public PropertyValidatorBuilder<TModel, TString> MaximumLength(Func<TModel, int> func, Action<PropertyValidatorOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(func);
            return builder.Add(new MaximumLengthValidator<TModel>(func), configure);
        }

        /// <summary>String length must be at least <paramref name="length" />.</summary>
        public PropertyValidatorBuilder<TModel, TString> MinimumLength(int length, Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new MinimumLengthValidator<TModel>(length), configure);

        /// <summary>String length must be at least the value returned by <paramref name="func" /> at validation time.</summary>
        public PropertyValidatorBuilder<TModel, TString> MinimumLength(Func<TModel, int> func, Action<PropertyValidatorOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(func);
            return builder.Add(new MinimumLengthValidator<TModel>(func), configure);
        }

        #endregion

        #region Regex Validation

        /// <summary>String must match the given regular expression.</summary>
        public PropertyValidatorBuilder<TModel, TString> Regex(Regex regex, Action<PropertyValidatorOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(regex);
            return builder.Add(new RegexValidator<TModel>(regex), configure);
        }

        /// <summary>String must match the regular expression built from <paramref name="regexString" />.</summary>
        public PropertyValidatorBuilder<TModel, TString> Regex(
            string regexString,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Add(new RegexValidator<TModel>(regexString, RegexOptions.None), configure);

        /// <summary>String must match the regular expression built from <paramref name="regexString" /> and <paramref name="regexOptions" />.</summary>
        public PropertyValidatorBuilder<TModel, TString> Regex(
            string regexString,
            RegexOptions regexOptions,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Add(new RegexValidator<TModel>(regexString, regexOptions), configure);

        /// <summary>String must match the given regular expression with a custom match timeout.</summary>
        public PropertyValidatorBuilder<TModel, TString> Regex(
            string regexString,
            RegexOptions regexOptions,
            TimeSpan matchTimeout,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Add(new RegexValidator<TModel>(regexString, regexOptions, matchTimeout), configure);

        /// <summary>String must match the regular expression returned by <paramref name="func" />.</summary>
        public PropertyValidatorBuilder<TModel, TString> Regex(Func<TModel, Regex?> func, Action<PropertyValidatorOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(func);
            return builder.Add(new RegexValidator<TModel>(func), configure);
        }

        /// <summary>String must match the regex returned by <paramref name="func" />. Compiled patterns are cached.</summary>
        public PropertyValidatorBuilder<TModel, TString> Regex(
            Func<TModel, string?> func,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Regex(func, RegexOptions.None, configure);

        /// <summary>
        /// String must match the regex built from what <paramref name="func" /> returns and <paramref name="regexOptions" />. Compiled patterns are cached.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TString> Regex(
            Func<TModel, string?> func,
            RegexOptions regexOptions,
            Action<PropertyValidatorOptions>? configure = null
        )
        {
            ArgumentNullException.ThrowIfNull(func);
            return builder.Add(new RegexValidator<TModel>(func, regexOptions), configure);
        }

        /// <summary>String must match the regex returned by <paramref name="func" /> with a custom match timeout. Compiled patterns are cached.</summary>
        public PropertyValidatorBuilder<TModel, TString> Regex(
            Func<TModel, string?> func,
            RegexOptions regexOptions,
            TimeSpan matchTimeout,
            Action<PropertyValidatorOptions>? configure = null
        )
        {
            ArgumentNullException.ThrowIfNull(func);
            return builder.Add(new RegexValidator<TModel>(func, regexOptions, matchTimeout), configure);
        }

        #endregion

        #region URI

        /// <summary>String must be an absolute http/https URL.</summary>
        public PropertyValidatorBuilder<TModel, TString> Url(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(UriValidator<TModel>.ForUrl(), configure);

        /// <summary>String must be an absolute URI. Any scheme is accepted.</summary>
        public PropertyValidatorBuilder<TModel, TString> Uri(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new UriValidator<TModel>(), configure);

        /// <summary>String must be a URI of the given <paramref name="kind" />. Any scheme is accepted.</summary>
        public PropertyValidatorBuilder<TModel, TString> Uri(UriKind kind, Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new UriValidator<TModel>(kind), configure);

        /// <summary>String must be a URI of the given <paramref name="kind" /> with one of the <paramref name="allowedSchemes" />.</summary>
        public PropertyValidatorBuilder<TModel, TString> Uri(
            UriKind kind,
            string[] allowedSchemes,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Add(new UriValidator<TModel>(kind, allowedSchemes), configure);

        #endregion
    }

    extension<TModel>(PropertyValidatorBuilder<TModel, DateTime> builder)
    {
        /// <summary>Date must be strictly before "now", by the clock in <see cref="ValidatorBuilderOptions.TimeProvider" />.</summary>
        public PropertyValidatorBuilder<TModel, DateTime> InPast(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new InPastValidator<TModel>(), configure);

        /// <summary>Date must be strictly after "now", by the clock in <see cref="ValidatorBuilderOptions.TimeProvider" />.</summary>
        public PropertyValidatorBuilder<TModel, DateTime> InFuture(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new InFutureValidator<TModel>(), configure);

        /// <summary>Date portion of the value must equal "today", by the clock in <see cref="ValidatorBuilderOptions.TimeProvider" />.</summary>
        public PropertyValidatorBuilder<TModel, DateTime> IsToday(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new IsTodayValidator<TModel>(), configure);
    }

    extension<TModel>(PropertyValidatorBuilder<TModel, DateTime?> builder)
    {
        /// <summary>
        /// If the date has a value, it must be strictly before "now", by the clock in <see cref="ValidatorBuilderOptions.TimeProvider" />. Null is skipped.
        /// </summary>
        public PropertyValidatorBuilder<TModel, DateTime?> InPast(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new NullableInPastValidator<TModel>(), configure);

        /// <summary>
        /// If the date has a value, it must be strictly after "now", by the clock in <see cref="ValidatorBuilderOptions.TimeProvider" />. Null is skipped.
        /// </summary>
        public PropertyValidatorBuilder<TModel, DateTime?> InFuture(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new NullableInFutureValidator<TModel>(), configure);

        /// <summary>
        /// If the date has a value, its date portion must equal "today", by the clock in <see cref="ValidatorBuilderOptions.TimeProvider" />. Null is skipped.
        /// </summary>
        public PropertyValidatorBuilder<TModel, DateTime?> IsToday(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new NullableIsTodayValidator<TModel>(), configure);
    }

    extension<TModel>(PropertyValidatorBuilder<TModel, DateTimeOffset> builder)
    {
        /// <summary>Value must be strictly before "now", compared as an instant, by the clock in <see cref="ValidatorBuilderOptions.TimeProvider" />.</summary>
        public PropertyValidatorBuilder<TModel, DateTimeOffset> InPast(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new DateTimeOffsetInPastValidator<TModel>(), configure);

        /// <summary>Value must be strictly after "now", compared as an instant, by the clock in <see cref="ValidatorBuilderOptions.TimeProvider" />.</summary>
        public PropertyValidatorBuilder<TModel, DateTimeOffset> InFuture(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new DateTimeOffsetInFutureValidator<TModel>(), configure);

        /// <summary>
        /// Date must equal "today" by the calendar of the value's own offset, by the clock in <see cref="ValidatorBuilderOptions.TimeProvider" />.
        /// </summary>
        public PropertyValidatorBuilder<TModel, DateTimeOffset> IsToday(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new DateTimeOffsetIsTodayValidator<TModel>(), configure);
    }

    extension<TModel>(PropertyValidatorBuilder<TModel, DateTimeOffset?> builder)
    {
        /// <summary>
        /// If the value is set, it must be strictly before "now", compared as an instant, by the clock in <see cref="ValidatorBuilderOptions.TimeProvider" />.
        /// Null is skipped.
        /// </summary>
        public PropertyValidatorBuilder<TModel, DateTimeOffset?> InPast(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new NullableDateTimeOffsetInPastValidator<TModel>(), configure);

        /// <summary>
        /// If the value is set, it must be strictly after "now", compared as an instant, by the clock in <see cref="ValidatorBuilderOptions.TimeProvider" />.
        /// Null is skipped.
        /// </summary>
        public PropertyValidatorBuilder<TModel, DateTimeOffset?> InFuture(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new NullableDateTimeOffsetInFutureValidator<TModel>(), configure);

        /// <summary>
        /// If the value is set, its date must equal "today" by the calendar of its own offset, by the clock in
        /// <see cref="ValidatorBuilderOptions.TimeProvider" />. Null is skipped.
        /// </summary>
        public PropertyValidatorBuilder<TModel, DateTimeOffset?> IsToday(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new NullableDateTimeOffsetIsTodayValidator<TModel>(), configure);
    }

    extension<TModel>(PropertyValidatorBuilder<TModel, DateOnly> builder)
    {
        /// <summary>Date must be before today's local date, by the clock in <see cref="ValidatorBuilderOptions.TimeProvider" />.</summary>
        public PropertyValidatorBuilder<TModel, DateOnly> InPast(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new DateOnlyInPastValidator<TModel>(), configure);

        /// <summary>Date must be after today's local date, by the clock in <see cref="ValidatorBuilderOptions.TimeProvider" />.</summary>
        public PropertyValidatorBuilder<TModel, DateOnly> InFuture(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new DateOnlyInFutureValidator<TModel>(), configure);

        /// <summary>Date must equal today's local date, by the clock in <see cref="ValidatorBuilderOptions.TimeProvider" />.</summary>
        public PropertyValidatorBuilder<TModel, DateOnly> IsToday(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new DateOnlyIsTodayValidator<TModel>(), configure);
    }

    extension<TModel>(PropertyValidatorBuilder<TModel, DateOnly?> builder)
    {
        /// <summary>
        /// If the date is set, it must be before today's local date, by the clock in <see cref="ValidatorBuilderOptions.TimeProvider" />. Null is skipped.
        /// </summary>
        public PropertyValidatorBuilder<TModel, DateOnly?> InPast(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new NullableDateOnlyInPastValidator<TModel>(), configure);

        /// <summary>
        /// If the date is set, it must be after today's local date, by the clock in <see cref="ValidatorBuilderOptions.TimeProvider" />. Null is skipped.
        /// </summary>
        public PropertyValidatorBuilder<TModel, DateOnly?> InFuture(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new NullableDateOnlyInFutureValidator<TModel>(), configure);

        /// <summary>
        /// If the date is set, it must equal today's local date, by the clock in <see cref="ValidatorBuilderOptions.TimeProvider" />. Null is skipped.
        /// </summary>
        public PropertyValidatorBuilder<TModel, DateOnly?> IsToday(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add(new NullableDateOnlyIsTodayValidator<TModel>(), configure);
    }

    extension<TModel>(PropertyValidatorBuilder<TModel, decimal> builder)
    {
        /// <summary>Decimal must have at most <paramref name="precision" /> total digits and <paramref name="scale" /> after the decimal point.</summary>
        public PropertyValidatorBuilder<TModel, decimal> PrecisionScale(
            int precision,
            int scale,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Add(new PrecisionScaleValidator<TModel>(precision, scale, false), configure);

        /// <summary>
        /// Decimal must have at most <paramref name="precision" /> total digits and <paramref name="scale" /> after the decimal point. With
        /// <paramref name="ignoreTrailingZeros" />, trailing zeros after the decimal point don't count.
        /// </summary>
        public PropertyValidatorBuilder<TModel, decimal> PrecisionScale(
            int precision,
            int scale,
            bool ignoreTrailingZeros,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Add(new PrecisionScaleValidator<TModel>(precision, scale, ignoreTrailingZeros), configure);
    }

    extension<TModel>(PropertyValidatorBuilder<TModel, decimal?> builder)
    {
        /// <summary>
        /// If the decimal has a value, it must have at most <paramref name="precision" /> total digits and <paramref name="scale" /> after the decimal point.
        /// Null is skipped.
        /// </summary>
        public PropertyValidatorBuilder<TModel, decimal?> PrecisionScale(
            int precision,
            int scale,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Add(new NullablePrecisionScaleValidator<TModel>(precision, scale, false), configure);

        /// <summary>
        /// If the decimal has a value, it must have at most <paramref name="precision" /> total digits and <paramref name="scale" /> after the decimal point.
        /// Null is skipped. With <paramref name="ignoreTrailingZeros" />, trailing zeros after the decimal point don't count.
        /// </summary>
        public PropertyValidatorBuilder<TModel, decimal?> PrecisionScale(
            int precision,
            int scale,
            bool ignoreTrailingZeros,
            Action<PropertyValidatorOptions>? configure = null
        ) => builder.Add(new NullablePrecisionScaleValidator<TModel>(precision, scale, ignoreTrailingZeros), configure);
    }

    // InEnum is declared for an enum and a nullable enum only, so a call on any other property type doesn't compile.
    extension<TModel, TEnum>(PropertyValidatorBuilder<TModel, TEnum> builder) where TEnum : struct, Enum
    {
        /// <summary>
        /// Value must be a defined member of <typeparamref name="TEnum" />, or for a <see cref="FlagsAttribute" /> enum a combination of its defined flags.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TEnum> InEnum(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add<EnumValidator<TModel, TEnum>>(configure);
    }

    extension<TModel, TEnum>(PropertyValidatorBuilder<TModel, TEnum?> builder) where TEnum : struct, Enum
    {
        /// <summary>
        /// If the value isn't <c>null</c>, it must be a defined member of <typeparamref name="TEnum" />, or for a <see cref="FlagsAttribute" /> enum a
        /// combination of its defined flags.
        /// </summary>
        public PropertyValidatorBuilder<TModel, TEnum?> InEnum(Action<PropertyValidatorOptions>? configure = null) =>
            builder.Add<NullableEnumValidator<TModel, TEnum>>(configure);
    }

    extension<TModel>(IValidator<TModel> validator)
    {
        /// <summary>
        /// Validates and throws a <see cref="ValidationException" /> when the result contains <see cref="ValidationErrorSeverity.Error" />-level failures.
        /// Warnings and information entries alone do not throw, but are included in the exception's <see cref="ValidationException.Errors" />.
        /// </summary>
        public void ValidateAndThrow(TModel model)
        {
            var result = validator.Validate(model);
            if (!result.IsValid) throw new ValidationException([.. result.Errors]);
        }

        /// <summary>Async variant of <see cref="ValidateAndThrow" />.</summary>
        public async ValueTask ValidateAndThrowAsync(TModel model, CancellationToken cancellationToken = default)
        {
            var result = await validator.ValidateAsync(model, cancellationToken).ConfigureAwait(false);
            if (!result.IsValid) throw new ValidationException([.. result.Errors]);
        }
    }

    private static void EnsureStringChain<TString>()
    {
        if (typeof(TString) != typeof(string))
        {
            throw new InvalidOperationException(
                $"A string rule checks a string, and this chain's type is {typeof(TString)}. Declare the property as a string, or check it with Must.");
        }
    }
}