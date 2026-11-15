namespace Sharp.Validator.Tests.Pipeline;

public sealed class IncludeCompositionContractTests
{
    [Fact]
    public void RejectedAsyncMutation_DoesNotPoisonFrozenSyncValidator()
    {
        var validator = new ExposedParentValidator();
        var model = ValidParent();
        Assert.True(validator.Validate(model).IsValid);
        Assert.Throws<InvalidOperationException>(validator.AddAsyncRule);
        Assert.True(validator.Validate(model).IsValid);
    }

    [Fact]
    public void RejectedInclude_DoesNotRunConfigurationOrFreezeChild()
    {
        var parent = new ExposedParentValidator();
        var child = new ExposedChildValidator();
        var configured = false;
        parent.Validate(ValidParent());
        Assert.Throws<InvalidOperationException>(() =>
            parent.AddInclude(child, _ => configured = true));

        Assert.False(configured);

        child.AddRule();

        Assert.True(parent.Validate(ValidParent()).IsValid);
    }

    [Fact]
    public void RejectedDerivedInclude_DoesNotRunConfigurationOrFreezeChild()
    {
        var parent = new ExposedParentValidator();
        var child = new ExposedDogValidator();
        var configured = false;
        parent.Validate(ValidParent());
        Assert.Throws<InvalidOperationException>(() =>
            parent.AddDerivedInclude(child, _ => configured = true));

        Assert.False(configured);

        child.AddRule();

        Assert.True(parent.Validate(ValidParent()).IsValid);
    }

    [Fact]
    public void Include_FreezesEveryChildMutationPathAtCompositionTime()
    {
        var child = new ExposedChildValidator();
        var parent = new ExposedParentValidator();
        parent.AddInclude(child);

        Assert.Throws<InvalidOperationException>(() => child.Configure(_ => { }));
        Assert.Throws<InvalidOperationException>(child.AddRule);
        Assert.Throws<InvalidOperationException>(child.AddWhere);
        Assert.Throws<InvalidOperationException>(child.AddBlockWhere);
        Assert.Throws<InvalidOperationException>(child.AddAsyncRule);
        Assert.False(parent.Validate(new Parent { Child = new Child() }).IsValid);
    }

    [Fact]
    public void DerivedInclude_FreezesChildAtCompositionTime()
    {
        var child = new ExposedDogValidator();
        var parent = new ExposedParentValidator();
        parent.AddDerivedInclude(child);

        Assert.Throws<InvalidOperationException>(child.AddRule);
        Assert.False(parent.Validate(new Parent { Pet = new Dog() }).IsValid);
    }

    [Fact]
    public async Task AsyncChild_WhenIncluded_IsStablyClassified()
    {
        var child = new ExposedChildValidator(true);
        var parent = new ExposedParentValidator();
        parent.AddInclude(child);
        Assert.Throws<InvalidOperationException>(() => parent.Validate(ValidParent()));
        Assert.True((await parent.ValidateAsync(ValidParent())).IsValid);
        Assert.Throws<InvalidOperationException>(child.AddRule);
    }

    [Fact]
    public void SelfInclude_IsRejectedWithoutFreezingOrMutatingParent()
    {
        var validator = new SelfIncludingValidator();
        Assert.Throws<InvalidOperationException>(validator.AddSelfInclude);

        validator.AddRule();
        Assert.True(validator.Validate(new SelfModel { Name = "ok" }).IsValid);
    }

    [Theory]
    [InlineData(ValidationErrorSeverity.Warning, false)]
    [InlineData(ValidationErrorSeverity.Information, false)]
    [InlineData(ValidationErrorSeverity.Warning, true)]
    [InlineData(ValidationErrorSeverity.Information, true)]
    public async Task IncludeEntriesOfNonErrorSeverity_DoNotStopThePropertyChain(ValidationErrorSeverity severity, bool asynchronous)
    {
        var nextRuleRan = false;
        var child = ChildWithFailure(severity, asynchronous);
        var parent = new Validator<Parent>(builder =>
        {
            var chain = builder
                .Property(x => x.Child!, options => options.CascadeMode = ValidationCascadeMode.Stop)
                .Include(child);

            if (asynchronous)
            {
                chain.MustAsync(_ =>
                {
                    nextRuleRan = true;
                    return Task.FromResult(false);
                });
            }
            else
            {
                chain.Must(_ =>
                {
                    nextRuleRan = true;
                    return false;
                });
            }
        });

        var result = await parent.ValidateAsync(new Parent { Child = new Child { Name = "x" } });
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal(severity, result.Errors[0].Severity);
        Assert.False(result.IsValid);
        Assert.True(nextRuleRan);
    }

