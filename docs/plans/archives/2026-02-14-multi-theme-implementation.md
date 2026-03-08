# Multi-Theme Switching Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add 4–5 fixed theme presets with an AppBar switcher and localStorage persistence so the app renders in the chosen theme and remembers it across reloads.

**Architecture:** Theme is identified by a string id (e.g. `editorial-dark`). The document root `<html>` gets `data-theme="<id>"` via JS; a single `app.css` defines `[data-theme="id"] { --seb-* }` for each theme. MainLayout holds `_currentThemeId`, drives `MudThemeProvider` and a theme menu; JS provides `getTheme()` / `setTheme(id)` and optional early script to avoid first-paint flash.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor, vanilla JS, localStorage, CSS custom properties.

**Design reference:** `docs/plans/2026-02-14-multi-theme-design.md`

---

## Task 1: Theme constants and validation

**Files:**
- Create: `SmallEBot/ThemeConstants.cs`
- Modify: none yet (this is the only change in this task)

**Step 1: Add theme ID constants and default**

Create `SmallEBot/ThemeConstants.cs`:

```csharp
namespace SmallEBot;

public static class ThemeConstants
{
    public const string DefaultThemeId = "editorial-dark";

    public static readonly IReadOnlyList<string> ThemeIds = new[]
    {
        "editorial-dark",
        "paper-light",
        "terminal",
        "dusk",
        "mono"
    };

    public static bool IsValidThemeId(string? id) =>
        !string.IsNullOrEmpty(id) && ThemeIds.Contains(id);

    public static string NormalizeThemeId(string? id) =>
        IsValidThemeId(id) ? id! : DefaultThemeId;
}
```

**Step 2: Build to verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 3: Commit**

```bash
git add SmallEBot/ThemeConstants.cs
git commit -m "feat(theme): add theme ID constants and validation"
```

---

## Task 2: JS getTheme / setTheme and early script

**Files:**
- Modify: `SmallEBot/wwwroot/js/chat.js` (add theme helpers)
- Modify: `SmallEBot/Components/App.razor` (add inline script in head)

**Step 1: Add getTheme and setTheme to chat.js**

In `SmallEBot/wwwroot/js/chat.js`, after the existing `SmallEBot` namespace block, add:

```javascript
// Theme persistence and root attribute
var DEFAULT_THEME = 'editorial-dark';
var THEME_KEY = 'smallebot.theme';
var VALID_IDS = ['editorial-dark', 'paper-light', 'terminal', 'dusk', 'mono'];

window.SmallEBot.getTheme = function () {
    try {
        var id = localStorage.getItem(THEME_KEY);
        if (id && VALID_IDS.indexOf(id) !== -1) return id;
    } catch (e) {}
    return DEFAULT_THEME;
};

window.SmallEBot.setTheme = function (id) {
    if (!id || VALID_IDS.indexOf(id) === -1) id = DEFAULT_THEME;
    try {
        localStorage.setItem(THEME_KEY, id);
        document.documentElement.setAttribute('data-theme', id);
    } catch (e) {}
};
// Expose for Blazor JSInvoke (cannot call SmallEBot.getTheme directly)
window.SmallEBotGetTheme = function () { return window.SmallEBot.getTheme(); };
window.SmallEBotSetTheme = function (id) { window.SmallEBot.setTheme(id); };
```

**Step 2: Add inline script in App.razor head**

In `SmallEBot/Components/App.razor`, inside `<head>`, add immediately before `</head>` (so it runs before body and CSS apply):

```html
    <script>
        (function(){
            var key = 'smallebot.theme', def = 'editorial-dark', ids = ['editorial-dark','paper-light','terminal','dusk','mono'];
            try {
                var id = localStorage.getItem(key);
                if (!id || ids.indexOf(id) === -1) id = def;
                document.documentElement.setAttribute('data-theme', id);
            } catch (e) { document.documentElement.setAttribute('data-theme', def); }
        })();
    </script>
```

**Step 3: Build and quick run check**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds. Optional: `dotnet run --project SmallEBot` and in browser console run `SmallEBot.getTheme()` and `SmallEBot.setTheme('terminal')` to confirm no errors.

**Step 4: Commit**

