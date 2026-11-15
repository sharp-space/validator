using System.Reflection;
using System.Reflection.Emit;
using Sharp.Validator.SourceGenerator.Generation;

namespace Sharp.Validator.SourceGenerator.Tests.Generator;

public sealed class VersionMetadataTests
{
    [Fact]
    public void Assembly_WithoutInformationalMetadata_UsesItsAssemblyVersion()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("NoMetadata") { Version = new Version(3, 4, 5, 6) },
            AssemblyBuilderAccess.Run);

        Assert.Equal("3.4.5.6", Emitter.ResolveToolVersion(assembly));
    }

    [Theory]
    [InlineData(null, "1.0.0.0")]
    [InlineData("", "1.0.0.0")]
    [InlineData("2.0.0", "2.0.0")]
    [InlineData("2.0.0+commit", "2.0.0")]
    public void OptionalVersionMetadata_HasAStableNormalizedStamp(string? version, string expected) =>
        Assert.Equal(expected, Emitter.NormalizeToolVersion(version, null));

    [Fact]
    public void MissingInformationalMetadata_UsesTheSuppliedVersion() =>
        Assert.Equal("2.3.4.5", Emitter.NormalizeToolVersion("", new Version(2, 3, 4, 5)));
}