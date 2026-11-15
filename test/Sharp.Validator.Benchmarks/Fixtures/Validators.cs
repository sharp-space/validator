// Linked into both benchmark projects under the consuming project's namespace, so the namespace is not the folder's.

#if COMPARISON_BENCHMARKS
namespace Sharp.Validator.ComparisonBenchmarks;
#else
namespace Sharp.Validator.Benchmarks;
#endif

// The Sharp.Validator rule set of each model in Models.cs.

/// <summary>Flat model rules.</summary>
public sealed class CustomerValidator : Validator<Customer>
{
    public CustomerValidator()
    {
        Builder.Property(x => x.Email).NotEmpty().Email();
        Builder.Property(x => x.Name).NotEmpty().Length(2, 50);
        Builder.Property(x => x.Age).InclusiveBetween(18, 120);
        Builder.Property(x => x.PostalCode).NotEmpty().Regex(@"^\d{5}$");
    }
}

/// <summary>Per-element rules via <c>Elements</c>.</summary>
public sealed class TagBagValidator : Validator<TagBag>
{
    public TagBagValidator() => Builder.Elements(x => x.Tags).NotEmpty().MaximumLength(16);
}

/// <summary>Child validator reused by the nested <see cref="OrderValidator" />.</summary>
public sealed class AddressValidator : Validator<Address>
{
    public AddressValidator()
    {
        Builder.Property(x => x.Street).NotEmpty();
        Builder.Property(x => x.City).NotEmpty();
        Builder.Property(x => x.PostalCode).NotEmpty().Regex(@"^\d{5}$");
    }
}

/// <summary>Composes two child validators over the object graph via <c>Include</c>.</summary>
public sealed class OrderValidator : Validator<Order>
{
    public OrderValidator()
    {
        Builder.Property(x => x.Id).NotEmpty();
        Builder.Property(x => x.Customer).Include(new CustomerValidator());
        Builder.Property(x => x.ShippingAddress).Include(new AddressValidator());
    }
}

/// <summary>An awaited rule via <c>MustAsync</c>; the whole pipeline returns a <see cref="ValueTask{T}" />.</summary>
public sealed class AccountValidator : Validator<Account>
{
    public AccountValidator()
    {
        Builder.Property(x => x.Username)
            .NotEmpty()
            .MustAsync((value, _) => Task.FromResult(value is { Length: >= 3 }));

        Builder.Property(x => x.Email).NotEmpty().Email();
    }
}

/// <summary>Value-type rules: Equal (boxing path) and InEnum over a normal and a flags enum.</summary>
public sealed class SettingsValidator : Validator<Settings>
{
    public SettingsValidator()
    {
        Builder.Property(x => x.Level).Equal(5);
        Builder.Property(x => x.Status).InEnum();
        Builder.Property(x => x.Access).InEnum();
    }
}