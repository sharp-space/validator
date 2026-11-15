using System.ComponentModel.DataAnnotations;

// ReSharper disable once CheckNamespace — each external library's types get a namespace of their own.
namespace Sharp.Validator.ComparisonBenchmarks.DataAnnotations;

using DataAnnotationsValidator = System.ComponentModel.DataAnnotations.Validator;

/// <summary>Attribute-annotated equivalent of the customer model, mirroring the Sharp/FluentValidation rule set.</summary>
public sealed class Customer
{
    [Required]
    [EmailAddress]
    public string Email { get; init; } = "";

    [Required]
    [StringLength(50, MinimumLength = 2)]
    public string Name { get; init; } = "";

    [Range(18, 120)]
    public int Age { get; init; }

    [Required]
    [RegularExpression(@"^\d{5}$")]
    public string PostalCode { get; init; } = "";
}

/// <summary>Wraps the in-box <see cref="DataAnnotationsValidator" /> so call sites don't pull the colliding type name into scope.</summary>
public static class DataAnnotationsRunner
{
    /// <summary>Validates <paramref name="model" /> and returns the error count (0 when valid).</summary>
    public static int Validate(Customer model)
    {
        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();
        DataAnnotationsValidator.TryValidateObject(model, context, results, true);
        return results.Count;
    }
}