using DownloadRouter.Core.Localization;
using Microsoft.UI.Xaml.Markup;

namespace DownloadRouter.App.Controls;

/// <summary>
/// XAML text in both languages side by side: <c>Text="{c:L Ko='규칙', En='Rules'}"</c>.
/// The language is fixed for the process lifetime (a change applies after restart).
/// </summary>
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed class L : MarkupExtension
{
    public string Ko { get; set; } = string.Empty;

    public string En { get; set; } = string.Empty;

    protected override object ProvideValue()
        => AppText.Pick(Ko, En);
}
