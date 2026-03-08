// SmallEBot/Components/Chat/Services/ChatPresentationService.cs

using SmallEBot.Components.Chat.ViewModels;
using SmallEBot.Components.Chat.ViewModels.Blocks;
using SmallEBot.Components.Chat.ViewModels.Bubbles;
using SmallEBot.Components.Chat.ViewModels.Reasoning;
using SmallEBot.Core.Models;

namespace SmallEBot.Components.Chat.Services;

/// <summary>
/// Presentation service: converts domain models to view models.
/// </summary>
public sealed class ChatPresentationService
{
    /// <summary>
    /// Convert ChatBubble list to view models.
    /// </summary>
    public IReadOnlyList<BubbleViewBase> ConvertBubbles(
        IReadOnlyList<ChatBubble> bubbles)
    {
        // Shell - will be implemented in Phase 4
        return bubbles.Select(ConvertBubble).ToList();
    }

    private BubbleViewBase ConvertBubble(ChatBubble bubble)
    {
        // Shell - will be implemented in Phase 4
        return bubble switch
        {
            UserBubble u => ConvertUserBubble(u),
            AssistantBubble a => ConvertAssistantBubble(a),
            _ => throw new InvalidOperationException($"Unknown bubble type: {bubble.GetType()}")
        };
    }

    private UserBubbleView ConvertUserBubble(UserBubble bubble)
    {
        // Shell implementation
        return new UserBubbleView
        {
            MessageId = bubble.Message.Id,
            Content = bubble.Message.Content,
            CreatedAt = bubble.Message.CreatedAt,
            IsEdited = bubble.Message.IsEdited,
            AttachedPaths = bubble.Message.AttachedPaths,
            RequestedSkillIds = bubble.Message.RequestedSkillIds
        };
    }

    private AssistantBubbleView ConvertAssistantBubble(AssistantBubble bubble)
    {
        var steps = bubble.Items
            .Select(TimelineItemToStepView)
            .Where(x => x != null)
            .Cast<ReasoningStepView>()
            .ToList();

        return new AssistantBubbleView
        {
            TurnId = bubble.TurnId,
            CreatedAt = bubble.Items.Count > 0 ? bubble.Items[0].CreatedAt : DateTime.UtcNow,
            IsThinkingMode = bubble.IsThinkingMode,
            IsError = IsErrorReply(bubble.Items),
            Steps = steps
        };
    }

    private ReasoningStepView? TimelineItemToStepView(TimelineItem item)
    {
        // Handle think blocks
        if (item.ThinkBlock is { } tb)
            return new ReasoningStepView { IsThink = true, Text = tb.Content };
        // Handle tool calls
        if (item.ToolCall is { } tc)
            return new ReasoningStepView
            {
                IsThink = false,
                ToolName = tc.ToolName,
                ToolArguments = tc.Arguments,
                ToolResult = tc.Result,
                Phase = ToolCallPhase.Completed
            };
        // Handle message content (actual text from assistant)
        if (item.Message is { } msg)
            return new ReasoningStepView { IsThink = false, Text = msg.Content };
        return null;
    }

