using System.Reflection;

namespace Sharp.Validator.Tests.Pipeline;

// A validator freezes at its first validation or Include: every public mutation path then throws, and the options
// objects that callers may still hold are read-only by then, so a change to one throws instead of going unseen.
public sealed class FreezeTests
{
    [Fact]
    public void FirstValidation_FreezesEveryPublicMutationPath()
    {
        var validator = new ExposedValidator();
        validator.Validate(new Model { Name = "ok", Items = [new Item { Label = "ok" }] });
        Assert.Throws<InvalidOperationException>(() => validator.Configure(x => x.PropertyNamePrefix = "late"));
        Assert.Throws<InvalidOperationException>(validator.AddProperty);
        Assert.Throws<InvalidOperationException>(validator.AddRule);
        Assert.Throws<InvalidOperationException>(validator.AddElementPropertyRule);
        Assert.Throws<InvalidOperationException>(validator.AddWhere);
        Assert.Throws<InvalidOperationException>(validator.AddBlockWhere);
    }

    [Fact]
    public void CapturedOptions_ThrowOnAChangeOnceRead()
    {
        ValidatorBuilderOptions? validatorOptions = null;
        PropertyValidatorBuilderOptions? propertyOptions = null;
        PropertyValidatorOptions? ruleOptions = null;
        IncludeValidatorOptions? includeOptions = null;

        var child = new Validator<Address>(x => x.Property(m => m.City).NotEmpty());
        var validator = new Validator<Model>(builder =>
        {
            builder.Property(x => x.Home!, options =>
                {
                    propertyOptions = options;
                    options.PropertyName = "home";
                    options.ErrorSeverity = ValidationErrorSeverity.Warning;
                    options.ErrorCode = "property";
                })
                .Include(child, options =>
                {
                    includeOptions = options;
                    options.IncludeParentPropertyName = true;
                });

            builder.Property(x => x.Name).NotEmpty(options =>
            {
                ruleOptions = options;
                options.ErrorCode = "rule";
            });
        });

        validator.Configure(options =>
        {
            validatorOptions = options;
            options.PropertyNamePrefix = "PRE_";
            options.PropertyNameSuffix = "_SUF";
        });

        var first = validator.Validate(new Model { Name = "ok", Home = new Address() });
        Assert.Equal("PRE_home.City_SUF", first.Errors[0].PropertyName);
        Assert.Throws<InvalidOperationException>(() => validatorOptions!.PropertyNamePrefix = "LATE_");
        Assert.Throws<InvalidOperationException>(() => propertyOptions!.PropertyName = "lateHome");
        Assert.Throws<InvalidOperationException>(() => ruleOptions!.ErrorCode = "late-rule");
        Assert.Throws<InvalidOperationException>(() => includeOptions!.IncludeParentPropertyName = false);

        var second = validator.Validate(new Model { Name = "", Home = new Address() });
        Assert.Equal("PRE_home.City_SUF", second.Errors[0].PropertyName);
        Assert.Equal("rule", second.Errors[1].Code);
    }

    [Fact]
    // A rule's own Configure hands its options to the rule's callback, which may keep them. They stay writable until a
    // chain takes the rule, which reads them, and then a change through the kept reference throws. The freeze still
    // works on a copy, so the kept reference never shows the frozen template.
    public void RuleOptionsItsConfigureHandedOut_AreReadOnlyOnceAChainTakesTheRule()
    {
        var rule = new KeepsItsOptions { Kept = { ErrorCode = "kept" } };
        var validator = new Validator<Model>(x => x.Property(m => m.Name).Add(rule));

        var exception = Assert.Throws<InvalidOperationException>(() => rule.Kept.ErrorCode = "late");
        Assert.Contains("before it is added to a chain", exception.Message);
        Assert.Equal("kept", validator.Validate(new Model { Name = "" }).Errors[0].Code);
        Assert.Equal("'{PropertyValue}' is kept.", rule.Kept.ErrorMessage);
    }

