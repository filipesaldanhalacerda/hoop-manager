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
            resources["BackgroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 32, 32));
            resources["SurfaceBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 45));
            resources["SurfaceSecondaryBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(58, 58, 58));
            resources["ForegroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
            resources["ForegroundSecondaryBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 170, 170));
        }
        else
        {
            resources["BackgroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 243, 243));
            resources["SurfaceBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255));
            resources["SurfaceSecondaryBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240));
            resources["ForegroundBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 32, 32));
            resources["ForegroundSecondaryBrush"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(96, 96, 96));
        }
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