    private static bool IsErrorReply(IReadOnlyList<TimelineItem> items)
    {
        if (items.Count != 1) return false;
        var item = items[0];
        return item.Message is { Role: "assistant" } msg &&
               msg.Content.StartsWith("Error: ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Convert StreamUpdate list to flat StreamItemView list.
    /// Merges consecutive text updates and consecutive think updates.
    /// Handles tool call lifecycle: Started -> Completed/Failed/Cancelled.
    /// Returns items ordered by SortOrder.
    /// </summary>
    public IReadOnlyList<StreamItemView> ConvertToStreamItems(
        IReadOnlyList<StreamUpdate> updates)
    {
        var items = new List<StreamItemView>();
        var order = 0;
        // Dictionary stores: CallId -> (Item, Position in items list)
        var toolCallsInProgress = new Dictionary<string, (ToolCallItemView Item, int Order)>();

        string? textBuffer = null;
        string? thinkBuffer = null;

        foreach (var update in updates)
        {
            switch (update)
            {
                case TextStreamUpdate text:
                    // Flush think buffer before text
                    FlushThinkBuffer(ref thinkBuffer, items, ref order);
                    // Merge consecutive text
                    textBuffer = (textBuffer ?? "") + text.Text;
                    break;

                case ThinkStreamUpdate think:
                    // Flush text buffer before think
                    FlushTextBuffer(ref textBuffer, items, ref order);
                    // Merge consecutive think
                    thinkBuffer = (thinkBuffer ?? "") + think.Text;
                    break;

                case ToolCallStreamUpdate tc:
                    // Flush both buffers before tool call
                    FlushThinkBuffer(ref thinkBuffer, items, ref order);
                    FlushTextBuffer(ref textBuffer, items, ref order);

                    if (tc.Phase == ToolCallPhase.Started)
                    {
                        // Create new tool call item and immediately add to list
                        var callId = tc.CallId ?? Guid.NewGuid().ToString();
                        var item = new ToolCallItemView
                        {
                            CallId = callId,
                            ToolName = tc.ToolName,
                            Arguments = tc.Arguments,
                            Phase = ToolCallPhase.Started,
                            SortOrder = order++,
                            Elapsed = tc.Elapsed
                        };
                        toolCallsInProgress[callId] = (item, items.Count);
                        items.Add(item);  // IMMEDIATELY add to list
                    }
                    else if (tc.Phase is ToolCallPhase.Completed or ToolCallPhase.Failed or ToolCallPhase.Cancelled)
                    {
                        // Result update: in-place update in items list
                        var callId = tc.CallId ?? "";
                        if (toolCallsInProgress.TryGetValue(callId, out var pending))
                        {
                            var updated = pending.Item with
                            {
                                Result = tc.Result,
                                Phase = tc.Phase,
                                Elapsed = tc.Elapsed
                            };
                            items[pending.Order] = updated;  // IN-PLACE update
                            toolCallsInProgress.Remove(callId);  // REMOVE from dictionary
                        }
                    }
                    break;

                case ApprovalRequestStreamUpdate approval:
                    // Flush both buffers before approval request
                    FlushThinkBuffer(ref thinkBuffer, items, ref order);
                    FlushTextBuffer(ref textBuffer, items, ref order);

                    items.Add(new ApprovalItemView
                    {
                        CallId = approval.CallId,
                        ToolName = approval.ToolName,
                        Arguments = approval.Arguments,
                        State = ApprovalState.Pending,
                        ConversationId = approval.ConversationId,
                        FunctionCallId = approval.FunctionCallId,
                        RawArguments = approval.RawArguments,
                        SortOrder = order++
                    });
                    break;
            }
        }

        // Flush remaining buffers
        FlushThinkBuffer(ref thinkBuffer, items, ref order);
        FlushTextBuffer(ref textBuffer, items, ref order);

        return items;
    }

    /// <summary>
    /// Convert persisted AssistantBubbleView to IBubbleBlock list for unified rendering.
    /// </summary>
    public IReadOnlyList<IBubbleBlock> ConvertToBubbleBlocks(AssistantBubbleView bubble)
    {
        var blocks = new List<IBubbleBlock>();
        foreach (var step in bubble.Steps)
        {
            if (step.IsThink && !string.IsNullOrEmpty(step.Text))
                blocks.Add(new ReasoningBlockModel(step.Text));
            else if (!string.IsNullOrEmpty(step.ToolName))
                blocks.Add(new ToolCallBlockModel(
                    CallId: "",
                    Name: step.ToolName,
                    Phase: step.Phase,
                    Arguments: step.ToolArguments,
                    Result: step.ToolResult,
                    Error: null,
                    Elapsed: step.Elapsed));
            else if (!string.IsNullOrEmpty(step.Text))
                blocks.Add(new TextBlock(step.Text));
        }
        return blocks;
    }

    /// <summary>
    /// Convert streaming StreamItemView list to IBubbleBlock list for unified rendering.
    /// </summary>
    public IReadOnlyList<IBubbleBlock> ConvertStreamToBubbleBlocks(IReadOnlyList<StreamItemView> items)
    {
        var blocks = new List<IBubbleBlock>();
        foreach (var item in items)
        {
            switch (item)
            {
                case ThinkItemView think:
                    blocks.Add(new ReasoningBlockModel(think.Content));
                    break;
                case TextItemView text:
                    blocks.Add(new TextBlock(text.Content));
                    break;
                case ToolCallItemView tc:
                    blocks.Add(new ToolCallBlockModel(
                        CallId: tc.CallId,
                        Name: tc.ToolName,
                        Phase: tc.Phase,
                        Arguments: tc.Arguments,
                        Result: tc.Result,
                        Error: null,
                        Elapsed: tc.Elapsed));
                    break;
                case ApprovalItemView approval:
                    blocks.Add(new ApprovalBlockModel(
                        CallId: approval.CallId,
                        ToolName: approval.ToolName,
                        Arguments: approval.Arguments,
                        State: approval.State,
                        ConversationId: approval.ConversationId,
                        FunctionCallId: approval.FunctionCallId,
                        RawArguments: approval.RawArguments));
                    break;
            }
        }
        return blocks;
    }

    private static void FlushTextBuffer(ref string? buffer, List<StreamItemView> items, ref int order)
    {
        if (string.IsNullOrEmpty(buffer)) return;
        items.Add(new TextItemView
        {
            Content = buffer,
            SortOrder = order++
        });
        buffer = null;
    }

    private static void FlushThinkBuffer(ref string? buffer, List<StreamItemView> items, ref int order)
    {
        if (string.IsNullOrEmpty(buffer)) return;
        items.Add(new ThinkItemView
        {
            Content = buffer,
            SortOrder = order++
        });
        buffer = null;
    }
}
