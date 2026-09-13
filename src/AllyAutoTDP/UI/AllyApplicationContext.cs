using AllyAutoTDP.Application;
using AllyAutoTDP.Configuration;
using AllyAutoTDP.Windows;

namespace AllyAutoTDP.UI;

public sealed class AllyApplicationContext : ApplicationContext
{
    private readonly AllyApplicationController _controller;
    private readonly QuickPanelForm _quickPanel;
    private readonly ProfileWorkflow _profileWorkflow;
    private readonly GlobalHotkeyHost _hotkeyHost;
    private readonly QuickPanelCoordinator _quickPanelCoordinator;
    private readonly NotifyIcon _notifyIcon;
    private readonly AllyTrayContextMenu _trayMenu;
    private readonly ToolStripMenuItem _autoTdpMenuItem;
    private readonly ToolStripMenuItem _openQuickPanelItem;
    private readonly System.Threading.Timer _pollTimer;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private int _pollInProgress;
    private bool _exiting;

    public AllyApplicationContext(
        AllyApplicationController? controller = null,
        bool startInBackground = false)
    {
        _controller = controller ?? AllyApplicationController.CreateDefault();
        _quickPanel = new QuickPanelForm(_controller);
        _profileWorkflow = new ProfileWorkflow(_controller);
        IsBackgroundMode = startInBackground;
        _quickPanelCoordinator = new QuickPanelCoordinator(
            _controller,
            _quickPanel,
            new User32WindowFocusService());
        _quickPanel.AutoTdpStateChanged += QuickPanelAutoTdpStateChanged;
        _quickPanel.AddProfileRequested += QuickPanelAddProfileRequested;
        _quickPanel.CloseRequested += QuickPanelCloseRequested;
        _hotkeyHost = new GlobalHotkeyHost();
        _hotkeyHost.HotkeyPressed += (_, _) => OpenMainQuickPanel();

        _trayMenu = new AllyTrayContextMenu();
        ToolStripMenuItem title = new("AllyAutoTDP")
        {
            Enabled = false,
            Font = QuickPanelTypography.Interface(9F, FontStyle.Bold)
        };
        _autoTdpMenuItem = new ToolStripMenuItem("AutoTDP activé");
        _openQuickPanelItem = new ToolStripMenuItem("Ouvrir AllyAutoTDP");
        var addGameItem = new ToolStripMenuItem("Ajouter un jeu…");
        var profilesItem = new ToolStripMenuItem("Profils");
        var exitItem = new ToolStripMenuItem("Quitter");

        _openQuickPanelItem.Click += (_, _) => OpenMainQuickPanel();
        addGameItem.Click += (_, _) => BrowseForProfile();
        profilesItem.Click += (_, _) => OpenProfiles();
        _autoTdpMenuItem.Click += (_, _) => ToggleAutoTdp();
        exitItem.Click += (_, _) => ExitApplication();

        _trayMenu.Items.Add(title);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(_autoTdpMenuItem);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(_openQuickPanelItem);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(addGameItem);
        _trayMenu.Items.Add(profilesItem);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(exitItem);
        _trayMenu.Opening += (_, _) => RefreshTrayMenu();

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "AllyAutoTDP",
            ContextMenuStrip = _trayMenu
        };
        _notifyIcon.MouseUp += NotifyIconMouseUp;

        if (!_hotkeyHost.TryRegister(HotkeyDefinition.Default))
        {
            string registrationError = _hotkeyHost.RegistrationErrorMessage ??
                $"code={_hotkeyHost.RegistrationErrorCode?.ToString() ?? "inconnu"}";
            _controller.LogUiWarning(
                $"Raccourci Ctrl+Alt+T indisponible " +
                $"({registrationError}).");
        }

