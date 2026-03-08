// SmallEBot/Components/Chat/Services/ChatPresentationService.cs

using SmallEBot.Components.Chat.ViewModels.Blocks;
using SmallEBot.Core.Models;

namespace SmallEBot.Components.Chat.Services;

/// <summary>
/// Presentation service: converts domain models for display.
/// </summary>
public sealed class ChatPresentationService
{
    /// <summary>
    /// Convert persisted AssistantBubble to IBubbleBlock list for unified rendering.
    /// </summary>
    public IReadOnlyList<IBubbleBlock> ConvertToBlocks(AssistantBubble bubble)
    {
        return bubble.Items
            .Select(TimelineItemToBlock)
            .Where(x => x != null)
            .Cast<IBubbleBlock>()
            .ToList();
    }

    private static IBubbleBlock? TimelineItemToBlock(TimelineItem item)
    {
        if (item.ThinkBlock is { } tb)
            return new ReasoningBlockModel(tb.Content);
        if (item.ToolCall is { } tc)
            return new ToolCallBlockModel(
                CallId: "",
                Name: tc.ToolName,
                Phase: ToolCallPhase.Completed,
                Arguments: tc.Arguments,
                Result: tc.Result,
                Error: null,
                Elapsed: null);
        if (item.Message is { } msg)
            return new TextBlock(msg.Content);
        return null;
    }

    /// <summary>
    /// Convert StreamUpdate list to IBubbleBlock list for unified rendering.
    /// Merges consecutive text/think updates. Handles tool call lifecycle.
    /// Uses pendingApprovals to reflect approval state (Approved/Rejected/Completed).
    /// </summary>
    public IReadOnlyList<IBubbleBlock> ConvertStreamToBubbleBlocks(
        IReadOnlyList<StreamUpdate> updates,
        IReadOnlyDictionary<string, ApprovalBlockModel>? pendingApprovals = null)
    {
        var blocks = new List<IBubbleBlock>();
        var toolCallsInProgress = new Dictionary<string, int>(); // CallId -> index in blocks

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
