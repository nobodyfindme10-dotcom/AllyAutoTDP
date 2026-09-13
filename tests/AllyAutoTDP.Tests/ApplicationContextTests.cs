using System.Reflection;
using System.Windows.Forms;
using AllyAutoTDP.Application;
using AllyAutoTDP.AutoTdp;
using AllyAutoTDP.Configuration;
using AllyAutoTDP.Hardware;
using AllyAutoTDP.Logging;
using AllyAutoTDP.Power;
using AllyAutoTDP.UI;
using AllyAutoTDP.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AllyAutoTDP.Tests;

[TestClass]
public sealed class ApplicationContextTests
{
    [TestMethod]
    public void BackgroundStartup_HidesQuickPanelAndKeepsTrayActive()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            using TestApplicationFixture fixture = new(temp.Path);
            using var context = new AllyApplicationContext(
                fixture.Controller,
                startInBackground: true);

            Assert.IsTrue(context.IsBackgroundMode);
            Assert.IsFalse(context.IsQuickPanelVisible);
            Assert.IsTrue(context.IsTrayActive);
        });
    }

    [TestMethod]
    public void HotkeyDefinition_DefaultIsCtrlAltT()
    {
        HotkeyDefinition definition = HotkeyDefinition.Default;

        Assert.AreEqual(Keys.T, definition.Key);
        Assert.IsTrue(definition.Modifiers.HasFlag(GlobalHotkeyModifiers.Control));
        Assert.IsTrue(definition.Modifiers.HasFlag(GlobalHotkeyModifiers.Alt));
    }

    [TestMethod]
    public void TrayMenu_IsSingleOfficialDarkMenuWithExpectedActions()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            using TestApplicationFixture fixture = new(temp.Path);
            using var context = new AllyApplicationContext(
                fixture.Controller,
                startInBackground: true);
            ContextMenuStrip menu = GetPrivate<ContextMenuStrip>(context, "_trayMenu");

            Assert.AreEqual("AllyTrayContextMenu", menu.GetType().Name);
            Assert.AreEqual(10, menu.Items.Count);
            Assert.AreEqual("AllyAutoTDP", menu.Items[0].Text);
            Assert.AreEqual("AutoTDP activé", menu.Items[2].Text);
            Assert.AreEqual("Ouvrir AllyAutoTDP", menu.Items[4].Text);
            Assert.AreEqual("Ajouter un jeu…", menu.Items[6].Text);
            Assert.AreEqual("Profils", menu.Items[7].Text);
            Assert.AreEqual("Quitter", menu.Items[9].Text);
            Assert.IsInstanceOfType(menu.Items[1], typeof(ToolStripSeparator));
            Assert.IsInstanceOfType(menu.Items[3], typeof(ToolStripSeparator));
            Assert.IsInstanceOfType(menu.Items[5], typeof(ToolStripSeparator));
            Assert.IsInstanceOfType(menu.Items[8], typeof(ToolStripSeparator));

            Assert.AreEqual("AllyTrayMenuRenderer", menu.Renderer.GetType().Name);
            Assert.AreEqual(
                new Padding(8, 5, 8, 5),
                ((ToolStripMenuItem)menu.Items[2]).Padding);

            ToolStripMenuItem addGame = (ToolStripMenuItem)menu.Items[6];
            Assert.AreEqual(0, addGame.DropDownItems.Count);
            Assert.IsFalse(menu.Items.Cast<ToolStripItem>()
                .Any(item => item.Text == "Application actuelle"));
        });
    }

    [TestMethod]
    public void TrayOpen_UsesTheQuickPanelInstanceOwnedByCoordinator()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            using TestApplicationFixture fixture = new(temp.Path);
            using var context = new AllyApplicationContext(
                fixture.Controller,
                startInBackground: true);
            ContextMenuStrip menu = GetPrivate<ContextMenuStrip>(context, "_trayMenu");
            ToolStripMenuItem openItem = (ToolStripMenuItem)menu.Items[4];

            openItem.PerformClick();

            object quickPanel = GetPrivate<object>(context, "_quickPanel");
            object coordinator = GetPrivate<object>(context, "_quickPanelCoordinator");
            object coordinatorPanel = GetPrivate<object>(coordinator, "_panel");
            Assert.IsTrue(context.IsQuickPanelVisible);
            Assert.AreSame(quickPanel, coordinatorPanel);
            Assert.AreEqual("Fermer AllyAutoTDP", openItem.Text);

            openItem.PerformClick();

            Assert.IsFalse(context.IsQuickPanelVisible);
            Assert.AreEqual("Ouvrir AllyAutoTDP", openItem.Text);
        });
    }

    [TestMethod]
    public void AllQuickPanelOpenPaths_CaptureInvokingApplicationOnce()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            ForegroundApplicationSnapshot application = Snapshot(
                Path.Combine(temp.Path, "Game.exe"));
            using TestApplicationFixture fixture = new(temp.Path, application);
            fixture.Controller.Poll();
            using var context = new AllyApplicationContext(
                fixture.Controller,
                startInBackground: true);
            GetPrivate<System.Threading.Timer>(context, "_pollTimer").Dispose();
            Thread.Sleep(50);

            void AssertCapturedOnce(int previousCount)
            {
                Assert.AreEqual(previousCount + 1, fixture.Detector.DetectCount);
                Assert.AreEqual(
                    application.ExecutablePath,
                    fixture.Controller.GetSnapshot()
                        .InvokingApplication!.Application.ExecutablePath);
                Assert.AreEqual(
                    application.DisplayName,
                    GetPrivate<Label>(
                        GetPrivate<QuickPanelForm>(context, "_quickPanel"),
                        "_applicationNameValue").Text);
                Assert.AreEqual(
                    "Main",
                    GetPrivate<object>(
                        GetPrivate<QuickPanelForm>(context, "_quickPanel"),
                        "_currentView").ToString());
            }

            int beforeHotkey = fixture.Detector.DetectCount;
            InvokePrivate(context, "OpenMainQuickPanel");
            AssertCapturedOnce(beforeHotkey);
            InvokePrivate(context, "ToggleQuickPanel");

            int beforeTrayClick = fixture.Detector.DetectCount;
            NotifyIcon icon = GetPrivate<NotifyIcon>(context, "_notifyIcon");
            InvokePrivate(
                context,
                "NotifyIconMouseUp",
                icon,
                new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
            AssertCapturedOnce(beforeTrayClick);
            InvokePrivate(context, "ToggleQuickPanel");

            int beforeTrayMenu = fixture.Detector.DetectCount;
            ContextMenuStrip menu = GetPrivate<ContextMenuStrip>(
                context,
                "_trayMenu");
            ((ToolStripMenuItem)menu.Items[4]).PerformClick();
            AssertCapturedOnce(beforeTrayMenu);
            Assert.AreEqual(
                "Main",
                GetPrivate<object>(
                    GetPrivate<QuickPanelForm>(context, "_quickPanel"),
                    "_currentView").ToString());
        });
    }

    [TestMethod]
    public void AllQuickPanelOpenPaths_ReplaceCandidateOnExplicitInvocation()
    {
        RunSta(() =>
        {
            void AssertPath(Action<AllyApplicationContext> open)
            {
                using TemporaryDirectory temp = new();
                ForegroundApplicationSnapshot first = Snapshot(
                    Path.Combine(temp.Path, "First.exe"));
                ForegroundApplicationSnapshot second = new(
                    43,
                    DateTimeOffset.UtcNow,
                    ExecutablePathIdentity.Normalize(
                        Path.Combine(temp.Path, "Second.exe")),
                    "Second.exe",
                    "Second Game");
                File.WriteAllText(second.ExecutablePath, string.Empty);
                using TestApplicationFixture fixture = new(temp.Path, first);
                fixture.Controller.Poll();
                using var context = new AllyApplicationContext(
                    fixture.Controller,
                    startInBackground: true);
                GetPrivate<System.Threading.Timer>(context, "_pollTimer").Dispose();
                Thread.Sleep(50);
                fixture.Detector.Snapshot = second;

                open(context);

                AllyApplicationSnapshot snapshot = fixture.Controller.GetSnapshot();
                Assert.AreSame(second, snapshot.CandidateApplication);
                Assert.AreSame(
                    second,
                    snapshot.InvokingApplication!.Application);
                Assert.AreEqual(
                    "Second Game",
                    GetPrivate<Label>(
                        GetPrivate<QuickPanelForm>(context, "_quickPanel"),
                        "_applicationNameValue").Text);
            }

            AssertPath(context => InvokePrivate(context, "OpenMainQuickPanel"));
            AssertPath(context =>
            {
                NotifyIcon icon = GetPrivate<NotifyIcon>(context, "_notifyIcon");
                InvokePrivate(
                    context,
                    "NotifyIconMouseUp",
                    icon,
                    new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
            });
            AssertPath(context =>
            {
                ContextMenuStrip menu = GetPrivate<ContextMenuStrip>(
                    context,
                    "_trayMenu");
                ((ToolStripMenuItem)menu.Items[4]).PerformClick();
            });
        });
    }

    [TestMethod]
    public void AllQuickPanelOpenPaths_CreateCandidateOnFirstExplicitInvocation()
    {
        RunSta(() =>
        {
            void AssertPath(Action<AllyApplicationContext> open)
            {
                using TemporaryDirectory temp = new();
                ForegroundApplicationSnapshot application = Snapshot(
                    Path.Combine(temp.Path, "Invoked.exe"));
                using TestApplicationFixture fixture = new(temp.Path, null);
                using var context = new AllyApplicationContext(
                    fixture.Controller,
                    startInBackground: true);
                GetPrivate<System.Threading.Timer>(context, "_pollTimer").Dispose();
                Thread.Sleep(50);
                fixture.Detector.Snapshot = application;

                open(context);

                AllyApplicationSnapshot snapshot = fixture.Controller.GetSnapshot();
                Assert.AreSame(application, snapshot.CandidateApplication);
                Assert.AreSame(
                    application,
                    snapshot.InvokingApplication!.Application);
                Assert.AreEqual(
                    application.DisplayName,
                    GetPrivate<Label>(
                        GetPrivate<QuickPanelForm>(context, "_quickPanel"),
                        "_applicationNameValue").Text);
            }

            AssertPath(context => InvokePrivate(context, "OpenMainQuickPanel"));
            AssertPath(context =>
            {
                NotifyIcon icon = GetPrivate<NotifyIcon>(context, "_notifyIcon");
                InvokePrivate(
                    context,
                    "NotifyIconMouseUp",
                    icon,
                    new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
            });
            AssertPath(context =>
            {
                ContextMenuStrip menu = GetPrivate<ContextMenuStrip>(
                    context,
                    "_trayMenu");
                ((ToolStripMenuItem)menu.Items[4]).PerformClick();
            });
        });
    }

    [TestMethod]
    public void TrayIconClick_TogglesTheSameQuickPanel()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            using TestApplicationFixture fixture = new(temp.Path);
            using var context = new AllyApplicationContext(
                fixture.Controller,
                startInBackground: true);
            NotifyIcon icon = GetPrivate<NotifyIcon>(context, "_notifyIcon");

            InvokePrivate(
                context,
                "NotifyIconMouseUp",
                icon,
                new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
            Assert.IsTrue(context.IsQuickPanelVisible);

            InvokePrivate(
                context,
                "NotifyIconMouseUp",
                icon,
                new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
            Assert.IsFalse(context.IsQuickPanelVisible);
        });
    }

    [TestMethod]
    public void QuickPanelCloseAction_HidesWithoutStoppingTray()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            using TestApplicationFixture fixture = new(temp.Path);
            using var context = new AllyApplicationContext(
                fixture.Controller,
                startInBackground: true);
            ContextMenuStrip menu = GetPrivate<ContextMenuStrip>(context, "_trayMenu");
            ((ToolStripMenuItem)menu.Items[4]).PerformClick();

            QuickPanelForm panel = GetPrivate<QuickPanelForm>(context, "_quickPanel");
            GetPrivate<Button>(panel, "_hideButton").PerformClick();

            Assert.IsFalse(context.IsQuickPanelVisible);
            Assert.IsTrue(context.IsTrayActive);
            Assert.AreEqual("Ouvrir AllyAutoTDP", menu.Items[4].Text);
        });
    }

    private static ForegroundApplicationSnapshot Snapshot(string executablePath) =>
        new(
            42,
            DateTimeOffset.UtcNow,
            ExecutablePathIdentity.Normalize(executablePath),
            Path.GetFileName(executablePath),
            Path.GetFileNameWithoutExtension(executablePath));

    [TestMethod]
    public void TrayAutoTdpToggle_UsesControllerState()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            using TestApplicationFixture fixture = new(temp.Path);
            using var context = new AllyApplicationContext(
                fixture.Controller,
                startInBackground: true);
            ContextMenuStrip menu = GetPrivate<ContextMenuStrip>(context, "_trayMenu");
            ToolStripMenuItem toggle = (ToolStripMenuItem)menu.Items[2];

            Assert.IsTrue(toggle.Checked);
            toggle.PerformClick();
            Assert.IsFalse(fixture.Controller.GetSnapshot().AutoTdpEnabled);
            Assert.IsFalse(toggle.Checked);
            toggle.PerformClick();
            Assert.IsTrue(fixture.Controller.GetSnapshot().AutoTdpEnabled);
            Assert.IsTrue(toggle.Checked);
        });
    }

    [TestMethod]
    public void TrayProfiles_UsesTheExistingQuickPanelOnProfilesView()
    {
        RunSta(() =>
        {
            using TemporaryDirectory temp = new();
            using TestApplicationFixture fixture = new(temp.Path);
            using var context = new AllyApplicationContext(
                fixture.Controller,
                startInBackground: true);
            ContextMenuStrip menu = GetPrivate<ContextMenuStrip>(context, "_trayMenu");

            ((ToolStripMenuItem)menu.Items[7]).PerformClick();

            QuickPanelForm panel = GetPrivate<QuickPanelForm>(context, "_quickPanel");
            Assert.AreEqual("Profiles", GetPrivate<object>(panel, "_currentView").ToString());
            Assert.IsTrue(context.IsQuickPanelVisible);
        });
    }

    [TestMethod]
    public void MainForm_IsAbsentFromFinalUiAssembly()
    {
        Assert.IsNull(
            typeof(AllyApplicationContext).Assembly.GetType(
                "AllyAutoTDP.UI.MainForm"));
        Assert.IsFalse(
            typeof(AllyApplicationContext)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Any(field => field.FieldType.Name == "MainForm"));
    }

    [TestMethod]
    public void BackgroundArgument_IsRecognizedCaseInsensitively()
    {
        Assert.IsTrue(StartupOptions.FromArgs(["--background"]).StartInBackground);
        Assert.IsTrue(StartupOptions.FromArgs(["--BACKGROUND"]).StartInBackground);
        Assert.IsFalse(StartupOptions.FromArgs([]).StartInBackground);
    }

    private static T GetPrivate<T>(object instance, string name)
    {
        FieldInfo field = instance.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new AssertFailedException($"Missing private field '{name}'.");
        return (T)(field.GetValue(instance) ??
            throw new AssertFailedException($"Private field '{name}' is null."));
    }

    private static void InvokePrivate(
        object instance,
        string name,
        params object?[] arguments)
    {
        MethodInfo method = instance.GetType().GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new AssertFailedException($"Missing private method '{name}'.");
        method.Invoke(instance, arguments);
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

    private sealed class TestApplicationFixture : IDisposable
    {
        public TestApplicationFixture(
            string directory,
            ForegroundApplicationSnapshot? snapshot = null)
        {
            Store = new ConfigurationStore(Path.Combine(directory, "config.json"));
            var profiles = new GameProfileService(Store);
            Runtime = new FakeRuntime();
            LifetimeChecker = new AliveProcessChecker();
            var manager = new GameSessionManager(
                profiles,
                Runtime,
                LifetimeChecker);
            Detector = new FakeForegroundDetector(snapshot);
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

        public void Dispose() => Controller.Dispose();
    }

    private sealed class FakeForegroundDetector : IForegroundApplicationDetector
    {
        public FakeForegroundDetector(ForegroundApplicationSnapshot? snapshot) =>
            Snapshot = snapshot;

        public ForegroundApplicationSnapshot? Snapshot { get; set; }

        public int DetectCount { get; private set; }

        public ForegroundDetectionResult Detect()
        {
            DetectCount++;
            return Snapshot is null
                ? new(null, null)
                : new(Snapshot, null, 100);
        }
    }

    private sealed class AliveProcessChecker : IProcessLifetimeChecker
    {
        public ProcessLifetimeStatus Check(GameProcessIdentity identity) =>
            ProcessLifetimeStatus.Alive;
    }

    private sealed class FakeRuntime : IAutoTdpRuntime
    {
        public AutoTdpRuntimeResult Start(GameProfile profile) =>
            AutoTdpRuntimeResult.Success();

        public AutoTdpRuntimeResult StopAndRestore() =>
            AutoTdpRuntimeResult.Success();

        public AutoTdpRuntimeSnapshot GetSnapshot() =>
            AutoTdpRuntimeSnapshot.Idle();
    }
}