        _pollTimer = new System.Threading.Timer(
            PollTimerCallback,
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1));
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _refreshTimer.Tick += (_, _) =>
        {
            RefreshViews();
            RefreshTrayMenu();
        };
        _refreshTimer.Start();
        RefreshTrayMenu();

        if (IsBackgroundMode)
            _quickPanel.Hide();
    }

    public bool IsBackgroundMode { get; }

    public bool IsQuickPanelVisible => _quickPanel.Visible;

    public bool IsTrayActive => _notifyIcon.Visible;

    private void PollTimerCallback(object? state)
    {
        if (Interlocked.Exchange(ref _pollInProgress, 1) != 0 || _exiting)
            return;

        try
        {
            _controller.Poll();
            if (!_quickPanel.IsDisposed &&
                _quickPanel.IsHandleCreated &&
                _quickPanel.Visible)
            {
                _quickPanel.BeginInvoke(new Action(RefreshViews));
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            Volatile.Write(ref _pollInProgress, 0);
        }
    }

    private void NotifyIconMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            OpenMainQuickPanel();
    }

    private void ToggleQuickPanel()
    {
        if (_quickPanelCoordinator.IsVisible)
            _quickPanelCoordinator.Toggle();
        else
            _quickPanelCoordinator.ShowPanel();

        RefreshTrayMenu();
    }

    private void OpenMainQuickPanel()
    {
        if (!_quickPanelCoordinator.IsVisible)
            _quickPanel.ShowMainView(refresh: false);

        ToggleQuickPanel();
    }

    private void BrowseForProfile()
    {
        bool panelWasVisible = _quickPanelCoordinator.IsVisible;
        HideQuickPanelForDialog();
        GameProfile? profile = _profileWorkflow.BrowseForExecutable(owner: null);
        if (profile is not null)
        {
            _quickPanelCoordinator.ShowPanel();
            _quickPanel.ShowProfilesView();
        }
        else if (panelWasVisible)
        {
            _quickPanelCoordinator.ShowPanel();
        }
        RefreshViews();
        RefreshTrayMenu();
    }

    private void OpenProfiles()
    {
        _quickPanelCoordinator.ShowPanel();
        _quickPanel.ShowProfilesView();
        RefreshViews();
        RefreshTrayMenu();
    }

    private void HideQuickPanelForDialog()
    {
        if (_quickPanelCoordinator.IsVisible)
            _quickPanelCoordinator.HideWithoutFocusRestore();
    }

    private void QuickPanelAutoTdpStateChanged(object? sender, EventArgs e)
    {
        RefreshViews();
        RefreshTrayMenu();
    }

    private void QuickPanelAddProfileRequested(object? sender, EventArgs e) =>
        BrowseForProfile();

    private void QuickPanelCloseRequested(object? sender, EventArgs e) =>
        RefreshTrayMenu();

    private void RefreshViews()
    {
        if (!_quickPanel.IsDisposed && _quickPanel.Visible)
            _quickPanel.RefreshFromController();
    }

    private void ToggleAutoTdp()
    {
        AllyApplicationSnapshot snapshot = _controller.GetSnapshot();
        try
        {
            _controller.SetAutoTdpEnabled(!snapshot.AutoTdpEnabled);
            RefreshViews();
            RefreshTrayMenu();
        }
        catch
        {
            MessageBox.Show(
                "Le changement d'état AutoTDP a échoué.",
                "Erreur AutoTDP",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void RefreshTrayMenu()
    {
        if (_exiting)
            return;

        AllyApplicationSnapshot snapshot = _controller.GetSnapshot();
        _openQuickPanelItem.Text = _quickPanelCoordinator.IsVisible
            ? "Fermer AllyAutoTDP"
            : "Ouvrir AllyAutoTDP";
        _autoTdpMenuItem.Checked = snapshot.AutoTdpEnabled;
        _autoTdpMenuItem.Text = snapshot.AutoTdpEnabled
            ? "AutoTDP activé"
            : "AutoTDP désactivé";
    }

    private void ExitApplication()
    {
        if (_exiting)
            return;

        AutoTdpRuntimeResult result = _controller.Shutdown();
        if (!result.IsSuccess)
        {
            MessageBox.Show(
                "La restauration n'est pas terminée. L'application reste ouverte.",
                "Erreur AutoTDP",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        _exiting = true;
        _quickPanelCoordinator.Dispose();
        _hotkeyHost.Dispose();
        _pollTimer.Dispose();
        _refreshTimer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _trayMenu.Dispose();
        _quickPanel.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (!_exiting)
            {
                _quickPanelCoordinator.Dispose();
                _controller.Shutdown();
            }
            else
            {
                _quickPanelCoordinator.Dispose();
            }
            _hotkeyHost.Dispose();
            _refreshTimer.Dispose();
            _pollTimer.Dispose();
            _notifyIcon.Dispose();
            _trayMenu.Dispose();
            _quickPanel.Dispose();
        }

        base.Dispose(disposing);
    }
}
