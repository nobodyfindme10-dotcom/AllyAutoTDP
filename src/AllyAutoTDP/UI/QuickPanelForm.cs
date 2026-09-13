using System.Globalization;
using System.Runtime.InteropServices;
using AllyAutoTDP.Application;
using AllyAutoTDP.AutoTdp;
using AllyAutoTDP.Configuration;
using AllyAutoTDP.Power;
using AllyAutoTDP.Windows;

namespace AllyAutoTDP.UI;

public sealed class QuickPanelForm : Form, IQuickPanelWindow
{
    private enum QuickPanelView
    {
        Main,
        Profiles,
        EditProfile
    }

    private readonly AllyApplicationController _controller;
    private readonly Panel _viewHost;
    private readonly Panel _navigationBar;
    private readonly QuickPanelButton _controlNavigationButton;
    private readonly QuickPanelButton _profilesNavigationButton;
    private readonly Control _mainView;
    private readonly Control _profilesView;
    private readonly Control _editProfileView;

    private readonly Label _autoTdpStatusValue;
    private readonly QuickPanelToggle _autoTdpToggle;
    private readonly QuickPanelButton _hideButton;
    private readonly Label _applicationNameValue;
    private readonly Label _profileStateValue;
    private readonly Panel _profileValueHost;
    private readonly QuickPanelButton _createProfileButton;
    private readonly QuickPanelStepper _targetFpsStepper;
    private readonly Label _targetFpsValue;
    private readonly Label _fpsDisplayValue;
    private readonly Label _tdpValue;
    private readonly Label _sourceValue;
    private readonly Label _rangeValue;

    private readonly Panel _profilesRows;
    private readonly Label _profilesEmptyValue;
    private readonly QuickPanelStepper _editTargetFpsStepper;
    private readonly QuickPanelToggle _editProfileToggle;
    private readonly Label _editProfileNameValue;
    private readonly QuickPanelButton _deleteProfileButton;
    private readonly QuickPanelButton _saveProfileButton;

    private QuickPanelView _currentView;
    private GameProfile? _editingProfile;
    private GameProfile? _editingDraft;
    private nint _anchorWindow;
    private bool _refreshing;
    private bool _mainTargetFpsDirty;
    private bool _applyingBounds;

