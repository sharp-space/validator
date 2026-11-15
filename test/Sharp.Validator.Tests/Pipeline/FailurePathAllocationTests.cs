namespace Sharp.Validator.Tests.Pipeline;

// Regression: every failure built message parameters its template never read, formatted a fixed message again, and
// recorded the chain in a HashSet that only the async phase consults, 300 to 700 bytes per failing rule on top of the
// error itself. A sync rule with a fixed message now costs one list entry, whatever the rule.
public sealed class FailurePathAllocationTests
{
    private static readonly Person Invalid = new() { Name = "x", Age = 1, Email = "bad", Password = "q" };

    private static readonly Validator<Address> AsyncAddress =
        new(x => x.Property(address => address.City).MustAsync((_, _) => Task.FromResult(true)));

    private static readonly Validator<Line> AsyncLine =
        new(x => x.Property(line => line.Address).MustAsync((_, _) => Task.FromResult(true)));

    private static readonly Order OneOfEach =
        new() { Codes = ["a"], Lines = [new Line { Address = new Address { City = "x" } }] };

    // Regression: a sync rule's error in an async chain recorded the chain too, though only a chain with Stop reads the
    // record, to skip its async rules. Under Continue, the default, the chain's async rules run anyway, so its error
    // now costs what its warning costs.
    private static readonly Validator<Address> SyncAddress = new(x => x.Property(address => address.City).NotEmpty());

