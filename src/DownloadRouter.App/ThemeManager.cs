using DownloadRouter.Core.Models;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

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

    public Brush? GetThemeBrush(string resourceKey)
    {
        var activeTheme = roots.Values.FirstOrDefault()?.ActualTheme == ElementTheme.Dark
            ? "Dark"
            : "Light";
        return Application.Current.Resources.ThemeDictionaries[activeTheme] is ResourceDictionary dictionary
            ? dictionary[resourceKey] as Brush
            : null;
    }

    private void Apply(Window window)
    {
        if (window.Content is FrameworkElement root)
        {
            root.RequestedTheme = ToElementTheme(CurrentPreference);
            ApplyBackground(root);
            ApplyTitleBar(window, root.ActualTheme);
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

    private void Root_ActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplyBackground(sender);
        var window = roots.FirstOrDefault(pair => ReferenceEquals(pair.Value, sender)).Key;
        if (window is not null)
        {
            ApplyTitleBar(window, sender.ActualTheme);
        }
    }

    private static void ApplyTitleBar(Window window, ElementTheme theme)
    {
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(
            WinRT.Interop.WindowNative.GetWindowHandle(window));
        var titleBar = AppWindow.GetFromWindowId(windowId).TitleBar;
        var dark = theme == ElementTheme.Dark;
        titleBar.BackgroundColor = dark ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 243, 243, 243);
        titleBar.ForegroundColor = dark ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 0, 0, 0);
        titleBar.ButtonBackgroundColor = titleBar.BackgroundColor;
        titleBar.ButtonForegroundColor = titleBar.ForegroundColor;
        titleBar.ButtonHoverBackgroundColor = dark ? Color.FromArgb(255, 56, 56, 56) : Color.FromArgb(255, 229, 229, 229);
        titleBar.ButtonHoverForegroundColor = titleBar.ForegroundColor;
        titleBar.ButtonPressedBackgroundColor = dark ? Color.FromArgb(255, 74, 74, 74) : Color.FromArgb(255, 210, 210, 210);
        titleBar.ButtonPressedForegroundColor = titleBar.ForegroundColor;
    }

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
