namespace DownloadRouter.Core.Jobs;

public readonly record struct NativeWindowState(
    nint Handle,
    bool IsVisible,
    bool IsIconic,
    nint OwnerHandle,
    nint ForegroundHandle)
{
    public bool IsForeground => Handle != 0 && ForegroundHandle == Handle;
}

public interface ISelectionWindowActivationOperations
{
    NativeWindowState Capture(nint windowHandle);

    bool ShowSelectionNormal(nint selectionWindowHandle);

    void ActivateSelection();

    bool BringSelectionToTop(nint selectionWindowHandle);

    bool TrySetSelectionForeground(nint selectionWindowHandle);

    bool TryAttachInputAndActivate(nint selectionWindowHandle);

    bool RaiseSelectionAboveOtherWindows(nint selectionWindowHandle);

    void FlashSelection(nint selectionWindowHandle);
}

public sealed record SelectionWindowActivationResult(
    NativeWindowState MainBefore,
    NativeWindowState SelectionBefore,
    NativeWindowState SelectionAfterShow,
    NativeWindowState MainAfter,
    NativeWindowState SelectionAfter,
    bool ShowNormalSucceeded,
    bool BringToTopSucceeded,
    bool DirectForegroundSucceeded,
    bool AttachedForegroundSucceeded,
    bool RaisedForegroundSucceeded,
    bool FlashFallbackUsed);

public sealed class SelectionWindowActivationCoordinator(
    ISelectionWindowActivationOperations operations)
{
    public SelectionWindowActivationResult Activate(
        nint mainWindowHandle,
        nint selectionWindowHandle)
    {
        ArgumentNullException.ThrowIfNull(operations);
        if (selectionWindowHandle == 0)
        {
            throw new ArgumentException("The selection window handle must be valid.", nameof(selectionWindowHandle));
        }

        var mainBefore = operations.Capture(mainWindowHandle);
        var selectionBefore = operations.Capture(selectionWindowHandle);
        var showNormalSucceeded = operations.ShowSelectionNormal(selectionWindowHandle);
        operations.ActivateSelection();
        var bringToTopSucceeded = operations.BringSelectionToTop(selectionWindowHandle);
        var selectionAfterShow = operations.Capture(selectionWindowHandle);

        var directForegroundSucceeded = operations.TrySetSelectionForeground(selectionWindowHandle);
        var attachedForegroundSucceeded = false;
        var raisedForegroundSucceeded = false;

        if (!operations.Capture(selectionWindowHandle).IsForeground)
        {
            attachedForegroundSucceeded = operations.TryAttachInputAndActivate(selectionWindowHandle);
        }

        if (!operations.Capture(selectionWindowHandle).IsForeground)
        {
            raisedForegroundSucceeded = operations.RaiseSelectionAboveOtherWindows(selectionWindowHandle);
        }

        var selectionAfter = operations.Capture(selectionWindowHandle);
        var flashFallbackUsed = !selectionAfter.IsForeground;
        if (flashFallbackUsed)
        {
            operations.FlashSelection(selectionWindowHandle);
        }

        return new SelectionWindowActivationResult(
            mainBefore,
            selectionBefore,
            selectionAfterShow,
            operations.Capture(mainWindowHandle),
            selectionAfter,
            showNormalSucceeded,
            bringToTopSucceeded,
            directForegroundSucceeded,
            attachedForegroundSucceeded,
            raisedForegroundSucceeded,
            flashFallbackUsed);
    }
}
