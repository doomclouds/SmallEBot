using System.Text.Json;
using AIAgentSession = Microsoft.Agents.AI.AgentSession;
using Microsoft.Agents.AI;

namespace SmallEBot.Infrastructure.Persistence.AgentSession;

/// <summary>
/// Serializes and deserializes AgentSession using AIAgent's serialization API.
/// Note: Serialization methods are on AIAgent, not AgentSession itself.
/// </summary>
public class AgentSessionSerializer(AIAgent agent)
{
    private readonly AIAgent _agent = agent ?? throw new ArgumentNullException(nameof(agent));

    /// <summary>
    /// Serializes an AgentSession to JsonElement using the AIAgent's API.
    /// </summary>
    public async ValueTask<JsonElement> SerializeAsync(AIAgentSession session, CancellationToken ct = default)
    {
        return await _agent.SerializeSessionAsync(session, cancellationToken: ct);
    }

    /// <summary>
    /// Deserializes a JsonElement to AgentSession using the AIAgent's API.
    /// </summary>
    public async ValueTask<AIAgentSession> DeserializeAsync(JsonElement json, CancellationToken ct = default)
    {
        return await _agent.DeserializeSessionAsync(json, cancellationToken: ct);
    }

    /// <summary>
    /// Serializes an AgentSession to JSON string.
    /// </summary>
    public async Task<string> SerializeToStringAsync(AIAgentSession session, CancellationToken ct = default)
    {
        var jsonElement = await SerializeAsync(session, ct);
        return jsonElement.GetRawText();
    }

    /// <summary>
    /// Deserializes a JSON string to AgentSession.
    /// </summary>
    public async Task<AIAgentSession> DeserializeFromStringAsync(string json, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(json);
        return await DeserializeAsync(doc.RootElement, ct);
    }
}
