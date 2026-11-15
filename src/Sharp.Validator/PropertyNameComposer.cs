using System.Globalization;
using System.Text.Json;

namespace Sharp.Validator;

/// <summary>Canonical property-path formatting shared by every evaluator and Include.</summary>
internal static class PropertyNameComposer
{
    internal static string ApplyPolicy(string path, PropertyNamePolicy policy)
    {
        if (path.Length == 0 || policy == PropertyNamePolicy.None) return path;

        var segments = path.Split('.');
        for (var index = 0; index < segments.Length; index++)
            segments[index] = ApplySegmentPolicy(segments[index], policy);

        return segments.Length == 1 ? segments[0] : string.Join('.', segments);
    }

    internal static string Wrap(string path, in ValidatorBuilderOptionsSnapshot options) =>
        string.Concat(options.PropertyNamePrefix, path, options.PropertyNameSuffix);

    internal static string FormatElement(
        string collectionPath,
        int elementIndex,
        string? elementPath,
        in ValidatorBuilderOptionsSnapshot options
    ) => Wrap(FormatElementCore(collectionPath, elementIndex, elementPath), options);

    internal static string FormatElementCore(
        string collectionPath,
        int elementIndex,
        string? elementPath = null
    )
    {
        var index = elementIndex.ToString(CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(elementPath)
            ? string.Concat(collectionPath, "[", index, "]")
            : string.Concat(collectionPath, "[", index, "].", elementPath);
    }

    internal static string FormatIncluded(
        string? parentCorePath,
        string childPropertyName,
        bool includeParentPropertyName,
        in ValidatorBuilderOptionsSnapshot options
    )
    {
        var core = includeParentPropertyName && !string.IsNullOrEmpty(parentCorePath)
            ? string.Concat(parentCorePath, ".", childPropertyName)
            : childPropertyName;

        return Wrap(core, options);
    }

    internal static string WrapMessage(string message, in ValidatorBuilderOptionsSnapshot options) =>
        string.Concat(options.ErrorMessagePrefix, message, options.ErrorMessageSuffix);

    // Delegated to System.Text.Json so an error key equals the member name a client receives in the JSON payload.
    private static string ApplySegmentPolicy(string value, PropertyNamePolicy policy) => policy switch
    {
        PropertyNamePolicy.PascalCase => value.PascalCase(),
        PropertyNamePolicy.CamelCase => JsonNamingPolicy.CamelCase.ConvertName(value),
        PropertyNamePolicy.SnakeCase => JsonNamingPolicy.SnakeCaseLower.ConvertName(value),
        PropertyNamePolicy.KebabCase => JsonNamingPolicy.KebabCaseLower.ConvertName(value),
        _ => value
    };
}

/// <summary>A static path or a collection path whose index is materialized only when an error is merged.</summary>
internal readonly record struct PropertyPathValue(
    string? CorePath,
    string? CollectionPath,
    int ElementIndex,
    string? ElementPath,
    bool IsIndexed
)
{
    internal static PropertyPathValue Property(string? corePath) =>
        new(corePath, null, 0, null, false);

    internal static PropertyPathValue Element(string collectionPath, int elementIndex, string? elementPath) =>
        new(null, collectionPath, elementIndex, elementPath, true);

    internal string? FormatCore() => IsIndexed
        ? PropertyNameComposer.FormatElementCore(CollectionPath!, ElementIndex, ElementPath)
        : CorePath;
}