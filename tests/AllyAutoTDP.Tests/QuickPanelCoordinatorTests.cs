using AllyAutoTDP.Application;
using AllyAutoTDP.AutoTdp;
using AllyAutoTDP.Configuration;
using AllyAutoTDP.Logging;
using AllyAutoTDP.UI;
using AllyAutoTDP.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AllyAutoTDP.Tests;

[TestClass]
public sealed class QuickPanelCoordinatorTests
{
    [TestMethod]
    public void ExternalProcessDeactivation_HidesPanelAndDoesNotRestoreGame()
    {
        using TemporaryDirectory temp = new();
        ForegroundApplicationSnapshot game = Snapshot(
            Path.Combine(temp.Path, "Game.exe"),
            42,
            100);
        using TestFixture fixture = new(temp.Path, game);
        var panel = new FakeQuickPanelWindow();
        var focus = new FakeWindowFocusService(100, 42, 999);
        using var coordinator = new QuickPanelCoordinator(
            fixture.Controller,
            panel,
            focus,
            ownProcessId: 999);

        coordinator.Toggle();
        focus.SetForeground(200, 777);
        panel.RaiseDeactivated();

        Assert.IsFalse(panel.Visible);
        Assert.IsFalse(panel.IsTopMost);
        Assert.AreEqual(0, focus.SetForegroundCallCount);
    }

    [TestMethod]
    public void AllyAutoTdpWindowDeactivation_DoesNotHidePanel()
    {
        using TemporaryDirectory temp = new();
        ForegroundApplicationSnapshot game = Snapshot(
            Path.Combine(temp.Path, "Game.exe"),
            42,
            100);
        using TestFixture fixture = new(temp.Path, game);
        var panel = new FakeQuickPanelWindow();
        var focus = new FakeWindowFocusService(100, 42, 999);
        using var coordinator = new QuickPanelCoordinator(
            fixture.Controller,
            panel,
            focus,
            ownProcessId: 999);

        coordinator.Toggle();
        focus.SetForeground(300, 999);
        panel.RaiseDeactivated();

        Assert.IsTrue(panel.Visible);
        Assert.IsTrue(panel.IsTopMost);
    }

    [TestMethod]
    public void Toggle_CapturesBeforeShowAndRestoresMatchingWindow()
    {
        using TemporaryDirectory temp = new();
        ForegroundApplicationSnapshot game = Snapshot(
            Path.Combine(temp.Path, "Game.exe"),
            42,
            100);
        using TestFixture fixture = new(temp.Path, game);
        var panel = new FakeQuickPanelWindow();
        var focus = new FakeWindowFocusService(100, 42, 999);
        using var coordinator = new QuickPanelCoordinator(
            fixture.Controller,
            panel,
            focus,
            ownProcessId: 999);

        coordinator.Toggle();
        Assert.IsTrue(panel.Visible);
        Assert.AreEqual(100, panel.AnchorWindow);
        Assert.AreEqual(1, fixture.Detector.DetectCount);
        Assert.AreEqual(100, fixture.Controller.GetSnapshot()
            .InvokingApplication!.WindowHandle);

        coordinator.Toggle();

        Assert.IsFalse(panel.Visible);
        Assert.IsFalse(panel.IsTopMost);
        Assert.AreEqual(100, focus.LastSetForegroundWindow);
    }

    private static ForegroundApplicationSnapshot Snapshot(
        string executablePath,
        int processId,
        nint windowHandle) =>
        new(
            processId,
            DateTimeOffset.UtcNow,
            ExecutablePathIdentity.Normalize(executablePath),
            Path.GetFileName(executablePath),
            Path.GetFileNameWithoutExtension(executablePath));

    private sealed class TestFixture : IDisposable
    {
        public TestFixture(
            string directory,
            ForegroundApplicationSnapshot? snapshot)
        {
            Store = new ConfigurationStore(Path.Combine(directory, "config.json"));
            var profiles = new GameProfileService(Store);
            Runtime = new FakeRuntime();
            var manager = new GameSessionManager(
                profiles,
                Runtime,
                new AliveProcessChecker());
            Detector = new FakeForegroundDetector(snapshot);
            Controller = new AllyApplicationController(
                Store,
                profiles,
                Detector,
                manager,
                Runtime,
                new AppLogger(TextWriter.Null));
        }

        public ConfigurationStore Store { get; }
        public FakeRuntime Runtime { get; }
        public FakeForegroundDetector Detector { get; }
        public AllyApplicationController Controller { get; }

        public void Dispose() => Controller.Dispose();
    }

    private sealed class FakeForegroundDetector : IForegroundApplicationDetector
    {
        private readonly ForegroundApplicationSnapshot? _snapshot;

        public FakeForegroundDetector(ForegroundApplicationSnapshot? snapshot) =>
            _snapshot = snapshot;

        public int DetectCount { get; private set; }

        public ForegroundDetectionResult Detect()
        {
            DetectCount++;
            return _snapshot is null
                ? new(null, null)
                : new(_snapshot, null, 100);
        }
    }

    private sealed class FakeWindowFocusService : IWindowFocusService
    {
        private readonly int _ownProcessId;
        private readonly Dictionary<nint, int> _processes = new();

        public FakeWindowFocusService(
            nint foregroundWindow,
            int foregroundProcessId,
            int ownProcessId)
        {
            CurrentWindow = foregroundWindow;
            _processes[foregroundWindow] = foregroundProcessId;
            _ownProcessId = ownProcessId;
        }

        public nint CurrentWindow { get; private set; }

        public nint LastSetForegroundWindow { get; private set; }

        public int SetForegroundCallCount { get; private set; }

        public nint GetForegroundWindow() => CurrentWindow;

        public bool IsWindow(nint windowHandle) =>
            _processes.ContainsKey(windowHandle);

        public int GetProcessId(nint windowHandle) =>
            _processes.TryGetValue(windowHandle, out int processId)
                ? processId
                : 0;

        public bool IsSameProcess(
            nint windowHandle,
            ForegroundApplicationSnapshot application) =>
            GetProcessId(windowHandle) == application.ProcessId;

        public bool TrySetForegroundWindow(nint windowHandle)
        {
            SetForegroundCallCount++;
            LastSetForegroundWindow = windowHandle;
            CurrentWindow = windowHandle;
            return true;
        }

        public void SetForeground(nint windowHandle, int processId)
        {
            CurrentWindow = windowHandle;
            _processes[windowHandle] = processId;
        }
    }

    private sealed class FakeQuickPanelWindow : IQuickPanelWindow
    {
        public bool Visible { get; private set; }

        public bool IsTopMost { get; private set; }

        public bool IsDisposed => false;

        public nint AnchorWindow { get; private set; }

        public event EventHandler? Deactivated;

        public event EventHandler? CloseRequested;

        public void ShowPanel(nint anchorWindow)
        {
            AnchorWindow = anchorWindow;
            Visible = true;
            IsTopMost = true;
        }

        public void HidePanel()
        {
            Visible = false;
            IsTopMost = false;
        }

        public void Post(Action action) => action();

        public void RaiseDeactivated() => Deactivated?.Invoke(this, EventArgs.Empty);

        public void RaiseCloseRequested() =>
            CloseRequested?.Invoke(this, EventArgs.Empty);
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
