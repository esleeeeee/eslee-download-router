using DownloadRouter.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DownloadRouter.App;

public sealed class ThemeManager(AppThemePreference initialPreference)
{
    private readonly HashSet<Window> windows = [];
    private readonly Dictionary<Window, FrameworkElement> roots = [];

    public AppThemePreference CurrentPreference { get; private set; } = initialPreference;

    public static ElementTheme ToElementTheme(AppThemePreference preference)
        => preference switch
        {
            AppThemePreference.Light => ElementTheme.Light,
            AppThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

    public void RegisterWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (windows.Add(window))
        {
            window.Closed += Window_Closed;
            if (window.Content is FrameworkElement registeredRoot)
            {
                roots[window] = registeredRoot;
                registeredRoot.ActualThemeChanged += Root_ActualThemeChanged;
            }
        }

        Apply(window);
    }

    public void SetPreference(AppThemePreference preference)
    {
        CurrentPreference = Enum.IsDefined(preference) ? preference : AppThemePreference.System;
        foreach (var window in windows.ToArray())
        {
            Apply(window);
        }
    }

    private void Apply(Window window)
    {
        if (window.Content is FrameworkElement root)
        {
            root.RequestedTheme = ToElementTheme(CurrentPreference);
            ApplyBackground(root);
        }
    }

    private static void ApplyBackground(FrameworkElement root)
    {
        if (root is not Panel panel)
        {
            return;
        }

        var themeKey = root.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        if (Application.Current.Resources.ThemeDictionaries[themeKey] is ResourceDictionary dictionary
            && dictionary["WindowBackgroundBrush"] is Brush brush)
        {
            panel.Background = brush;
        }
    }

    private static void Root_ActualThemeChanged(FrameworkElement sender, object args)
        => ApplyBackground(sender);

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        if (sender is Window window)
        {
            windows.Remove(window);
            if (roots.Remove(window, out var root))
            {
                root.ActualThemeChanged -= Root_ActualThemeChanged;
            }
        }
    }
}
