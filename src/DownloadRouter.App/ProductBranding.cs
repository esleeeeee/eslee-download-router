using Microsoft.UI;
using Microsoft.UI.Windowing;

namespace DownloadRouter.App;

internal static class ProductBranding
{
    private const uint ApplicationIconResourceId = 32512;

    public static void ApplyWindowIcon(AppWindow window)
        => window.SetIcon(new IconId { Value = ApplicationIconResourceId });
}
