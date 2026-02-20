using Microsoft.JSInterop;

namespace SmallEBot.Services.Presentation;

/// <summary>Manages keyboard shortcuts via JS interop.</summary>
public sealed class KeyboardShortcutService(IJSRuntime js) : IAsyncDisposable
{
    private DotNetObjectReference<KeyboardShortcutService>? _dotNetRef;

    public event Func<Task>? OnSend;
    public event Func<Task>? OnCancel;
    public event Func<Task>? OnNewConversation;
    public event Func<Task>? OnFocusSearch;
    public event Func<Task>? OnToggleToolCalls;
    public event Func<Task>? OnToggleReasoning;

    public async Task RegisterAsync()
    {
        _dotNetRef = DotNetObjectReference.Create(this);
        var shortcuts = new[]
        {
            new { key = "Enter", ctrl = true, shift = false, alt = false, method = "InvokeSend" },
            new { key = "Escape", ctrl = false, shift = false, alt = false, method = "InvokeCancel" },
            new { key = "n", ctrl = true, shift = false, alt = false, method = "InvokeNewConversation" },
            new { key = "/", ctrl = true, shift = false, alt = false, method = "InvokeFocusSearch" },
            new { key = "t", ctrl = true, shift = true, alt = false, method = "InvokeToggleToolCalls" },
            new { key = "r", ctrl = true, shift = true, alt = false, method = "InvokeToggleReasoning" }
        };
        await js.InvokeVoidAsync("keyboardShortcuts.register", _dotNetRef, shortcuts);
    }

    [JSInvokable]
    public async Task InvokeSend() => await (OnSend?.Invoke() ?? Task.CompletedTask);

    [JSInvokable]
    public async Task InvokeCancel() => await (OnCancel?.Invoke() ?? Task.CompletedTask);

    [JSInvokable]
    public async Task InvokeNewConversation() => await (OnNewConversation?.Invoke() ?? Task.CompletedTask);

    [JSInvokable]
    public async Task InvokeFocusSearch() => await (OnFocusSearch?.Invoke() ?? Task.CompletedTask);

    [JSInvokable]
    public async Task InvokeToggleToolCalls() => await (OnToggleToolCalls?.Invoke() ?? Task.CompletedTask);

    [JSInvokable]
    public async Task InvokeToggleReasoning() => await (OnToggleReasoning?.Invoke() ?? Task.CompletedTask);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await js.InvokeVoidAsync("keyboardShortcuts.unregister");
        }
        catch (JSDisconnectedException)
        {
            // Ignore when circuit has been disconnected
        }
        _dotNetRef?.Dispose();
    }
}
