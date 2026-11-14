using Microsoft.JSInterop;

namespace Triominos.Web.Storage;

/// <summary>A place to keep small text values between visits.</summary>
public interface IPreferenceStore
{
    /// <summary>Returns the text stored under a key, or null when there is none or storage is unavailable.</summary>
    /// <param name="key">The key.</param>
    ValueTask<string?> GetAsync(string key);

    /// <summary>Stores a text; does nothing when storage is unavailable.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The text.</param>
    ValueTask SetAsync(string key, string value);
}

/// <summary>Keeps the values in the browser's local storage through <c>wwwroot/js/storage.js</c>.</summary>
public sealed class LocalStoragePreferenceStore(IJSRuntime js) : IPreferenceStore, IAsyncDisposable
{
    private IJSObjectReference? _module;

    /// <inheritdoc />
    public async ValueTask<string?> GetAsync(string key)
    {
        try
        {
            var module = await ModuleAsync();
            return await module.InvokeAsync<string?>("get", key);
        }
        catch (JSException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async ValueTask SetAsync(string key, string value)
    {
        try
        {
            var module = await ModuleAsync();
            await module.InvokeVoidAsync("set", key, value);
        }
        catch (JSException)
        {
            // Not stored; the game goes on with what it has.
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The page is closing.
        }

        _module = null;
    }

    private async ValueTask<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/storage.js");
}
