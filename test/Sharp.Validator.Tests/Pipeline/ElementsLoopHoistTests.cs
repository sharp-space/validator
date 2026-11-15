namespace Sharp.Validator.Tests.Pipeline;

public sealed class ElementsLoopHoistTests
{
    [Fact]
    public void ElementsAccessor_RunsOncePerValidationRegardlessOfValidatorCount()
    {
        var validator = new Validator<CountingPerson>(builder => builder.Elements(x => x.Items!)
            .NotEmpty()
            .NotEmpty()
            .NotEmpty());

        var person = new CountingPerson { Items = ["a", "b"] };
        validator.Validate(person);

        Assert.Equal(1, person.Reads);
    }

    private sealed class CountingPerson
    {
        public int Reads;

        public List<string?>? Items
        {
            get
            {
                Reads++;
                return field;
            }
            init;
        }
    }
}