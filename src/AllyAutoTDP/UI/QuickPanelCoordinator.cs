using AllyAutoTDP.Application;
using AllyAutoTDP.Windows;

namespace AllyAutoTDP.UI;

public interface IQuickPanelWindow
{
    bool Visible { get; }

    bool IsTopMost { get; }

    bool IsDisposed { get; }

    event EventHandler? Deactivated;

    event EventHandler? CloseRequested;

    void ShowPanel(nint anchorWindow);

    void HidePanel();

    void Post(Action action);
}

public sealed class QuickPanelCoordinator : IDisposable
{
    private readonly AllyApplicationController _controller;
    private readonly IQuickPanelWindow _panel;
    private readonly IWindowFocusService _focusService;
    private readonly int _ownProcessId;
    private nint _previousForegroundWindow;
    private bool _suppressDeactivation;
    private bool _disposed;

    public QuickPanelCoordinator(
        AllyApplicationController controller,
        IQuickPanelWindow panel,
        IWindowFocusService focusService,
        int? ownProcessId = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _focusService = focusService ?? throw new ArgumentNullException(nameof(focusService));
        _ownProcessId = ownProcessId ?? Environment.ProcessId;
        _panel.Deactivated += PanelDeactivated;
        _panel.CloseRequested += PanelCloseRequested;
    }

    public bool IsVisible => _panel.Visible;

    public void Toggle()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_panel.Visible)
            HideAndRestoreFocus();
        else
            Show();
    }

    public void ShowPanel()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_panel.Visible)
            Show();
    }

    public void HideWithoutFocusRestore()
    {
        if (_disposed || !_panel.Visible)
            return;

        _suppressDeactivation = true;
        try
        {
            _panel.HidePanel();
            _controller.ClearInvokingApplication();
            _previousForegroundWindow = nint.Zero;
        }
        finally
        {
            _suppressDeactivation = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _panel.Deactivated -= PanelDeactivated;
        _panel.CloseRequested -= PanelCloseRequested;
        HideWithoutFocusRestore();
        _disposed = true;
    }

    private void Show()
    {
        _previousForegroundWindow = _focusService.GetForegroundWindow();
        _controller.CaptureInvokingApplication();
        InvokingApplicationSnapshot? invoking =
            _controller.GetSnapshot().InvokingApplication;
        nint anchorWindow = invoking?.WindowHandle ?? _previousForegroundWindow;

        _suppressDeactivation = true;
        try
        {
            _panel.ShowPanel(anchorWindow);
        }
        finally
        {
            _suppressDeactivation = false;
        }
    }

    private void HideAndRestoreFocus()
    {
        _suppressDeactivation = true;
        try
        {
            _panel.HidePanel();
        }
        finally
        {
            _suppressDeactivation = false;
        }

        RestorePreviousFocus();
        _controller.ClearInvokingApplication();
        _previousForegroundWindow = nint.Zero;
    }

    private void PanelDeactivated(object? sender, EventArgs e)
    {
        if (_suppressDeactivation || !_panel.Visible)
            return;

        _panel.Post(ConfirmExternalForeground);
    }

    private void ConfirmExternalForeground()
    {
        if (_suppressDeactivation || !_panel.Visible)
            return;

        nint foregroundWindow = _focusService.GetForegroundWindow();
        if (foregroundWindow == nint.Zero)
            return;

        int processId = _focusService.GetProcessId(foregroundWindow);
        if (processId == 0 || processId == _ownProcessId)
            return;

        HideWithoutFocusRestore();
    }

    private void PanelCloseRequested(object? sender, EventArgs e) =>
        HideAndRestoreFocus();

    private void RestorePreviousFocus()
    {
        if (_previousForegroundWindow == nint.Zero ||
            !_focusService.IsWindow(_previousForegroundWindow))
        {
            return;
        }

        InvokingApplicationSnapshot? invoking =
            _controller.GetSnapshot().InvokingApplication;
        if (invoking is not null &&
            invoking.WindowHandle == _previousForegroundWindow &&
            !_focusService.IsSameProcess(
                _previousForegroundWindow,
                invoking.Application))
        {
            return;
        }

        _focusService.TrySetForegroundWindow(_previousForegroundWindow);
    }
}
