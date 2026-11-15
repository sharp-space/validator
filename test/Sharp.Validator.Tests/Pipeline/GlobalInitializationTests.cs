namespace Sharp.Validator.Tests.Pipeline;

[Collection(GlobalOptions.Name)]
public sealed class GlobalInitializationTests
{
    [Fact]
    public void Initialization_WhenAnotherInitializerArrives_KeepsTheWinningOptions()
    {
        var previous = ValidatorBuilderOptions.InternalGlobal;

        try
        {
            ValidatorBuilderOptions.InternalGlobal = null;
            var winner = new ValidatorBuilderOptions();
            Assert.Same(winner, ValidatorBuilderOptions.InitializeGlobal(winner));
            Assert.Same(winner, ValidatorBuilderOptions.InitializeGlobal(new()));
        }
        finally
        {
            ValidatorBuilderOptions.InternalGlobal = previous;
        }
    }

    [Fact]
    // A freeze read Global only if someone had created it, so in an app that set Global after a validator had frozen,
    // the setting reached the validators that froze later and silently missed that one. A freeze creates Global if
    // needed and makes it read-only, so the late setting throws; new options assigned to Global still serve later ones.
    public void Global_ThatNoOneReadBeforeAFreeze_IsReadOnlyAfterIt()
    {
        var previous = ValidatorBuilderOptions.InternalGlobal;

        try
        {
            ValidatorBuilderOptions.InternalGlobal = null;
            new Validator<Person>(x => x.Property(person => person.Name).NotEmpty()).Validate(new Person());

            Assert.Throws<InvalidOperationException>(() => ValidatorBuilderOptions.Global.PropertyNamePrefix = "late.");

            ValidatorBuilderOptions.Global = new ValidatorBuilderOptions { PropertyNamePrefix = "new." };

            var later = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty()).Validate(new Person());
            Assert.Equal("new.Name", Assert.Single(later.Errors).PropertyName);
        }
        finally
        {
            ValidatorBuilderOptions.InternalGlobal = previous;
        }
    }
}