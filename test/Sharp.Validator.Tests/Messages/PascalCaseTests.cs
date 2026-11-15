namespace Sharp.Validator.Tests.Messages;

// Regression: PascalCase used to preserve leading separators because the regex consumed only one separator per match.
// "  test" rendered as " Test" rather than "Test".
public static class PascalCaseTests
{
    [Collection(GlobalOptions.Name)]
    public class WithGlobalPolicy
    {
        [Fact]
        public void LeadingSpaces_BeforePascalCasing_AreCollapsed()
        {
            var snapshot = ValidatorBuilderOptions.Global;

            try
            {
                ValidatorBuilderOptions.Global = new ValidatorBuilderOptions
                {
                    PropertyNamePrefix = "  ", // two leading spaces
                    PropertyNamePolicy = PropertyNamePolicy.PascalCase
                };

                var validator = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty());
                var result = validator.Validate(new Person { Name = null });

                // Prefixes are literal external wrappers and are not transformed by the policy.
                Assert.Equal("  Name", result.Errors[0].PropertyName);
            }
            finally
            {
                ValidatorBuilderOptions.Global = snapshot;
            }
        }
    }
}