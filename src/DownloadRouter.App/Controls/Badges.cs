using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DownloadRouter.App.Controls;

/// <summary>Semantic color of a badge or icon plate. Each kind maps to palette tokens in Controls.xaml.</summary>
public enum BadgeKind
{
    Neutral,
    Accent,
    Success,
    Caution,
    Critical,
}

/// <summary>
/// Pill-shaped status label. The kind selects a style whose brushes are theme resources,
/// so the badge follows light/dark switches without being rebuilt.
/// </summary>
public sealed class StatusBadge : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(StatusBadge), new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(BadgeKind), typeof(StatusBadge), new PropertyMetadata(BadgeKind.Neutral, OnChanged));

    private readonly Border border = new();
    private readonly TextBlock text = new();

    public StatusBadge()
    {
        border.Child = text;
        Content = border;
        IsTabStop = false;
        Apply();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public BadgeKind Kind
    {
        get => (BadgeKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private static void OnChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((StatusBadge)sender).Apply();

    private void Apply()
    {
        border.Style = ThemeStyles.Get<Style>($"{Kind}BadgeStyle");
        text.Style = ThemeStyles.Get<Style>($"{Kind}BadgeTextStyle");
        text.Text = Text;
        Visibility = string.IsNullOrEmpty(Text) ? Visibility.Collapsed : Visibility.Visible;
    }
}

/// <summary>Round tinted plate with a Segoe Fluent Icons glyph, used as the leading icon of list rows.</summary>
public sealed class IconPlate : UserControl
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(IconPlate), new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(BadgeKind), typeof(IconPlate), new PropertyMetadata(BadgeKind.Neutral, OnChanged));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(IconPlate), new PropertyMetadata(36d, OnChanged));

    private readonly Border border = new();
    private readonly FontIcon icon = new();

    public IconPlate()
    {
        border.Child = icon;
        Content = border;
        IsTabStop = false;
        Apply();
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public BadgeKind Kind
    {
        get => (BadgeKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private static void OnChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((IconPlate)sender).Apply();

    private void Apply()
    {
        border.Style = ThemeStyles.Get<Style>($"{Kind}IconPlateStyle");
        icon.Style = ThemeStyles.Get<Style>($"{Kind}IconGlyphStyle");
        icon.Glyph = Glyph;
        border.Width = Size;
        border.Height = Size;
        border.CornerRadius = new CornerRadius(Size / 2);
        icon.FontSize = Math.Round(Size * 0.45);
    }
}

internal static class ThemeStyles
{
    /// <summary>
    /// The indexer (unlike TryGetValue) also searches merged dictionaries, where Controls.xaml lives.
    /// </summary>
    public static T? Get<T>(string key)
        where T : class
    {
        try
        {
            return Application.Current.Resources[key] as T;
        }
        catch (Exception exception) when (exception is KeyNotFoundException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
