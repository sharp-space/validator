using System.Collections.Frozen;
using System.Numerics;
using System.Runtime.CompilerServices;

// ReSharper disable StaticMemberInGenericType — cached per closed generic, intentional.

namespace Sharp.Validator.Validators;

/// <summary>
/// Value must be a defined member of its enum type; for <see cref="FlagsAttribute" /> enums, any combination of defined flags is accepted.
/// <c>null</c> is skipped.
/// </summary>
/// <remarks>
/// <c>InEnum()</c> on a nullable enum adds a rule of its own, which accepts the same values and checks a flags value without boxing it. Constructed
/// directly over a nullable enum, this validator boxes.
/// </remarks>
public sealed class EnumValidator<TModel, TProperty> : PropertyValidator<TModel, TProperty>
{
    /// <summary>
    /// Creates the validator. Throws when <typeparamref name="TProperty" /> (or its nullable underlying type) is not an enum over an integer type.
    /// </summary>
    public EnumValidator()
    {
        if (!EnumRange<TProperty>.IsEnum)
        {
            throw new InvalidOperationException(
                $"InEnum() requires an enum type, but '{typeof(TProperty)}' is not an enum.");
        }

        EnumRange<TProperty>.ThrowIfNotOverAnInteger();

        SetDefaults(options =>
        {
            options.ErrorCode = ErrorCodes.InEnum;
            options.ErrorMessage = "'{PropertyName}' has a range of values which does not include '{PropertyValue}'.";
        });
    }

    /// <inheritdoc />
    public override bool Validate(TProperty value, ValidationContext<TModel> context) =>
        value == null || EnumRange<TProperty>.Contains(value);
}

/// <summary>Nullable variant of <see cref="EnumValidator{TModel, TProperty}" />. <c>null</c> is skipped.</summary>
internal sealed class NullableEnumValidator<TModel, TEnum> : PropertyValidator<TModel, TEnum?>
    where TEnum : struct, Enum
{
    /// <summary>Creates the validator. Throws when <typeparamref name="TEnum" /> is not over an integer type.</summary>
    public NullableEnumValidator()
    {
        EnumRange<TEnum>.ThrowIfNotOverAnInteger();

        SetDefaults(options =>
        {
            options.ErrorCode = ErrorCodes.InEnum;
            options.ErrorMessage = "'{PropertyName}' has a range of values which does not include '{PropertyValue}'.";
        });
    }

    // Checks the unwrapped TEnum, whose bits EnumRange reads in place: a TEnum? can only be converted by boxing it.
    /// <inheritdoc />
    public override bool Validate(TEnum? value, ValidationContext<TModel> context) =>
        !value.HasValue || EnumRange<TEnum>.Contains(value.Value);
}

// An enum's defined values and flags depend on the enum alone, so every validator of one enum type shares them,
// whatever its model. Keyed by the enum rather than the validator, the statics are also exact in the validators' code
// shared across reference-type models, where the JIT folds the checks. The underlying-type checks fold under Native AOT
// as well, because they come from typeof(TProperty) instead of statics (see UsesUInt64).
internal static class EnumRange<TProperty>
{
    private static readonly Type EnumType =
        Nullable.GetUnderlyingType(typeof(TProperty)) ?? typeof(TProperty);

    private static readonly bool IsFlags = EnumType.IsEnum &&
        EnumType.IsDefined(typeof(FlagsAttribute), false);

    private static readonly bool IsUInt64Underlying = EnumType.IsEnum &&
        Enum.GetUnderlyingType(EnumType) == typeof(ulong);

    // IL also allows an enum over bool, float or double, which C# doesn't. Its values aren't the integers the checks
    // below compare, so the validators refuse one when they're built, and the initializers read nothing from it. The
    // type codes from Char to UInt64 are char's and the eight integer types'.
    private static readonly bool IsOverAnInteger = EnumType.IsEnum &&
        Type.GetTypeCode(Enum.GetUnderlyingType(EnumType)) is >= TypeCode.Char and <= TypeCode.UInt64;

    // The members' values as ToBits reads a value: sign-extended from a signed underlying type, zero-extended from any
    // other. Read once, AOT-safely: Enum.GetValues(Type) is RequiresDynamicCode.
    private static readonly ulong[] Members = ReadMembers();

    // A non-flags enum whose members all lie within 64 values of the smallest, as nearly every enum's do, is checked
    // against a mask: bit i is set when Minimum + i is a member. That costs a subtraction and a shift, where a
    // FrozenSet looked the value up through a comparer.
    private static readonly ulong Minimum = SmallestMember();

    private static readonly bool UsesMask = !IsFlags && MembersFitAMask();

