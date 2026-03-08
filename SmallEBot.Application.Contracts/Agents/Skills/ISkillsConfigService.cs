using SmallEBot.Core.Models;

namespace SmallEBot.Application.Contracts.Agents.Skills;

/// <summary>
/// Service for managing skills configuration.
/// Skills are loaded from sys.skills/ and skills/ directories under the workspace.
/// </summary>
public interface ISkillsConfigService
{
    /// <summary>
    /// Gets all skill metadata (both system and user skills).
    /// </summary>
    Task<IReadOnlyList<SkillMetadata>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets skill metadata for agent use.
    /// </summary>
    Task<IReadOnlyList<SkillMetadata>> GetMetadataForAgentAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates a new user skill with the specified metadata.
    /// </summary>
    Task AddUserSkillAsync(string id, string name, string description, string? body = null, CancellationToken ct = default);

    /// <summary>
    /// Deletes a user skill by ID.
    /// </summary>
    Task DeleteUserSkillAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Imports a skill from file contents dictionary (key: relative path, value: content).
    /// Must contain SKILL.md file.
    /// </summary>
    Task ImportUserSkillFromFileContentsAsync(string? id, IReadOnlyDictionary<string, string> fileContents, CancellationToken ct = default);

    /// <summary>
    /// Returns the raw content of SKILL.md for the given skill id, or null if not found.
    /// </summary>
    Task<string?> GetSkillContentAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Creates a new skill directory and returns its absolute path.
    /// </summary>
    Task<string> CreateSkillAsync(string skillId, CancellationToken ct = default);

    /// <summary>
    /// Writes a file to a skill directory.
    /// </summary>
    Task WriteSkillFileAsync(string skillId, string relativePath, string content, CancellationToken ct = default);
}
