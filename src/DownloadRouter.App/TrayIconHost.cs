using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace DownloadRouter.App;

public sealed class TrayIconHost : IDisposable
{
    private const uint TrayMessage = 0x8001;
    private const uint IconId = 1;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifInfo = 0x00000010;
    private const uint NotifyIconVersion4 = 4;
    private const uint WmLeftButtonDoubleClick = 0x0203;
    private const uint WmRightButtonUp = 0x0205;
    private const uint WmContextMenu = 0x007B;
    private const uint WmCommand = 0x0111;
    private const uint NinSelect = 0x0400;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;
    private const uint OpenCommand = 1001;
    private const uint PendingCommand = 1002;
    private const uint SettingsCommand = 1003;
    private const uint ExitCommand = 1004;
    private static readonly ConcurrentDictionary<nint, TrayIconHost> Instances = new();
    private static readonly WindowProcedureDelegate WindowProcedureInstance = WindowProcedure;

    private readonly DispatcherQueue dispatcher;
    private readonly Action open;
    private readonly Action openPending;
    private readonly Action openSettings;
    private readonly Action exit;
    private readonly string windowClassName = $"eslee.DownloadRouter.Tray.{Environment.ProcessId}";
    private nint windowHandle;
    private nint menuHandle;
    private bool disposed;

    public TrayIconHost(
        DispatcherQueue dispatcher,
        Action open,
        Action openPending,
        Action openSettings,
        Action exit)
    {
        this.dispatcher = dispatcher;
        this.open = open;
        this.openPending = openPending;
        this.openSettings = openSettings;
        this.exit = exit;
        CreateMessageWindow();
        CreateMenu();
        AddIcon();
    }

    public void ShowSelectionNotification(int pendingCount)
    {
        var data = CreateIconData(NifInfo);
        data.InfoTitle = "저장 위치 선택 대기";
        data.Info = $"선택을 기다리는 다운로드가 {pendingCount}개 있습니다.";
        data.InfoFlags = 0x00000001;
        _ = ShellNotifyIcon(NimModify, ref data);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        var data = CreateIconData(0);
        _ = ShellNotifyIcon(NimDelete, ref data);
        Instances.TryRemove(windowHandle, out _);
        if (menuHandle != 0)
        {
            _ = DestroyMenu(menuHandle);
        }
        if (windowHandle != 0)
        {
            _ = DestroyWindow(windowHandle);
        }
    }

    private void CreateMessageWindow()
    {
        var windowClass = new WindowClass
        {
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedureInstance),
            Instance = GetModuleHandle(null),
            ClassName = windowClassName,
        };
        if (RegisterClass(ref windowClass) == 0)
        {
            throw new InvalidOperationException("The tray message window class could not be registered.");
        }

        windowHandle = CreateWindowEx(
            0,
            windowClassName,
            "eslee Download Router tray",
            0,
            0,
            0,
            0,
            0,
            new nint(-3),
            0,
            windowClass.Instance,
            0);
        if (windowHandle == 0)
        {
            throw new InvalidOperationException("The tray message window could not be created.");
        }

        Instances[windowHandle] = this;
    }

    private void CreateMenu()
    {
        menuHandle = CreatePopupMenu();
        _ = AppendMenu(menuHandle, MfString, OpenCommand, "eslee Download Router 열기");
        _ = AppendMenu(menuHandle, MfString, PendingCommand, "저장 위치 선택 대기 열기");
        _ = AppendMenu(menuHandle, MfString, SettingsCommand, "일반 설정");
        _ = AppendMenu(menuHandle, MfSeparator, 0, string.Empty);
        _ = AppendMenu(menuHandle, MfString, ExitCommand, "종료");
    }

    private void AddIcon()
    {
        var data = CreateIconData(NifMessage | NifIcon | NifTip);
        data.CallbackMessage = TrayMessage;
        data.Icon = LoadIcon(0, new nint(32512));
        data.Tip = "eslee Download Router";
        if (!ShellNotifyIcon(NimAdd, ref data))
        {
            throw new InvalidOperationException("The system tray icon could not be created.");
        }

        data.TimeoutOrVersion = NotifyIconVersion4;
        _ = ShellNotifyIcon(NimSetVersion, ref data);
    }

    private NotifyIconData CreateIconData(uint flags)
        => new()
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            Window = windowHandle,
            Id = IconId,
            Flags = flags,
            Tip = string.Empty,
            Info = string.Empty,
            InfoTitle = string.Empty,
        };

    private void HandleNotification(uint notification)
    {
        if (notification is WmLeftButtonDoubleClick or NinSelect)
        {
            dispatcher.TryEnqueue(() => open());
            return;
        }

        if (notification is not WmRightButtonUp and not WmContextMenu)
        {
            return;
        }

        _ = GetCursorPos(out var point);
        _ = SetForegroundWindow(windowHandle);
        var command = TrackPopupMenu(
            menuHandle,
            TpmRightButton | TpmReturnCommand,
            point.X,
            point.Y,
            0,
            windowHandle,
            0);
        HandleCommand(command);
    }

    private void HandleCommand(uint command)
    {
        var action = command switch
        {
            OpenCommand => open,
            PendingCommand => openPending,
            SettingsCommand => openSettings,
            ExitCommand => exit,
            _ => null,
        };
        if (action is not null)
        {
            dispatcher.TryEnqueue(() => action());
        }
    }

    private static nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == TrayMessage && Instances.TryGetValue(window, out var instance))
        {
            instance.HandleNotification((uint)(lParam.ToInt64() & 0xffff));
            return 0;
        }

        if (message == WmCommand && Instances.TryGetValue(window, out instance))
        {
            instance.HandleCommand((uint)(wParam.ToInt64() & 0xffff));
            return 0;
        }

        return DefWindowProc(window, message, wParam, lParam);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedureDelegate(nint window, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Style;
        public nint WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid ItemGuid;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(nint menu, uint flags, uint id, string text);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(
        nint menu,
        uint flags,
        int x,
        int y,
        int reserved,
        nint window,
        nint rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadIcon(nint instance, nint iconName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
