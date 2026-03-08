# IronPython sandbox and RunPython built-in tool

**Date:** 2026-02-17  
**Status:** Design  
**Scope:** IronPython-based in-process Python executor as a built-in agent tool; supports both inline code and script file path. No dependency on system Python.

---

## 1. Purpose and scope

- **Goal:** Run Python code inside the app using **IronPython** (pure .NET), so the agent can execute scripts without requiring the user to have Python on PATH. Execution is sandboxed (timeout, controlled input), and output is captured as text.
- **Built-in tool:** One tool **RunPython** with optional parameters: **code** (inline Python), **scriptPath** (path to a `.py` file under the run directory), **workingDirectory** (optional). At least one of `code` or `scriptPath` must be provided. If both are provided, **scriptPath wins** (execute file content); otherwise the provided parameter is used.
- **Path rules:** `scriptPath` and `workingDirectory` must resolve under `AppDomain.CurrentDomain.BaseDirectory`; allowed script extension is `.py` only, consistent with the idea that RunPython executes what ReadFile can read for scripts.

---

## 2. Architecture and tool contract

**Components**

- **IPythonSandbox** (Host): `string Execute(string? code, string? scriptPath, string? workingDirectory, TimeSpan? timeout)`. At least one of `code` or `scriptPath` must be non-empty; if both are set, only `scriptPath` is used (file content is read and executed). Paths are relative to the app base and must stay under it; working directory is validated the same way (used for future use, e.g. script-relative paths if IronPython supports it).
- **RunPython built-in tool:** Created by `IBuiltInToolFactory`; parameters: `code` (optional), `scriptPath` (optional), `workingDirectory` (optional). The tool validates “code or scriptPath at least one” and path rules, then calls `IPythonSandbox.Execute`. Timeout comes from `ITerminalConfigService.GetCommandTimeoutSeconds()` or a dedicated cap (e.g. 60s). Return format: `Stdout:\n...\nStderr:\n...`; on exception or timeout, a short error message.
- **Sandbox behavior:** One engine per app (or per request); scope per execution. Redirect `Runtime.IO` to capture stdout/stderr. First version: timeout + output redirect only; optional later: restrict builtins (e.g. `open`/`import`) for tighter isolation.

**Data flow**

Agent calls RunPython(code=..., scriptPath=...) → BuiltInToolFactory validates → IPythonSandbox: if scriptPath present, resolve path, read file content as code; else use code → set IO redirect and timeout → IronPython executes → return combined stdout/stderr string.

---

## 3. Error handling and path consistency

**Validation and errors**

- **Parameters:** If both `code` and `scriptPath` are null/whitespace, return `"Error: provide either code or scriptPath."`
- **Paths:** Resolve `scriptPath` and `workingDirectory` under base directory. If outside base: `"Error: path must be under the run directory."` If file does not exist or extension is not `.py`: `"Error: script file not found or not a .py file."`
- **Execution:** Catch IronPython execution exceptions; return `"Error: " + ex.Message` and include engine traceback if available. On timeout: return `"Error: Script timed out after N seconds."` (execution may not be aborted if IronPython has no cancel API).
- **Output:** On success return `"Stdout:\n" + stdout + "\nStderr:\n" + stderr` (stderr may be empty).

**Consistency with ReadFile**

- Script path rules align with ReadFile’s allowed `.py` subset: relative to run directory, no `..`, resolved path under base, extension `.py`. Agent can use ReadFile to read a script and then RunPython(scriptPath=...), or call RunPython(scriptPath=...) only and let the sandbox read the file.

**Security (first version)**

- No arbitrary file access: only execute code we supply (inline or from a validated script path). Rely on timeout and “only run given code”; optional later: restrict builtins in scope.

---

## 4. Implementation notes

**Engine and scope**

- Create engine with `IronPython.Hosting.Python.CreateEngine()`; hold one engine (e.g. in the class that implements `IPythonSandbox`). Per execution: `engine.CreateScope()`, then `engine.Execute(code, scope)`. Set `engine.Runtime.IO.SetOutput(outputStream, Encoding.UTF8)` and `SetErrorStream(errorStream, Encoding.UTF8)` (e.g. `StringWriter` or `MemoryStream` + StreamWriter) before execute; read back after.

**Timeout**

- Run `engine.Execute` inside `Task.Run`; outer wait with `Task.WaitAsync(timeout)`. On timeout, return error and do not wait for the task to finish (IronPython may not support cancellation).

**DI and registration**

- Define `IPythonSandbox` in Host (e.g. `SmallEBot.Services.Sandbox` or `SmallEBot.Services.Agent`) with `string Execute(string? code, string? scriptPath, string? workingDirectory, TimeSpan? timeout)`. Implement as `IronPythonSandbox`; optionally inject `ITerminalConfigService` for default timeout. Register as Singleton (if engine is shared) in `AddSmallEBotHostServices`.
- Inject `IPythonSandbox` into `BuiltInToolFactory`; add `RunPython` to `CreateTools()` as an instance method with optional parameters `code`, `scriptPath`, `workingDirectory`. Validation and call to `pythonSandbox.Execute` inside the method; timeout e.g. `TimeSpan.FromSeconds(terminalConfig.GetCommandTimeoutSeconds())` or a separate cap.

**Dependencies**

- Add NuGet **IronPython** only to the Host project (SmallEBot). Core/Application/Infrastructure do not reference IronPython. Keep `IPythonSandbox` and implementation in Host so the agent uses the sandbox only via the built-in tool.

---

## 5. Summary

| Item | Choice |
|------|--------|
| Runtime | IronPython (in-process, no system Python) |
| Tool | RunPython(code?, scriptPath?, workingDirectory?) |
| Priority | If both code and scriptPath provided → use scriptPath |
| Paths | Under base directory; scriptPath must be `.py` |
| Output | Stdout:\n...\nStderr:\n... ; errors as "Error: ..." |
| Timeout | From terminal config or dedicated cap |
| DI | IPythonSandbox in Host; BuiltInToolFactory creates RunPython tool |
