using System.Windows;
using Microsoft.Win32;

namespace RopeShelf.App;

internal static class ThemePalette
{
    public static void ApplyFromSystem()
    {
        bool dark = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch { /* Registry unavailable: retain the readable default palette. */ }
        Apply(dark, SystemParameters.HighContrast);
    }
    internal static void Apply(bool dark, bool highContrast)
    {
        var resources = Application.Current.Resources;
        if (highContrast)
        {
            resources["Ink"] = SystemColors.WindowTextBrush; resources["Paper"] = SystemColors.WindowBrush;
            resources["Glass"] = SystemColors.WindowBrush; resources["Highlight"] = SystemColors.WindowTextBrush;
            return;
        }
        var palette = resources.MergedDictionaries.First(d => d.Contains("DarkGlass"));
        foreach (var name in new[] { "Ink", "Paper", "Highlight", "Glass" }) resources[name] = palette[(dark ? "Dark" : "") + name];
    }
}
