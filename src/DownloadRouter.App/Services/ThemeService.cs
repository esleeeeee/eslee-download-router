using System.Runtime.InteropServices;
using DownloadRouter.Core.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace DownloadRouter.App.Services;

/// <summary>
/// Applies the Light / Dark / Windows-setting choice to every open window. Colors live in
/// Themes/Palette.xaml and are referenced with {ThemeResource}, so setting RequestedTheme on a
/// window root restyles it in place. The native title bar is matched through AppWindow colors
/// and the DWM dark-mode (20) and caption-color (35) attributes.
/// </summary>
public sealed class ThemeService
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmCaptionColor = 35;

    private readonly List<Window> windows = [];
    private readonly UISettings uiSettings = new();
    private readonly DispatcherQueue dispatcher;

    public ThemeService(AppThemePreference preference, DispatcherQueue dispatcher)
    {
        Preference = Enum.IsDefined(preference) ? preference : AppThemePreference.System;
        this.dispatcher = dispatcher;
        // Fires on a background thread when Windows switches between light and dark apps.
        uiSettings.ColorValuesChanged += (_, _) => this.dispatcher.TryEnqueue(ApplyAll);
    }

    public AppThemePreference Preference { get; private set; }

    public event EventHandler? ThemeApplied;

    public static ElementTheme ToElementTheme(AppThemePreference preference)
        => preference switch
        {
            AppThemePreference.Light => ElementTheme.Light,
            AppThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

    /// <summary>Call after the window content is set.</summary>
    public void Register(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!windows.Contains(window))
        {
            windows.Add(window);
            window.Closed += (_, _) => windows.Remove(window);
            if (window.Content is FrameworkElement root)
            {
                root.ActualThemeChanged += (_, _) => ApplyTitleBar(window);
            }
        }

        Apply(window);
    }

    public void SetPreference(AppThemePreference preference)
    {
        Preference = Enum.IsDefined(preference) ? preference : AppThemePreference.System;
        ApplyAll();
    }

    public bool IsDark(Window window)
        => window.Content is FrameworkElement root && root.ActualTheme == ElementTheme.Dark;

    private void ApplyAll()
    {
        foreach (var window in windows.ToArray())
        {
            Apply(window);
        }

        ThemeApplied?.Invoke(this, EventArgs.Empty);
    }

    private void Apply(Window window)
    {
        if (window.Content is FrameworkElement root)
        {
            // "Windows setting" resolves explicitly so a running app follows a later system change.
            root.RequestedTheme = Preference == AppThemePreference.System
                ? (IsSystemDark() ? ElementTheme.Dark : ElementTheme.Light)
                : ToElementTheme(Preference);
        }

        ApplyTitleBar(window);
    }

    private bool IsSystemDark()
    {
        // The background color is white in light app mode and black in dark app mode.
        var background = uiSettings.GetColorValue(UIColorType.Background);
        return background.R + background.G + background.B < 3 * 128;
    }

    private static void ApplyTitleBar(Window window)
    {
        if (window.Content is not FrameworkElement root)
        {
            return;
        }

        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (handle == 0)
        {
            return;
        }

        var dark = root.ActualTheme == ElementTheme.Dark;
        var themeKey = dark ? "Dark" : "Light";
        var background = ReadColor(themeKey, "TitleBarBackgroundColor");
        var foreground = ReadColor(themeKey, "TitleBarForegroundColor");
        var hover = ReadColor(themeKey, "TitleBarButtonHoverColor");
        var pressed = ReadColor(themeKey, "TitleBarButtonPressedColor");
        var inactive = ReadColor(themeKey, "TitleBarInactiveForegroundColor");

        var useDark = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref useDark, sizeof(int));
        var caption = background.R | (background.G << 8) | (background.B << 16);
        _ = DwmSetWindowAttribute(handle, DwmCaptionColor, ref caption, sizeof(int));

        var titleBar = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle)).TitleBar;
        titleBar.BackgroundColor = background;
        titleBar.InactiveBackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveForegroundColor = inactive;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = inactive;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressed;
        titleBar.ButtonPressedForegroundColor = foreground;
    }

    /// <summary>Reads a palette color for the given theme without depending on which window asks.</summary>
    private static Color ReadColor(string themeKey, string key)
    {
        foreach (var dictionary in Application.Current.Resources.MergedDictionaries)
        {
            if (dictionary.ThemeDictionaries.TryGetValue(themeKey, out var theme)
                && theme is ResourceDictionary themeDictionary
                && themeDictionary.TryGetValue(key, out var value)
                && value is Color color)
            {
                return color;
            }
        }

        return default;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
