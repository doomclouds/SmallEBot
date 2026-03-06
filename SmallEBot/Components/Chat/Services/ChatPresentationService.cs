// SmallEBot/Components/Chat/Services/ChatPresentationService.cs

using SmallEBot.Components.Chat.ViewModels;
using SmallEBot.Components.Chat.ViewModels.Bubbles;
using SmallEBot.Components.Chat.ViewModels.Reasoning;
using SmallEBot.Components.Chat.ViewModels.Streaming;
using SmallEBot.Core.Models;

namespace SmallEBot.Components.Chat.Services;

/// <summary>
/// Presentation service: converts domain models to view models.
/// </summary>
public sealed class ChatPresentationService
{
    /// <summary>
    /// Internal representation of a reasoning step during streaming.
    /// </summary>
    private sealed class ReasoningStep
    {
        public bool IsThink { get; init; }
        public string? Text { get; set; }
        public string? ToolName { get; init; }
        public string? ToolCallId { get; init; }
        public string? ToolArguments { get; init; }
        public string? ToolResult { get; set; }
        public ToolCallPhase Phase { get; set; }
        public TimeSpan? Elapsed { get; set; }
    }

    /// <summary>
    /// Internal representation of a stream display item.
    /// </summary>
    private sealed class StreamDisplayItem
    {
        public bool IsReasoningBlock { get; init; }
        public List<ReasoningStep>? ReasoningSteps { get; init; }
        public bool IsText { get; init; }
        public string? Text { get; init; }
        public bool IsReplyTool { get; init; }
        public string? ToolName { get; init; }
        public string? ToolCallId { get; init; }
        public string? ToolArguments { get; init; }
        public string? ToolResult { get; set; }
        public ToolCallPhase Phase { get; set; }
        public TimeSpan? Elapsed { get; set; }
    }

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

    /// <summary>
    /// Convert streaming updates to display item views.
    /// </summary>
    public IReadOnlyList<StreamingDisplayItemView> ConvertStreamingUpdates(
        IReadOnlyList<StreamUpdate> updates)
    {
        var items = GetStreamingDisplayItems(updates).ToList();
        var views = new List<StreamingDisplayItemView>();
        foreach (var x in items)
        {
            if (x is { IsReasoningBlock: true, ReasoningSteps: not null })
            {
                views.Add(new StreamingDisplayItemView
                {
                    IsReasoningBlock = true,
                    Steps = x.ReasoningSteps.Select(ToReasoningStepView).ToList()
                });
            }
            else if (x.IsText)
            {
                views.Add(new StreamingDisplayItemView { IsText = true, Text = x.Text });
            }
            else if (x.IsReplyTool)
            {
                views.Add(new StreamingDisplayItemView
                {
                    IsReplyTool = true,
                    ToolName = x.ToolName,
                    ToolCallId = x.ToolCallId,
                    ToolArguments = x.ToolArguments,
                    ToolResult = x.ToolResult,
                    Phase = x.Phase,
                    Elapsed = x.Elapsed
                });
            }
        }
        return views;
    }

    /// <summary>
    /// Boundary rule: after think appears, everything until text is part of reasoning; once text is seen, further content goes to reply.
    /// </summary>
    private IEnumerable<StreamDisplayItem> GetStreamingDisplayItems(IReadOnlyList<StreamUpdate> updates)
    {
        var reasoningSteps = new List<ReasoningStep>();
        var replyItems = new List<StreamDisplayItem>();
        var seenText = false;

        foreach (var update in updates)
        {
            if (update is TextStreamUpdate t)
            {
                seenText = true;
                if (replyItems.Count > 0 && replyItems[^1] is { IsText: true } lastText)
                    replyItems[^1] = new StreamDisplayItem { IsText = true, Text = (lastText.Text ?? "") + t.Text };
                else
                    replyItems.Add(new StreamDisplayItem { IsText = true, Text = t.Text });
                continue;
            }
            if (update is ThinkStreamUpdate think)
            {
                if (seenText)
                {
                    if (replyItems.Count > 0 && replyItems[^1] is { IsText: true } lt)
                        replyItems[^1] = new StreamDisplayItem { IsText = true, Text = (lt.Text ?? "") + think.Text };
                    else
                        replyItems.Add(new StreamDisplayItem { IsText = true, Text = think.Text });
                }
                else
                {
                    if (reasoningSteps.Count > 0 && reasoningSteps[^1].IsThink)
                        reasoningSteps[^1].Text = (reasoningSteps[^1].Text ?? "") + think.Text;
                    else
                        reasoningSteps.Add(new ReasoningStep { IsThink = true, Text = think.Text });
                }
                continue;
            }
            if (update is ToolCallStreamUpdate tc)
            {
                if (tc.Phase is ToolCallPhase.Completed or ToolCallPhase.Failed or ToolCallPhase.Cancelled)
                {
                    var lastReplyTool = replyItems.LastOrDefault(x => x.IsReplyTool && x.ToolCallId == tc.CallId);
                    if (lastReplyTool != null)
                    {
                        lastReplyTool.ToolResult = tc.Result;
                        lastReplyTool.Phase = tc.Phase;
                        lastReplyTool.Elapsed = tc.Elapsed;
                    }
                    else
                    {
                        var lastReasoningTool = reasoningSteps.LastOrDefault(x => !x.IsThink && x.ToolCallId == tc.CallId);
                        if (lastReasoningTool != null)
                        {
                            lastReasoningTool.ToolResult = tc.Result;
                            lastReasoningTool.Phase = tc.Phase;
                            lastReasoningTool.Elapsed = tc.Elapsed;
                        }
                    }
                    continue;
                }
                if (string.IsNullOrEmpty(tc.ToolName) && tc.CallId == null)
                    continue;
                var toolItem = new StreamDisplayItem
                {
                    IsReplyTool = true,
                    ToolCallId = tc.CallId,
                    ToolName = tc.ToolName,
                    ToolArguments = tc.Arguments,
                    Phase = tc.Phase,
                    Elapsed = tc.Elapsed
                };
                var reasoningToolStep = new ReasoningStep
                {
                    IsThink = false,
                    ToolCallId = tc.CallId,
                    ToolName = tc.ToolName,
                    ToolArguments = tc.Arguments,
                    Phase = tc.Phase,
                    Elapsed = tc.Elapsed
                };
                if (seenText)
                    replyItems.Add(toolItem);
                else
                    reasoningSteps.Add(reasoningToolStep);
            }
        }

        var result = new List<StreamDisplayItem>();
        if (reasoningSteps.Count > 0)
        {
            result.Add(new StreamDisplayItem
            {
                IsReasoningBlock = true,
                ReasoningSteps = reasoningSteps
            });
        }
        result.AddRange(replyItems);
        return result;
    }

    private static ReasoningStepView ToReasoningStepView(ReasoningStep step)
    {
        return step.IsThink
            ? new ReasoningStepView { IsThink = true, Text = step.Text ?? "" }
            : new ReasoningStepView { IsThink = false, ToolName = step.ToolName, ToolArguments = step.ToolArguments, ToolResult = step.ToolResult, Phase = step.Phase, Elapsed = step.Elapsed };
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
                            ToolName = tc.ToolName ?? "unknown",
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