    public QuickPanelForm(AllyApplicationController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));

        Text = "AllyAutoTDP";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = QuickPanelTypography.Interface(9F);
        BackColor = QuickPanelPalette.Panel;
        ForeColor = QuickPanelPalette.Foreground;
        AutoSize = false;
        MinimumSize = new Size(1, 1);

        _autoTdpStatusValue = CreateMutedLabel("Désactivé");
        _autoTdpToggle = new QuickPanelToggle("AutoTDP");
        _hideButton = new QuickPanelButton
        {
            Text = string.Empty,
            IconKind = QuickPanelIconKind.ChevronRight,
            IconColor = QuickPanelPalette.Muted,
            CenterIconOnly = true,
            Borderless = true,
            BackColor = QuickPanelPalette.Surface,
            BorderColor = Color.Transparent,
            Width = QuickPanelMetrics.Scale(48, 96),
            AccessibleName = "Masquer le QuickPanel"
        };

        _applicationNameValue = CreateValueLabel();
        _profileStateValue = CreateValueLabel();
        _profileValueHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = QuickPanelPalette.Surface,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _createProfileButton = new QuickPanelButton
        {
            Text = "Créer le profil",
            IconKind = QuickPanelIconKind.Plus,
            IconColor = QuickPanelPalette.Amber,
            Borderless = true,
            BackColor = QuickPanelPalette.Surface,
            BorderColor = Color.Transparent,
            AccentColor = QuickPanelPalette.Amber,
            Dock = DockStyle.Fill,
            Visible = false,
            AccessibleName = "Créer le profil du jeu actuel"
        };
        _profileStateValue.Dock = DockStyle.Fill;
        _profileValueHost.Controls.Add(_profileStateValue);
        _profileValueHost.Controls.Add(_createProfileButton);

        _targetFpsStepper = new QuickPanelStepper { Dock = DockStyle.Fill };
        _targetFpsValue = CreateMetricLabel("60 FPS", QuickPanelPalette.Amber);
        _fpsDisplayValue = CreateMetricLabel("--", QuickPanelPalette.Cyan);
        _tdpValue = CreateMetricLabel("--", QuickPanelPalette.Cyan);
        _sourceValue = CreateValueLabel("Indisponible");
        _rangeValue = CreateValueLabel("Indisponible");

        _profilesRows = new Panel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            BackColor = QuickPanelPalette.Surface,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _profilesEmptyValue = CreateMutedLabel("Aucun profil enregistré");
        _profilesEmptyValue.Dock = DockStyle.Top;
        _profilesEmptyValue.Height = QuickPanelMetrics.Scale(72, 96);
        _profilesEmptyValue.TextAlign = ContentAlignment.MiddleCenter;

        _editProfileNameValue = CreateValueLabel();
        _editTargetFpsStepper = new QuickPanelStepper { Dock = DockStyle.Fill };
        _editProfileToggle = new QuickPanelToggle("Profil activé")
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _deleteProfileButton = new QuickPanelButton
        {
            Text = "Supprimer",
            IconKind = QuickPanelIconKind.Close,
            IconColor = QuickPanelPalette.Danger,
            Danger = true,
            BackColor = QuickPanelPalette.Surface,
            BorderColor = QuickPanelPalette.Danger
        };
        _saveProfileButton = new QuickPanelButton
        {
            Text = "Enregistrer",
            IconKind = QuickPanelIconKind.Power,
            IconColor = QuickPanelPalette.Amber,
            Emphasized = true,
            AccentColor = QuickPanelPalette.Amber,
            BackColor = QuickPanelPalette.Surface,
            BorderColor = QuickPanelPalette.Amber
        };

        _mainView = CreateMainView();
        _profilesView = CreateProfilesView();
        _editProfileView = CreateEditProfileView();
        _viewHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = QuickPanelPalette.Panel,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _navigationBar = CreateNavigationBar(
            out _controlNavigationButton,
            out _profilesNavigationButton);
        Controls.Add(_navigationBar);
        Controls.Add(_viewHost);

        _autoTdpToggle.CheckedChanged += AutoTdpToggleChanged;
        _createProfileButton.Click += (_, _) => CreateCurrentProfile();
        _hideButton.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        _targetFpsStepper.ValueChanged += TargetFpsValueChanged;
        _editTargetFpsStepper.ValueChanged += EditTargetFpsChanged;
        _editProfileToggle.CheckedChanged += EditProfileToggleChanged;
        _deleteProfileButton.Click += (_, _) => DeleteEditingProfile();
        _saveProfileButton.Click += (_, _) => SaveEditingProfile();
        _controlNavigationButton.Click += (_, _) => ShowMainView();
        _profilesNavigationButton.Click += (_, _) => ShowProfilesView();

        ShowView(QuickPanelView.Main);
    }

    public event EventHandler? Deactivated;

    public event EventHandler? CloseRequested;

    public event EventHandler? AutoTdpStateChanged;

    public event EventHandler? AddProfileRequested;

    public bool IsTopMost => TopMost;

    public void ShowPanel(nint anchorWindow)
    {
        _anchorWindow = anchorWindow;
        WindowState = FormWindowState.Normal;
        TopMost = true;
        Show();
        RefreshFromController();
        ApplyWorkingAreaBounds();
        Activate();
        BringToFront();
        ApplyWorkingAreaBounds();
    }

    public void HidePanel()
    {
        TopMost = false;
        Hide();
    }

    public void Post(Action action)
    {
        if (IsDisposed)
            return;

        if (IsHandleCreated)
            BeginInvoke(action);
        else
            action();
    }

    public void ShowMainView(bool refresh = true)
    {
        _editingProfile = null;
        _editingDraft = null;
        ShowView(QuickPanelView.Main);
        if (refresh)
            RefreshFromController();
    }

    public void ShowProfilesView()
    {
        _editingProfile = null;
        _editingDraft = null;
        ShowView(QuickPanelView.Profiles);
        RefreshProfilesView();
    }

    public void ShowProfileEditor(GameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        _editingProfile = profile;
        _editingDraft = CloneProfile(profile);
        _refreshing = true;
        try
        {
            _editProfileNameValue.Text = profile.DisplayName;
            _editTargetFpsStepper.Value = profile.TargetFps;
            _editProfileToggle.Checked = profile.Enabled;
        }
        finally
        {
            _refreshing = false;
        }

        ShowView(QuickPanelView.EditProfile);
    }

    public void RefreshFromController()
    {
        if (IsDisposed)
            return;

        AllyApplicationSnapshot snapshot = _controller.GetSnapshot();
        _refreshing = true;
        try
        {
            _autoTdpToggle.Checked = snapshot.AutoTdpEnabled;
            RefreshMainView(snapshot);
        }
        finally
        {
            _refreshing = false;
        }

        if (_currentView == QuickPanelView.Profiles)
            RefreshProfilesView(snapshot);
        else if (_currentView == QuickPanelView.EditProfile)
            RefreshEditingProfile(snapshot);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Deactivated?.Invoke(this, e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplyWorkingAreaBounds();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Visible && !_applyingBounds)
            ApplyWorkingAreaBounds();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (Visible)
            ApplyWorkingAreaBounds(e.DeviceDpiNew);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnFormClosing(e);
    }

    private Control CreateMainView()
    {
        var root = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = QuickPanelPalette.Panel,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        Panel body = CreateScrollableBody();
        TableLayoutPanel stack = CreateStack(body);
        stack.Controls.Add(CreateGameModule(), 0, 0);
        stack.Controls.Add(CreateFpsModule(), 0, 1);
        stack.Controls.Add(CreateTdpModule(), 0, 2);
        body.Controls.Add(stack);
        Panel header = CreateMainHeader();
        root.Controls.Add(body);
        root.Controls.Add(header);
        return root;
    }

    private Panel CreateMainHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = QuickPanelMetrics.Scale(60, 96),
            BackColor = QuickPanelPalette.Surface,
            Padding = new Padding(
                QuickPanelMetrics.Scale(16, 96),
                QuickPanelMetrics.Scale(6, 96),
                QuickPanelMetrics.Scale(8, 96),
                QuickPanelMetrics.Scale(6, 96)),
            Margin = new Padding(0)
        };
        panel.Paint += PaintSurface;

        var titleStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = QuickPanelPalette.Surface,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        titleStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        titleStack.RowStyles.Add(new RowStyle(
            SizeType.Absolute,
            QuickPanelMetrics.Scale(2, 96)));
        titleStack.RowStyles.Add(new RowStyle(
            SizeType.Absolute,
            QuickPanelMetrics.Scale(18, 96)));
        var title = new Label
        {
            Text = "AllyAutoTDP",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = QuickPanelTypography.Interface(12F, FontStyle.Bold),
            ForeColor = QuickPanelPalette.Foreground,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        };
        _autoTdpStatusValue.Dock = DockStyle.Fill;
        titleStack.Controls.Add(title, 0, 0);
        titleStack.Controls.Add(_autoTdpStatusValue, 0, 2);

        var headerActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = QuickPanelMetrics.Scale(114, 96),
            Height = QuickPanelMetrics.Scale(44, 96),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = QuickPanelPalette.Surface,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _autoTdpToggle.Dock = DockStyle.None;
        _autoTdpToggle.Margin = new Padding(0, QuickPanelMetrics.Scale(7, 96), QuickPanelMetrics.Scale(8, 96), QuickPanelMetrics.Scale(7, 96));
        _hideButton.Dock = DockStyle.None;
        _hideButton.Margin = new Padding(0);
        _hideButton.Height = QuickPanelMetrics.Scale(44, 96);
        headerActions.Controls.Add(_autoTdpToggle);
        headerActions.Controls.Add(_hideButton);

        panel.Controls.Add(titleStack);
        panel.Controls.Add(headerActions);
        return panel;
    }

    private Control CreateGameModule()
    {
        var module = CreateModule(108);
        module.ShowAccent = true;
        module.AccentColor = QuickPanelPalette.Amber;
        module.Padding = new Padding(
            QuickPanelMetrics.Scale(16, 96),
            QuickPanelMetrics.Scale(12, 96),
            QuickPanelMetrics.Scale(12, 96),
            QuickPanelMetrics.Scale(12, 96));

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = QuickPanelPalette.Surface,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 24));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        var heading = CreateSectionLabel("JEU ACTUEL");
        _applicationNameValue.Dock = DockStyle.Fill;
        _applicationNameValue.Font = QuickPanelTypography.Interface(11F, FontStyle.Bold);
        table.Controls.Add(heading, 0, 0);
        table.Controls.Add(_applicationNameValue, 0, 1);
        table.Controls.Add(_profileValueHost, 0, 2);
        module.Controls.Add(table);
        return module;
    }

    private Control CreateFpsModule()
    {
        var module = CreateModule(196);
        module.Padding = new Padding(
            QuickPanelMetrics.Scale(16, 96),
            QuickPanelMetrics.Scale(12, 96),
            QuickPanelMetrics.Scale(16, 96),
            QuickPanelMetrics.Scale(12, 96));
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            BackColor = QuickPanelPalette.Surface,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, QuickPanelMetrics.Scale(24, 96)));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, QuickPanelMetrics.Scale(36, 96)));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(CreateSectionLabel("FPS CIBLE"), 0, 0);
        content.Controls.Add(CreateSectionLabel("FPS RÉEL"), 1, 0);
        _targetFpsValue.Dock = DockStyle.Fill;
        _fpsDisplayValue.Dock = DockStyle.Fill;
        content.Controls.Add(_targetFpsValue, 0, 1);
        content.Controls.Add(_fpsDisplayValue, 1, 1);
        content.Controls.Add(_targetFpsStepper, 0, 2);
        content.SetColumnSpan(_targetFpsStepper, 2);
        module.Controls.Add(content);
        return module;
    }

    private Control CreateTdpModule()
    {
        var module = CreateModule(154);
        module.Padding = new Padding(
            QuickPanelMetrics.Scale(16, 96),
            QuickPanelMetrics.Scale(12, 96),
            QuickPanelMetrics.Scale(16, 96),
            QuickPanelMetrics.Scale(12, 96));
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = QuickPanelPalette.Surface,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, QuickPanelMetrics.Scale(24, 96)));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, QuickPanelMetrics.Scale(48, 96)));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(CreateSectionLabel("PUISSANCE ACTUELLE"), 0, 0);
        _tdpValue.Dock = DockStyle.Fill;
        content.Controls.Add(_tdpValue, 0, 1);
        var details = CreateTwoColumnTable();
        AddKeyValueRow(details, 0, "Source", _sourceValue);
        AddKeyValueRow(details, 1, "Plage", _rangeValue);
        content.Controls.Add(details, 0, 2);
        module.Controls.Add(content);
        return module;
    }

    private Control CreateProfilesView()
    {
        var root = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = QuickPanelPalette.Panel,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        Panel body = CreateScrollableBody();
        body.Padding = new Padding(
            QuickPanelMetrics.Scale(16, 96),
            QuickPanelMetrics.Scale(24, 96),
            QuickPanelMetrics.Scale(16, 96),
            QuickPanelMetrics.Scale(16, 96));
        body.Controls.Add(_profilesRows);
        body.Controls.Add(_profilesEmptyValue);
        Panel header = CreateHeader(
            "Profils",
            null,
            null,
            "Ajouter",
            (_, _) => AddProfileRequested?.Invoke(this, EventArgs.Empty));
        root.Controls.Add(body);
        root.Controls.Add(header);
        return root;
    }

    private Control CreateEditProfileView()
    {
        var root = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = QuickPanelPalette.Panel,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        Panel body = CreateScrollableBody();
        body.Padding = new Padding(
            QuickPanelMetrics.Scale(16, 96),
            QuickPanelMetrics.Scale(24, 96),
            QuickPanelMetrics.Scale(16, 96),
            QuickPanelMetrics.Scale(16, 96));
        QuickPanelSurface card = CreateModule(240);
        card.Padding = new Padding(
            QuickPanelMetrics.Scale(16, 96),
            QuickPanelMetrics.Scale(16, 96),
            QuickPanelMetrics.Scale(16, 96),
            QuickPanelMetrics.Scale(16, 96));
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = QuickPanelPalette.Surface,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, QuickPanelMetrics.Scale(36, 96)));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, QuickPanelMetrics.Scale(24, 96)));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, QuickPanelMetrics.Scale(72, 96)));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, QuickPanelMetrics.Scale(48, 96)));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _editProfileNameValue.Dock = DockStyle.Fill;
        _editProfileNameValue.Font = QuickPanelTypography.Interface(12F, FontStyle.Bold);
        content.Controls.Add(_editProfileNameValue, 0, 0);
        content.Controls.Add(CreateSectionLabel("FPS CIBLE"), 0, 1);
        content.Controls.Add(_editTargetFpsStepper, 0, 2);
        var profileRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = QuickPanelPalette.Surface,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        profileRow.Padding = new Padding(0, 0, QuickPanelMetrics.Scale(2, 96), 0);
        profileRow.Controls.Add(CreateSectionLabel("PROFIL"), 0, 0);
        profileRow.Controls.Add(_editProfileToggle, 1, 0);
        content.Controls.Add(profileRow, 0, 3);
        card.Controls.Add(content);
        body.Controls.Add(card);

        Panel footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = QuickPanelMetrics.Scale(80, 96),
            BackColor = QuickPanelPalette.Panel,
            Padding = new Padding(
                QuickPanelMetrics.Scale(16, 96),
                QuickPanelMetrics.Scale(16, 96),
                QuickPanelMetrics.Scale(16, 96),
                QuickPanelMetrics.Scale(16, 96)),
            Margin = new Padding(0)
        };
        _deleteProfileButton.Height = QuickPanelMetrics.Scale(48, 96);
        _saveProfileButton.Height = QuickPanelMetrics.Scale(48, 96);
        _deleteProfileButton.IconKind = QuickPanelIconKind.None;
        _saveProfileButton.IconKind = QuickPanelIconKind.None;
        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = QuickPanelPalette.Panel,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _deleteProfileButton.Dock = DockStyle.Fill;
        _deleteProfileButton.Margin = new Padding(0, 0, QuickPanelMetrics.Scale(4, 96), 0);
        _saveProfileButton.Dock = DockStyle.Fill;
        _saveProfileButton.Margin = new Padding(QuickPanelMetrics.Scale(4, 96), 0, 0, 0);
        actions.Controls.Add(_deleteProfileButton, 0, 0);
        actions.Controls.Add(_saveProfileButton, 1, 0);
        footer.Controls.Add(actions);
        Panel header = CreateHeader("Modifier le profil", "Retour", ShowProfilesView);
        root.Controls.Add(body);
        root.Controls.Add(footer);
        root.Controls.Add(header);
        return root;
    }

    private Panel CreateNavigationBar(
        out QuickPanelButton controlButton,
        out QuickPanelButton profilesButton)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = QuickPanelMetrics.Scale(58, 96),
            BackColor = QuickPanelPalette.Panel,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = QuickPanelPalette.Panel,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        controlButton = CreateNavigationButton("Contrôle", QuickPanelIconKind.Gauge);
        profilesButton = CreateNavigationButton("Profils", QuickPanelIconKind.Profiles);
        table.Controls.Add(controlButton, 0, 0);
        table.Controls.Add(profilesButton, 1, 0);
        panel.Controls.Add(table);
        return panel;
    }

    private static QuickPanelButton CreateNavigationButton(
        string text,
        QuickPanelIconKind iconKind) => new()
    {
        Text = text,
        IconKind = iconKind,
        CenterContent = true,
        Dock = DockStyle.Fill,
        Borderless = true,
        VerticalContent = false,
        BackColor = QuickPanelPalette.Panel,
        BorderColor = Color.Transparent,
        IconColor = QuickPanelPalette.Muted,
        ForeColor = QuickPanelPalette.Muted,
        Margin = new Padding(0),
        TabStop = true
    };

    private static QuickPanelSurface CreateModule(int logicalHeight) => new()
    {
        Height = QuickPanelMetrics.Scale(logicalHeight, 96),
        Dock = DockStyle.Top,
        BackColor = QuickPanelPalette.Surface,
        Margin = new Padding(0, 0, 0, QuickPanelMetrics.Scale(16, 96)),
        Padding = new Padding(0)
    };

    private static Panel CreateScrollableBody() => new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        BackColor = QuickPanelPalette.Panel,
        Margin = new Padding(0),
        Padding = new Padding(QuickPanelMetrics.Scale(16, 96))
    };

    private static TableLayoutPanel CreateStack(Control body)
    {
        var stack = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            Dock = DockStyle.Top,
            Width = Math.Max(1, body.ClientSize.Width),
            BackColor = QuickPanelPalette.Panel,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        body.Resize += (_, _) => stack.Width = Math.Max(1, body.ClientSize.Width);
        return stack;
    }

    private static Panel CreateHeader(
        string titleText,
        string? backText,
        Action? backAction,
        string? actionText = null,
        EventHandler? action = null)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = QuickPanelMetrics.Scale(60, 96),
            BackColor = QuickPanelPalette.Surface,
            Padding = new Padding(
                QuickPanelMetrics.Scale(8, 96),
                QuickPanelMetrics.Scale(6, 96),
                QuickPanelMetrics.Scale(8, 96),
                QuickPanelMetrics.Scale(6, 96)),
            Margin = new Padding(0)
        };
        panel.Paint += PaintSurface;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            BackColor = QuickPanelPalette.Surface,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.ColumnCount = 1 +
            (backText is not null && backAction is not null ? 1 : 0) +
            (actionText is not null && action is not null ? 1 : 0);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        int titleColumn = 0;
        if (backText is not null && backAction is not null)
        {
            layout.ColumnStyles.Add(new ColumnStyle(
                SizeType.Absolute,
                QuickPanelMetrics.Scale(104, 96)));
            var backButton = new QuickPanelButton
            {
                Text = backText,
                IconKind = QuickPanelIconKind.Back,
                IconColor = QuickPanelPalette.Foreground,
                CenterContent = true,
                Borderless = true,
                BackColor = QuickPanelPalette.Surface,
                BorderColor = Color.Transparent,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                AccessibleName = backText
            };
            backButton.Click += (_, _) => backAction();
            layout.Controls.Add(backButton, 0, 0);
            titleColumn = 1;
        }

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bool rightAlignTitle = backText is not null &&
            backAction is not null &&
            actionText is null &&
            action is null;
        var title = new Label
        {
            Text = titleText,
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = QuickPanelTypography.Interface(12F, FontStyle.Bold),
            ForeColor = QuickPanelPalette.Foreground,
            TextAlign = rightAlignTitle
                ? ContentAlignment.MiddleRight
                : ContentAlignment.MiddleLeft,
            Padding = rightAlignTitle
                ? new Padding(0, 0, QuickPanelMetrics.Scale(8, 96), 0)
                : new Padding(QuickPanelMetrics.Scale(8, 96), 0, 0, 0),
            Margin = new Padding(0)
        };
        layout.Controls.Add(title, titleColumn, 0);

        if (actionText is not null && action is not null)
        {
            int actionColumn = titleColumn + 1;
            layout.ColumnStyles.Add(new ColumnStyle(
                SizeType.Absolute,
                QuickPanelMetrics.Scale(120, 96)));
            var actionButton = new QuickPanelButton
            {
                Text = actionText,
                IconKind = QuickPanelIconKind.Plus,
                IconColor = QuickPanelPalette.Amber,
                CenterContent = true,
                Borderless = true,
                BackColor = QuickPanelPalette.Surface,
                BorderColor = Color.Transparent,
                ForeColor = QuickPanelPalette.Foreground,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                AccessibleName = actionText
            };
            actionButton.Click += action;
            layout.Controls.Add(actionButton, actionColumn, 0);
        }

        panel.Controls.Add(layout);
        return panel;
    }

    private static Label CreateSectionLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        AutoSize = false,
        Font = QuickPanelTypography.Interface(8F, FontStyle.Bold),
        ForeColor = QuickPanelPalette.Muted,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0),
        Padding = new Padding(0)
    };

    private static Label CreateMutedLabel(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Font = QuickPanelTypography.Interface(8F),
        ForeColor = QuickPanelPalette.Muted,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0),
        Padding = new Padding(0)
    };

    private static Label CreateValueLabel(string text = "--") => new()
    {
        Text = text,
        AutoSize = false,
        Font = QuickPanelTypography.Interface(9F),
        ForeColor = QuickPanelPalette.Foreground,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0),
        Padding = new Padding(0)
    };

    private static Label CreateMetricLabel(string text, Color color) => new()
    {
        Text = text,
        AutoSize = false,
        Font = QuickPanelTypography.Metrics(18F, FontStyle.Bold),
        ForeColor = color,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0),
        Padding = new Padding(0)
    };

    private static TableLayoutPanel CreateTwoColumnTable() => new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 2,
        RowCount = 2,
        BackColor = QuickPanelPalette.Surface,
        Margin = new Padding(0),
        Padding = new Padding(0)
    };

    private static void AddKeyValueRow(
        TableLayoutPanel table,
        int row,
        string key,
        Control value)
    {
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        table.Controls.Add(CreateMutedLabel(key), 0, row);
        value.Dock = DockStyle.Fill;
        if (value is Label label)
            label.TextAlign = ContentAlignment.MiddleRight;
        table.Controls.Add(value, 1, row);
    }

    private static void PaintSurface(object? sender, PaintEventArgs e)
    {
        if (sender is not Control control || control.Width < 2 || control.Height < 2)
            return;

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(QuickPanelPalette.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, control.Width - 1, control.Height - 1);
    }

    private void ShowView(QuickPanelView view)
    {
        _currentView = view;
        _viewHost.SuspendLayout();
        try
        {
            _viewHost.Controls.Clear();
            Control selected = view switch
            {
                QuickPanelView.Main => _mainView,
                QuickPanelView.Profiles => _profilesView,
                QuickPanelView.EditProfile => _editProfileView,
                _ => _mainView
            };
            selected.Dock = DockStyle.Fill;
            _viewHost.Controls.Add(selected);
            bool showNavigation = view != QuickPanelView.EditProfile;
            _navigationBar.Visible = showNavigation;
            UpdateNavigationButtons(view);
        }
        finally
        {
            _viewHost.ResumeLayout(true);
        }
    }

    private void UpdateNavigationButtons(QuickPanelView view)
    {
        bool controlActive = view == QuickPanelView.Main;
        _controlNavigationButton.Active = controlActive;
        _controlNavigationButton.BackColor = controlActive
            ? QuickPanelPalette.Active
            : QuickPanelPalette.Panel;
        _controlNavigationButton.ForeColor = controlActive
            ? QuickPanelPalette.Foreground
            : QuickPanelPalette.Muted;
        _controlNavigationButton.IconColor = controlActive
            ? QuickPanelPalette.Cyan
            : QuickPanelPalette.Muted;
        _profilesNavigationButton.Active = view == QuickPanelView.Profiles;
        _profilesNavigationButton.BackColor = view == QuickPanelView.Profiles
            ? QuickPanelPalette.Active
            : QuickPanelPalette.Panel;
        _profilesNavigationButton.ForeColor = view == QuickPanelView.Profiles
            ? QuickPanelPalette.Foreground
            : QuickPanelPalette.Muted;
        _profilesNavigationButton.IconColor = view == QuickPanelView.Profiles
            ? QuickPanelPalette.Cyan
            : QuickPanelPalette.Muted;
        _controlNavigationButton.Invalidate();
        _profilesNavigationButton.Invalidate();
    }

    private void RefreshMainView(AllyApplicationSnapshot snapshot)
    {
        GameProfile? activeProfile = snapshot.ActiveSession?.Profile;
        ForegroundApplicationSnapshot? application = activeProfile is null
            ? GetCurrentApplication(snapshot)
            : null;
        GameProfile? profile = activeProfile ?? FindProfile(snapshot, application);
        bool relevant = activeProfile is not null || application is not null;

        if (!relevant)
        {
            _applicationNameValue.Text = "Aucun jeu détecté";
            _profileStateValue.Text = string.Empty;
            _profileValueHost.Visible = false;
            _targetFpsStepper.Enabled = false;
            _targetFpsValue.Text = "--";
        }
        else
        {
            _profileValueHost.Visible = true;
            _applicationNameValue.Text = activeProfile?.DisplayName ??
                application!.DisplayName;
            if (profile is null)
            {
                _profileStateValue.Visible = false;
                _createProfileButton.Visible = true;
            }
            else
            {
                _profileStateValue.Visible = true;
                _profileStateValue.Text = profile.Enabled
                    ? "Profil Activé"
                    : "Profil Désactivé";
                _createProfileButton.Visible = false;
            }

            _targetFpsStepper.Enabled = true;
            if (!_mainTargetFpsDirty)
                _targetFpsStepper.Value = profile?.TargetFps ?? snapshot.ProfileDefaults.TargetFps;
            _targetFpsValue.Text = $"{_targetFpsStepper.Value} FPS";
        }

        RefreshMetrics(snapshot);
        _autoTdpStatusValue.Text = GetAutoTdpStatus(snapshot);
    }

    private void RefreshProfilesView()
    {
        RefreshProfilesView(_controller.GetSnapshot());
    }

    private void RefreshProfilesView(AllyApplicationSnapshot snapshot)
    {
        ForegroundApplicationSnapshot? currentApplication = GetCurrentApplication(snapshot);
        string? currentPath = snapshot.ActiveSession?.Profile.ExecutablePath ??
            currentApplication?.ExecutablePath;
        _profilesRows.SuspendLayout();
        try
        {
            foreach (Control control in _profilesRows.Controls.Cast<Control>().ToArray())
            {
                _profilesRows.Controls.Remove(control);
                control.Dispose();
            }

            foreach (GameProfile profile in snapshot.GameProfiles.Reverse())
            {
                bool isCurrent = currentPath is not null && string.Equals(
                    currentPath,
                    profile.ExecutablePath,
                    StringComparison.OrdinalIgnoreCase);
                var row = new QuickPanelProfileRow(
                    profile.DisplayName,
                    profile.ExecutablePath,
                    isCurrent);
                row.Click += ProfileRowClicked;
                _profilesRows.Controls.Add(row);
            }

            _profilesEmptyValue.Visible = snapshot.GameProfiles.Count == 0;
        }
        finally
        {
            _profilesRows.ResumeLayout(true);
        }
    }

    private void ProfileRowClicked(object? sender, EventArgs e)
    {
        if (sender is not QuickPanelProfileRow row)
            return;

        GameProfile? profile = FindProfileByPath(
            _controller.GetSnapshot().GameProfiles,
            row.ExecutablePath);
        if (profile is not null)
            ShowProfileEditor(profile);
    }

    private void RefreshEditingProfile(AllyApplicationSnapshot snapshot)
    {
        if (_editingProfile is null)
            return;

        GameProfile? current = FindProfileByPath(
            snapshot.GameProfiles,
            _editingProfile.ExecutablePath);
        if (current is null)
            ShowProfilesView();
    }

    private void AutoTdpToggleChanged(object? sender, EventArgs e)
    {
        if (_refreshing)
            return;

        RunAction(
            () => _controller.SetAutoTdpEnabled(_autoTdpToggle.Checked),
            () =>
            {
                AutoTdpStateChanged?.Invoke(this, EventArgs.Empty);
                RefreshFromController();
            },
            "Le changement d'état AutoTDP a échoué.");
    }

    private void CreateCurrentProfile()
    {
        if (_refreshing)
            return;

        AllyApplicationSnapshot snapshot = _controller.GetSnapshot();
        ForegroundApplicationSnapshot? application = GetCurrentApplication(snapshot);
        if (application is null ||
            FindProfile(snapshot, application) is not null)
        {
            RefreshMainView(snapshot);
            return;
        }

        int targetFps = _targetFpsStepper.Value;
        RunAction(
            () => _controller.CreateProfileForCurrentApplication(targetFps),
            () =>
            {
                _mainTargetFpsDirty = false;
                RefreshFromController();
            },
            "La création du profil a échoué.");
    }

    private void TargetFpsValueChanged(object? sender, EventArgs e)
    {
        if (_refreshing || !_targetFpsStepper.Enabled)
            return;

        int targetFps = _targetFpsStepper.Value;
        _targetFpsValue.Text = $"{targetFps} FPS";
        _mainTargetFpsDirty = true;
        RunAction(
            () => PersistTargetFps(targetFps),
            () => _mainTargetFpsDirty = false,
            "La cible FPS n'a pas pu être enregistrée.");
    }

    private void PersistTargetFps(int targetFps)
    {
        AllyApplicationSnapshot snapshot = _controller.GetSnapshot();
        ForegroundApplicationSnapshot? application = GetCurrentApplication(snapshot);
        GameProfile? profile = snapshot.ActiveSession?.Profile ??
            FindProfile(snapshot, application);
        if (profile is null)
        {
            _controller.UpdateProfileDefaults(new ProfileDefaults
            {
                TargetFps = targetFps,
                RestoreMode = snapshot.ProfileDefaults.RestoreMode
            });
            return;
        }

        if (profile.TargetFps == targetFps)
            return;

        GameProfile updated = CloneProfile(profile);
        updated.TargetFps = targetFps;
        _controller.UpdateProfile(profile.ExecutablePath, updated);
    }

    private void EditTargetFpsChanged(object? sender, EventArgs e)
    {
        if (!_refreshing && _editingDraft is not null)
            _editingDraft.TargetFps = _editTargetFpsStepper.Value;
    }

    private void EditProfileToggleChanged(object? sender, EventArgs e)
    {
        if (!_refreshing && _editingDraft is not null)
            _editingDraft.Enabled = _editProfileToggle.Checked;
    }

    private void SaveEditingProfile()
    {
        if (_editingProfile is null || _editingDraft is null)
            return;

        GameProfile original = _editingProfile;
        GameProfile updated = CloneProfile(_editingDraft);
        RunAction(
            () => _controller.UpdateProfile(original.ExecutablePath, updated),
            ShowProfilesView,
            "Le profil n'a pas pu être enregistré.");
    }

    private void DeleteEditingProfile()
    {
        if (_editingProfile is null)
            return;

        DialogResult answer = MessageBox.Show(
            $"Supprimer le profil « {_editingProfile.DisplayName} » ?",
            "Supprimer un profil",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
            return;

        string path = _editingProfile.ExecutablePath;
        RunAction(
            () => _controller.RemoveProfile(path),
            ShowProfilesView,
            "Le profil n'a pas pu être supprimé.");
    }

    private void RefreshMetrics(AllyApplicationSnapshot snapshot)
    {
        if (snapshot.Runtime.Status is not AutoTdpStatus status ||
            !snapshot.Runtime.IsRunning)
        {
            _fpsDisplayValue.Text = "--";
            _tdpValue.Text = "--";
            _sourceValue.Text = "Indisponible";
            _rangeValue.Text = "Indisponible";
            return;
        }

        _fpsDisplayValue.Text = status.Fps.IsValid && status.Fps.Value.HasValue
            ? status.Fps.Value.Value.ToString("0.0", CultureInfo.CurrentCulture)
            : "--";
        _tdpValue.Text = status.CurrentTdp > 0
            ? $"{status.CurrentTdp} W"
            : "--";
        _sourceValue.Text = status.PowerSource.IsKnown
            ? UiStateMapper.MapPowerSource(status.PowerSource.Source)
            : "Indisponible";
        _rangeValue.Text = status.PowerSource.IsKnown
            ? FormatRange(snapshot.PowerLimits.GetEffectiveRange(status.PowerSource.Source))
            : "Indisponible";
    }

    private static string GetAutoTdpStatus(AllyApplicationSnapshot snapshot)
    {
        if (!snapshot.AutoTdpEnabled)
            return "Désactivé";
        if (snapshot.Runtime.Status is AutoTdpStatus status)
            return UiStateMapper.MapAutoTdpState(status.State);
        return UiStateMapper.MapSessionState(snapshot.SessionState);
    }

    private static string FormatRange(TdpRange range) =>
        $"{range.MinTdp}–{range.MaxTdp} W";

    private static GameProfile CloneProfile(GameProfile profile) => new()
    {
        ExecutablePath = profile.ExecutablePath,
        DisplayName = profile.DisplayName,
        Enabled = profile.Enabled,
        TargetFps = profile.TargetFps,
        RestoreMode = profile.RestoreMode
    };

    private static ForegroundApplicationSnapshot? GetCurrentApplication(
        AllyApplicationSnapshot snapshot)
    {
        ForegroundApplicationSnapshot? invoking =
            snapshot.InvokingApplication?.Application;
        if (invoking is not null && FindProfile(snapshot, invoking) is not null)
            return invoking;

        ForegroundApplicationSnapshot? detected = snapshot.DetectedApplication;
        if (detected is not null && FindProfile(snapshot, detected) is not null)
            return detected;

        return snapshot.CandidateApplication;
    }

    private static GameProfile? FindProfile(
        AllyApplicationSnapshot snapshot,
        ForegroundApplicationSnapshot? application) =>
        application is null
            ? null
            : FindProfileByPath(snapshot.GameProfiles, application.ExecutablePath);

    private static GameProfile? FindProfileByPath(
        IEnumerable<GameProfile> profiles,
        string executablePath) => profiles.FirstOrDefault(profile =>
        string.Equals(
            profile.ExecutablePath,
            executablePath,
            StringComparison.OrdinalIgnoreCase));

    private void RunAction(
        Action action,
        Action onSuccess,
        string errorMessage)
    {
        try
        {
            action();
            onSuccess();
        }
        catch (Exception exception)
        {
            _controller.LogUiWarning($"{errorMessage} {exception.Message}");
            MessageBox.Show(
                errorMessage,
                "AllyAutoTDP",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            RefreshFromController();
        }
    }

    private void ApplyWorkingAreaBounds(int? dpiOverride = null)
    {
        if (_applyingBounds || IsDisposed)
            return;

        _applyingBounds = true;
        try
        {
            Screen targetScreen = SelectTargetScreen();
            int dpi = dpiOverride.GetValueOrDefault(DeviceDpi);
            if (dpi <= 0)
                dpi = 96;
            Rectangle bounds = QuickPanelLayout.Calculate(targetScreen.WorkingArea, dpi);
            Rectangle finalBounds = new(
                bounds.Right - bounds.Width,
                bounds.Top,
                bounds.Width,
                bounds.Height);
            Bounds = finalBounds;
        }
        finally
        {
            _applyingBounds = false;
        }
    }

    private Screen SelectTargetScreen()
    {
        if (_anchorWindow != nint.Zero)
        {
            try
            {
                return Screen.FromHandle(_anchorWindow);
            }
            catch (ArgumentException)
            {
            }
        }

        if (IsHandleCreated)
        {
            try
            {
                return Screen.FromHandle(Handle);
            }
            catch (ArgumentException)
            {
            }
        }

        return Screen.PrimaryScreen ?? Screen.AllScreens.First();
    }

    [DllImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    private static extern uint GetDpiForWindow(nint hWnd);
}
