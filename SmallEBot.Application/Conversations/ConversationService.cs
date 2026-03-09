using Microsoft.Extensions.AI;
using SmallEBot.Application.Contracts.Conversations;
using SmallEBot.Application.Contracts.Conversations.Session;
using SmallEBot.Application.Contracts.Conversations.TaskList;
using SmallEBot.Domain.Conversations.Metadata;

namespace SmallEBot.Application.Conversations;

/// <summary>Orchestrates conversation CRUD. No Agent dependency.</summary>
public sealed class ConversationService(
    IConversationMetadataRepository metadataRepository,
    IAgentSessionReader sessionReader,
    ITaskListService taskListService) : IConversationService
{
    public async Task<ConversationDto> CreateConversationAsync(string userName, string title = "New conversation", CancellationToken cancellationToken = default)
    {
        var metadata = ConversationMetadata.Create(userName, title);
        await metadataRepository.SaveAsync(metadata, cancellationToken);
        return ToDto(metadata);
    }

    public async Task<List<ConversationDto>> GetConversationsAsync(string userName, CancellationToken cancellationToken = default)
    {
        var list = await metadataRepository.GetByUserNameAsync(userName, cancellationToken);
        return list.Select(ToDto).ToList();
    }

    public async Task<List<ConversationDto>> SearchConversationsAsync(string userName, string query, CancellationToken cancellationToken = default)
    {
        var list = await metadataRepository.SearchAsync(userName, query, cancellationToken);
        return list.Select(ToDto).ToList();
    }

    public async Task<ConversationDto?> GetConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default)
    {
        var m = await metadataRepository.GetByIdAsync(id, cancellationToken);
        if (m == null || m.UserName != userName) return null;
        return ToDto(m);
    }

    public async Task<bool> DeleteConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default)
    {
        var m = await metadataRepository.GetByIdAsync(id, cancellationToken);
        if (m == null || m.UserName != userName) return false;
        await metadataRepository.DeleteAsync(id, cancellationToken);
        await taskListService.ClearTasksAsync(id, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        return await sessionReader.GetMessagesAsync(conversationId, cancellationToken);
    }

    public async Task<bool> HasMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var messages = await sessionReader.GetMessagesAsync(conversationId, cancellationToken);
        return messages.Any(m => m.Role == ChatRole.User);
    }

    public async Task SetTitleAsync(Guid conversationId, string title, CancellationToken cancellationToken = default)
    {
        var metadata = await metadataRepository.GetByIdAsync(conversationId, cancellationToken);
        if (metadata == null) return;
        metadata.SetTitle(title);
        await metadataRepository.SaveAsync(metadata, cancellationToken);
    }

    private static ConversationDto ToDto(ConversationMetadata m) => new()
    {
        Id = m.Id,
        Title = m.Title,
        UserName = m.UserName,
        CreatedAt = m.CreatedAt,
        UpdatedAt = m.UpdatedAt,
        CompressedContext = m.CompressedContext,
        CompressedAt = m.CompressedAt
    };
}
