using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DownloadRouter.App.Controls;

/// <summary>
/// One row of a Windows 11 Settings style section: glyph, header, description, and the
/// control that changes the value. The hosted control inherits the header as its screen
/// reader name unless it already has one.
/// </summary>
public sealed class SettingsCard : ContentControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SettingsCard), new PropertyMetadata(string.Empty, OnHeaderChanged));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingsCard), new PropertyMetadata(string.Empty, OnDescriptionChanged));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(SettingsCard), new PropertyMetadata(string.Empty));

    private TextBlock? descriptionText;

    public SettingsCard()
    {
        DefaultStyleKey = typeof(SettingsCard);
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        ApplyAutomationName();
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        descriptionText = GetTemplateChild("DescriptionText") as TextBlock;
        UpdateDescriptionVisibility();
        ApplyAutomationName();
    }

    private static void OnDescriptionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((SettingsCard)sender).UpdateDescriptionVisibility();

    private void UpdateDescriptionVisibility()
    {
        if (descriptionText is not null)
        {
            descriptionText.Visibility = string.IsNullOrEmpty(Description) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private static void OnHeaderChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((SettingsCard)sender).ApplyAutomationName();

    private void ApplyAutomationName()
    {
        if (Content is DependencyObject control
            && string.IsNullOrEmpty(AutomationProperties.GetName(control))
            && !string.IsNullOrEmpty(Header))
        {
            AutomationProperties.SetName(control, Header);
        }
    }
}