    private static readonly ulong MemberMask = BuildMemberMask();

    // Any other non-flags enum keeps its members in a set, and so does a Nullable<TEnum>, whose bits can't be read in
    // place. The look-up neither allocates nor boxes.
    private static readonly FrozenSet<TProperty> DefinedValues = BuildDefinedValues();

    // A flags enum nearly always gives every bit it uses a member of its own. Its members then combine into exactly the
    // values with no other bit, which one AND checks. Any other flags enum is checked member by member.
    private static readonly ulong FlagBits = CombineMembers(false);

    private static readonly bool BitsAreMembers = IsFlags && CombineMembers(true) == FlagBits;

    internal static bool IsEnum => EnumType.IsEnum;

    // typeof(TProperty).IsEnum and GetEnumUnderlyingType() fold to constants for an enum under the JIT and Native AOT
    // alike, so Contains keeps one path. Static readonly facts fold only under the JIT, once the class is initialized:
    // Native AOT can't run the reflection behind them at compile time, so it loaded and branched on each one per call.
    // Only a Nullable<TEnum> isn't an enum itself. An EnumValidator<TModel, TEnum?> constructed directly passes one,
    // and its bits can't be reinterpreted (its layout is hasValue + value), so that path keeps the cached fact and the
    // boxing Convert. InEnum() adds NullableEnumValidator instead, which unwraps the value first.
    private static bool UsesUInt64
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => typeof(TProperty).IsEnum
            ? typeof(TProperty).GetEnumUnderlyingType() == typeof(ulong)
            : IsUInt64Underlying;
    }

    internal static void ThrowIfNotOverAnInteger()
    {
        if (!IsOverAnInteger)
        {
            throw new InvalidOperationException(
                $"InEnum() requires an enum over an integer type, but '{EnumType}' is over " +
                $"'{Enum.GetUnderlyingType(EnumType)}'.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    // The validators skip null before they ask, so value is never null here.
    internal static bool Contains(TProperty value)
    {
        if (IsFlags)
        {
            var bits = ToBits(value);
            if (BitsAreMembers) return (bits & ~FlagBits) == 0;

            return bits == 0 || CombinesMembers(bits);
        }

        if (!UsesMask || !typeof(TProperty).IsEnum) return DefinedValues.Contains(value);

        var offset = ToBits(value) - Minimum;
        return offset < 64 && ((MemberMask >> (int)offset) & 1) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ToBits(TProperty value) => UsesUInt64 ? ToUnsigned(value) : unchecked((ulong)ToSigned(value));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    // Reinterprets the enum's bits without boxing: for a generic TProperty, Convert.To(U)Int64 binds to the object
    // overload, which boxes on every call and dispatches through IConvertible. The extension must match ReadMembers'
    // Convert.ToInt64, which sign-extends a signed underlying type and zero-extends any other. Reading by size rather
    // than by type also covers an enum over char, which IL allows and C# doesn't. Folding leaves one read.
    private static long ToSigned(TProperty value)
    {
        if (!typeof(TProperty).IsEnum) return Convert.ToInt64(value);

        var underlying = typeof(TProperty).GetEnumUnderlyingType();
        var signed = underlying == typeof(sbyte) || underlying == typeof(short) || underlying == typeof(int)
            || underlying == typeof(long);

        return Unsafe.SizeOf<TProperty>() switch
        {
            1 => signed ? Unsafe.As<TProperty, sbyte>(ref value) : Unsafe.As<TProperty, byte>(ref value),
            2 => signed ? Unsafe.As<TProperty, short>(ref value) : Unsafe.As<TProperty, ushort>(ref value),
            4 => signed ? Unsafe.As<TProperty, int>(ref value) : Unsafe.As<TProperty, uint>(ref value),
            _ => Unsafe.As<TProperty, long>(ref value)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ToUnsigned(TProperty value) =>
        typeof(TProperty).IsEnum ? Unsafe.As<TProperty, ulong>(ref value) : Convert.ToUInt64(value);

    // Whether value is a union of members. The checks are bitwise, so sign-extended values compare as they would in
    // the underlying type.
    private static bool CombinesMembers(ulong value)
    {
        ulong union = 0;
        foreach (var member in Members)
        {
            if ((member & value) != member) continue;

            union |= member;
            if (union == value) return true;
        }

        return false;
    }

    // The static initializers below are guarded so a non-enum TProperty surfaces as the friendly
    // InvalidOperationException thrown by EnumValidator's constructor, not a TypeInitializationException from here.
    private static ulong[] ReadMembers()
    {
        if (!IsOverAnInteger) return [];

        // UInt64 values >= 2^63 overflow Convert.ToInt64, which is a checked conversion.
        var underlying = Enum.GetValuesAsUnderlyingType(EnumType);
        var members = new ulong[underlying.Length];
        for (var index = 0; index < members.Length; index++)
        {
            var member = underlying.GetValue(index)!;
            members[index] = IsUInt64Underlying ? (ulong)member : unchecked((ulong)Convert.ToInt64(member));
        }

        return members;
    }

    // Flipping a signed value's sign bit orders it as an unsigned one, and it changes no difference between two values,
    // so a member's offset from the smallest is a plain subtraction.
    private static ulong OrderKey(ulong member) => IsUInt64Underlying ? member : member ^ (1UL << 63);

    private static ulong SmallestMember()
    {
        var smallest = ulong.MaxValue;
        foreach (var member in Members) smallest = Math.Min(smallest, OrderKey(member));

        return OrderKey(smallest);
    }

    private static bool MembersFitAMask()
    {
        ulong largestOffset = 0;
        foreach (var member in Members) largestOffset = Math.Max(largestOffset, member - Minimum);

        return largestOffset < 64;
    }

    private static ulong BuildMemberMask()
    {
        if (!UsesMask) return 0;

        ulong mask = 0;
        foreach (var member in Members) mask |= 1UL << (int)(member - Minimum);

        return mask;
    }

    private static FrozenSet<TProperty> BuildDefinedValues()
    {
        if (!IsOverAnInteger || IsFlags || (UsesMask && typeof(TProperty).IsEnum)) return FrozenSet<TProperty>.Empty;

        var underlying = Enum.GetValuesAsUnderlyingType(EnumType);
        var set = new HashSet<TProperty>(underlying.Length);
        foreach (var rawValue in underlying)
            set.Add((TProperty)Enum.ToObject(EnumType, rawValue!));

        return set.ToFrozenSet();
    }

    private static ulong CombineMembers(bool singleBitsOnly)
    {
        ulong bits = 0;
        foreach (var member in Members)
            if (!singleBitsOnly || BitOperations.IsPow2(member))
                bits |= member;

        return bits;
    }
}

/// <summary>
/// String must match one of the enum's member names under the configured <see cref="StringComparison" />. <c>null</c>/empty is skipped.
/// </summary>
internal sealed class EnumStringValidator<TModel> : PropertyValidator<TModel, string?>
{
    // CurrentCulture[IgnoreCase] resolve against the ambient culture, which can differ between the validator's
    // construction (e.g. a DI singleton) and a Validate() call (e.g. per-request localization). Baking those into a
    // comparer via StringComparer.FromComparison would freeze the construction-time culture, so they keep the raw names
    // and re-compare per call — still allocation-free (a plain loop, not a `.Any(lambda)` closure).
    private readonly string[]? _cultureSensitiveNames;

    // Culture-stable comparisons (Ordinal*, Invariant*) are baked into an O(1) FrozenSet once.
    private readonly FrozenSet<string>? _enumNames;
    private readonly StringComparison _stringComparison;

    /// <summary>
    /// Creates the validator for <paramref name="enumType" />'s names, compared with <paramref name="stringComparison" />. Throws when the type is
    /// not an enum.
    /// </summary>
    public EnumStringValidator(Type enumType, StringComparison stringComparison)
    {
        ArgumentNullException.ThrowIfNull(enumType);
        if (!enumType.IsEnum)
        {
            var message = $"The type '{enumType.Name}' is not an enum and can't be used with InEnum.";
            throw new ArgumentOutOfRangeException(nameof(enumType), message);
        }

        _stringComparison = stringComparison;
        var names = Enum.GetNames(enumType);
        if (stringComparison is StringComparison.CurrentCulture or StringComparison.CurrentCultureIgnoreCase)
            _cultureSensitiveNames = names;
        else _enumNames = names.ToFrozenSet(StringComparer.FromComparison(stringComparison));

        SetDefaults(options =>
        {
            options.ErrorCode = ErrorCodes.InEnum;
            options.ErrorMessage = "'{PropertyName}' has a range of values which does not include '{PropertyValue}'.";
        });
    }

    /// <inheritdoc />
    public override bool Validate(string? value, ValidationContext<TModel> context)
    {
        // IsNullOrEmpty (not IsNullOrWhiteSpace): whitespace-only input is validated — and fails — like every other
        // string validator in the library ("null and empty skip" policy).
        if (string.IsNullOrEmpty(value)) return true;
        if (_enumNames != null) return _enumNames.Contains(value);

        var names = _cultureSensitiveNames!;
        for (var index = 0; index < names.Length; index++)
            if (string.Equals(names[index], value, _stringComparison))
                return true;

        return false;
    }
}