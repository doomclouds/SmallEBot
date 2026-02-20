using SmallEBot.Application.Streaming;
using SmallEBot.Core.Models;
using SmallEBot.Core.Repositories;
using ConversationEntity = SmallEBot.Core.Entities.Conversation;

namespace SmallEBot.Application.Conversation;

public sealed class AgentConversationService(
    IConversationRepository repository,
    IAgentRunner agentRunner,
    ICommandConfirmationContext commandConfirmationContext,
    IConversationTaskContext conversationTaskContext) : IAgentConversationService
{
    public Task<ConversationEntity> CreateConversationAsync(string userName, CancellationToken cancellationToken = default) =>
        repository.CreateAsync(userName, "New conversation", cancellationToken);

    public Task<List<ConversationEntity>> GetConversationsAsync(string userName, CancellationToken cancellationToken = default) =>
        repository.GetListAsync(userName, cancellationToken);

    public Task<List<ConversationEntity>> SearchConversationsAsync(string userName, string query, CancellationToken cancellationToken = default) =>
        repository.SearchAsync(userName, query, false, cancellationToken);

    public Task<ConversationEntity?> GetConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default) =>
        repository.GetByIdAsync(id, userName, cancellationToken);

    public Task<bool> DeleteConversationAsync(Guid id, string userName, CancellationToken cancellationToken = default) =>
        repository.DeleteAsync(id, userName, cancellationToken);

    public Task<int> GetMessageCountAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        repository.GetMessageCountAsync(conversationId, cancellationToken);

    public async Task<Guid> CreateTurnAndUserMessageAsync(
        Guid conversationId,
        string userName,
        string userMessage,
        bool useThinking,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null)
    {
        var count = await repository.GetMessageCountAsync(conversationId, cancellationToken);
        var newTitle = count == 0 ? await agentRunner.GenerateTitleAsync(userMessage, cancellationToken) : null;
        return await repository.AddTurnAndUserMessageAsync(conversationId, userName, userMessage, useThinking, newTitle, cancellationToken);
    }

    public async Task StreamResponseAndCompleteAsync(
        Guid conversationId,
        Guid turnId,
        string userMessage,
        bool useThinking,
        IStreamSink sink,
        CancellationToken cancellationToken = default,
        string? commandConfirmationContextId = null,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null)
    {
        commandConfirmationContext.SetCurrentId(commandConfirmationContextId);
        conversationTaskContext.SetConversationId(conversationId);
        try
        {
            var updates = new List<StreamUpdate>();
            await foreach (var update in agentRunner.RunStreamingAsync(conversationId, userMessage, useThinking, cancellationToken, attachedPaths, requestedSkillIds))
            {
                updates.Add(update);
                await sink.OnNextAsync(update, cancellationToken);
            }
            var segments = StreamUpdateToSegments.ToSegments(updates, useThinking);
            await repository.CompleteTurnWithAssistantAsync(conversationId, turnId, segments, cancellationToken);
        }
        finally
        {
            conversationTaskContext.SetConversationId(null);
        }
    }

    public Task CompleteTurnWithAssistantAsync(
        Guid conversationId,
        Guid turnId,
        IReadOnlyList<AssistantSegment> segments,
        CancellationToken cancellationToken = default) =>
        repository.CompleteTurnWithAssistantAsync(conversationId, turnId, segments, cancellationToken);

    public Task CompleteTurnWithErrorAsync(
        Guid conversationId,
        Guid turnId,
        string errorMessage,
        CancellationToken cancellationToken = default) =>
        repository.CompleteTurnWithErrorAsync(conversationId, turnId, errorMessage, cancellationToken);

    public Task<(Guid TurnId, string UserMessage)?> ReplaceUserMessageAsync(
        Guid conversationId,
        string userName,
        Guid messageId,
        string newContent,
        bool useThinking,
        CancellationToken cancellationToken = default) =>
        repository.ReplaceUserMessageAsync(conversationId, userName, messageId, newContent, useThinking, cancellationToken);

    public Task<(Guid TurnId, string UserMessage, bool UseThinking)?> PrepareTurnForRegenerateAsync(
        Guid conversationId,
        string userName,
        Guid turnId,
        CancellationToken cancellationToken = default) =>
        repository.GetTurnForRegenerateAsync(conversationId, userName, turnId, cancellationToken);

    public async Task ReplaceMessageAndRegenerateAsync(
        Guid conversationId,
        string userName,
        Guid messageId,
        string newContent,
        bool useThinking,
        IStreamSink sink,
        CancellationToken cancellationToken = default,
        string? commandConfirmationContextId = null,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null)
    {
        var result = await repository.ReplaceUserMessageAsync(conversationId, userName, messageId, newContent, useThinking, cancellationToken);
        if (result == null) return;

        commandConfirmationContext.SetCurrentId(commandConfirmationContextId);
        conversationTaskContext.SetConversationId(conversationId);
        try
        {
            var updates = new List<StreamUpdate>();
            await foreach (var update in agentRunner.RunStreamingAsync(conversationId, result.Value.UserMessage, useThinking, cancellationToken, attachedPaths, requestedSkillIds))
            {
                updates.Add(update);
                await sink.OnNextAsync(update, cancellationToken);
            }
            var segments = StreamUpdateToSegments.ToSegments(updates, useThinking);
            await repository.CompleteTurnWithAssistantAsync(conversationId, result.Value.TurnId, segments, cancellationToken);
        }
        finally
        {
            conversationTaskContext.SetConversationId(null);
        }
    }

    public async Task RegenerateAsync(
        Guid conversationId,
        string userName,
        Guid turnId,
        IStreamSink sink,
        CancellationToken cancellationToken = default,
        string? commandConfirmationContextId = null,
        IReadOnlyList<string>? attachedPaths = null,
        IReadOnlyList<string>? requestedSkillIds = null)
    {
        var result = await repository.GetTurnForRegenerateAsync(conversationId, userName, turnId, cancellationToken);
        if (result == null) return;

        commandConfirmationContext.SetCurrentId(commandConfirmationContextId);
        conversationTaskContext.SetConversationId(conversationId);
        try
        {
            var updates = new List<StreamUpdate>();
            await foreach (var update in agentRunner.RunStreamingAsync(conversationId, result.Value.UserMessage, result.Value.UseThinking, cancellationToken, attachedPaths, requestedSkillIds))
            {
                updates.Add(update);
                await sink.OnNextAsync(update, cancellationToken);
            }
            var segments = StreamUpdateToSegments.ToSegments(updates, result.Value.UseThinking);
            await repository.CompleteTurnWithAssistantAsync(conversationId, result.Value.TurnId, segments, cancellationToken);
        }
        finally
        {
            conversationTaskContext.SetConversationId(null);
        }
    }
}