    [Theory]
    [InlineData(ValidationErrorSeverity.Warning, false)]
    [InlineData(ValidationErrorSeverity.Information, false)]
    [InlineData(ValidationErrorSeverity.Warning, true)]
    [InlineData(ValidationErrorSeverity.Information, true)]
    public async Task DerivedIncludeEntriesOfNonErrorSeverity_DoNotStopThePropertyChain(ValidationErrorSeverity severity, bool asynchronous)
    {
        var nextRuleRan = false;
        var child = DogWithFailure(severity, asynchronous);
        var parent = new Validator<Parent>(builder =>
        {
            var chain = builder
                .Property(x => x.Pet!, options => options.CascadeMode = ValidationCascadeMode.Stop)
                .Include(child);

            if (asynchronous)
            {
                chain.MustAsync(_ =>
                {
                    nextRuleRan = true;
                    return Task.FromResult(false);
                });
            }
            else
            {
                chain.Must(_ =>
                {
                    nextRuleRan = true;
                    return false;
                });
            }
        });

        var result = await parent.ValidateAsync(new Parent { Pet = new Dog { Name = "x" } });
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal(severity, result.Errors[0].Severity);
        Assert.False(result.IsValid);
        Assert.True(nextRuleRan);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncludeErrorEntries_StopThePropertyChain(bool asynchronous)
    {
        var nextRuleRan = false;
        var child = ChildWithFailure(ValidationErrorSeverity.Error, asynchronous);
        var parent = new Validator<Parent>(builder => builder
            .Property(x => x.Child!, options => options.CascadeMode = ValidationCascadeMode.Stop)
            .Include(child)
            .MustAsync(_ =>
            {
                nextRuleRan = true;
                return Task.FromResult(false);
            }));

        var result = await parent.ValidateAsync(new Parent { Child = new Child { Name = "x" } });
        Assert.Single(result.Errors);
        Assert.False(nextRuleRan);

        var secondRun = await parent.ValidateAsync(new Parent { Child = new Child { Name = "x" } });
        Assert.Single(secondRun.Errors);
        Assert.False(nextRuleRan);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Include_SharesOnlyParametersWithChildContext(bool asynchronous, bool derived)
    {
        var childSawIsolatedRuleState = false;

        var parent = derived
            ? ParentWithDerivedContextChild(ContextDog(asynchronous, Observe), asynchronous)
            : ParentWithContextChild(ContextChild(asynchronous, Observe), asynchronous);

        var model = derived
            ? new Parent { Pet = new Dog { Name = "x" }, Marker = "ok" }
            : new Parent { Child = new Child { Name = "x" }, Marker = "ok" };

        var parentContext = new ValidationContext<Parent>(model)
        {
            Parameters = { ["request"] = "root" },
            ErrorMessage = "parent-only",
            MessageParameters = { { "parent", "only" } }
        };

        var first = await parent.ValidateAsync(parentContext);
        Assert.Empty(first.Errors);
        Assert.True(first.IsValid);
        Assert.True(childSawIsolatedRuleState);
        Assert.Equal(1, parentContext.Parameters["childRuns"]);

        var second = await parent.ValidateAsync(parentContext);
        Assert.Empty(second.Errors);
        Assert.True(second.IsValid);
        Assert.Equal(2, parentContext.Parameters["childRuns"]);

        return;

        void Observe<TModel>(ValidationContext<TModel> context)
        {
            childSawIsolatedRuleState = context.ErrorMessage == null && context.MessageParameters.Count == 0;

            Assert.Equal("root", context.Parameters["request"]);

            context.Parameters["childRuns"] = context.Parameters.TryGetValue("childRuns", out var runs) && runs is int count
                ? count + 1
                : 1;
        }
    }

    private static Validator<Child> ChildWithFailure(ValidationErrorSeverity severity, bool asynchronous) =>
        asynchronous
            ? new Validator<Child>(builder => builder.Property(x => x.Name)
                .MustAsync(_ => Task.FromResult(false), options => options.ErrorSeverity = severity))
            : new Validator<Child>(builder => builder.Property(x => x.Name)
                .Must(_ => false, options => options.ErrorSeverity = severity));

    private static Validator<Dog> DogWithFailure(ValidationErrorSeverity severity, bool asynchronous) =>
        asynchronous
            ? new Validator<Dog>(builder => builder.Property(x => x.Name)
                .MustAsync(_ => Task.FromResult(false), options => options.ErrorSeverity = severity))
            : new Validator<Dog>(builder => builder.Property(x => x.Name)
                .Must(_ => false, options => options.ErrorSeverity = severity));

    private static Validator<Child> ContextChild(
        bool asynchronous,
        Action<ValidationContext<Child>> observe
    ) => asynchronous
        ? new Validator<Child>(builder => builder.Property(x => x.Name).MustAsync((_, context, _) =>
        {
            observe(context);
            return Task.FromResult(true);
        }))
        : new Validator<Child>(builder => builder.Property(x => x.Name).Must((_, context) =>
        {
            observe(context);
            return true;
        }));

    private static Validator<Dog> ContextDog(
        bool asynchronous,
        Action<ValidationContext<Dog>> observe
    ) => asynchronous
        ? new Validator<Dog>(builder => builder.Property(x => x.Name).MustAsync((_, context, _) =>
        {
            observe(context);
            return Task.FromResult(true);
        }))
        : new Validator<Dog>(builder => builder.Property(x => x.Name).Must((_, context) =>
        {
            observe(context);
            return true;
        }));

    private static Validator<Parent> ParentWithContextChild(Validator<Child> child, bool asynchronous) => new(builder =>
    {
        builder.Property(x => x.Child!).Include(child);
        var marker = builder.Property(x => x.Marker);
        if (asynchronous)
        {
            marker.MustAsync((_, context, _) => Task.FromResult(
                context.Parameters.TryGetValue("childRuns", out var runs) && runs is > 0));
        }
        else
        {
            marker.Must((_, context) =>
                context.Parameters.TryGetValue("childRuns", out var runs) && runs is > 0);
        }
    });

    private static Validator<Parent> ParentWithDerivedContextChild(Validator<Dog> child, bool asynchronous) => new(builder =>
    {
        builder.Property(x => x.Pet!).Include(child);
        var marker = builder.Property(x => x.Marker);
        if (asynchronous)
        {
            marker.MustAsync((_, context, _) => Task.FromResult(
                context.Parameters.TryGetValue("childRuns", out var runs) && runs is > 0));
        }
        else
        {
            marker.Must((_, context) =>
                context.Parameters.TryGetValue("childRuns", out var runs) && runs is > 0);
        }
    });

    private static Parent ValidParent() => new()
    {
        Child = new Child { Name = "ok" },
        Pet = new Dog { Name = "ok" },
        Marker = "ok"
    };

    private sealed class ExposedParentValidator : Validator<Parent>
    {
        private readonly PropertyValidatorBuilder<Parent, Child> _child;
        private readonly PropertyValidatorBuilder<Parent, string?> _marker;
        private readonly PropertyValidatorBuilder<Parent, Animal> _pet;

        internal ExposedParentValidator()
        {
            _marker = Builder.Property(x => x.Marker).NotEmpty();
            _child = Builder.Property(x => x.Child!);
            _pet = Builder.Property(x => x.Pet!);
        }

        internal void AddAsyncRule() => _marker.MustAsync(_ => Task.FromResult(true));

        internal void AddInclude(Validator<Child> child, Action<IncludeValidatorOptions>? configure = null) =>
            _child.Include(child, configure);

        internal void AddDerivedInclude(Validator<Dog> child, Action<IncludeValidatorOptions>? configure = null) =>
            _pet.Include(child, configure);
    }

    private sealed class ExposedChildValidator : Validator<Child>
    {
        private readonly PropertyValidatorBuilder<Child, string?> _name;

        internal ExposedChildValidator(bool addAsyncRule = false)
        {
            _name = Builder.Property(x => x.Name).NotEmpty();
            if (addAsyncRule) _name.MustAsync(_ => Task.FromResult(true));
        }

        internal void AddRule() => _name.NotNull();

        internal void AddAsyncRule() => _name.MustAsync(_ => Task.FromResult(true));

        internal void AddWhere() => _name.Where(_ => true);

        internal void AddBlockWhere() => Builder.Where(_ => true, () => Builder.Property(x => x.Name).NotNull());
    }

    private sealed class ExposedDogValidator : Validator<Dog>
    {
        private readonly PropertyValidatorBuilder<Dog, string?> _name;

        internal ExposedDogValidator() => _name = Builder.Property(x => x.Name).NotEmpty();

        internal void AddRule() => _name.NotNull();
    }

    private sealed class SelfIncludingValidator : Validator<SelfModel>
    {
        private readonly PropertyValidatorBuilder<SelfModel, SelfModel> _nested;

        internal SelfIncludingValidator() => _nested = Builder.Property(x => x.Nested!);

        internal void AddSelfInclude() => _nested.Include(this);

        internal void AddRule() => Builder.Property(x => x.Name).NotEmpty();
    }

    private sealed class Parent
    {
        public Child? Child { get; init; }

        public Animal? Pet { get; init; }

        public string? Marker { get; init; }
    }

    private class Child
    {
        public string? Name { get; init; }
    }

    private class Animal;

    private sealed class Dog : Animal
    {
        public string? Name { get; init; }
    }

    private sealed class SelfModel
    {
        public string? Name { get; init; }

        public SelfModel? Nested { get; init; }
    }
}