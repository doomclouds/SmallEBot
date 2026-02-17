# Single Agent with Per-Request Thinking Parameter — Design

**Status:** Draft  
**Date:** 2026-02-17  
**Goal:** Use one cached agent (reasoner model) and control thinking via a per-request parameter instead of switching between two agents.

---

## 1. Goal and context

Today SmallEBot uses two agent instances: one with `deepseek-chat` (no thinking) and one with `deepseek-reasoner` (thinking). The UI toggle and `useThinking` select which agent to use. This duplicates cache, config, and mental model.

We want a single agent: always the **reasoner** model (e.g. `deepseek-reasoner`). Whether the model actually does extended thinking for a given request is controlled by a **per-request parameter** (e.g. `thinking: { type: "enabled" }` or equivalent in the SDK). DeepSeek’s API supports this: same model can be called with or without the thinking flag.

**Out of scope:** Supporting a separate “chat” model when thinking is off (e.g. reasoner vs chat). This design assumes one model (reasoner) and a parameter to enable/disable thinking per call.

---

## 2. Architecture

**AgentBuilder:** Build and cache a single `AIAgent` instance, created with the reasoner model from config (e.g. `DeepSeek:ThinkingModel` or a single `Model` key). Remove `_agent` / `_agentWithThinking` duality; keep one `_agent` and one `GetOrCreateAgentAsync(CancellationToken)` (or keep the existing signature and ignore the boolean for agent selection). Tools and MCP loading stay unchanged; only the agent-creation branch is simplified.

**AgentRunnerAdapter:** Continue to receive `useThinking` from the conversation pipeline. Instead of choosing between two agents, get the single agent once and pass `useThinking` into the **run options** for `RunStreamingAsync(messages, session, options, cancellationToken)`. The options (e.g. `ChatClientAgentRunOptions` or provider-specific options) must carry a “thinking enabled” flag so the Anthropic/DeepSeek client can send `thinking: { type: "enabled" }` or omit it. If the C# SDK does not expose thinking in run options, fallback is either (a) pass via `AdditionalProperties` / extra request fields if the stack allows, or (b) keep a single agent and use `useThinking` only for UI (show/hide reasoning segments) and document that API-level disabling is pending SDK support.

**Application layer:** No change to `useThinking` flow: `CreateTurnAndUserMessageAsync` and `StreamResponseAndCompleteAsync` still receive `useThinking` from the UI; only the runner and builder use it differently (options instead of agent selection).

---

## 3. Configuration and fallback

**Config:** Prefer a single model key for the agent (e.g. `Anthropic:Model` or `DeepSeek:ThinkingModel` = `deepseek-reasoner`). Legacy `DeepSeek:Model` / `ThinkingModel` can remain for backward compatibility but only the reasoner value is used when building the single agent. **API key** can be set via config (`Anthropic:ApiKey` or `DeepSeek:ApiKey`) or environment (`ANTHROPIC_API_KEY`, `DeepseekKey`); do not commit secrets. Title generation continues to use the same agent with thinking disabled via options (if supported) to avoid long reasoning for short titles.

**Fallback if SDK lacks per-request thinking:**  
- Option A: One agent (reasoner), always request with thinking enabled; use `useThinking` only in the UI (ReasoningSegmenter / display) to show or hide reasoning.  
- Option B: Document “per-request thinking not yet supported in C#” and temporarily keep the current two-agent switch until the SDK exposes the option.  

Recommendation: implement Option A so we still remove the two-agent split and only add the options path when the SDK supports it.

---

## 4. Implementation notes

- **GenerateTitleAsync:** Use the single agent with options that disable thinking (Reasoning = null) to keep title generation fast.
- **InvalidateAsync:** Clear the single `_agent` and keep MCP/tools cleanup as today.

---

## 5. SDK support (verified 2026-02-17)

Per-request thinking **is supported** in the current stack:

1. **RunStreamingAsync** (Microsoft.Agents.AI) accepts `ChatClientAgentRunOptions` as the third parameter. That type has a **ChatOptions** property that is applied to the agent invocation.

2. **ChatOptions** (Microsoft.Extensions.AI) has a **Reasoning** property of type **ReasoningOptions?**. Setting it enables reasoning for that request; leaving it `null` uses the provider default (no reasoning when not requested).

3. **ReasoningOptions** has:
   - **Output** (ReasoningOutput?) — how reasoning content is included in the response.
   - **Effort** (ReasoningEffort?) — level of reasoning effort.

4. **Usage in AgentRunnerAdapter:** Build run options per request:
   - `var chatOptions = new ChatOptions { Reasoning = useThinking ? new ReasoningOptions() { ... } : null };`
   - `var runOptions = new ChatClientAgentRunOptions(chatOptions);`
   - `agent.RunStreamingAsync(frameworkMessages, null, runOptions, cancellationToken)`.

5. The underlying Anthropic C# SDK supports `Thinking = new ThinkingConfigEnabled() { BudgetTokens = ... }` on message create params; the Agent Framework’s Anthropic provider is expected to map ChatOptions.Reasoning to the provider API. If a preview build does not yet map it, use **ChatOptions.AdditionalProperties** to pass provider-specific keys until the mapping is available.

---

## 6. References

- DeepSeek thinking mode: https://api-docs.deepseek.com/guides/thinking_mode (parameter `thinking: { type: "enabled" }`).
- Existing design: `docs/plans/2026-02-14-deepseek-anthropic-thinking-design.md`.
- Agent builder and runner: `SmallEBot/Services/Agent/AgentBuilder.cs`, `AgentRunnerAdapter.cs`.
