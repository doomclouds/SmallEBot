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
        var t = new MudTheme
        {
            PaletteDark = new PaletteDark
            {
                Primary = "#e4a853",
                PrimaryContrastText = "#1a1a1d",
                Secondary = "#71717a",
                Background = "#1a1a1d",
                BackgroundGray = "#252529",
                Surface = "#252529"
            }
        };
        return t;
    }

    private static MudTheme CreatePaperLight()
    {
        var t = new MudTheme
        {
            PaletteLight = new PaletteLight
            {
                Primary = "#2d5016",
                PrimaryContrastText = "#fffef9",
                Secondary = "#57534e",
                Background = "#f5f0e8",
                BackgroundGray = "#faf8f4",
                Surface = "#fffef9"
            }
        };
        return t;
    }

    private static MudTheme CreateTerminal()
    {
        var t = new MudTheme
        {
            PaletteDark = new PaletteDark
            {
                Primary = "#22c55e",
                PrimaryContrastText = "#0d0d0d",
                Secondary = "#a1a1aa",
                Background = "#0d0d0d",
                BackgroundGray = "#1a1a1a",
                Surface = "#1a1a1a"
            }
        };
        return t;
    }

    private static MudTheme CreateDusk()
    {
        var t = new MudTheme
        {
            PaletteDark = new PaletteDark
            {
                Primary = "#e9a23b",
                PrimaryContrastText = "#1e1b2e",
                Secondary = "#a1a1aa",
                Background = "#1e1b2e",
                BackgroundGray = "#2d2842",
                Surface = "#2d2842"
            }
        };
        return t;
    }

    private static MudTheme CreateMono()
    {
        var t = new MudTheme
        {
            PaletteDark = new PaletteDark
            {
                Primary = "#e4e4e7",
                PrimaryContrastText = "#0f0f0f",
                Secondary = "#a1a1aa",
                Background = "#0f0f0f",
                BackgroundGray = "#1a1a1a",
                Surface = "#1a1a1a"
            }
        };
        return t;
    }
}
