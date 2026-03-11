using SmallEBot.Application.Contracts.Agents.Compression;
using SmallEBot.Application.Contracts.Agents.Config;
using SmallEBot.Application.Contracts.Agents.Execution;
using SmallEBot.Application.Agents.Streaming;
using SmallEBot.Components.Chat.Services;
using SmallEBot.Components.Chat.ViewModels.Blocks;
using SmallEBot.Core.Models;
using MudBlazor;

namespace SmallEBot.Components.Chat.Orchestration;

/// <summary>
/// Orchestrates streaming, approval, and compression for chat. Pure logic; UI concerns (scroll, snackbar) are delegated via callbacks.
/// Register as Scoped. Component must set InvokeOnUI and ShowMessage before use.
/// </summary>
public class ChatOrchestrator : IDisposable
{
    private readonly IConversationAgentDispatcher _agentDispatcher;
    private readonly IContextUsageEstimator _contextUsageEstimator;
    private readonly IAgentInvalidationService _agentInvalidation;
    private readonly ITerminalConfigService _terminalConfig;
    private readonly ChatPresentationService _presentation;
    private readonly ILogger<ChatOrchestrator> _log;

    private readonly List<StreamUpdate> _streamingUpdates = [];
    private readonly Dictionary<string, ApprovalBlockModel> _pendingApprovals = new();
    private readonly Dictionary<string, TaskCompletionSource<bool>> _approvalWaitHandles = new();
    private Timer? _waitingCheckTimer;
    private CancellationTokenSource? _sendCts;
    private bool _contextRefreshRequested;

    private static readonly TimeSpan ApprovalTimeout = TimeSpan.FromMinutes(5);

    public ChatOrchestrator(
        IConversationAgentDispatcher agentDispatcher,
        IContextUsageEstimator contextUsageEstimator,
        IAgentInvalidationService agentInvalidation,
        ITerminalConfigService terminalConfig,
        ChatPresentationService presentation,
        ILogger<ChatOrchestrator> log)
    {
        _agentDispatcher = agentDispatcher;
        _contextUsageEstimator = contextUsageEstimator;
        _agentInvalidation = agentInvalidation;
        _terminalConfig = terminalConfig;
        _presentation = presentation;
        _log = log;
    }

    /// <summary>Required: component's InvokeAsync for marshalling to UI thread.</summary>
    public Func<Func<Task>, Task>? InvokeOnUI { get; set; }

    /// <summary>Optional: show message to user (e.g. Snackbar).</summary>
    public Func<string, Severity, Task>? ShowMessage { get; set; }

    public Guid? ConversationId { get; private set; }
    public bool IsStreaming { get; private set; }
    public bool IsCompressing { get; private set; }
    public bool IsWaitingForApproval { get; private set; }
    /// <summary>CallId of the approval currently being processed. Only that block's buttons are disabled; new approval requests remain clickable.</summary>
    public string? ApprovalProcessingCallId { get; private set; }
    public bool ShowWaitingForToolParams { get; private set; }
    public TimeSpan WaitingElapsed => ShowWaitingForToolParams && _waitingForToolParamsSince.HasValue
        ? DateTime.UtcNow - _waitingForToolParamsSince.Value
        : TimeSpan.Zero;
    public IReadOnlyList<IBubbleBlock> StreamItems { get; private set; } = [];
    /// <summary>First pending approval for popover display. Null when none.</summary>
    public ApprovalBlockModel? PendingApprovalForPopover =>
        _pendingApprovals.Values.FirstOrDefault(a => a.State == ApprovalState.Pending);
    public string ContextPercentText { get; private set; } = "—";
    public string? ContextUsageTooltip { get; private set; }
    public string CompressionMessage { get; private set; } = "";
    public TimeSpan CompressionElapsed => IsCompressing && _compressionStartedAt.HasValue
        ? DateTime.UtcNow - _compressionStartedAt.Value
        : TimeSpan.Zero;

    private DateTime? _lastStreamActivityAt;
    private DateTime? _waitingForToolParamsSince;
    private DateTime? _compressionStartedAt;
    private Timer? _compressionRefreshTimer;

    public event Action? OnStateChanged;

    /// <summary>Fired when streaming completes successfully (persisted). Component should call OnMessageSent.</summary>
    public event Action? OnStreamingCompleted;

    /// <summary>Fired when compression completes successfully. Component should refresh messages and conversation.</summary>
    public event Action? OnCompressionCompletedForRefresh;

