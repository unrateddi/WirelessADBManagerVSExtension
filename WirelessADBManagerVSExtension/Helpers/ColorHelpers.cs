using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using System;
using System.Windows.Media;

namespace WirelessADBManagerVSExtension.Helpers;

internal static class ColorHelpers
{
    internal static Color GetVsThemeColor(ThemeResourceKey themeResourceKey, Color returnFallbackColor)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var color = VSColorTheme.GetThemedColor(themeResourceKey);
            return Color.FromArgb(color.A, color.R, color.G, color.B);
        }
        catch (Exception)
        {
            return returnFallbackColor; // Or a default color
        }
    }
}
