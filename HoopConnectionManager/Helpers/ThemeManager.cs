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
            SetBrush(resources, "BackgroundBrush", 7, 17, 29);
            SetBrush(resources, "NavigationBrush", 10, 18, 38);
            SetBrush(resources, "NavigationSurfaceBrush", 17, 29, 55);
            SetBrush(resources, "NavigationForegroundBrush", 247, 248, 252);
            SetBrush(resources, "NavigationMutedBrush", 156, 167, 190);
            SetBrush(resources, "SurfaceBrush", 16, 31, 46);
            SetBrush(resources, "SurfaceSecondaryBrush", 22, 42, 60);
            SetBrush(resources, "ElevatedBrush", 26, 49, 69);
            SetBrush(resources, "ForegroundBrush", 242, 247, 250);
            SetBrush(resources, "ForegroundSecondaryBrush", 145, 166, 183);
            SetBrush(resources, "BorderBrush", 40, 66, 87);
            SetBrush(resources, "AccentSoftBrush", 22, 61, 82);
            SetBrush(resources, "SuccessSurfaceBrush", 22, 63, 55);
            SetBrush(resources, "WarningSurfaceBrush", 73, 60, 32);
            SetBrush(resources, "ErrorSurfaceBrush", 73, 33, 43);
        }
        else
        {
            SetBrush(resources, "BackgroundBrush", 237, 244, 248);
            SetBrush(resources, "NavigationBrush", 10, 18, 38);
            SetBrush(resources, "NavigationSurfaceBrush", 17, 29, 55);
            SetBrush(resources, "NavigationForegroundBrush", 247, 248, 252);
            SetBrush(resources, "NavigationMutedBrush", 156, 167, 190);
            SetBrush(resources, "SurfaceBrush", 255, 255, 255);
            SetBrush(resources, "SurfaceSecondaryBrush", 239, 246, 249);
            SetBrush(resources, "ElevatedBrush", 228, 239, 245);
            SetBrush(resources, "ForegroundBrush", 12, 31, 44);
            SetBrush(resources, "ForegroundSecondaryBrush", 79, 105, 122);
            SetBrush(resources, "BorderBrush", 190, 211, 222);
            SetBrush(resources, "AccentSoftBrush", 232, 237, 248);
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
