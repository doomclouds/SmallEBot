using Microsoft.Extensions.AI;
using SmallEBot.Components.Chat.ViewModels.Blocks;
using SmallEBot.Core.Models;

namespace SmallEBot.Components.Chat.Services;

/// <summary>
/// Presentation service: converts ChatMessage content and StreamUpdate sequences to IBubbleBlock lists.
/// </summary>
public sealed class ChatPresentationService
{
    /// <summary>
    /// Convert a persisted assistant ChatMessage into IBubbleBlock list for rendering.
    /// Parses AIContent items (TextContent, TextReasoningContent, FunctionCallContent, FunctionResultContent).
    /// </summary>
    public IReadOnlyList<IBubbleBlock> ConvertMessageToBlocks(ChatMessage message)
    {
        var blocks = new List<IBubbleBlock>();
        if (message.Contents is not { Count: > 0 }) return blocks;

        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case TextReasoningContent reasoning when !string.IsNullOrEmpty(reasoning.Text):
                    blocks.Add(new ReasoningBlockModel(reasoning.Text));
                    break;
                case TextContent text when !string.IsNullOrEmpty(text.Text):
                    blocks.Add(new TextBlock(text.Text));
                    break;
                case FunctionCallContent fnCall:
                    blocks.Add(new ToolCallBlockModel(
                        CallId: fnCall.CallId ?? "",
                        Name: fnCall.Name,
                        Phase: ToolCallPhase.Started,
                        Arguments: fnCall.Arguments?.ToString(),
                        Result: null,
                        Error: null,
                        Elapsed: null));
                    break;
                case FunctionResultContent fnResult:
                    blocks.Add(new ToolCallBlockModel(
                        CallId: fnResult.CallId ?? "",
                        Name: fnResult.CallId ?? "tool",
                        Phase: ToolCallPhase.Completed,
                        Arguments: null,
                        Result: fnResult.Result?.ToString(),
                        Error: null,
                        Elapsed: null));
                    break;
            }
        }

        return blocks;
    }

    /// <summary>
    /// Convert StreamUpdate list to IBubbleBlock list for live rendering.
    /// Merges consecutive text/think updates. Handles tool call lifecycle.
    /// </summary>
    public IReadOnlyList<IBubbleBlock> ConvertStreamToBubbleBlocks(
        IReadOnlyList<StreamUpdate> updates,
        IReadOnlyDictionary<string, ApprovalBlockModel>? pendingApprovals = null)
    {
        var blocks = new List<IBubbleBlock>();
        var toolCallsInProgress = new Dictionary<string, int>();

        string? textBuffer = null;
        string? thinkBuffer = null;

        foreach (var update in updates)
        {
            switch (update)
            {
                case TextStreamUpdate text:
                    FlushThinkBuffer(ref thinkBuffer, blocks);
                    textBuffer = (textBuffer ?? "") + text.Text;
                    break;

                case ThinkStreamUpdate think:
                    FlushTextBuffer(ref textBuffer, blocks);
                    thinkBuffer = (thinkBuffer ?? "") + think.Text;
                    break;

                case ToolCallStreamUpdate tc:
                    FlushThinkBuffer(ref thinkBuffer, blocks);
                    FlushTextBuffer(ref textBuffer, blocks);

                    if (tc.Phase == ToolCallPhase.Started)
                    {
                        var callId = tc.CallId ?? Guid.NewGuid().ToString();
                        blocks.Add(new ToolCallBlockModel(
                            CallId: callId,
                            Name: tc.ToolName,
                            Phase: ToolCallPhase.Started,
                            Arguments: tc.Arguments,
                            Result: null,
                            Error: null,
                            Elapsed: tc.Elapsed));
                        toolCallsInProgress[callId] = blocks.Count - 1;
                    }
                    else if (tc.Phase is ToolCallPhase.Completed or ToolCallPhase.Failed or ToolCallPhase.Cancelled)
                    {
                        var callId = tc.CallId ?? "";
                        if (toolCallsInProgress.TryGetValue(callId, out var idx) && idx < blocks.Count && blocks[idx] is ToolCallBlockModel existing)
                        {
                            blocks[idx] = existing with { Result = tc.Result, Phase = tc.Phase, Elapsed = tc.Elapsed };
                            toolCallsInProgress.Remove(callId);
                        }
                    }
                    break;

                case ApprovalRequestStreamUpdate approval:
                    FlushThinkBuffer(ref thinkBuffer, blocks);
                    FlushTextBuffer(ref textBuffer, blocks);
                    var approvalState = ApprovalState.Pending;
                    if (pendingApprovals != null && pendingApprovals.TryGetValue(approval.CallId, out var pending))
                        approvalState = pending.State;
                    blocks.Add(new ApprovalBlockModel(
                        CallId: approval.CallId,
                        ToolName: approval.ToolName,
                        Arguments: approval.Arguments,
                        State: approvalState,
                        ConversationId: approval.ConversationId,
                        FunctionCallId: approval.FunctionCallId,
                        RawArguments: approval.RawArguments));
                    break;
            }
        }

        FlushThinkBuffer(ref thinkBuffer, blocks);
        FlushTextBuffer(ref textBuffer, blocks);
        return blocks;
    }

    private static void FlushTextBuffer(ref string? buffer, List<IBubbleBlock> blocks)
    {
        if (string.IsNullOrEmpty(buffer)) return;
        blocks.Add(new TextBlock(buffer));
        buffer = null;
    }

    private static void FlushThinkBuffer(ref string? buffer, List<IBubbleBlock> blocks)
    {
        if (string.IsNullOrEmpty(buffer)) return;
        blocks.Add(new ReasoningBlockModel(buffer));
        buffer = null;
    }
}
