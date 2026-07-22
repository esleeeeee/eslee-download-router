using DownloadRouter.Core.Models;
using Microsoft.UI.Xaml;

namespace DownloadRouter.App;

public sealed class ThemeManager(AppThemePreference initialPreference)
{
    private readonly HashSet<Window> windows = [];

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
        }
    }

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        if (sender is Window window)
        {
            windows.Remove(window);
        }
    }
}