```bash
git add SmallEBot/wwwroot/js/chat.js SmallEBot/Components/App.razor
git commit -m "feat(theme): add getTheme/setTheme JS and early data-theme script"
```

---

## Task 3: app.css – move :root to [data-theme="editorial-dark"] and add other themes

**Files:**
- Modify: `SmallEBot/wwwroot/app.css` (lines 1–15 and new blocks)

**Step 1: Replace :root with editorial-dark block**

In `SmallEBot/wwwroot/app.css`, replace the existing `:root { ... }` block (lines 1–15) with:

```css
/* SmallEBot design tokens – editorial dark (default) */
[data-theme="editorial-dark"] {
    --seb-bg: #1a1a1d;
    --seb-surface: #252529;
    --seb-surface-elevated: #2d2d32;
    --seb-primary: #e4a853;
    --seb-primary-muted: rgba(228, 168, 83, 0.15);
    --seb-text: #e4e4e7;
    --seb-text-secondary: #a1a1aa;
    --seb-border: #3f3f46;
    --seb-chat-user: #2d2d32;
    --seb-chat-assistant: #252529;
    --seb-font-ui: 'Outfit', sans-serif;
    --seb-font-body: 'Spectral', Georgia, serif;
}
```

**Step 2: Add fallback so body/html still get variables before JS runs**

Right after the block above, add a fallback for first paint (no data-theme yet):

```css
/* Fallback when data-theme not yet set (e.g. before JS) */
html:not([data-theme]) {
    --seb-bg: #1a1a1d;
    --seb-surface: #252529;
    --seb-surface-elevated: #2d2d32;
    --seb-primary: #e4a853;
    --seb-primary-muted: rgba(228, 168, 83, 0.15);
    --seb-text: #e4e4e7;
    --seb-text-secondary: #a1a1aa;
    --seb-border: #3f3f46;
    --seb-chat-user: #2d2d32;
    --seb-chat-assistant: #252529;
    --seb-font-ui: 'Outfit', sans-serif;
    --seb-font-body: 'Spectral', Georgia, serif;
}
```

**Step 3: Add paper-light, terminal, dusk, mono blocks**

Append to `app.css` (after the fallback block, before `html, body {`):

```css
[data-theme="paper-light"] {
    --seb-bg: #f5f0e8;
    --seb-surface: #fffef9;
    --seb-surface-elevated: #faf8f4;
    --seb-primary: #2d5016;
    --seb-primary-muted: rgba(45, 80, 22, 0.12);
    --seb-text: #1c1c1a;
    --seb-text-secondary: #57534e;
    --seb-border: #d6d3ce;
    --seb-chat-user: #e8e4dc;
    --seb-chat-assistant: #fffef9;
    --seb-font-ui: 'Outfit', sans-serif;
    --seb-font-body: 'Spectral', Georgia, serif;
}

[data-theme="terminal"] {
    --seb-bg: #0d0d0d;
    --seb-surface: #1a1a1a;
    --seb-surface-elevated: #262626;
    --seb-primary: #22c55e;
    --seb-primary-muted: rgba(34, 197, 94, 0.15);
    --seb-text: #e4e4e7;
    --seb-text-secondary: #a1a1aa;
    --seb-border: #3f3f46;
    --seb-chat-user: #262626;
    --seb-chat-assistant: #1a1a1a;
    --seb-font-ui: ui-monospace, 'Cascadia Code', monospace;
    --seb-font-body: ui-monospace, 'Cascadia Code', monospace;
}

[data-theme="dusk"] {
    --seb-bg: #1e1b2e;
    --seb-surface: #2d2842;
    --seb-surface-elevated: #36304a;
    --seb-primary: #e9a23b;
    --seb-primary-muted: rgba(233, 162, 59, 0.15);
    --seb-text: #e4e4e7;
    --seb-text-secondary: #a1a1aa;
    --seb-border: #4a4563;
    --seb-chat-user: #36304a;
    --seb-chat-assistant: #2d2842;
    --seb-font-ui: 'Outfit', sans-serif;
    --seb-font-body: 'Spectral', Georgia, serif;
}

[data-theme="mono"] {
    --seb-bg: #0f0f0f;
    --seb-surface: #1a1a1a;
    --seb-surface-elevated: #262626;
    --seb-primary: #e4e4e7;
    --seb-primary-muted: rgba(228, 228, 231, 0.12);
    --seb-text: #e4e4e7;
    --seb-text-secondary: #a1a1aa;
    --seb-border: #3f3f46;
    --seb-chat-user: #262626;
    --seb-chat-assistant: #1a1a1a;
    --seb-font-ui: ui-sans-serif, system-ui, sans-serif;
    --seb-font-body: ui-sans-serif, system-ui, sans-serif;
}
```

