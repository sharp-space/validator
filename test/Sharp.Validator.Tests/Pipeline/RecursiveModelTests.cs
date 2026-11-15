namespace Sharp.Validator.Tests.Pipeline;

// Recursive includes aren't supported: a cycle in the object graph would overflow the stack, which .NET can't catch.
// README documents a bounded alternative, a new validator per level, and these tests keep that example working.
public sealed class RecursiveModelTests
{
    [Fact]
    public void SelfInclude_ExplainsTheAlternative()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new SelfIncluding());
        Assert.Contains("fixed depth", exception.Message);
    }

    [Fact]
    public void PerLevelValidators_ReportTheFullPath()
    {
        var tree = new Node { Name = "root", Children = [new Node { Name = "a", Children = [new Node { Name = "" }] }] };
        var result = new NodeValidator().Validate(tree);
        Assert.Equal("Children[0].Children[0].Name", Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void Levels_PastTheDepth_AreNotValidated()
    {
        var tree = new Node { Name = "root", Children = [new Node { Name = "a", Children = [new Node { Name = "" }] }] };
        Assert.True(new NodeValidator(1).Validate(tree).IsValid);
    }

    [Fact]
    public void Cycle_StopsAtTheDepth()
    {
        var children = new List<Node>();
        var node = new Node { Name = "", Children = children };
        children.Add(node);

        Assert.Equal(4, new NodeValidator(3).Validate(node).Errors.Count);
    }

    private sealed class Node
    {
        public string? Name { get; init; }

        public List<Node>? Children { get; init; }
    }

    private sealed class NodeValidator : Validator<Node>
    {
        public NodeValidator(int depth = 8)
        {
            Builder.Property(x => x.Name).NotEmpty();
            if (depth > 0) Builder.Elements(x => x.Children!).Include(new NodeValidator(depth - 1));
        }
    }

    private sealed class SelfIncluding : Validator<Node>
    {
        public SelfIncluding() => Builder.Elements(x => x.Children!).Include(this);
    }
}