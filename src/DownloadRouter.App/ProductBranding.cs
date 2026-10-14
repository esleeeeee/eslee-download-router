using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;

namespace DownloadRouter.App;

/// <summary>
/// Gives every window the application icon embedded in the executable (assets/branding ICO).
/// AppWindow.SetIcon needs an icon handle; v1.x passed the resource number itself as a handle,
/// so the title bar and Alt+Tab showed no icon.
/// </summary>
internal static class ProductBranding
{
    private const uint ApplicationIconResourceId = 32512;
    private const uint ImageIcon = 1;
    private const uint DefaultSize = 0x00000040;
    private const uint Shared = 0x00008000;
    private static nint iconHandle;

    public static void ApplyWindowIcon(AppWindow window)
    {
        if (iconHandle == 0)
        {
            // Shared icons are owned by the system and stay valid for the process lifetime.
            iconHandle = LoadImage(GetModuleHandle(null), (nint)ApplicationIconResourceId, ImageIcon, 0, 0, DefaultSize | Shared);
            if (iconHandle == 0 && Environment.ProcessPath is string executable)
            {
                _ = ExtractIconEx(executable, 0, out iconHandle, out var small, 1);
                if (small != 0)
                {
                    _ = DestroyIcon(small);
                }
            }
        }

        if (iconHandle != 0)
        {
            window.SetIcon(Win32Interop.GetIconIdFromIcon(iconHandle));
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadImage(nint instance, nint name, uint type, int width, int height, uint load);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, out nint large, out nint small, uint count);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
}
