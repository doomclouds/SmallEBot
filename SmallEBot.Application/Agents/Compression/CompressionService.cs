using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using SmallEBot.Application.Contracts.Agents.Execution;
using SmallEBot.Application.Contracts.Agents.Compression;

namespace SmallEBot.Application.Agents.Compression;

/// <summary>Compresses conversation history by calling LLM with compact skill prompt.</summary>
public sealed class CompressionService(IAgentBuilder agentBuilder, ILogger<CompressionService> logger) : ICompressionService
{
    // Aligned with claude-code-system-prompts/system-prompts/agent-prompt-conversation-summarization.md
    private const string CompactPrompt = """
                                         Your task is to create a detailed summary of the conversation so far, paying close attention to the user's explicit requests and your previous actions. This summary should be thorough in capturing technical details, code patterns, and architectural decisions that would be essential for continuing development work without losing context.

                                         ## Input
                                         You will receive:
                                         1. Previous summary (if exists) - merge with new messages
                                         2. New conversation messages to compress

                                         ## Task
                                         Generate a MERGED summary. Before your final summary, wrap your analysis in <analysis> tags. Chronologically analyze each message: user requests and intents, your approach, key decisions, file names, code snippets, file edits, errors and how you fixed them, and user feedback (especially if the user told you to do something differently).

                                         Your summary should include the following sections:

                                         1. Primary Request and Intent: Capture all of the user's explicit requests and intents in detail
                                         2. Key Technical Concepts: List all important technical concepts, technologies, and frameworks discussed
                                         3. Files and Code Sections: Enumerate specific files and code sections examined, modified, or created. Include full code snippets where applicable and why each file read or edit is important
                                         4. Errors and fixes: List all errors that you ran into and how you fixed them. Pay special attention to specific user feedback
                                         5. Problem Solving: Document problems solved and any ongoing troubleshooting efforts
                                         6. All user messages: List ALL user messages that are not tool results. Critical for understanding feedback and changing intent
                                         7. Pending Tasks: Outline any pending tasks that you have explicitly been asked to work on
                                         8. Current Work: Describe in detail precisely what was being worked on immediately before this summary, including file names and code snippets where applicable
                                         9. Optional Next Step: List the next step related to the most recent work. Ensure it is DIRECTLY in line with the user's most recent explicit requests. If there is a next step, include direct quotes from the most recent conversation showing exactly what task you were working on and where you left off

                                         ## Output Format
                                         Wrap your analysis in <analysis> tags first, then wrap your final summary in <summary> tags. Structure your output exactly like this:

                                         <analysis>
                                         [Your thought process, ensuring all points are covered thoroughly and accurately]
                                         </analysis>

                                         <summary>
                                         1. Primary Request and Intent:
                                            [Detailed description]

                                         2. Key Technical Concepts:
                                            - [Concept 1]
                                            - [Concept 2]
                                            - [...]

                                         3. Files and Code Sections:
                                            - [File Name 1]
                                               - [Summary of why this file is important]
                                               - [Summary of the changes made to this file, if any]
                                               - [Important Code Snippet]
                                            - [File Name 2]
                                               - [Important Code Snippet]
                                            - [...]

                                         4. Errors and fixes:
                                             - [Detailed description of error 1]:
                                               - [How you fixed the error]
                                               - [User feedback on the error if any]
                                             - [...]

                                         5. Problem Solving:
                                            [Description of solved problems and ongoing troubleshooting]

                                         6. All user messages:
                                             - [Detailed non tool use user message]
                                             - [...]

                                         7. Pending Tasks:
                                            - [Task 1]
                                            - [Task 2]
                                            - [...]

                                         8. Current Work:
                                            [Precise description of current work]

                                         9. Optional Next Step:
                                            [Optional Next step to take]
                                         </summary>

                                         Please provide your summary based on the conversation, following this structure and ensuring precision and thoroughness in your response.
                                         """;

    public async Task<string?> GenerateSummaryAsync(
        IReadOnlyList<ChatMessage> messages,
        int toolResultMaxLength,
        string? existingSummary = null,
        CancellationToken ct = default)
    {
        if (messages.Count == 0 && string.IsNullOrEmpty(existingSummary))
            return existingSummary;

        var sb = new StringBuilder();

        // Include existing summary if present
        if (!string.IsNullOrEmpty(existingSummary))
        {
            sb.AppendLine("## Previous Summary (merge with new messages)");
            sb.AppendLine(existingSummary);
            sb.AppendLine();
        }

        if (messages.Count > 0)
        {
            sb.AppendLine("## New Messages to Compress");
            sb.AppendLine();

            // Process messages and extract content including tool calls embedded in Contents
            foreach (var message in messages)
            {
                var role = message.Role == ChatRole.User ? "User" : "Assistant";
                sb.AppendLine($"[{role}]:");

                foreach (var content in message.Contents)
                {
                    if (content is TextContent textContent)
                    {
                        sb.AppendLine(textContent.Text);
                    }
                    else if (content is TextReasoningContent reasoning)
                    {
                        var reasoningPreview = reasoning.Text.Length > 200
                            ? reasoning.Text[..200] + "..."
                            : reasoning.Text;
                        sb.AppendLine($"[Thinking]: {reasoningPreview}");
                    }
                    else if (content is FunctionCallContent fnCall)
                    {
                        sb.AppendLine($"[Tool: {fnCall.Name}]");
                        sb.AppendLine($"Arguments: {ToJsonString(fnCall.Arguments)}");
                    }
                    else if (content is FunctionResultContent fnResult)
                    {
                        var result = TruncateResult(fnResult.Result?.ToString(), toolResultMaxLength);
                        sb.AppendLine($"[Tool Result]: {result}");
                    }
                }

                sb.AppendLine();
            }
        }

        try
        {
            var agent = await agentBuilder.GetOrCreateAgentAsync(useThinking: false, ct);
            var chatMessages = new List<ChatMessage>
            {
                new(ChatRole.System, CompactPrompt),
                new(ChatRole.User, sb.ToString())
            };

            var chatOptions = new ChatOptions { Reasoning = null };
            var runOptions = new ChatClientAgentRunOptions(chatOptions);
            var result = await agent.RunAsync(chatMessages, null, runOptions, ct);
            var summary = ExtractSummaryContent(result.Text);
            logger.LogInformation("Compression generated summary: {Length} chars", summary.Length);
            return summary;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate compression summary");
            return null;
        }
    }

    /// <summary>Extracts content inside &lt;summary&gt; tags if present; otherwise returns the full text.</summary>
    private static string ExtractSummaryContent(string text)
    {
        const string startTag = "<summary>";
        const string endTag = "</summary>";
        var start = text.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return text.Trim();
        start += startTag.Length;
        var end = text.IndexOf(endTag, start, StringComparison.OrdinalIgnoreCase);
        if (end < 0) return text.Trim();
        return text[start..end].Trim();
    }

    private static string TruncateResult(string? result, int maxLength)
    {
        if (result == null) return "null";
        if (result.Length <= maxLength) return result;
        return result[..maxLength] + "... [truncated]";
    }

    private static string ToJsonString(IDictionary<string, object?>? arguments)
    {
        if (arguments == null || arguments.Count == 0)
            return "{}";
        return JsonSerializer.Serialize(arguments);
    }
}