    public void SetConversation(Guid? id)
    {
        ConversationId = id;
        if (!id.HasValue)
        {
            ContextPercentText = "—";
            ContextUsageTooltip = null;
        }
        else
        {
            _contextRefreshRequested = true;
        }
    }

    public void RequestStop()
    {
        foreach (var tcs in _approvalWaitHandles.Values)
            tcs.TrySetResult(false);
        _sendCts?.Cancel();
    }

    public async Task RefreshContextUsageAsync()
    {
        if (!ConversationId.HasValue) return;
        try
        {
            var d = await _contextUsageEstimator.GetEstimatedContextUsageDetailAsync(ConversationId.Value);
            if (d != null)
            {
                ContextPercentText = $"{d.Ratio:P1}";
                ContextUsageTooltip = $"Context: {d.Ratio:P1} · {_contextUsageEstimator.FormatTokenCount(d.UsedTokens)}/{_contextUsageEstimator.FormatTokenCount(d.ContextWindowTokens)}";
            }
            else
            {
                ContextPercentText = "—";
                ContextUsageTooltip = null;
            }
            OnStateChanged?.Invoke();
        }
        catch
        {
            ContextPercentText = "—";
            ContextUsageTooltip = null;
        }
    }

    public async Task CompressAsync()
    {
        if (!ConversationId.HasValue || IsCompressing) return;

        IsCompressing = true;
        _compressionStartedAt = DateTime.UtcNow;
        CompressionMessage = "Compressing context...";
        _compressionRefreshTimer?.Dispose();
        _compressionRefreshTimer = new Timer(_ => _ = InvokeOnUIAsync(() => { OnStateChanged?.Invoke(); return Task.CompletedTask; }), null, 1000, 1000);
        OnStateChanged?.Invoke();

        try
        {
            var compressed = await _agentDispatcher.CompactConversationAsync(ConversationId.Value);
            if (compressed)
            {
                await RefreshContextUsageAsync();
                OnCompressionCompletedForRefresh?.Invoke();
                await ShowMessageAsync("Context compressed successfully.", Severity.Success);
            }
            else
            {
                await ShowMessageAsync("No messages to compress or compression failed.", Severity.Info);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to compress context");
            await ShowMessageAsync($"Compression failed: {ex.Message}", Severity.Warning);
        }
        finally
        {
            IsCompressing = false;
            _compressionStartedAt = null;
            _compressionRefreshTimer?.Dispose();
            _compressionRefreshTimer = null;
            CompressionMessage = "";
            OnStateChanged?.Invoke();
        }
    }

    public async Task<bool> CheckAndCompactIfNeededAsync()
    {
        if (!ConversationId.HasValue) return false;
        try
        {
            return await _agentDispatcher.CheckAndCompactIfNeededAsync(ConversationId.Value);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to check/compress context");
            return false;
        }
    }

    public Task RunStreamingLoopAsync(
        string userMessage,
        bool useThinking,
        string? circuitContextId,
        CancellationTokenSource sendCts) =>
        RunStreamingLoopCoreAsync(userMessage, useThinking, circuitContextId, sendCts);

    private async Task RunStreamingLoopCoreAsync(
        string userMessage,
        bool useThinking,
        string? circuitContextId,
        CancellationTokenSource sendCts)
    {
        var didPersist = false;
        _sendCts = sendCts;
        IsStreaming = true;
        _streamingUpdates.Clear();
        StreamItems = [];
        _lastStreamActivityAt = null;
        ShowWaitingForToolParams = false;
        _pendingApprovals.Clear();
        _approvalWaitHandles.Clear();
        StartWaitingCheckTimer();
        OnStateChanged?.Invoke();

        var channel = System.Threading.Channels.Channel.CreateUnbounded<StreamUpdate>();
        var sink = new ChannelStreamSink(channel.Writer);
        var runTask = _agentDispatcher
            .StreamResponseAsync(ConversationId!.Value, userMessage, useThinking, sink, sendCts.Token, circuitContextId)
            .ContinueWith(_ => channel.Writer.TryComplete(null));

        try
        {
            await foreach (var update in channel.Reader.ReadAllAsync(sendCts.Token))
            {
                sendCts.Token.ThrowIfCancellationRequested();
                _lastStreamActivityAt = DateTime.UtcNow;
                ShowWaitingForToolParams = false;

                switch (update)
                {
                    case TextStreamUpdate:
                    case ThinkStreamUpdate:
                    case ToolCallStreamUpdate:
                    case SubAgentStreamUpdate:
                        _streamingUpdates.Add(update);
                        break;
                    case ApprovalRequestStreamUpdate approval:
                        _streamingUpdates.Add(update);
                        _pendingApprovals[approval.CallId] = new ApprovalBlockModel(
                            approval.CallId,
                            approval.ToolName,
                            approval.Arguments,
                            ApprovalState.Pending,
                            approval.ConversationId,
                            approval.FunctionCallId,
                            approval.RawArguments);
                        break;
                }
                StreamItems = _presentation.ConvertStreamToBubbleBlocks(_streamingUpdates, _pendingApprovals);
                OnStateChanged?.Invoke();
            }
            await runTask;
            didPersist = true;

            while (_pendingApprovals.Count > 0)
            {
                if (sendCts.Token.IsCancellationRequested) break;

                var pendingApproval = _pendingApprovals.Values.FirstOrDefault(a => a.State == ApprovalState.Pending);
                if (pendingApproval == null) break;

                OnStateChanged?.Invoke();

                if (pendingApproval.ToolName == "ExecuteCommand" && await IsCommandWhitelistedAsync(pendingApproval))
                {
                    await ApproveAsync(pendingApproval);
                    StreamItems = _presentation.ConvertStreamToBubbleBlocks(_streamingUpdates, _pendingApprovals);
                    OnStateChanged?.Invoke();
                    continue;
                }

                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _approvalWaitHandles[pendingApproval.CallId] = tcs;

                using var timeoutCts = new CancellationTokenSource(ApprovalTimeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(sendCts.Token, timeoutCts.Token);
                try
                {
                    var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(Timeout.Infinite, linkedCts.Token));
                    if (completedTask == tcs.Task) { /* user responded */ }
                    else
                    {
                        _pendingApprovals[pendingApproval.CallId] = pendingApproval with { State = ApprovalState.Rejected };
                        await ShowMessageAsync("Approval request timed out after 5 minutes.", Severity.Warning);
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    if (sendCts.Token.IsCancellationRequested)
                    {
                        _pendingApprovals[pendingApproval.CallId] = pendingApproval with { State = ApprovalState.Rejected };
                        break;
                    }
                    _pendingApprovals[pendingApproval.CallId] = pendingApproval with { State = ApprovalState.Rejected };
                    await ShowMessageAsync("Approval request timed out after 5 minutes.", Severity.Warning);
                    break;
                }
                finally
                {
                    _approvalWaitHandles.Remove(pendingApproval.CallId);
                }
                StreamItems = _presentation.ConvertStreamToBubbleBlocks(_streamingUpdates, _pendingApprovals);
                OnStateChanged?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            channel.Writer.TryComplete(null);
        }
        catch (Exception ex)
        {
            await InvokeOnUIAsync(async () =>
            {
                await ShowMessageAsync($"Error: {ex.Message}", Severity.Error);
            });
            channel.Writer.TryComplete(null);
        }
        finally
        {
            StopWaitingCheckTimer();
            _sendCts = null;
            _pendingApprovals.Clear();
            _approvalWaitHandles.Clear();
            IsStreaming = false;
            if (didPersist)
                OnStreamingCompleted?.Invoke();
            ShowWaitingForToolParams = false;
            _streamingUpdates.Clear();
            StreamItems = [];
            OnStateChanged?.Invoke();
        }
    }

    /// <summary>Approves and adds the command's executable (first token) to the whitelist. Only applies to ExecuteCommand.</summary>
    public async Task ApproveAndWhitelistAsync(ApprovalBlockModel approval)
    {
        if (IsWaitingForApproval || !ConversationId.HasValue) return;
        if (approval.ToolName != "ExecuteCommand") return;

        var executable = ExtractExecutableFromApproval(approval);
        if (!string.IsNullOrWhiteSpace(executable))
        {
            try
            {
                await _terminalConfig.AddToWhitelistAndSaveAsync(executable.Trim());
                await ShowMessageAsync($"Added to whitelist: {executable.Trim()}", Severity.Success);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to add {Executable} to whitelist", executable);
                await ShowMessageAsync($"Failed to add to whitelist: {ex.Message}", Severity.Warning);
            }
        }

        await ApproveAsync(approval);
    }

    private static string? ExtractExecutableFromApproval(ApprovalBlockModel approval)
    {
        var command = GetCommandFromApproval(approval);
        if (string.IsNullOrWhiteSpace(command)) return null;
        var parts = command.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : null;
    }

    private async Task<bool> IsCommandWhitelistedAsync(ApprovalBlockModel approval)
    {
        var command = GetCommandFromApproval(approval);
        if (string.IsNullOrWhiteSpace(command)) return false;
        var normalized = System.Text.RegularExpressions.Regex.Replace(command.Trim(), @"\s+", " ");
        var whitelist = await _terminalConfig.GetCommandWhitelistAsync();
        return whitelist.Any(w =>
        {
            var entry = w.Trim();
            if (string.IsNullOrEmpty(entry)) return false;
            return normalized.Equals(entry, StringComparison.OrdinalIgnoreCase)
                || (normalized.StartsWith(entry, StringComparison.OrdinalIgnoreCase)
                    && (normalized.Length == entry.Length || char.IsWhiteSpace(normalized[entry.Length])));
        });
    }

    private static string? GetCommandFromApproval(ApprovalBlockModel approval)
    {
        // 1. Try RawArguments (case-insensitive key)
        if (approval.RawArguments != null)
        {
            var cmdObj = approval.RawArguments
                .FirstOrDefault(kv => string.Equals(kv.Key, "command", StringComparison.OrdinalIgnoreCase))
                .Value;
            if (cmdObj != null)
            {
                var s = cmdObj is System.Text.Json.JsonElement je ? je.GetString() : cmdObj.ToString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        }
        // 2. Fallback: parse Approval.Arguments JSON
        if (!string.IsNullOrWhiteSpace(approval.Arguments))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(approval.Arguments);
                if (doc.RootElement.TryGetProperty("command", out var cmdProp))
                {
                    var s = cmdProp.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
            catch { /* ignore parse errors */ }
        }
        return null;
    }

    public async Task ApproveAsync(ApprovalBlockModel approval)
    {
        if (IsWaitingForApproval || !ConversationId.HasValue) return;

        _pendingApprovals[approval.CallId] = approval with { State = ApprovalState.Approved };
        StreamItems = _presentation.ConvertStreamToBubbleBlocks(_streamingUpdates, _pendingApprovals);
        IsWaitingForApproval = true;
        ApprovalProcessingCallId = approval.CallId;
        OnStateChanged?.Invoke();

        try
        {
            _sendCts ??= new CancellationTokenSource();
            StartWaitingCheckTimer();

            await foreach (var update in _agentDispatcher.ContinueWithApprovalAsync(
                approval.ConversationId,
                approval.CallId,
                approval.ToolName,
                approval.FunctionCallId,
                approved: true,
                reason: null,
                approval.RawArguments,
                _sendCts.Token))
            {
                _lastStreamActivityAt = DateTime.UtcNow;
                ShowWaitingForToolParams = false;

                if (update is ApprovalRequestStreamUpdate newApproval)
                {
                    _pendingApprovals[newApproval.CallId] = new ApprovalBlockModel(
                        newApproval.CallId,
                        newApproval.ToolName,
                        newApproval.Arguments,
                        ApprovalState.Pending,
                        newApproval.ConversationId,
                        newApproval.FunctionCallId,
                        newApproval.RawArguments);
                }
                else
                {
                    _streamingUpdates.Add(update);
                }
                StreamItems = _presentation.ConvertStreamToBubbleBlocks(_streamingUpdates, _pendingApprovals);
                OnStateChanged?.Invoke();
            }

            if (_pendingApprovals.TryGetValue(approval.CallId, out var completed))
            {
                _pendingApprovals[approval.CallId] = completed with { State = ApprovalState.Completed };
                StreamItems = _presentation.ConvertStreamToBubbleBlocks(_streamingUpdates, _pendingApprovals);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error during approval continue flow");
            await ShowMessageAsync($"Error: {ex.Message}", Severity.Error);
        }
        finally
        {
            StopWaitingCheckTimer();
            IsWaitingForApproval = false;
            ApprovalProcessingCallId = null;
            if (_approvalWaitHandles.TryGetValue(approval.CallId, out var tcs))
                tcs.TrySetResult(true);
            OnStateChanged?.Invoke();
        }
    }

    public async Task RejectAsync(ApprovalBlockModel approval)
    {
        if (IsWaitingForApproval || !ConversationId.HasValue) return;

        _pendingApprovals[approval.CallId] = approval with { State = ApprovalState.Rejected };
        StreamItems = _presentation.ConvertStreamToBubbleBlocks(_streamingUpdates, _pendingApprovals);
        IsWaitingForApproval = true;
        ApprovalProcessingCallId = approval.CallId;
        OnStateChanged?.Invoke();

        try
        {
            _sendCts ??= new CancellationTokenSource();
            StartWaitingCheckTimer();

            await foreach (var update in _agentDispatcher.ContinueWithApprovalAsync(
                approval.ConversationId,
                approval.CallId,
                approval.ToolName,
                approval.FunctionCallId,
                approved: false,
                reason: "User rejected",
                rawArguments: null,
                _sendCts.Token))
            {
                _lastStreamActivityAt = DateTime.UtcNow;
                ShowWaitingForToolParams = false;

                if (update is ApprovalRequestStreamUpdate newApproval)
                {
                    _pendingApprovals[newApproval.CallId] = new ApprovalBlockModel(
                        newApproval.CallId,
                        newApproval.ToolName,
                        newApproval.Arguments,
                        ApprovalState.Pending,
                        newApproval.ConversationId,
                        newApproval.FunctionCallId,
                        newApproval.RawArguments);
                }
                else
                {
                    _streamingUpdates.Add(update);
                }
                StreamItems = _presentation.ConvertStreamToBubbleBlocks(_streamingUpdates, _pendingApprovals);
                OnStateChanged?.Invoke();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error sending rejection response");
            await ShowMessageAsync($"Error: {ex.Message}", Severity.Error);
        }
        finally
        {
            StopWaitingCheckTimer();
            IsWaitingForApproval = false;
            ApprovalProcessingCallId = null;
            if (_approvalWaitHandles.TryGetValue(approval.CallId, out var tcs))
                tcs.TrySetResult(false);
            OnStateChanged?.Invoke();
        }
    }

    public void OnCompressionStarted(Guid conversationId)
    {
        if (conversationId != ConversationId) return;
        IsCompressing = true;
        _compressionStartedAt = DateTime.UtcNow;
        CompressionMessage = "Compressing context...";
        _compressionRefreshTimer?.Dispose();
        _compressionRefreshTimer = new Timer(_ => _ = InvokeOnUIAsync(() => { OnStateChanged?.Invoke(); return Task.CompletedTask; }), null, 1000, 1000);
        OnStateChanged?.Invoke();
    }

    public void OnCompressionCompleted(Guid conversationId, bool success)
    {
        if (conversationId != ConversationId) return;
        IsCompressing = false;
        _compressionStartedAt = null;
        _compressionRefreshTimer?.Dispose();
        _compressionRefreshTimer = null;
        CompressionMessage = success ? "Context compressed" : "Compression failed";
        _ = InvokeOnUIAsync(async () =>
        {
            await Task.Delay(2000);
            CompressionMessage = "";
            OnStateChanged?.Invoke();
        });
        _contextRefreshRequested = true;
        if (success)
            _ = InvokeOnUIAsync(() => { OnCompressionCompletedForRefresh?.Invoke(); return Task.CompletedTask; });
    }

    public void OnModelConfigChanged()
    {
        _ = InvokeOnUIAsync(async () =>
        {
            await _agentInvalidation.InvalidateAgentAsync();
            await RefreshContextUsageAsync();
        });
    }

    public void RequestContextRefresh()
    {
        _contextRefreshRequested = true;
    }

    public bool ConsumeContextRefreshRequest()
    {
        var v = _contextRefreshRequested;
        _contextRefreshRequested = false;
        return v;
    }

    private void StartWaitingCheckTimer()
    {
        _waitingCheckTimer?.Dispose();
        _waitingCheckTimer = new Timer(_ => _ = InvokeOnUIAsync(RefreshWaitingStateAsync), null, 500, 500);
    }

    private void StopWaitingCheckTimer()
    {
        _waitingCheckTimer?.Dispose();
        _waitingCheckTimer = null;
    }

    private Task RefreshWaitingStateAsync()
    {
        if (!IsStreaming || _lastStreamActivityAt is null) return Task.CompletedTask;
        var elapsed = (DateTime.UtcNow - _lastStreamActivityAt.Value).TotalSeconds;
        if (!ShowWaitingForToolParams && elapsed >= 2)
        {
            ShowWaitingForToolParams = true;
            _waitingForToolParamsSince = _lastStreamActivityAt;
        }
        OnStateChanged?.Invoke();
        return Task.CompletedTask;
    }

    private async Task InvokeOnUIAsync(Func<Task> work)
    {
        if (InvokeOnUI != null)
            await InvokeOnUI(work);
        else
            await work();
    }

    private async Task ShowMessageAsync(string message, Severity severity)
    {
        if (ShowMessage != null)
            await ShowMessage(message, severity);
    }

    public void Dispose()
    {
        StopWaitingCheckTimer();
        _compressionRefreshTimer?.Dispose();
        _compressionRefreshTimer = null;
    }
}
