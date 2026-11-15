using FluentValidation;

// ReSharper disable once CheckNamespace — each external library's types get a namespace of their own.
namespace Sharp.Validator.ComparisonBenchmarks.FluentValidation;

/// <summary>Flat model rules, mirroring the Sharp rule set over the same POCO.</summary>
public sealed class CustomerValidator : AbstractValidator<Customer>
{
    public CustomerValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Name).NotEmpty().Length(2, 50);
        RuleFor(x => x.Age).InclusiveBetween(18, 120);
        RuleFor(x => x.PostalCode).NotEmpty().Matches(@"^\d{5}$");
    }
}

/// <summary>Per-element rules via <c>RuleForEach</c>.</summary>
public sealed class TagBagValidator : AbstractValidator<TagBag>
{
    public TagBagValidator() =>
        RuleForEach(x => x.Tags).NotEmpty().MaximumLength(16);
}

/// <summary>Child validator reused by the nested <see cref="OrderValidator" />.</summary>
public sealed class AddressValidator : AbstractValidator<Address>
{
    public AddressValidator()
    {
        RuleFor(x => x.Street).NotEmpty();
        RuleFor(x => x.City).NotEmpty();
        RuleFor(x => x.PostalCode).NotEmpty().Matches(@"^\d{5}$");
    }
}

/// <summary>Composes two child validators over the object graph via <c>SetValidator</c>.</summary>
public sealed class OrderValidator : AbstractValidator<Order>
{
    public OrderValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Customer).SetValidator(new CustomerValidator());
        RuleFor(x => x.ShippingAddress).SetValidator(new AddressValidator());
    }
}

/// <summary>An awaited rule via <c>MustAsync</c>; the whole pipeline returns a <see cref="Task{T}" />.</summary>
public sealed class AccountValidator : AbstractValidator<Account>
{
    public AccountValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .MustAsync((value, _) => Task.FromResult(value.Length >= 3));

        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}