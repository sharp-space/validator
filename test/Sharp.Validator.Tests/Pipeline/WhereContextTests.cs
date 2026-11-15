namespace Sharp.Validator.Tests.Pipeline;

// The Where overloads that take no context forwarded to the ones that do, so every condition went through those. They
// now hand their condition to the gate as written. These check the context-taking overloads on their own: on a property
// chain, an elements chain and a block, sync and async, the condition reads the validation's own context.
public sealed class WhereContextTests
{
    private static readonly Model Blank = new() { Name = "", Tags = [""] };

    private static void SwitchOn(ValidationContext<Model> context) => context.Parameters["on"] = true;

    private static bool IsOn(ValidationContext<Model> context) => context.Parameters.ContainsKey("on");

    [Fact]
    public void Where_SyncConditionTakesTheContext_ReadsTheValidationsContext()
    {
        AssertGated(new Validator<Model>(x => x.Property(model => model.Name).Where((_, context) => IsOn(context)).NotEmpty()));
        AssertGated(new Validator<Model>(x => x.Elements(model => model.Tags).Where((_, context) => IsOn(context)).NotEmpty()));
        AssertGated(new Validator<Model>(x => x.Where((_, context) => IsOn(context), () => x.Property(model => model.Name).NotEmpty())));
    }

    [Fact]
    public async Task Where_AsyncConditionTakesTheContext_ReadsTheValidationsContext()
    {
        await AssertGatedAsync(new Validator<Model>(x => x.Property(model => model.Name)
            .Where((_, context, _) => Task.FromResult(IsOn(context))).NotEmpty()));

        await AssertGatedAsync(new Validator<Model>(x => x.Elements(model => model.Tags)
            .Where((_, context, _) => Task.FromResult(IsOn(context))).NotEmpty()));

        await AssertGatedAsync(new Validator<Model>(x => x.Where(
            (_, context, _) => Task.FromResult(IsOn(context)), () => x.Property(model => model.Name).NotEmpty())));
    }

    private static void AssertGated(Validator<Model> validator) =>
        Assert.Equal((false, true), (validator.Validate(Blank, SwitchOn).IsValid, validator.Validate(Blank).IsValid));

    private static async Task AssertGatedAsync(Validator<Model> validator) =>
        Assert.Equal((false, true), ((await validator.ValidateAsync(Blank, SwitchOn)).IsValid, (await validator.ValidateAsync(Blank)).IsValid));

    private sealed class Model
    {
        public string? Name { get; init; }

        public List<string>? Tags { get; init; }
    }
}