    [Fact]
    // A rule's Configure called after a chain had taken it handed out the copy the freeze makes, which is never read
    // again, so the change was lost or not depending on whether the failure had been cached yet. It throws instead.
    public void RuleConfigure_AfterAChainTookTheRule_Throws()
    {
        var rule = new KeepsItsOptions();
        var validator = new Validator<Model>(x => x.Property(m => m.Name).Add(rule));

        Assert.Throws<InvalidOperationException>(rule.Reconfigure);

        validator.Validate(new Model { Name = "" });

        Assert.Throws<InvalidOperationException>(rule.Reconfigure);
    }

    [Fact]
    // A built-in rule sets its defaults without handing its options out, so no caller can hold them and they freeze in
    // place. Copying them cost every rule an object at every freeze, which a Scoped validator does in every scope.
    public void BuiltInRule_FreezesItsOptionsInPlace()
    {
        var rule = new NotEmptyValidator<Model, string?>();
        var before = rule.Options;
        new Validator<Model>(x => x.Property(m => m.Name).Add(rule)).Validate(new Model { Name = "ok" });

        Assert.NotNull(before);
        Assert.Same(before, rule.Options);
    }

    [Fact]
    // Freeze checks the flag again inside its lock: a caller that was waiting on the gate while another thread froze
    // the builder must leave the finished plan alone instead of building it a second time.
    public void CallerThatWaited_OnTheFreezeGate_LeavesTheFrozenPlanAlone()
    {
        var builder = new ExposedValidator().ExposedBuilder;
        var gate = (Lock)typeof(ValidatorBuilder<Model>)
            .GetField("_freezeGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(builder)!;

        var waiter = new Thread(builder.Freeze);
        SyncRuleStep<Model>[] plan;
        gate.Enter();

        try
        {
            waiter.Start();
            Assert.True(SpinWait.SpinUntil(
                () => (waiter.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(30)));

            builder.Freeze();
            plan = builder.SyncPlan;
        }
        finally
        {
            gate.Exit();
        }

        Assert.True(waiter.Join(TimeSpan.FromSeconds(30)));
        Assert.True(builder.IsFrozen);
        Assert.Same(plan, builder.SyncPlan);
    }

    private sealed class KeepsItsOptions : PropertyValidator<Model, string?>
    {
        public KeepsItsOptions() => Configure(options =>
        {
            Kept = options;
            options.ErrorCode = "configured";
            options.ErrorMessage = "'{PropertyValue}' is kept.";
        });

        public PropertyValidatorOptions Kept { get; private set; } = null!;

        public void Reconfigure() => Configure(options => options.ErrorMessage = "late");

        public override bool Validate(string? value, ValidationContext<Model> context) => !string.IsNullOrEmpty(value);
    }

    private sealed class ExposedValidator : Validator<Model>
    {
        private readonly PropertyValidatorBuilder<Model, string?> _itemLabel;
        private readonly ElementValidatorBuilder<Model, Item> _items;
        private readonly PropertyValidatorBuilder<Model, string?> _name;

        internal ExposedValidator()
        {
            _name = Builder.Property(x => x.Name).NotEmpty();
            _items = Builder.Elements(x => x.Items).Where(_ => true);
            _itemLabel = _items.Property(x => x.Label).NotEmpty();
        }

        internal ValidatorBuilder<Model> ExposedBuilder => Builder;

        internal void AddProperty() => Builder.Property(x => x.Name).NotNull();

        internal void AddRule() => _name.NotNull();

        internal void AddElementPropertyRule() => _itemLabel.NotNull();

        internal void AddWhere() => _items.Where(_ => true);

        internal void AddBlockWhere() => Builder.Where(_ => true, () => Builder.Property(x => x.Name).NotNull());
    }

    private sealed class Model
    {
        public string? Name { get; init; }

        public Address? Home { get; init; }

        public List<Item> Items { get; init; } = [];
    }

    private sealed class Item
    {
        public string? Label { get; init; }
    }
}