    private static long AllocatedBytes(Validator<Person> validator, Person model)
    {
        validator.Validate(model);
        var before = GC.GetAllocatedBytesForCurrentThread();
        validator.Validate(model);

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static long Baseline() =>
        AllocatedBytes(new Validator<Person>(x => x.Property(person => person.Password).NotNull().Null()), Invalid);

    [Fact]
    public void Length_BuildsNoParametersForAFixedMessage() =>
        Assert.Equal(Baseline(), AllocatedBytes(new Validator<Person>(x => x.Property(person => person.Name).Length(2, 5)), Invalid));

    [Fact]
    public void Regex_BuildsNoParametersForAFixedMessage() =>
        Assert.Equal(Baseline(), AllocatedBytes(new Validator<Person>(x => x.Property(person => person.Name).Regex("^y$")), Invalid));

    [Fact]
    // A string target on purpose: without optimizations, the null check on a generic value type boxes it.
    public void FixedComparisonTarget_BuildsNoParameters() =>
        Assert.Equal(Baseline(), AllocatedBytes(new Validator<Person>(x => x.Property(person => person.Name).GreaterThan("y")), Invalid));

    [Fact]
    public void FixedEqualityTarget_BuildsNoParameters() =>
        Assert.Equal(Baseline(), AllocatedBytes(new Validator<Person>(x => x.Property(person => person.Name).Equal("y")), Invalid));

    [Fact]
    public void SeveralFailingChains_AddListGrowthOnly()
    {
        var one = new Validator<Person>(x => x.Property(person => person.Name).NotEmpty().Length(2, 5));
        var three = new Validator<Person>(x =>
        {
            x.Property(person => person.Name).Length(2, 5);
            x.Property(person => person.Email).Length(5, 9);
            x.Property(person => person.Password).Length(5, 9);
        });

        // Three entries fit the list's first array, so nothing grows with the number of chains.
        Assert.Equal(AllocatedBytes(one, Invalid), AllocatedBytes(three, Invalid));
    }

    private static async Task<long> AllocatedBytesAsync<TModel>(Validator<TModel> validator, TModel model)
    {
        await validator.ValidateAsync(model);
        var before = GC.GetAllocatedBytesForCurrentThread();
        await validator.ValidateAsync(model);

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    // Regression: an async rule's error recorded its chain as failed, in the context's extras and a HashSet, 224 bytes,
    // though the async phase reads that record only before a chain's async rules start. A warning never recorded
    // anything, so an error now costs what a warning costs. The rules complete synchronously, so the whole validation
    // runs on this thread.
    private static async Task AssertErrorCostsWhatAWarningCosts<TModel>(
        Func<ValidationErrorSeverity, Validator<TModel>> create,
        TModel model
    ) => Assert.Equal(
        await AllocatedBytesAsync(create(ValidationErrorSeverity.Warning), model),
        await AllocatedBytesAsync(create(ValidationErrorSeverity.Error), model));

    [Fact]
    public Task AsyncPropertyRule_ErrorCostsWhatAWarningCosts() =>
        AssertErrorCostsWhatAWarningCosts(
            severity => new Validator<Person>(x => x.Property(person => person.Name)
                .MustAsync((_, _) => Task.FromResult(false), options => options.ErrorSeverity = severity)),
            Invalid);

    [Fact]
    public Task AsyncPropertyRuleAfterAnAsyncInclude_ErrorCostsWhatAWarningCosts() =>
        AssertErrorCostsWhatAWarningCosts(
            severity => new Validator<Person>(x => x.Property(person => person.Address).Include(AsyncAddress)
                .MustAsync((_, _) => Task.FromResult(false), options => options.ErrorSeverity = severity)),
            new Person { Address = new Address { City = "x" } });

    [Fact]
    public Task AsyncElementRule_ErrorCostsWhatAWarningCosts() =>
        AssertErrorCostsWhatAWarningCosts(
            severity => new Validator<Order>(x => x.Elements(order => order.Lines)
                .MustAsync((_, _) => Task.FromResult(false), options => options.ErrorSeverity = severity)),
            OneOfEach);

    [Fact]
    public Task AsyncElementRuleOverASet_ErrorCostsWhatAWarningCosts() =>
        AssertErrorCostsWhatAWarningCosts(
            severity => new Validator<Order>(x => x.Elements(order => order.Codes)
                .MustAsync((_, _) => Task.FromResult(false), options => options.ErrorSeverity = severity)),
            OneOfEach);

    [Fact]
    public Task AsyncElementRuleAfterAnAsyncInclude_ErrorCostsWhatAWarningCosts() =>
        AssertErrorCostsWhatAWarningCosts(
            severity => new Validator<Order>(x => x.Elements(order => order.Lines).Include(AsyncLine)
                .MustAsync((_, _) => Task.FromResult(false), options => options.ErrorSeverity = severity)),
            OneOfEach);

    [Fact]
    public Task AsyncElementPropertyRule_ErrorCostsWhatAWarningCosts() =>
        AssertErrorCostsWhatAWarningCosts(
            severity => new Validator<Order>(x => x.Elements(order => order.Lines).Property(line => line.Address)
                .MustAsync((_, _) => Task.FromResult(false), options => options.ErrorSeverity = severity)),
            OneOfEach);

    [Fact]
    public Task AsyncElementPropertyRuleAfterAnAsyncInclude_ErrorCostsWhatAWarningCosts() =>
        AssertErrorCostsWhatAWarningCosts(
            severity => new Validator<Order>(x => x.Elements(order => order.Lines).Property(line => line.Address)
                .Include(AsyncAddress)
                .MustAsync((_, _) => Task.FromResult(false), options => options.ErrorSeverity = severity)),
            OneOfEach);

    [Fact]
    public Task SyncRuleInAnAsyncChain_ErrorCostsWhatAWarningCosts() =>
        AssertErrorCostsWhatAWarningCosts(
            severity => new Validator<Person>(x => x.Property(person => person.Name)
                .Must(_ => false, options => options.ErrorSeverity = severity)
                .MustAsync((_, _) => Task.FromResult(true))),
            Invalid);

    [Fact]
    public Task SyncRuleAfterAnIncludeInAnAsyncChain_ErrorCostsWhatAWarningCosts() =>
        AssertErrorCostsWhatAWarningCosts(
            severity => new Validator<Person>(x => x.Property(person => person.Address).Include(SyncAddress)
                .Must(_ => false, options => options.ErrorSeverity = severity)
                .MustAsync((_, _) => Task.FromResult(true))),
            new Person { Address = new Address { City = "x" } });

    [Fact]
    public Task SyncElementRuleInAnAsyncChain_ErrorCostsWhatAWarningCosts() =>
        AssertErrorCostsWhatAWarningCosts(
            severity => new Validator<Order>(x => x.Elements(order => order.Lines)
                .Must(_ => false, options => options.ErrorSeverity = severity)
                .MustAsync((_, _) => Task.FromResult(true))),
            OneOfEach);

    [Fact]
    public Task SyncElementPropertyRuleInAnAsyncChain_ErrorCostsWhatAWarningCosts() =>
        AssertErrorCostsWhatAWarningCosts(
            severity => new Validator<Order>(x => x.Elements(order => order.Lines).Property(line => line.Address)
                .Must(_ => false, options => options.ErrorSeverity = severity)
                .MustAsync((_, _) => Task.FromResult(true))),
            OneOfEach);

    [Fact]
    // A chain with Stop but no async rules records nothing: only an async chain asks for the record.
    public void SyncOnlyChainWithStop_ErrorCostsWhatAWarningCosts()
    {
        static Validator<Person> Create(ValidationErrorSeverity severity) =>
            new(x => x.Property(person => person.Name, options => options.CascadeMode = ValidationCascadeMode.Stop)
                .Must(_ => false, options => options.ErrorSeverity = severity));

        Assert.Equal(
            AllocatedBytes(Create(ValidationErrorSeverity.Warning), Invalid),
            AllocatedBytes(Create(ValidationErrorSeverity.Error), Invalid));
    }

    [Fact]
    // Regression: with Stop on the validator, a chain that sets no mode of its own stops at a failure too, so its sync
    // error recorded the chain, though the run ends at that error before the async phase could read the record. Both
    // runs here end there, so the chain costs what a chain that continues costs.
    public async Task SyncRuleInAnAsyncChain_UnderAValidatorStop_CostsWhatAContinuingChainCosts()
    {
        static Validator<Person> Create(ValidationCascadeMode? chainMode)
        {
            var validator = new Validator<Person>(x => x
                .Property(person => person.Name, options =>
                {
                    if (chainMode is { } mode) options.CascadeMode = mode;
                })
                .Must(_ => false)
                .MustAsync((_, _) => Task.FromResult(true)));

            validator.Configure(options => options.CascadeMode = ValidationCascadeMode.Stop);
            return validator;
        }

        Assert.Equal(
            await AllocatedBytesAsync(Create(ValidationCascadeMode.Continue), Invalid),
            await AllocatedBytesAsync(Create(null), Invalid));
    }

    private sealed class Order
    {
        public HashSet<string>? Codes { get; init; }

        public List<Line>? Lines { get; init; }
    }

    private sealed class Line
    {
        public Address? Address { get; init; }
    }
}