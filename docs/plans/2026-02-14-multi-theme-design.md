# Multi-Theme Switching Design

**Date:** 2026-02-14  
**Status:** Implemented (2026-02-14)  
**Scope:** 4–5 fixed theme presets, AppBar-only switcher, localStorage persistence.

---

## 1. Goals and constraints

- **Presets:** 4–5 fixed themes with clearly distinct aesthetics (no user-customization in this phase).
- **Switcher:** Only in AppBar (e.g. icon + dropdown/menu), no separate settings page.
- **Persistence:** Selected theme stored in `localStorage` (e.g. key `smallebot.theme`), restored on next load; invalid or missing value falls back to default.
- **Tech:** Root element gets `data-theme="<id>"`; single `app.css` uses `[data-theme="<id>"]` to override the same `--seb-*` variables. MudBlazor `MudThemeProvider` receives theme and dark-mode per theme ID so components match CSS.

---

## 2. Theme list and semantics

| Id               | Working name   | Direction |
|------------------|----------------|-----------|
| `editorial-dark` | 编辑深色       | Current: dark base, amber accent, Outfit + Spectral. |
| `paper-light`    | 纸感浅色       | Light paper/cream base, dark brown or green accent, serif. |
| `terminal`       | 终端           | Dark black/gray, single accent (e.g. green or amber), monospace feel. |
| `dusk`          | 暮色           | Deep blue–purple gradient base, gold/orange accent. |
| `mono`          | 单色           | Grayscale only, high contrast, minimal. |

Default theme: `editorial-dark`. All tokens (e.g. `--seb-bg`, `--seb-surface`, `--seb-primary`, `--seb-text`, `--seb-font-ui`, `--seb-font-body`) are defined per `[data-theme="id"]`; shared selectors (`.smallebot-appbar`, `.markdown-body`, etc.) stay unchanged.

---

## 3. AppBar interaction and persistence

- **Control:** In MainLayout AppBar, add a theme switcher: e.g. `MudIconButton` with icon `Icons.Material.Filled.Palette` (or `Brush`), Tooltip "主题" / "Theme", opening a `MudMenu` with one item per theme (label + optional check for current).
- **On select:** Call JS to `localStorage.setItem("smallebot.theme", themeId)` and `document.documentElement.setAttribute("data-theme", themeId)`; in MainLayout set `_currentThemeId = themeId`, recompute `MudTheme` and `IsDarkMode`, then `StateHasChanged()`.
- **On load:** Before or as Blazor runs, ensure `<html>` has `data-theme` set: either a small inline script in `App.razor` head that reads `localStorage.getItem("smallebot.theme")` and sets `document.documentElement.setAttribute("data-theme", id || "editorial-dark")`, or MainLayout calls JS interop to get the stored id and then sets the attribute (and stores id in `_currentThemeId`). Using an early inline script avoids a flash of wrong theme.

---

## 4. Root element and CSS

- **Root:** Set `data-theme` on `<html>` so that `:root` and `body` can use the same variables. Use JS for this (inline script on first load; on theme change, Blazor invokes JS to update attribute and localStorage).
- **CSS:** In `app.css`, replace the current `:root { ... }` block with `[data-theme="editorial-dark"] { ... }` (same variable set). Add blocks for `[data-theme="paper-light"]`, `[data-theme="terminal"]`, `[data-theme="dusk"]`, `[data-theme="mono"]`, each overriding only the `--seb-*` tokens. Existing rules that reference `var(--seb-*)` remain unchanged.

---

## 5. MudTheme and C#

- **Per-theme config:** For each theme ID, define a `MudTheme` (PaletteLight or PaletteDark, Primary, Background, Surface, etc.) and whether `IsDarkMode` is true or false. Implement a helper (e.g. `GetMudTheme(themeId)`) that returns the corresponding `MudTheme` and a `IsDarkMode(themeId)` (or return both in a small DTO).
- **MainLayout:** Hold `_currentThemeId` (from JS on init; from user choice on switch). Pass `GetMudTheme(_currentThemeId)` to `MudThemeProvider` and the correct `IsDarkMode`. No separate theme service required unless we add more themes or customization later.

---

## 6. JS API

- **Location:** Add to `wwwroot/js/chat.js` or a new `wwwroot/js/theme.js`.
- **Functions:**
  - `getTheme()`: returns `localStorage.getItem("smallebot.theme")` or `"editorial-dark"` if null/invalid.
  - `setTheme(id)`: `localStorage.setItem("smallebot.theme", id)` and `document.documentElement.setAttribute("data-theme", id)`.
- **Inline script (optional):** In `App.razor` head, a short script that runs once: read theme with `getTheme()` (or inline `localStorage.getItem`), then set `document.documentElement.setAttribute("data-theme", ...)` so the first paint uses the saved theme.

---

## 7. Implementation checklist

1. **Constants:** Define theme IDs and default (e.g. `ThemeIds`, `DefaultThemeId`); validate stored value on read.
2. **app.css:** Move current `:root` variables into `[data-theme="editorial-dark"]`; add four more blocks for paper-light, terminal, dusk, mono (only `--seb-*` overrides).
3. **JS:** Implement `getTheme()` and `setTheme(id)`; optionally add inline script in head to set `data-theme` on `<html>` before first paint.
4. **MainLayout:** On init, call JS `getTheme()` and set `_currentThemeId`; compute `MudTheme` and `IsDarkMode` from `_currentThemeId`; add AppBar theme menu (MudMenu + items); on item click call JS `setTheme(id)` and update `_currentThemeId`, then `StateHasChanged`.
5. **MudTheme helper:** Implement `GetMudTheme(themeId)` (and dark-mode flag) for all five theme IDs.

---

## 8. Out of scope (this phase)

- User-defined or custom themes.
- Theme selection anywhere other than AppBar.
- Per-conversation or per-device theme (we only persist one global choice in localStorage).
