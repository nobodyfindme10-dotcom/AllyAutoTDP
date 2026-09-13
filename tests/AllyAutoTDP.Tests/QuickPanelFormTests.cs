using System.Globalization;
using System.Reflection;
using System.Windows.Forms;
using AllyAutoTDP.Application;
using AllyAutoTDP.AutoTdp;
using AllyAutoTDP.Configuration;
using AllyAutoTDP.Hardware;
using AllyAutoTDP.Hardware.AMD;
using AllyAutoTDP.Logging;
using AllyAutoTDP.Power;
using AllyAutoTDP.UI;
using AllyAutoTDP.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AllyAutoTDP.Tests;

[TestClass]
public sealed class QuickPanelFormTests
{
    [TestMethod]
    public void TargetFpsStepper_UsesOnlyTheSixAllowedValues()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            using TestFixture fixture = new(temp.Path, null);
            using var form = new QuickPanelForm(fixture.Controller);

            CollectionAssert.AreEqual(
                new[] { 30, 40, 45, 60, 90, 120 },
                GetStepperAllowedValues(form, "_targetFpsStepper"));
        });
    }

    [TestMethod]
    public void TargetFpsStepper_IsBoundedAtBothEnds()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            using TestFixture fixture = new(temp.Path, null);
            using var form = new QuickPanelForm(fixture.Controller);
            object stepper = GetField<object>(form, "_targetFpsStepper");

            SetStepperValue(stepper, 30);
            Invoke(stepper, "StepDown");
            Assert.AreEqual(30, GetStepperValue(stepper));
            SetStepperValue(stepper, 120);
            Invoke(stepper, "StepUp");
            Assert.AreEqual(120, GetStepperValue(stepper));
        });
    }

    [TestMethod]
    public void TargetFpsSelection_PersistsExistingProfileImmediately()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            ForegroundApplicationSnapshot game = CreateGame(temp.Path);
            using TestFixture fixture = new(temp.Path, game);
            fixture.CreateProfile(60);
            fixture.Controller.Poll();
            Assert.IsTrue(fixture.Controller.CaptureInvokingApplication());
            using var form = new QuickPanelForm(fixture.Controller);
            form.ShowPanel(nint.Zero);

            SetStepperValue(GetField<object>(form, "_targetFpsStepper"), 45);

            Assert.AreEqual(45, fixture.Store.Current.GameProfiles.Single().TargetFps);
        });
    }

    [TestMethod]
    public void GlobalToggle_UsesControllerState()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            using TestFixture fixture = new(temp.Path, null);
            using var form = new QuickPanelForm(fixture.Controller);
            form.ShowPanel(nint.Zero);
            Button toggle = GetField<Button>(form, "_autoTdpToggle");

            toggle.PerformClick();
            Assert.IsFalse(fixture.Controller.GetSnapshot().AutoTdpEnabled);
            toggle.PerformClick();
            Assert.IsTrue(fixture.Controller.GetSnapshot().AutoTdpEnabled);
        });
    }

    [TestMethod]
    public void RuntimeRefresh_BatteryDisplaysConfiguredRange()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            ForegroundApplicationSnapshot game = CreateGame(temp.Path);
            using TestFixture fixture = new(temp.Path, game);
            fixture.Controller.Poll();
            Assert.IsTrue(fixture.Controller.CaptureInvokingApplication());
            fixture.Runtime.Snapshot = new AutoTdpRuntimeSnapshot(
                true,
                new AutoTdpStatus(
                    FpsReading.Valid(59),
                    PowerReading.Valid(18),
                    new PowerSourceReading(PowerSourceKind.Battery, null),
                    60,
                    18,
                    25,
                    AutoTdpState.Stable,
                    null),
                null);
            using var form = new QuickPanelForm(fixture.Controller);
            form.ShowPanel(nint.Zero);

            Assert.AreEqual("Batterie", GetField<Label>(form, "_sourceValue").Text);
            Assert.AreEqual("6–25 W", GetField<Label>(form, "_rangeValue").Text);
            Assert.AreEqual("18 W", GetField<Label>(form, "_tdpValue").Text);
            Assert.AreEqual(
                59f.ToString("0.0", CultureInfo.CurrentCulture),
                GetField<Label>(form, "_fpsDisplayValue").Text);
        });
    }

    [TestMethod]
    public void GenericForegroundApplication_CanBecomeCandidateOnInvocation()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            string path = Path.Combine(temp.Path, "Explorer.exe");
            File.WriteAllText(path, string.Empty);
            var application = new ForegroundApplicationSnapshot(
                321,
                DateTimeOffset.UtcNow,
                ExecutablePathIdentity.Normalize(path),
                "Explorer.exe",
                "Windows Explorer");
            using TestFixture fixture = new(temp.Path, application);
            Assert.IsTrue(fixture.Controller.CaptureInvokingApplication());
            using var form = new QuickPanelForm(fixture.Controller);
            form.ShowPanel(nint.Zero);

            Assert.AreEqual(
                "Windows Explorer",
                GetField<Label>(form, "_applicationNameValue").Text);
            Assert.IsTrue(GetField<Panel>(form, "_profileValueHost").Visible);
            Assert.IsTrue(GetField<Button>(form, "_createProfileButton").Visible);
        });
    }

    [TestMethod]
    public void CandidateApplication_RemainsDisplayedAfterForegroundChanges()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            ForegroundApplicationSnapshot firstGame = CreateGame(temp.Path);
            string secondPath = Path.Combine(temp.Path, "SecondGame.exe");
            File.WriteAllText(secondPath, string.Empty);
            ForegroundApplicationSnapshot secondGame = new(
                654,
                DateTimeOffset.UtcNow,
                ExecutablePathIdentity.Normalize(secondPath),
                "SecondGame.exe",
                "Second Game");
            using TestFixture fixture = new(temp.Path, firstGame);
            fixture.Controller.Poll();

            fixture.Detector.Snapshot = secondGame;
            fixture.Controller.Poll();
            fixture.Detector.Snapshot = null;
            fixture.Controller.Poll();

            using var form = new QuickPanelForm(fixture.Controller);
            form.ShowPanel(nint.Zero);

            Assert.AreEqual(
                "Example Game",
                GetField<Label>(form, "_applicationNameValue").Text);
            Assert.IsTrue(GetField<Panel>(form, "_profileValueHost").Visible);
            Assert.IsTrue(GetField<Button>(form, "_createProfileButton").Visible);

            GetField<Button>(form, "_createProfileButton").PerformClick();

            Assert.AreEqual(
                firstGame.ExecutablePath,
                fixture.Store.Current.GameProfiles.Single().ExecutablePath);
            Assert.IsFalse(GetField<Button>(form, "_createProfileButton").Visible);
        });
    }

    [TestMethod]
    public void ProfiledInvocation_TakesPriorityOverStaleCandidate()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            ForegroundApplicationSnapshot profiledGame = CreateGame(temp.Path);
            string secondPath = Path.Combine(temp.Path, "SecondGame.exe");
            File.WriteAllText(secondPath, string.Empty);
            ForegroundApplicationSnapshot secondGame = new(
                654,
                DateTimeOffset.UtcNow,
                ExecutablePathIdentity.Normalize(secondPath),
                "SecondGame.exe",
                "Second Game");
            using TestFixture fixture = new(temp.Path, profiledGame);
            fixture.Controller.Poll();
            fixture.Controller.CreateProfileForCurrentApplication(60);

            fixture.Detector.Snapshot = secondGame;
            Assert.IsTrue(fixture.Controller.CaptureInvokingApplication());
            fixture.Detector.Snapshot = profiledGame;
            Assert.IsTrue(fixture.Controller.CaptureInvokingApplication());

            using var form = new QuickPanelForm(fixture.Controller);
            form.ShowPanel(nint.Zero);

            Assert.AreEqual(
                profiledGame.DisplayName,
                GetField<Label>(form, "_applicationNameValue").Text);
            Assert.AreEqual(
                "Profil Activé",
                GetField<Label>(form, "_profileStateValue").Text);
            Assert.IsFalse(GetField<Button>(form, "_createProfileButton").Visible);
            Assert.AreSame(
                secondGame,
                fixture.Controller.GetSnapshot().CandidateApplication);
        });
    }

    [TestMethod]
    public void ProfiledDetectedApplication_TakesPriorityOverStaleCandidate()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            ForegroundApplicationSnapshot profiledGame = CreateGame(temp.Path);
            string secondPath = Path.Combine(temp.Path, "SecondGame.exe");
            File.WriteAllText(secondPath, string.Empty);
            ForegroundApplicationSnapshot secondGame = new(
                654,
                DateTimeOffset.UtcNow,
                ExecutablePathIdentity.Normalize(secondPath),
                "SecondGame.exe",
                "Second Game");
            using TestFixture fixture = new(temp.Path, profiledGame);
            fixture.Controller.Poll();
            fixture.Controller.CreateProfileForCurrentApplication(60);

            fixture.Detector.Snapshot = secondGame;
            Assert.IsTrue(fixture.Controller.CaptureInvokingApplication());
            fixture.Controller.ClearInvokingApplication();
            fixture.Controller.SetAutoTdpEnabled(false);
            fixture.Detector.Snapshot = profiledGame;
            fixture.Controller.Poll();

            using var form = new QuickPanelForm(fixture.Controller);
            form.ShowPanel(nint.Zero);

            Assert.AreEqual(
                profiledGame.DisplayName,
                GetField<Label>(form, "_applicationNameValue").Text);
            Assert.AreEqual(
                "Profil Activé",
                GetField<Label>(form, "_profileStateValue").Text);
            Assert.IsFalse(GetField<Button>(form, "_createProfileButton").Visible);
            Assert.AreSame(
                secondGame,
                fixture.Controller.GetSnapshot().CandidateApplication);
        });
    }

    [TestMethod]
    public void NewProfile_CreateFromControl_StaysInControlAndUsesSelectedFps()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            ForegroundApplicationSnapshot game = CreateGame(temp.Path);
            using TestFixture fixture = new(temp.Path, game);
            fixture.Controller.Poll();
            Assert.IsTrue(fixture.Controller.CaptureInvokingApplication());
            using var form = new QuickPanelForm(fixture.Controller);
            form.ShowPanel(nint.Zero);
            SetStepperValue(GetField<object>(form, "_targetFpsStepper"), 45);

            GetField<Button>(form, "_createProfileButton").PerformClick();

            GameProfile profile = fixture.Store.Current.GameProfiles.Single();
            Assert.AreEqual(45, profile.TargetFps);
            Assert.IsTrue(profile.Enabled);
            Assert.AreEqual("Profil Activé", GetField<Label>(form, "_profileStateValue").Text);
            Assert.IsFalse(GetField<Button>(form, "_createProfileButton").Visible);
            Assert.AreEqual("Main", GetField<object>(form, "_currentView").ToString());
        });
    }

    [TestMethod]
    public void ProfileEnabledState_IsReadDirectlyFromStoredProfile()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            ForegroundApplicationSnapshot game = CreateGame(temp.Path);
            using TestFixture fixture = new(temp.Path, game);
            fixture.CreateProfile(60);
            fixture.Store.Current.GameProfiles.Single().Enabled = false;
            fixture.Controller.Poll();
            Assert.IsTrue(fixture.Controller.CaptureInvokingApplication());
            using var form = new QuickPanelForm(fixture.Controller);
            form.ShowPanel(nint.Zero);

            Assert.AreEqual(
                "Profil Désactivé",
                GetField<Label>(form, "_profileStateValue").Text);
            Assert.IsFalse(GetField<Button>(form, "_createProfileButton").Visible);
        });
    }

    [TestMethod]
    public void ProfileRowClick_OpensEditorInSameFormWithoutNavbar()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            ForegroundApplicationSnapshot game = CreateGame(temp.Path);
            using TestFixture fixture = new(temp.Path, game);
            fixture.CreateProfile(60);
            using var form = new QuickPanelForm(fixture.Controller);
            form.ShowPanel(nint.Zero);
            form.ShowProfilesView();

            Panel rows = GetField<Panel>(form, "_profilesRows");
            Assert.AreEqual(1, rows.Controls.Count);
            Button row = (Button)rows.Controls[0];
            row.PerformClick();

            Assert.AreEqual("EditProfile", GetField<object>(form, "_currentView").ToString());
            Assert.IsFalse(GetField<Panel>(form, "_navigationBar").Visible);
            GetField<Button>(form, "_editProfileToggle").PerformClick();
            SetStepperValue(GetField<object>(form, "_editTargetFpsStepper"), 45);
            GetField<Button>(form, "_saveProfileButton").PerformClick();

            Assert.AreEqual(45, fixture.Store.Current.GameProfiles.Single().TargetFps);
            Assert.IsFalse(fixture.Store.Current.GameProfiles.Single().Enabled);
            Assert.AreEqual("Profiles", GetField<object>(form, "_currentView").ToString());
            Assert.IsTrue(GetField<Panel>(form, "_navigationBar").Visible);
        });
    }

    [TestMethod]
    public void ProfileRows_DisplayOnlyUserFacingNameAndChevron()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            ForegroundApplicationSnapshot game = CreateGame(temp.Path);
            using TestFixture fixture = new(temp.Path, game);
            fixture.CreateProfile(60);
            using var form = new QuickPanelForm(fixture.Controller);
            form.ShowProfilesView();

            Button row = (Button)GetField<Panel>(form, "_profilesRows").Controls[0];
            Assert.AreEqual("Example Game", row.AccessibleName);
            Assert.IsFalse(row.Text.Contains(".exe", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(row.Text.Contains(temp.Path, StringComparison.OrdinalIgnoreCase));
        });
    }

    [TestMethod]
    public void VisiblePanelBoundsMatchTheSelectedWorkingArea()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            using TestFixture fixture = new(temp.Path, null);
            using var form = new QuickPanelForm(fixture.Controller);
            form.ShowPanel(nint.Zero);

            Rectangle workingArea = Screen.FromControl(form).WorkingArea;
            Assert.AreEqual(workingArea.Top, form.Top);
            Assert.AreEqual(workingArea.Right, form.Right);
            Assert.AreEqual(workingArea.Bottom, form.Bottom);
            Assert.AreEqual(workingArea.Height, form.Height);
        });
    }

    [TestMethod]
    public void Layout_UsesTwoCenteredNavbarZonesAndKeepsFixedControlsContained()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            using TestFixture fixture = new(temp.Path, null);
            using var form = new QuickPanelForm(fixture.Controller);
            form.ShowPanel(nint.Zero);

            Panel navigation = GetField<Panel>(form, "_navigationBar");
            Assert.AreEqual(1, navigation.Controls.Count);
            TableLayoutPanel table = (TableLayoutPanel)navigation.Controls[0];
            Assert.AreEqual(2, table.Controls.Count);
            Assert.AreEqual(50F, table.ColumnStyles[0].Width);
            Assert.AreEqual(50F, table.ColumnStyles[1].Width);

            int[] columnWidths = table.GetColumnWidths();
            Assert.AreEqual(2, columnWidths.Length);
            Assert.IsTrue(Math.Abs(columnWidths[0] - columnWidths[1]) <= 1);

            Button controlButton = GetField<Button>(form, "_controlNavigationButton");
            Button profilesButton = GetField<Button>(form, "_profilesNavigationButton");
            Assert.AreEqual("Contrôle", controlButton.Text);
            Assert.AreEqual("Profils", profilesButton.Text);
            Assert.AreEqual(DockStyle.Fill, controlButton.Dock);
            Assert.AreEqual(DockStyle.Fill, profilesButton.Dock);
            Assert.IsTrue(table.ClientRectangle.Contains(controlButton.Bounds));
            Assert.IsTrue(table.ClientRectangle.Contains(profilesButton.Bounds));

            Button hideButton = GetField<Button>(form, "_hideButton");
            Assert.AreEqual("ChevronRight", GetPropertyValue(hideButton, "IconKind"));
            Assert.IsTrue(hideButton.Width > hideButton.Height);
            Assert.IsNotNull(hideButton.Parent);
            Assert.IsTrue(hideButton.Parent!.ClientRectangle.Contains(hideButton.Bounds));

            Button autoTdpToggle = GetField<Button>(form, "_autoTdpToggle");
            Assert.IsTrue(autoTdpToggle.Width > autoTdpToggle.Height);
            Assert.IsNotNull(autoTdpToggle.Parent);
            Assert.IsTrue(autoTdpToggle.Parent!.ClientRectangle.Contains(autoTdpToggle.Bounds));

            Rectangle expectedBounds = QuickPanelLayout.Calculate(
                Screen.FromControl(form).WorkingArea,
                form.DeviceDpi);
            Assert.AreEqual(expectedBounds.Width, form.Width);
        });
    }

    [TestMethod]
    public void Headers_KeepFixedTextVisibleAndRenderSingleAddAction()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            ForegroundApplicationSnapshot game = CreateGame(temp.Path);
            using TestFixture fixture = new(temp.Path, game);
            fixture.CreateProfile(60);
            using var form = new QuickPanelForm(fixture.Controller);
            form.ShowPanel(nint.Zero);

            form.ShowProfilesView();
            Control profilesView = GetField<Control>(form, "_profilesView");
            Button[] addButtons = Descendants(profilesView)
                .OfType<Button>()
                .Where(button => button.Text == "Ajouter")
                .ToArray();
            Assert.AreEqual(1, addButtons.Length);
            Assert.AreEqual("Plus", GetPropertyValue(addButtons[0], "IconKind"));
            Assert.IsFalse(addButtons[0].Text.Contains('+'));
            AssertTextFits(FindLabel(profilesView, "Profils"));

            form.ShowMainView();
            Control mainView = GetField<Control>(form, "_mainView");
            AssertTextFits(FindLabel(mainView, "AllyAutoTDP"));

            form.ShowProfilesView();
            Button row = (Button)GetField<Panel>(form, "_profilesRows").Controls[0];
            row.PerformClick();
            Control editorView = GetField<Control>(form, "_editProfileView");
            AssertTextFits(FindLabel(editorView, "Modifier le profil"));
        });
    }

    [TestMethod]
    public void EditProfileToggle_IsFullyContainedInProfileRow()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            ForegroundApplicationSnapshot game = CreateGame(temp.Path);
            using TestFixture fixture = new(temp.Path, game);
            fixture.CreateProfile(60);
            using var form = new QuickPanelForm(fixture.Controller);
            form.ShowPanel(nint.Zero);
            form.ShowProfilesView();
            ((Button)GetField<Panel>(form, "_profilesRows").Controls[0]).PerformClick();

            Button profileToggle = GetField<Button>(form, "_editProfileToggle");
            Assert.IsTrue(profileToggle.Width > profileToggle.Height);
            Assert.IsNotNull(profileToggle.Parent);
            Assert.IsTrue(profileToggle.Parent!.ClientRectangle.Contains(profileToggle.Bounds));
            Assert.IsTrue(profileToggle.Right <= profileToggle.Parent!.ClientSize.Width);
            AssertTextFits(FindLabel(GetField<Control>(form, "_editProfileView"), "Modifier le profil"));
        });
    }

    [TestMethod]
    public void MainHeader_SeparatesTitleStatusAndActionsWithoutClipping()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            using TestFixture fixture = new(temp.Path, null);
            using var form = new QuickPanelForm(fixture.Controller);
            form.ShowPanel(nint.Zero);

            Control mainView = GetField<Control>(form, "_mainView");
            Label title = FindLabel(mainView, "AllyAutoTDP");
            Label status = GetField<Label>(form, "_autoTdpStatusValue");
            Assert.AreSame(title.Parent, status.Parent);
            Assert.IsTrue(title.Parent!.ClientRectangle.Contains(title.Bounds));
            Assert.IsTrue(title.Parent.ClientRectangle.Contains(status.Bounds));
            Assert.IsFalse(title.Bounds.IntersectsWith(status.Bounds));
            Assert.IsTrue(title.Bottom <= status.Top);
            Assert.IsTrue(title.Height >= Math.Ceiling(title.Font.GetHeight()));
            AssertTextFits(title);
            AssertTextFits(status);
            AssertTextFitsVertically(status);

            Control header = title.Parent.Parent!;
            Button autoTdpToggle = GetField<Button>(form, "_autoTdpToggle");
            Button hideButton = GetField<Button>(form, "_hideButton");
            Assert.AreSame(autoTdpToggle.Parent, hideButton.Parent);
            Assert.IsTrue(header.ClientRectangle.Contains(autoTdpToggle.Parent!.Bounds));
            Assert.IsFalse(title.Parent.Bounds.IntersectsWith(autoTdpToggle.Parent.Bounds));
        });
    }

    [TestMethod]
    public void EditProfileHeaderAndFooter_UseDistinctContainedZones()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            ForegroundApplicationSnapshot game = CreateGame(temp.Path);
            using TestFixture fixture = new(temp.Path, game);
            fixture.CreateProfile(60);
            using var form = new QuickPanelForm(fixture.Controller);
            form.ShowPanel(nint.Zero);
            form.ShowProfilesView();
            ((Button)GetField<Panel>(form, "_profilesRows").Controls[0]).PerformClick();

            Control editor = GetField<Control>(form, "_editProfileView");
            Label title = FindLabel(editor, "Modifier le profil");
            Button back = Descendants(editor)
                .OfType<Button>()
                .Single(button => button.Text == "Retour");
            Assert.AreSame(title.Parent, back.Parent);
            Assert.IsFalse(title.Bounds.IntersectsWith(back.Bounds));
            Assert.IsTrue(title.Parent!.ClientRectangle.Contains(title.Bounds));
            Assert.IsTrue(title.Parent.ClientRectangle.Contains(back.Bounds));
            Assert.AreEqual(DockStyle.Fill, title.Dock);
            Assert.AreEqual(DockStyle.Fill, back.Dock);
            Assert.AreEqual(ContentAlignment.MiddleRight, title.TextAlign);
            Assert.IsTrue(title.Padding.Right > 0);
            AssertTextFits(title);
            AssertButtonTextFits(back);

            Button delete = Descendants(editor)
                .OfType<Button>()
                .Single(button => button.Text == "Supprimer");
            Button save = Descendants(editor)
                .OfType<Button>()
                .Single(button => button.Text == "Enregistrer");
            Assert.AreSame(delete.Parent, save.Parent);
            Assert.IsFalse(delete.Bounds.IntersectsWith(save.Bounds));
            Assert.IsTrue(delete.Parent!.ClientRectangle.Contains(delete.Bounds));
            Assert.IsTrue(delete.Parent.ClientRectangle.Contains(save.Bounds));
            Assert.AreEqual(DockStyle.Fill, delete.Dock);
            Assert.AreEqual(DockStyle.Fill, save.Dock);
            Assert.AreEqual("None", GetPropertyValue(delete, "IconKind"));
            Assert.AreEqual("None", GetPropertyValue(save, "IconKind"));
            AssertButtonTextFits(delete);
            AssertButtonTextFits(save);
        });
    }

    [TestMethod]
    public void ActiveSession_RemainsDisplayedWhenAllyAutoTdpIsForeground()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            ForegroundApplicationSnapshot game = CreateGame(temp.Path);
            using TestFixture fixture = new(temp.Path, game);
            fixture.CreateProfile(60);
            fixture.Controller.Poll();

            fixture.Detector.Snapshot = null;
            fixture.Controller.Poll();
            fixture.Detector.Snapshot = new ForegroundApplicationSnapshot(
                654,
                DateTimeOffset.UtcNow,
                Path.Combine(temp.Path, "Launcher.exe"),
                "Launcher.exe",
                "Launcher");
            Assert.IsTrue(fixture.Controller.CaptureInvokingApplication());

            using var form = new QuickPanelForm(fixture.Controller);
            form.ShowPanel(nint.Zero);

            Assert.AreEqual(
                "Example Game",
                GetField<Label>(form, "_applicationNameValue").Text);
            Assert.AreEqual(
                "Profil Activé",
                GetField<Label>(form, "_profileStateValue").Text);
        });
    }

    [TestMethod]
    public void TerminatedActiveSession_IsNotDisplayedAsCurrentGame()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            ForegroundApplicationSnapshot game = CreateGame(temp.Path);
            using TestFixture fixture = new(temp.Path, game);
            fixture.CreateProfile(60);
            fixture.Controller.Poll();

            fixture.LifetimeChecker.Status = ProcessLifetimeStatus.Terminated;
            fixture.Detector.Snapshot = null;
            fixture.Controller.Poll();

            using var form = new QuickPanelForm(fixture.Controller);
            form.ShowPanel(nint.Zero);

            Assert.AreEqual(
                "Aucun jeu détecté",
                GetField<Label>(form, "_applicationNameValue").Text);
        });
    }

    private static ForegroundApplicationSnapshot CreateGame(string directory)
    {
        string path = Path.Combine(directory, "ExampleGame.exe");
        File.WriteAllText(path, string.Empty);
        return new(
            321,
            DateTimeOffset.UtcNow,
            ExecutablePathIdentity.Normalize(path),
            Path.GetFileName(path),
            "Example Game");
    }

    private static int GetStepperValue(object stepper) =>
        (int)(stepper.GetType().GetProperty("Value")?.GetValue(stepper) ??
            throw new AssertFailedException("Stepper value is unavailable."));

    private static int[] GetStepperAllowedValues(QuickPanelForm form, string fieldName) =>
        ((IEnumerable<int>)(GetField<object>(form, fieldName)
            .GetType().GetProperty("AllowedValues")?.GetValue(GetField<object>(form, fieldName)) ??
            throw new AssertFailedException("Stepper values are unavailable."))).ToArray();

    private static void SetStepperValue(object stepper, int value) =>
        stepper.GetType().GetProperty("Value")?.SetValue(stepper, value);

    private static void Invoke(object target, string methodName) =>
        target.GetType().GetMethod(methodName)?.Invoke(target, null);

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static Label FindLabel(Control root, string text) =>
        Descendants(root)
            .OfType<Label>()
            .Single(label => label.Text == text);

    private static void AssertTextFits(Label label)
    {
        Size measured = TextRenderer.MeasureText(
            label.Text,
            label.Font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        Assert.IsTrue(
            measured.Width <= label.ClientSize.Width - label.Padding.Horizontal,
            $"'{label.Text}' does not fit in {label.ClientSize.Width}px.");
    }

    private static void AssertTextFitsVertically(Control control)
    {
        Size measured = TextRenderer.MeasureText(
            control.Text,
            control.Font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        Assert.IsTrue(
            measured.Height <= control.ClientSize.Height,
            $"'{control.Text}' does not fit vertically in {control.ClientSize.Height}px.");
    }

    private static void AssertButtonTextFits(Button button)
    {
        Size measured = TextRenderer.MeasureText(
            button.Text,
            button.Font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        int iconAllowance = GetPropertyValue(button, "IconKind") == "None" ? 4 : 40;
        Assert.IsTrue(
            measured.Width + iconAllowance <= button.ClientSize.Width,
            $"'{button.Text}' does not fit in {button.ClientSize.Width}px.");
        Assert.IsTrue(
            measured.Height <= button.ClientSize.Height - 2,
            $"'{button.Text}' does not fit vertically in {button.ClientSize.Height}px.");
    }

    private static string GetPropertyValue(object instance, string name) =>
        instance.GetType().GetProperty(name)?.GetValue(instance)?.ToString() ??
        throw new AssertFailedException($"Missing property '{name}'.");

    private static T GetField<T>(QuickPanelForm form, string name)
    {
        FieldInfo field = typeof(QuickPanelForm).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new AssertFailedException($"Missing QuickPanel field '{name}'.");
        return (T)(field.GetValue(form) ??
            throw new AssertFailedException($"QuickPanel field '{name}' is null."));
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.IsNull(failure, failure?.ToString());
    }

    private sealed class TestFixture : IDisposable
    {
        private readonly ForegroundApplicationSnapshot? _foreground;

        public TestFixture(string directory, ForegroundApplicationSnapshot? foreground)
        {
            _foreground = foreground;
            Store = new ConfigurationStore(Path.Combine(directory, "config.json"));
            var profiles = new GameProfileService(Store);
            Runtime = new FakeRuntime();
            LifetimeChecker = new AliveProcessChecker();
            Detector = new FakeForegroundDetector(foreground);
            var manager = new GameSessionManager(
                profiles,
                Runtime,
                LifetimeChecker);
            Controller = new AllyApplicationController(
                Store,
                profiles,
                Detector,
                manager,
                Runtime,
                new AppLogger(TextWriter.Null),
                processLifetimeChecker: LifetimeChecker);
        }

        public ConfigurationStore Store { get; }

        public FakeRuntime Runtime { get; }

        public FakeForegroundDetector Detector { get; }

        public AliveProcessChecker LifetimeChecker { get; }

        public AllyApplicationController Controller { get; }

        public void CreateProfile(int targetFps)
        {
            Assert.IsNotNull(_foreground);
            new GameProfileService(Store).CreateFromForeground(
                _foreground!,
                targetFps,
                RestoreMode.Balanced,
                enabled: true);
        }

        public void Dispose() => Controller.Dispose();
    }

    private sealed class FakeForegroundDetector : IForegroundApplicationDetector
    {
        public ForegroundApplicationSnapshot? Snapshot { get; set; }

        public FakeForegroundDetector(ForegroundApplicationSnapshot? snapshot) =>
            Snapshot = snapshot;

        public ForegroundDetectionResult Detect() =>
            Snapshot is null
                ? new(null, null)
                : new(Snapshot, null, 100);
    }

    private sealed class AliveProcessChecker : IProcessLifetimeChecker
    {
        public ProcessLifetimeStatus Status { get; set; } = ProcessLifetimeStatus.Alive;

        public ProcessLifetimeStatus Check(GameProcessIdentity identity) =>
            Status;
    }

    private sealed class FakeRuntime : IAutoTdpRuntime
    {
        public AutoTdpRuntimeSnapshot Snapshot { get; set; } =
            AutoTdpRuntimeSnapshot.Idle();

        public AutoTdpRuntimeResult Start(GameProfile profile) =>
            AutoTdpRuntimeResult.Success();

        public AutoTdpRuntimeResult StopAndRestore() =>
            AutoTdpRuntimeResult.Success();

        public AutoTdpRuntimeSnapshot GetSnapshot() => Snapshot;
    }
}
