# Single Agent with Per-Request Thinking Parameter — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Use one cached agent (reasoner model) and control thinking via per-request `ChatOptions.Reasoning` instead of switching between two agents.

**Architecture:** AgentBuilder builds and caches a single `AIAgent` with the reasoner model. AgentRunnerAdapter gets that agent once per run and passes `ChatClientAgentRunOptions` with `ChatOptions.Reasoning` set from `useThinking`. Title generation uses the same agent with `Reasoning = null`.

**Tech Stack:** .NET 10, Microsoft.Agents.AI.Anthropic, Microsoft.Extensions.AI (ChatOptions, ReasoningOptions), SmallEBot.Application (IAgentRunner), SmallEBot (AgentBuilder, AgentRunnerAdapter).

**Spec:** `docs/plans/2026-02-17-single-agent-thinking-parameter-design.md`

---

## Task 1: AgentBuilder — single agent (keep existing signature)

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentBuilder.cs`

**Step 1: Use only reasoner model and one cache field**

In `AgentBuilder`:
- Remove field `_agentWithThinking`; keep only `_agent`.
- Keep the interface and method signature `GetOrCreateAgentAsync(bool useThinking, CancellationToken ct)` unchanged so all call sites continue to compile.
- In `GetOrCreateAgentAsync`: remove the switch that returns one of two agents. Always use the reasoner model: `var model = config["Anthropic:ThinkingModel"] ?? config["DeepSeek:ThinkingModel"] ?? "deepseek-reasoner";`. If `_agent != null`, return `_agent`. Otherwise build once with `anthropicClient.AsAIAgent(model: model, name: "SmallEBot", instructions: instructions, tools: _allTools)`, assign to `_agent`, return it. Ignore the `useThinking` parameter for agent selection.
- In `InvalidateAsync`: set only `_agent = null` (remove `_agentWithThinking = null`).
- Remove the unused variable `model` for the non-thinking path (the old `var model = config["Anthropic:Model"] ?? ...`); use only the reasoner model variable as above.

**Step 2: Build**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: PASS.

**Step 3: Commit**

```bash
git add SmallEBot/Services/Agent/AgentBuilder.cs
git commit -m "refactor(agent): use single cached agent with reasoner model"
```

---

## Task 2: AgentRunnerAdapter — pass run options with Reasoning

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentRunnerAdapter.cs`

**Step 1: Add usings**

At the top of `AgentRunnerAdapter.cs` add:

```csharp
using Microsoft.Agents.AI;
```

`ChatOptions` and `ReasoningOptions` are in `Microsoft.Extensions.AI` (already used). If the compiler cannot find `ChatClientAgentRunOptions`, ensure the Host project references `Microsoft.Agents.AI` (transitive via `Microsoft.Agents.AI.Anthropic`).

**Step 2: Build run options and pass to RunStreamingAsync**

In `RunStreamingAsync`, after building `frameworkMessages`, add:

```csharp
var chatOptions = new ChatOptions
{
    Reasoning = useThinking ? new ReasoningOptions() : null
};
var runOptions = new ChatClientAgentRunOptions(chatOptions);
```

Replace:

```csharp
await foreach (var update in agent.RunStreamingAsync(frameworkMessages, null, null, cancellationToken))
```

with:

```csharp
await foreach (var update in agent.RunStreamingAsync(frameworkMessages, null, runOptions, cancellationToken))
```

**Step 3: GenerateTitleAsync — same agent with options that disable thinking**

In `GenerateTitleAsync`, after getting the agent, build options and pass to `RunAsync`:

```csharp
var agent = await agentBuilder.GetOrCreateAgentAsync(useThinking: false, cancellationToken);
var titleOptions = new ChatClientAgentRunOptions(new ChatOptions { Reasoning = null });
var result = await agent.RunAsync(prompt, null, titleOptions, cancellationToken);
```

(Keep using `useThinking: false` for the builder call so the builder still receives a valid call; the builder now ignores it.)

**Step 4: Build**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: PASS.

**Step 5: Commit**

```bash
git add SmallEBot/Services/Agent/AgentRunnerAdapter.cs
git commit -m "feat(agent): pass per-request Reasoning options in runner"
```

---

## Task 3: Update AGENTS.md

**Files:**
- Modify: `AGENTS.md`

**Step 1: Update AgentBuilder description**

In the section that describes **AgentBuilder** (e.g. “Two cached variants: normal and thinking mode”), replace with: **Single cached agent (reasoner model); thinking on/off per request via ChatOptions.Reasoning in run options.**

**Step 2: Commit**

```bash
git add AGENTS.md
git commit -m "docs: update AGENTS.md for single-agent thinking option"
```

---

## Task 4: Verify end-to-end

**Step 1: Run app**

Run: `dotnet run --project SmallEBot`  
Expected: App starts without errors.

**Step 2: Manual check**

- Open a conversation. With “thinking” **off**, send a message; confirm reply appears and no thinking block (or reasoning hidden per existing UI).
- Turn “thinking” **on**, send another message; confirm thinking/reasoning content appears if the model returns it.
- Start a new conversation (first message) and confirm title is generated (same agent with Reasoning = null).

**Step 3: Commit (if any small fixes)**

If you made any fixes during verification, commit with a message like `fix: adjust run options for title generation` or similar.

---

## Task 5: Optional — ReasoningOptions defaults

**Files:**
- Modify: `SmallEBot/Services/Agent/AgentRunnerAdapter.cs`

If the provider expects explicit `ReasoningOptions` values (e.g. `Output`, `Effort`), set them when `useThinking` is true, e.g.:

```csharp
Reasoning = useThinking ? new ReasoningOptions() { Output = ReasoningOutput.Include, Effort = ReasoningEffort.Low } : null
```

Use enum values that exist in `Microsoft.Extensions.AI` (ReasoningOutput, ReasoningEffort). If the build fails due to missing or different enum names, remove or adjust per actual API. This task is optional and can be done in a follow-up if the first run works without it.

---

## Execution handoff

Plan complete and saved to `docs/plans/2026-02-17-single-agent-thinking-parameter-implementation-plan.md`.

**Two execution options:**

1. **Subagent-Driven (this session)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Parallel Session (separate)** — Open a new session with executing-plans and run through the plan with checkpoints.

**Which approach?**
