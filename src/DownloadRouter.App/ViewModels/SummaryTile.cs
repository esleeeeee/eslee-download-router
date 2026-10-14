using DownloadRouter.App.Controls;
using DownloadRouter.App.Views;

namespace DownloadRouter.App.ViewModels;

/// <summary>A count on the home page that opens the matching download filter.</summary>
internal sealed class SummaryTile(string title, string glyph, DownloadFilter filter, bool attentionWhenPositive) : ObservableObject
{
    private int count;

    public string Title { get; } = title;

    public string Glyph { get; } = glyph;

    public DownloadFilter Filter { get; } = filter;

    public string Value => count.ToString(System.Globalization.CultureInfo.CurrentCulture);

    public BadgeKind Kind => attentionWhenPositive && count > 0 ? BadgeKind.Caution : BadgeKind.Accent;

    public string AutomationName => $"{Title}: {Value}";

    public void SetCount(int value)
    {
        if (count == value)
        {
            return;
        }

        count = value;
        Raise(nameof(Value));
        Raise(nameof(Kind));
        Raise(nameof(AutomationName));
    }

    public override string ToString() => AutomationName;
}
