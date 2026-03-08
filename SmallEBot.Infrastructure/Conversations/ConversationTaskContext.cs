using SmallEBot.Application.Contracts.Conversations.Context;

namespace SmallEBot.Infrastructure.Conversations;

/// <summary>Stores the current conversation id in AsyncLocal so task list tools use the correct file.</summary>
public sealed class ConversationTaskContext : IConversationTaskContext
{
    private static readonly AsyncLocal<Guid?> CurrentId = new();

    /// <inheritdoc />
    public void SetConversationId(Guid? id) => CurrentId.Value = id;

    /// <inheritdoc />
    public Guid? GetConversationId() => CurrentId.Value;
}