**Step 4: Ensure html/body use inherited variables**

Confirm `html, body { font-family: var(--seb-font-body); background: var(--seb-bg); color: var(--seb-text); ... }` still follows the new blocks and does not use `:root`. Variables are inherited from `html[data-theme]` or `html:not([data-theme])`.

**Step 5: Build and run, verify themes**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Run: `dotnet run --project SmallEBot`  
In browser: open DevTools console, run `document.documentElement.setAttribute('data-theme','paper-light')` then `terminal`, `dusk`, `mono`, `editorial-dark`. Confirm colors/fonts change.

**Step 6: Commit**

```bash
git add SmallEBot/wwwroot/app.css
git commit -m "feat(theme): add data-theme CSS blocks for all five themes"
```

---

## Task 4: MudTheme helper (GetMudTheme and IsDarkMode)

**Files:**
- Create: `SmallEBot/ThemeProviderHelper.cs` (or add to existing file; if you prefer, add static methods to `ThemeConstants.cs`)
- Modify: none else

**Step 1: Implement GetMudTheme and IsDarkModeForTheme**

Create `SmallEBot/ThemeProviderHelper.cs`:

```csharp
using MudBlazor;

namespace SmallEBot;

public static class ThemeProviderHelper
{
    public static bool IsDarkModeForTheme(string themeId)
    {
        return themeId != "paper-light";
    }

    public static MudTheme GetMudTheme(string themeId)
    {
        var id = ThemeConstants.NormalizeThemeId(themeId);
        return id switch
        {
            "paper-light" => CreatePaperLight(),
            "terminal" => CreateTerminal(),
            "dusk" => CreateDusk(),
            "mono" => CreateMono(),
            _ => CreateEditorialDark()
        };
    }

    private static MudTheme CreateEditorialDark()
    {
        var t = new MudTheme();
        t.PaletteDark = new PaletteDark
        {
            Primary = "#e4a853",
            PrimaryContrastText = "#1a1a1d",
            Secondary = "#71717a",
            Background = "#1a1a1d",
            BackgroundGray = "#252529",
            Surface = "#252529"
        };
        return t;
    }

    private static MudTheme CreatePaperLight()
    {
        var t = new MudTheme();
        t.PaletteLight = new PaletteLight
        {
            Primary = "#2d5016",
            PrimaryContrastText = "#fffef9",
            Secondary = "#57534e",
            Background = "#f5f0e8",
            BackgroundGray = "#faf8f4",
            Surface = "#fffef9"
        };
        return t;
    }

    private static MudTheme CreateTerminal()
    {
        var t = new MudTheme();
        t.PaletteDark = new PaletteDark
        {
            Primary = "#22c55e",
            PrimaryContrastText = "#0d0d0d",
            Secondary = "#a1a1aa",
            Background = "#0d0d0d",
            BackgroundGray = "#1a1a1a",
            Surface = "#1a1a1a"
        };
        return t;
    }

    private static MudTheme CreateDusk()
    {
        var t = new MudTheme();
        t.PaletteDark = new PaletteDark
        {
            Primary = "#e9a23b",
            PrimaryContrastText = "#1e1b2e",
            Secondary = "#a1a1aa",
            Background = "#1e1b2e",
            BackgroundGray = "#2d2842",
            Surface = "#2d2842"
        };
        return t;
    }

    private static MudTheme CreateMono()
    {
        var t = new MudTheme();
        t.PaletteDark = new PaletteDark
        {
            Primary = "#e4e4e7",
            PrimaryContrastText = "#0f0f0f",
            Secondary = "#a1a1aa",
            Background = "#0f0f0f",
            BackgroundGray = "#1a1a1a",
            Surface = "#1a1a1a"
        };
        return t;
    }
}
```

