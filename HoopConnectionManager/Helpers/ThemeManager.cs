using System.Windows;
using HoopConnectionManager.Configuration;
using HoopConnectionManager.Services.Abstractions;

namespace HoopConnectionManager.Helpers;

/// <summary>
/// Gerencia a aplicação de temas claro, escuro ou automático.
/// </summary>
public static class ThemeManager
{
    public static void ApplyTheme(string theme)
    {
        var resourceDictionary = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Resources/Styles/Colors.xaml")
        };

        var isDark = theme switch
        {
            "Dark" => true,
            "Light" => false,
            "Auto" => IsSystemDarkTheme(),
            _ => IsSystemDarkTheme()
        };

        if (System.Windows.Application.Current.Resources.MergedDictionaries.Count > 0)
        {
            System.Windows.Application.Current.Resources.MergedDictionaries[0] = resourceDictionary;
        }
        else
        {
            System.Windows.Application.Current.Resources.MergedDictionaries.Add(resourceDictionary);
        }

        OverrideColors(isDark);
    }

    private static void OverrideColors(bool isDark)
    {
        var resources = System.Windows.Application.Current.Resources;

        if (isDark)
        {
            SetBrush(resources, "BackgroundBrush", 11, 16, 32);
            SetBrush(resources, "NavigationBrush", 10, 18, 38);
            SetBrush(resources, "NavigationSurfaceBrush", 17, 29, 55);
            SetBrush(resources, "NavigationForegroundBrush", 247, 248, 252);
            SetBrush(resources, "NavigationMutedBrush", 156, 167, 190);
            SetBrush(resources, "SurfaceBrush", 17, 24, 39);
            SetBrush(resources, "SurfaceSecondaryBrush", 24, 34, 53);
            SetBrush(resources, "ElevatedBrush", 31, 43, 65);
            SetBrush(resources, "ForegroundBrush", 244, 247, 251);
            SetBrush(resources, "ForegroundSecondaryBrush", 167, 177, 194);
            SetBrush(resources, "BorderBrush", 42, 53, 74);
            SetBrush(resources, "AccentBrush", 79, 117, 190);
            SetBrush(resources, "AccentHoverBrush", 100, 140, 215);
            SetBrush(resources, "AccentSoftBrush", 23, 38, 74);
            SetBrush(resources, "AccentForegroundBrush", 255, 255, 255);
            SetBrush(resources, "RowHoverBrush", 24, 40, 58);
            SetBrush(resources, "SelectionBrush", 29, 51, 87);
            SetBrush(resources, "SuccessBrush", 66, 217, 160);
            SetBrush(resources, "WarningBrush", 245, 196, 81);
            SetBrush(resources, "ErrorBrush", 255, 115, 133);
            SetBrush(resources, "SuccessSurfaceBrush", 22, 63, 55);
            SetBrush(resources, "WarningSurfaceBrush", 73, 60, 32);
            SetBrush(resources, "ErrorSurfaceBrush", 73, 33, 43);
        }
        else
        {
            SetBrush(resources, "BackgroundBrush", 246, 247, 249);
            SetBrush(resources, "NavigationBrush", 10, 18, 38);
            SetBrush(resources, "NavigationSurfaceBrush", 17, 29, 55);
            SetBrush(resources, "NavigationForegroundBrush", 247, 248, 252);
            SetBrush(resources, "NavigationMutedBrush", 156, 167, 190);
            SetBrush(resources, "SurfaceBrush", 255, 255, 255);
            SetBrush(resources, "SurfaceSecondaryBrush", 241, 243, 246);
            SetBrush(resources, "ElevatedBrush", 233, 237, 242);
            SetBrush(resources, "ForegroundBrush", 23, 27, 36);
            SetBrush(resources, "ForegroundSecondaryBrush", 101, 112, 132);
            SetBrush(resources, "BorderBrush", 220, 225, 232);
            SetBrush(resources, "AccentBrush", 49, 92, 170);
            SetBrush(resources, "AccentHoverBrush", 40, 78, 148);
            SetBrush(resources, "AccentSoftBrush", 233, 238, 250);
            SetBrush(resources, "AccentForegroundBrush", 255, 255, 255);
            SetBrush(resources, "RowHoverBrush", 245, 247, 250);
            SetBrush(resources, "SelectionBrush", 233, 238, 250);
            SetBrush(resources, "SuccessBrush", 22, 134, 95);
            SetBrush(resources, "WarningBrush", 154, 107, 15);
            SetBrush(resources, "ErrorBrush", 196, 62, 85);
            SetBrush(resources, "SuccessSurfaceBrush", 216, 244, 233);
            SetBrush(resources, "WarningSurfaceBrush", 255, 246, 214);
            SetBrush(resources, "ErrorSurfaceBrush", 255, 229, 233);
        }
    }

    private static void SetBrush(ResourceDictionary resources, string key, byte red, byte green, byte blue)
    {
        resources[key] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue));
    }

    private static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int useLightTheme && useLightTheme == 0;
        }
        catch
        {
            return true;
        }
    }
}
