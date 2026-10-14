using DownloadRouter.App.Controls;
using DownloadRouter.Core.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DownloadRouter.App.Services;

public enum DialogTone
{
    Information,
    Success,
    Warning,
    Error,
    Question,
    Danger,
}

/// <summary>
/// Themed replacement for message boxes: an icon plate, a title, a message, and Fluent buttons.
/// Only one dialog can be open per window; later requests wait for the current one.
/// The dialog's theme is copied from the window root because popups do not inherit it.
/// </summary>
public static class AppDialog
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static int openCount;

    /// <summary>True while any app dialog is showing; live refreshes avoid rebuilding pages then.</summary>
    public static bool IsOpen => Volatile.Read(ref openCount) > 0;

    public static Task ShowMessageAsync(XamlRoot? root, string message, DialogTone tone = DialogTone.Information, string? title = null)
        => ShowAsync(root, title ?? DefaultTitle(tone), message, tone, primaryText: null, closeText: AppText.Pick("확인", "OK"));

    public static async Task<bool> ConfirmAsync(
        XamlRoot? root,
        string title,
        string message,
        string confirmText,
        bool destructive = false)
        => await ShowAsync(
            root,
            title,
            message,
            destructive ? DialogTone.Danger : DialogTone.Question,
            primaryText: confirmText,
            closeText: AppText.Pick("취소", "Cancel")).ConfigureAwait(true) == ContentDialogResult.Primary;

    public static async Task<ContentDialogResult> ShowAsync(
        XamlRoot? root,
        string title,
        string message,
        DialogTone tone,
        string? primaryText,
        string closeText,
        string? secondaryText = null,
        UIElement? extraContent = null)
    {
        if (root is null)
        {
            return ContentDialogResult.None;
        }

        await Gate.WaitAsync().ConfigureAwait(true);
        Interlocked.Increment(ref openCount);
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = root,
                Content = BuildContent(title, message, tone, extraContent),
                CloseButtonText = closeText,
                DefaultButton = tone == DialogTone.Danger ? ContentDialogButton.Close : ContentDialogButton.Primary,
            };
            if (root.Content is FrameworkElement rootElement)
            {
                dialog.RequestedTheme = rootElement.ActualTheme;
            }

            if (primaryText is not null)
            {
                dialog.PrimaryButtonText = primaryText;
                dialog.PrimaryButtonStyle = tone == DialogTone.Danger
                    ? ThemeStyles.Get<Style>("DangerButtonStyle")
                    : ThemeStyles.Get<Style>("AccentButtonStyle");
            }
            else
            {
                dialog.DefaultButton = ContentDialogButton.Close;
            }

            if (secondaryText is not null)
            {
                dialog.SecondaryButtonText = secondaryText;
            }

            return await dialog.ShowAsync();
        }
        finally
        {
            Interlocked.Decrement(ref openCount);
            Gate.Release();
        }
    }

    private static UIElement BuildContent(string title, string message, DialogTone tone, UIElement? extraContent)
    {
        var (glyph, kind) = tone switch
        {
            DialogTone.Success => ("", BadgeKind.Success),
            DialogTone.Warning => ("", BadgeKind.Caution),
            DialogTone.Error => ("", BadgeKind.Critical),
            DialogTone.Danger => ("", BadgeKind.Critical),
            DialogTone.Question => ("", BadgeKind.Accent),
            _ => ("", BadgeKind.Accent),
        };

        var grid = new Grid { ColumnSpacing = 16, MaxWidth = 460 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var plate = new IconPlate { Glyph = glyph, Kind = kind, Size = 40, VerticalAlignment = VerticalAlignment.Top };
        grid.Children.Add(plate);

        var text = new StackPanel { Spacing = 8 };
        text.Children.Add(new TextBlock
        {
            Text = title,
            Style = ThemeStyles.Get<Style>("SubtitleTextBlockStyle"),
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
        });
        text.Children.Add(new TextBlock
        {
            Text = message,
            Style = ThemeStyles.Get<Style>("SecondaryTextStyle"),
            IsTextSelectionEnabled = true,
        });
        if (extraContent is not null)
        {
            text.Children.Add(extraContent);
        }

        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    private static string DefaultTitle(DialogTone tone)
        => tone switch
        {
            DialogTone.Error => AppText.Pick("작업을 완료하지 못했습니다", "Something went wrong"),
            DialogTone.Warning => AppText.Pick("확인이 필요합니다", "Please check"),
            DialogTone.Success => AppText.Pick("완료했습니다", "Done"),
            _ => "eslee Download Router",
        };
}
