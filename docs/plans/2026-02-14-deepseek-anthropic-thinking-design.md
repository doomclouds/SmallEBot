# DeepSeek via Anthropic API and Thinking Support — Design

**Status:** Draft (API research)  
**Date:** 2026-02-14  
**Target:** Use DeepSeek's Anthropic-compatible endpoint with Microsoft Agent Framework; support think + tool in one AI reply.

---

## 1. Goal

- Use **DeepSeek** via **Anthropic API** at `https://api.deepseek.com/anthropic` (same API key as DeepSeek).
- Use **Microsoft Agent Framework’s Anthropic provider** (`Microsoft.Agents.AI.Anthropic`) so SmallEBot keeps one framework and gains thinking + tool in one reply.
- Later: accept **Think** content in the UI; one AI reply = one group; within it, multiple “think sub-groups” (each think block can be followed by tool_use).

---

## 2. API Research Summary

### 2.1 DeepSeek Anthropic API

- **Endpoint:** `https://api.deepseek.com/anthropic`
- **Auth:** Same API key as DeepSeek (e.g. `ANTHROPIC_API_KEY` = DeepSeek key when using this base URL).
- **Supported:** `thinking` content type in messages, tool definitions, `tool_use`, `tool_result`, `text`; streaming; system prompt; `temperature`, `top_p`, `max_tokens`, `stop_sequences`.  
- **Unsupported:** image/document content, `cache_control`, some beta headers.  
- **Models:** e.g. `deepseek-chat`; unsupported names are mapped to `deepseek-chat`.

### 2.2 Microsoft Agent Framework — Anthropic

- **Package:** `Microsoft.Agents.AI.Anthropic` (preview).
- **Docs:** [Anthropic Agents | Microsoft Learn](https://learn.microsoft.com/en-us/agent-framework/agents/providers/anthropic?pivots=programming-language-csharp).
- **Usage:** Create `AnthropicClient`, then `client.AsAIAgent(model, name, instructions, tools, ...)`. Same `AIAgent` abstraction as OpenAI (e.g. `RunStreamingAsync`, tools, history).
- **Config:** Environment variables: `ANTHROPIC_API_KEY`, `ANTHROPIC_DEPLOYMENT_NAME` (model name).  
- **Custom endpoint:** The underlying Anthropic C# SDK supports:
  - **Environment:** `ANTHROPIC_BASE_URL` (e.g. `https://api.deepseek.com/anthropic`).
  - **Code:** `AnthropicClient` with `BaseUrl = "https://api.deepseek.com/anthropic"` (if the Agent Framework exposes or constructs the client in a way that allows setting this; otherwise env var is the reliable path).

So: **using the Agent Framework’s Anthropic API with `ANTHROPIC_BASE_URL=https://api.deepseek.com/anthropic` and `ANTHROPIC_API_KEY=<DeepSeekKey>` is the intended way to “用微软提供的 Agent Framework 的 Anthropic API” 对接 DeepSeek.**

### 2.3 Thinking + Tool in One Reply (Anthropic Format)

- **Anthropic Messages API:** Assistant message `content` is an array of blocks. Supported block types include:
  - `type: "thinking"` — reasoning (e.g. extended thinking).
  - `type: "text"` — final answer text.
  - `type: "tool_use"` — tool call (id, name, input).
- **Order:** In one assistant turn you can have multiple blocks in sequence, e.g. `[thinking, tool_use, tool_use, thinking, tool_use, text]`. That matches “一个大的组内，可以出现多个 think 小组，每个 think 后可有 tool”.
- **Streaming:** Content blocks are streamed in order (`content_block_start`, `content_block_delta`, `content_block_stop`), so the client can reconstruct think vs text vs tool_use and group them.

Implementation work will be: ensure the Agent Framework’s Anthropic provider (and our `AgentService`) expose these content types in the streaming DTO (e.g. a `ThinkStreamUpdate` or equivalent) and that the UI can render “think sub-groups” (think content + following tool calls) inside one assistant bubble.

---

## 3. Configuration

For DeepSeek via Anthropic API:

- **Base URL:** Config `Anthropic:BaseUrl` or `DeepSeek:AnthropicBaseUrl` (default `https://api.deepseek.com/anthropic`), or env `ANTHROPIC_BASE_URL`.
- **API key:** Config `Anthropic:ApiKey` or `DeepSeek:ApiKey` (e.g. user secrets), or environment `ANTHROPIC_API_KEY` or `DeepseekKey`. Do not commit secrets.
- **`Anthropic:Model`** / **`DeepSeek:Model`** — single model for the agent (e.g. `deepseek-reasoner`); thinking on/off is per request via options.

---

## 4. Implementation Directions (High Level)

1. **Switch to Anthropic provider for DeepSeek**
   - Add `Microsoft.Agents.AI.Anthropic`.
   - In config/startup: set `ANTHROPIC_BASE_URL=https://api.deepseek.com/anthropic` and `ANTHROPIC_API_KEY` from existing DeepSeek key (or keep one key in env and document that it’s used for both).
   - In `AgentService`: create `AnthropicClient` (with `BaseUrl` if needed), then `client.AsAIAgent(model: "deepseek-chat", ...)` with same tools/MCP pattern as today; replace OpenAI `ChatClientAgent` with this agent.
   - Verify: same chat + tool-call behavior as today, with requests going to DeepSeek’s Anthropic endpoint.

2. **Enable extended thinking**
   - When using DeepSeek via Anthropic, enable thinking in the request (e.g. `thinking: { type: "enabled" }`). This depends on the Anthropic client/Agent Framework API exposing this option; if not, follow SDK/Agent Framework docs or issues for how to pass extra request fields.

3. **Streaming and Think content**
   - Extend `StreamUpdate` (or equivalent) with a variant for “thinking” content (e.g. `ThinkStreamUpdate(string Text)`).
   - In the streaming loop over the Anthropic agent’s updates: map `thinking` content blocks to `ThinkStreamUpdate`, keep existing mapping for text and tool_use.
   - Persistence: same as current design — optional to persist only final text for history; think content can be display-only for the current reply.

4. **UI: think sub-groups**
   - In the assistant bubble, maintain an ordered list of items: think segments and tool calls. Group them so that “a think + its following tool_use(s)” form one collapsible “think group” when multiple such groups exist in one reply. Details (collapse/expand, styling) can follow the existing tool-block design.

5. **是否使用思考模式：AppBar 思考切换按钮**
   - 是否启用思考模式由 **AppBar 的“思考”切换按钮** 控制（与现有“工具调用”显示切换并列）。
   - 用户开启“思考”时，发送消息携带“使用思考模式”；后端在该次请求中启用 extended thinking（如 `thinking: { type: "enabled" }`）。
   - 用户关闭“思考”时，不传思考选项，模型按普通模式回复，不返回 thinking 内容。
   - 状态通过 CascadingValue（如 `UseThinkingMode`）传到 ChatArea，再作为参数传入 `SendMessageStreamingAsync(..., useThinking)`。不要求持久化该开关（会话内生效即可）。

---

## 5. References

- DeepSeek Anthropic API: https://api-docs.deepseek.com/guides/anthropic_api  
- Microsoft Agent Framework — Anthropic: https://learn.microsoft.com/en-us/agent-framework/agents/providers/anthropic?pivots=programming-language-csharp  
- Anthropic C# SDK BaseUrl: `ANTHROPIC_BASE_URL` or `AnthropicClient.BaseUrl` (platform docs).  
- Existing tool-calling design: `docs/plans/2026-02-14-tool-calling-design.md`.
