// SmallEBot.Domain/Conversations/Turn.cs
using SmallEBot.Domain.Common;
using SmallEBot.Domain.Conversations.ValueObjects;

namespace SmallEBot.Domain.Conversations;

/// <summary>
/// Represents a single turn in a conversation (user message + assistant response).
/// </summary>
public class Turn : IEntity<Guid>
{
    public Guid Id { get; init; }
    public DateTime CreatedAt { get; init; }
    public UserTurnMessage UserMessage { get; private set; }
    public AssistantTurnResponse? AssistantResponse { get; private set; }

    public Turn(
        Guid id,
        DateTime createdAt,
        UserTurnMessage userMessage,
        AssistantTurnResponse? assistantResponse = null)
    {
        Id = id;
        CreatedAt = createdAt;
        UserMessage = userMessage;
        AssistantResponse = assistantResponse;
    }

    /// <summary>
    /// Sets the assistant response for this turn.
    /// </summary>
    public void SetAssistantResponse(AssistantTurnResponse response)
    {
        AssistantResponse = response ?? AssistantTurnResponse.Empty;
    }

    /// <summary>
    /// Updates the user message content.
    /// </summary>
    public void UpdateUserMessage(string newContent, string[]? attachedPaths = null, string[]? requestedSkillIds = null)
    {
        UserMessage = new UserTurnMessage(
            newContent,
            attachedPaths ?? UserMessage.AttachedPaths,
            requestedSkillIds ?? UserMessage.RequestedSkillIds);
    }
}
