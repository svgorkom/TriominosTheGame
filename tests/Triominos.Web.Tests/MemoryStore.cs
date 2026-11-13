using Triominos.Web.Storage;

namespace Triominos.Web.Tests;

/// <summary>A stand-in for the browser's local storage.</summary>
internal sealed class MemoryStore : IPreferenceStore
{
    public Dictionary<string, string> Values { get; } = [];

    public ValueTask<string?> GetAsync(string key) => ValueTask.FromResult(Values.GetValueOrDefault(key));

    public ValueTask SetAsync(string key, string value)
    {
        Values[key] = value;
        return ValueTask.CompletedTask;
    }
}
