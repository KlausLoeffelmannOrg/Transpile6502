using System.Reflection;

namespace DotNet6502;

class ReflectionCache
{
    readonly Dictionary<string, MethodInfo> _cache = new(StringComparer.Ordinal);

    public MethodInfo GetMethod(string name)
    {
        if (!_cache.TryGetValue(name, out var method))
        {
                throw new InvalidOperationException($"Unable to find method named '{name}'!");
        }
        return method;
    }

    public int GetNumberOfArguments(string name) => GetMethod(name).GetParameters().Length;

    public bool HasReturnValue(string name) => GetMethod(name).ReturnType != null;
}