**Step 2: Build**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Expected: Build succeeds.

**Step 3: Commit**

```bash
git add SmallEBot/ThemeProviderHelper.cs
git commit -m "feat(theme): add GetMudTheme and IsDarkModeForTheme per theme id"
```

---

## Task 5: MainLayout – theme state, JS interop, theme menu

**Files:**
- Modify: `SmallEBot/Components/Layout/MainLayout.razor` (full updates below)

**Step 1: Inject IJSRuntime and add theme state**

In `MainLayout.razor`:
- Add `@inject IJSRuntime JS`
- Replace `Theme="_theme" IsDarkMode="true"` with theme from state: use `_currentThemeId`, `_mudTheme`, `_isDarkMode` (set from helper).
- Replace the single `CreateTheme()` and `_theme` with: `string _currentThemeId = ThemeConstants.DefaultThemeId;`, and computed `_mudTheme` / `_isDarkMode` from `ThemeProviderHelper` based on `_currentThemeId`.

**Step 2: Load theme from JS on first render**

Override `OnAfterRenderAsync(bool firstRender)`. When `firstRender` is true, invoke `JS.InvokeAsync<string>("eval", "window.SmallEBot.getTheme ? window.SmallEBot.getTheme() : 'editorial-dark'")` (or use a proper JS module invoke if you add one). Set `_currentThemeId = ThemeConstants.NormalizeThemeId(result)`, then `StateHasChanged()`.

**Step 3: Add theme menu to AppBar**

In the AppBar, before the tool-calls switch (or after the logo, before `MudSpacer`), add:

- `MudIconButton` with `Icon="Icons.Material.Filled.Palette"`, `Tooltip="主题"`, that opens a `MudMenu` (anchor).
- `MudMenu` with one `MudMenuItem` per entry in `ThemeConstants.ThemeIds`, label from a small method or switch (e.g. "编辑深色", "纸感浅色", "终端", "暮色", "单色"). On click: call `await JS.InvokeVoidAsync("eval", "window.SmallEBot.setTheme('" + id + "')")` (prefer `InvokeVoidAsync` with a dedicated JS function name if you expose one), set `_currentThemeId = id`, then `StateHasChanged()`.

**Step 4: Bind MudThemeProvider to current theme**

Ensure `MudThemeProvider` uses:
- `Theme="@(ThemeProviderHelper.GetMudTheme(_currentThemeId))"`
- `IsDarkMode="@(ThemeProviderHelper.IsDarkModeForTheme(_currentThemeId))"`

So that when `_currentThemeId` changes, the provider updates.

**Step 5: Full MainLayout.razor reference**

Replace the entire `@code` block and add the menu. Minimal structure:

```razor
@inherits LayoutComponentBase
@inject UserNameService UserNameSvc
@inject IJSRuntime JS

<MudThemeProvider Theme="@(ThemeProviderHelper.GetMudTheme(_currentThemeId))" IsDarkMode="@(ThemeProviderHelper.IsDarkModeForTheme(_currentThemeId))" />
<MudDialogProvider />
<MudSnackbarProvider />
<MudPopoverProvider />

<MudLayout Class="smallebot-layout">
    <MudAppBar Elevation="0" Class="smallebot-appbar">
        <MudText Typo="Typo.h6" Class="smallebot-logo">SmallEBot</MudText>
        <MudSpacer />
        <MudMenu AnchorOrigin="Origin.BottomRight" TransformOrigin="Origin.TopRight" @bind-Open="_themeMenuOpen">
            <MudMenuItem Icon="@Icons.Material.Filled.Palette" OnClick="@(() => _themeMenuOpen = true)" Tag="activator">
                <MudIconButton Icon="@Icons.Material.Filled.Palette" />
            </MudMenuItem>
            @foreach (var id in ThemeConstants.ThemeIds)
            {
                var idCopy = id;
                <MudMenuItem OnClick="@(() => SelectTheme(idCopy))">@GetThemeLabel(id) @(_currentThemeId == id ? " ✓" : "")</MudMenuItem>
            }
        </MudMenu>
        <MudTooltip Text="@(_showToolCalls ? "隐藏工具调用" : "显示工具调用")">
            ...
        </MudTooltip>
        ...
    </MudAppBar>
    ...
</MudLayout>

@code {
    private string _currentThemeId = ThemeConstants.DefaultThemeId;
    private bool _themeMenuOpen;
    private bool _showToolCalls = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                var id = await JS.InvokeAsync<string>("SmallEBotGetTheme");
                _currentThemeId = ThemeConstants.NormalizeThemeId(id);
                await InvokeAsync(StateHasChanged);
            }
            catch { /* keep default */ }
        }
    }

    private async Task SelectTheme(string id)
    {
        _currentThemeId = ThemeConstants.NormalizeThemeId(id);
        try { await JS.InvokeVoidAsync("SmallEBotSetTheme", id); } catch { }
        _themeMenuOpen = false;
        StateHasChanged();
    }

    private static string GetThemeLabel(string id) => id switch
    {
        "editorial-dark" => "编辑深色",
        "paper-light" => "纸感浅色",
        "terminal" => "终端",
        "dusk" => "暮色",
        "mono" => "单色",
        _ => id
    };

    private void ToggleShowToolCalls() { _showToolCalls = !_showToolCalls; StateHasChanged(); }
}
```

