using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SmallEBot.Services.Agent.Tools.SkillGeneration;
using SmallEBot.Services.Skills;

namespace SmallEBot.Services.Agent.Tools;

/// <summary>Provides skill generation tools.</summary>
public sealed class SkillGenerationToolProvider(
    ISkillsConfigService skillsConfig) : IToolProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string Name => "SkillGeneration";
    public bool IsEnabled => true;

    public IEnumerable<AITool> GetTools()
    {
        yield return AIFunctionFactory.Create(GenerateSkill);
    }

    [Description("Create a new skill in .agents/skills/<skillId>/ with SKILL.md and optional examples/references/scripts directories. Parameters: skillId (lowercase-hyphen format), name (display name), description (< 1024 chars), instructions (markdown body), examples/references/scripts (arrays of {filename, content}). Returns { ok, skillPath, filesCreated } on success or { ok: false, error } on failure.")]
    private async Task<string> GenerateSkill(string inputJson)
    {
        GenerateSkillInput? input;
        try
        {
            input = JsonSerializer.Deserialize<GenerateSkillInput>(inputJson, JsonOptions);
        }
        catch
        {
            return JsonSerializer.Serialize(new { ok = false, error = "Invalid JSON input." }, JsonOptions);
        }

        if (input == null)
            return JsonSerializer.Serialize(new { ok = false, error = "Input is required." }, JsonOptions);

        // Validate skillId format
        if (string.IsNullOrWhiteSpace(input.SkillId))
            return JsonSerializer.Serialize(new { ok = false, error = "skillId is required." }, JsonOptions);

        if (!IsValidSkillId(input.SkillId))
            return JsonSerializer.Serialize(new { ok = false, error = "Invalid skillId format. Use lowercase letters, numbers, and hyphens only. Must start with a letter." }, JsonOptions);

        // Validate required fields
        if (string.IsNullOrWhiteSpace(input.Name))
            return JsonSerializer.Serialize(new { ok = false, error = "name is required." }, JsonOptions);

        if (string.IsNullOrWhiteSpace(input.Description))
            return JsonSerializer.Serialize(new { ok = false, error = "description is required." }, JsonOptions);

        if (input.Description.Length > 1024)
            return JsonSerializer.Serialize(new { ok = false, error = "description must be less than 1024 characters." }, JsonOptions);

        if (string.IsNullOrWhiteSpace(input.Instructions))
            return JsonSerializer.Serialize(new { ok = false, error = "instructions is required." }, JsonOptions);

        // Check if skill already exists
        var metadata = await skillsConfig.GetAllAsync(CancellationToken.None);
        if (metadata.Any(s => s.Id.Equals(input.SkillId, StringComparison.OrdinalIgnoreCase)))
            return JsonSerializer.Serialize(new { ok = false, error = $"Skill '{input.SkillId}' already exists." }, JsonOptions);

        // Create skill via config service
        try
        {
            var filesCreated = new List<string>();
            await skillsConfig.CreateSkillAsync(input.SkillId, CancellationToken.None);

            // Create SKILL.md
            var skillContent = BuildSkillContent(input);
            await skillsConfig.WriteSkillFileAsync(input.SkillId, "SKILL.md", skillContent, CancellationToken.None);
            filesCreated.Add("SKILL.md");

            // Create examples
            if (input.Examples != null)
            {
                foreach (var ex in input.Examples)
                {
                    var path = $"examples/{ex.Filename}";
                    await skillsConfig.WriteSkillFileAsync(input.SkillId, path, ex.Content, CancellationToken.None);
                    filesCreated.Add(path);
                }
            }

            // Create references
            if (input.References != null)
            {
                foreach (var @ref in input.References)
                {
                    var path = $"references/{@ref.Filename}";
                    await skillsConfig.WriteSkillFileAsync(input.SkillId, path, @ref.Content, CancellationToken.None);
                    filesCreated.Add(path);
                }
            }

            // Create scripts
            if (input.Scripts != null)
            {
                foreach (var script in input.Scripts)
                {
                    var path = $"scripts/{script.Filename}";
                    await skillsConfig.WriteSkillFileAsync(input.SkillId, path, script.Content, CancellationToken.None);
                    filesCreated.Add(path);
                }
            }

            return JsonSerializer.Serialize(new
            {
                ok = true,
                skillPath = $".agents/skills/{input.SkillId}/",
                filesCreated
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { ok = false, error = $"Failed to create skill: {ex.Message}" }, JsonOptions);
        }
    }

    private static bool IsValidSkillId(string skillId)
    {
        if (string.IsNullOrEmpty(skillId)) return false;
        if (!char.IsLetter(skillId[0])) return false;
        return skillId.All(c => char.IsLower(c) || char.IsDigit(c) || c == '-');
    }

    private static string BuildSkillContent(GenerateSkillInput input)
    {
        return $"""
                ---
                name: {input.Name}
                description: {input.Description}
                ---

                # {input.Name}

                {input.Instructions}
                """;
    }
}
