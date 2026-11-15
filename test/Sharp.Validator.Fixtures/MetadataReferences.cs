using Microsoft.CodeAnalysis;

namespace Sharp.Validator.Fixtures;

public static class MetadataReferences
{
    // The test process's trusted platform assemblies include the runtime, Sharp.Validator and its dependencies (the
    // test project references them), so no hand-curated reference list is needed.
    public static IReadOnlyList<MetadataReference> Trusted { get; } =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(static path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
    ];
}