Note: MudMenu activator is usually the button that opens the menu; menu items should not use the same MenuItem as activator. Use a single `MudIconButton` as activator and `MudMenu` with `ActivatorContent` or child activator. Correct pattern:

```razor
<MudMenu @bind-Open="_themeMenuOpen" AnchorOrigin="Origin.BottomRight" TransformOrigin="Origin.TopRight">
    <MudIconButton Icon="@Icons.Material.Filled.Palette" Slot="ActivatorContent" />
    <MudTooltip Text="主题" />
    @foreach (var id in ThemeConstants.ThemeIds)
    {
        var idCopy = id;
        <MudMenuItem OnClick="@(() => SelectTheme(idCopy))">@GetThemeLabel(id) @(_currentThemeId == id ? " ✓" : "")</MudMenuItem>
    }
</MudMenu>
```

(Adjust to actual MudBlazor API: MudMenu may use `ActivatorContent` or a render fragment; confirm in MudBlazor docs.)

**Step 6: Blazor JS interop**

Task 2 added `window.SmallEBotGetTheme` and `window.SmallEBotSetTheme` in `chat.js` so Blazor can call `JS.InvokeAsync<string>("SmallEBotGetTheme")` and `JS.InvokeVoidAsync("SmallEBotSetTheme", id)`.

**Step 7: Build and run, verify**

Run: `dotnet build SmallEBot/SmallEBot.csproj`  
Run: `dotnet run --project SmallEBot`  
Verify: Open app, click palette icon, choose another theme; confirm CSS and MudBlazor components update. Reload page; confirm theme persists.

**Step 8: Commit**

```bash
git add SmallEBot/Components/Layout/MainLayout.razor
git commit -m "feat(theme): wire MainLayout theme menu and JS get/set persistence"
```

---

## Task 6: Verification and doc update

**Files:**
- Modify: `docs/plans/2026-02-14-multi-theme-design.md` (set Status to Implemented or add “Implemented 2026-02-14” note)

**Step 1: Manual verification**

- All five themes selectable from AppBar; labels and check mark correct.
- After reload, stored theme is applied (no flash if inline script is present).
- Build: `dotnet build SmallEBot/SmallEBot.csproj` succeeds.

**Step 2: Update design doc status**

In `docs/plans/2026-02-14-multi-theme-design.md`, change **Status:** to `Implemented` or add a line: `**Implemented:** 2026-02-14`

**Step 3: Commit**

```bash
git add docs/plans/2026-02-14-multi-theme-design.md
git commit -m "docs: mark multi-theme design as implemented"
```

---

## Execution handoff

Plan complete and saved to `docs/plans/2026-02-14-multi-theme-implementation.md`.

Two execution options:

1. **Subagent-Driven (this session)** – Dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Parallel Session (separate)** – Open a new session with executing-plans and run through the plan with checkpoints.

Which approach?
