using System.Reflection;

namespace Sharp.Validator.Fixtures;

// A stand-in for a Roslyn interface (symbol, operation) with only the members a test sets: a getter returns the value
// given for it, or the default of its type. OriginalDefinition is the stub itself, as for a symbol that is not
// constructed from another.
public class StubProxy : DispatchProxy
{
    private object _self = null!;
    private Dictionary<string, object?> _values = [];

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        var name = method!.Name.Replace("get_", "", StringComparison.Ordinal);
        if (_values.TryGetValue(name, out var value)) return value;
        if (name == "OriginalDefinition") return _self;

        return method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
    }

    public static T Of<T>(params (string Key, object? Value)[] values) where T : class
    {
        var result = Create<T, StubProxy>();
        var stub = (StubProxy)(object)result;
        stub._values = values.ToDictionary(x => x.Key, x => x.Value);
        stub._self = result;
        return result;
    }
}