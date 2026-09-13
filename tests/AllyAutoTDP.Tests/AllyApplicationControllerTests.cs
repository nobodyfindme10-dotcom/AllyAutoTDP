using AllyAutoTDP.Application;
using AllyAutoTDP.Configuration;
using AllyAutoTDP.Hardware;
using AllyAutoTDP.Logging;
using AllyAutoTDP.Power;
using AllyAutoTDP.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AllyAutoTDP.Tests;

[TestClass]
public sealed class AllyApplicationControllerTests
{
    [TestMethod]
    public void CurrentApplicationProfile_UsesForegroundAndGeneralDefaults()
    {
        using TemporaryDirectory temp = new();
        string executablePath = Path.Combine(temp.Path, "Current.exe");
        File.WriteAllText(executablePath, string.Empty);
        ForegroundApplicationSnapshot snapshot = Snapshot(executablePath);
        using TestApplicationFixture fixture = new(temp.Path, snapshot);

        fixture.Controller.Poll();
        GameProfile profile = fixture.Controller.CreateProfileForCurrentApplication();

        Assert.AreEqual(snapshot.ExecutablePath, profile.ExecutablePath);
        Assert.AreEqual(snapshot.DisplayName, profile.DisplayName);
        Assert.IsTrue(profile.Enabled);
        Assert.AreEqual(60, profile.TargetFps);
        Assert.AreEqual(RestoreMode.Balanced, profile.RestoreMode);
        Assert.IsTrue(File.Exists(fixture.Store.ConfigPath));
    }

    [TestMethod]
    public void UnknownForeground_BecomesCandidateAndIsRetained()
    {
        using TemporaryDirectory temp = new();
        string firstPath = Path.Combine(temp.Path, "First.exe");
        string secondPath = Path.Combine(temp.Path, "Second.exe");
        File.WriteAllText(firstPath, string.Empty);
        File.WriteAllText(secondPath, string.Empty);
        DateTimeOffset firstStart = DateTimeOffset.UtcNow;
        ForegroundApplicationSnapshot first = Snapshot(
            firstPath,
            processId: 42,
            processStartTime: firstStart);
        ForegroundApplicationSnapshot second = Snapshot(
            secondPath,
            processId: 43,
            processStartTime: firstStart.AddSeconds(1));
        using TestApplicationFixture fixture = new(temp.Path, first);

        fixture.Controller.Poll();
        fixture.Detector.Snapshot = second;
        fixture.Controller.Poll();
        AllyApplicationSnapshot duringSecondForeground =
            fixture.Controller.GetSnapshot();
        Assert.AreSame(second, duringSecondForeground.DetectedApplication);
        Assert.AreSame(first, duringSecondForeground.CandidateApplication);
        fixture.Detector.Snapshot = null;
        fixture.Controller.Poll();

        AllyApplicationSnapshot snapshot = fixture.Controller.GetSnapshot();
        Assert.IsNull(snapshot.DetectedApplication);
        Assert.AreSame(first, snapshot.CandidateApplication);
        Assert.IsNull(snapshot.ActiveSession);
        Assert.AreEqual(0, fixture.Runtime.StartCount);
    }

    [TestMethod]
    public void ExplicitInvocation_WithoutCandidateCreatesCandidateImmediately()
    {
        using TemporaryDirectory temp = new();
        string executablePath = Path.Combine(temp.Path, "Invoked.exe");
        File.WriteAllText(executablePath, string.Empty);
        ForegroundApplicationSnapshot invoked = Snapshot(executablePath);
        using TestApplicationFixture fixture = new(temp.Path, invoked);

        Assert.IsTrue(fixture.Controller.CaptureInvokingApplication());

        AllyApplicationSnapshot snapshot = fixture.Controller.GetSnapshot();
        Assert.IsNull(snapshot.DetectedApplication);
        Assert.AreSame(invoked, snapshot.CandidateApplication);
        Assert.AreSame(invoked, snapshot.InvokingApplication!.Application);

        GameProfile profile = fixture.Controller.CreateProfileForCurrentApplication(90);
        Assert.AreEqual(invoked.ExecutablePath, profile.ExecutablePath);
        Assert.AreEqual(90, profile.TargetFps);
        Assert.IsTrue(profile.Enabled);
        Assert.AreEqual(0, fixture.Runtime.StartCount);
    }

    [TestMethod]
    public void ExplicitInvocation_ReplacesLivingCandidateAndCreatesProfileFromNewApplication()
    {
        using TemporaryDirectory temp = new();
        string firstPath = Path.Combine(temp.Path, "First.exe");
        string secondPath = Path.Combine(temp.Path, "Second.exe");
        File.WriteAllText(firstPath, string.Empty);
        File.WriteAllText(secondPath, string.Empty);
        ForegroundApplicationSnapshot first = Snapshot(firstPath, processId: 42);
        ForegroundApplicationSnapshot second = Snapshot(secondPath, processId: 43);
        using TestApplicationFixture fixture = new(temp.Path, first);

        fixture.Controller.Poll();
        fixture.Detector.Snapshot = second;
        fixture.Controller.Poll();

        Assert.IsTrue(fixture.Controller.CaptureInvokingApplication());

        AllyApplicationSnapshot snapshot = fixture.Controller.GetSnapshot();
        Assert.AreSame(second, snapshot.DetectedApplication);
        Assert.AreSame(second, snapshot.CandidateApplication);
        Assert.AreSame(
            second,
            snapshot.InvokingApplication!.Application);

        GameProfile profile = fixture.Controller.CreateProfileForCurrentApplication(90);
        Assert.AreEqual(second.ExecutablePath, profile.ExecutablePath);
        Assert.AreEqual(90, profile.TargetFps);
        Assert.AreEqual(0, fixture.Runtime.StartCount);
    }

    [TestMethod]
    public void ExplicitInvocation_OfProfiledApplicationDoesNotCreateCandidate()
    {
        using TemporaryDirectory temp = new();
        string firstPath = Path.Combine(temp.Path, "First.exe");
        string secondPath = Path.Combine(temp.Path, "Second.exe");
        File.WriteAllText(firstPath, string.Empty);
        File.WriteAllText(secondPath, string.Empty);
        ForegroundApplicationSnapshot first = Snapshot(firstPath, processId: 42);
        ForegroundApplicationSnapshot second = Snapshot(secondPath, processId: 43);
        using TestApplicationFixture fixture = new(temp.Path, first);

        fixture.Controller.AddManualProfile(second.ExecutablePath);
        fixture.Controller.Poll();
        fixture.Detector.Snapshot = second;

        Assert.IsTrue(fixture.Controller.CaptureInvokingApplication());
        Assert.AreSame(first, fixture.Controller.GetSnapshot().CandidateApplication);

        fixture.Controller.Poll();

        AllyApplicationSnapshot snapshot = fixture.Controller.GetSnapshot();
        Assert.AreEqual(
            second.ExecutablePath,
            snapshot.ActiveSession!.Profile.ExecutablePath);
        Assert.AreSame(first, snapshot.CandidateApplication);
        Assert.AreEqual(1, fixture.Runtime.StartCount);
    }

    [TestMethod]
    public void Candidate_IsClearedWhenItsProcessTerminates()
    {
        using TemporaryDirectory temp = new();
        string executablePath = Path.Combine(temp.Path, "Candidate.exe");
        File.WriteAllText(executablePath, string.Empty);
        ForegroundApplicationSnapshot candidate = Snapshot(executablePath);
        using TestApplicationFixture fixture = new(temp.Path, candidate);

        fixture.Controller.Poll();
        fixture.LifetimeChecker.Status = ProcessLifetimeStatus.Terminated;
        fixture.Detector.Snapshot = null;
        fixture.Controller.Poll();

        Assert.IsNull(fixture.Controller.GetSnapshot().CandidateApplication);
    }

    [TestMethod]
    public void Candidate_RejectsPidReuseByProcessStartTime()
    {
        using TemporaryDirectory temp = new();
        string firstPath = Path.Combine(temp.Path, "First.exe");
        string secondPath = Path.Combine(temp.Path, "Second.exe");
        File.WriteAllText(firstPath, string.Empty);
        File.WriteAllText(secondPath, string.Empty);
        DateTimeOffset firstStart = DateTimeOffset.UtcNow;
        ForegroundApplicationSnapshot first = Snapshot(
            firstPath,
            processId: 42,
            processStartTime: firstStart);
        ForegroundApplicationSnapshot replacement = Snapshot(
            secondPath,
            processId: 42,
            processStartTime: firstStart.AddSeconds(1));
        using TestApplicationFixture fixture = new(temp.Path, first);

        fixture.Controller.Poll();
        fixture.LifetimeChecker.Status = ProcessLifetimeStatus.Terminated;
        fixture.Detector.Snapshot = replacement;
        fixture.Controller.Poll();

        Assert.AreSame(
            replacement,
            fixture.Controller.GetSnapshot().CandidateApplication);
    }

    [TestMethod]
    public void ProfileCreatedFromCandidate_UsesCandidatePathAndMakesProfileAuthoritative()
    {
        using TemporaryDirectory temp = new();
        string executablePath = Path.Combine(temp.Path, "Candidate.exe");
        File.WriteAllText(executablePath, string.Empty);
        ForegroundApplicationSnapshot candidate = Snapshot(executablePath);
        using TestApplicationFixture fixture = new(temp.Path, candidate);

        fixture.Controller.Poll();
        GameProfile profile = fixture.Controller.CreateProfileForCurrentApplication(90);

        Assert.AreEqual(candidate.ExecutablePath, profile.ExecutablePath);
        AllyApplicationSnapshot snapshot = fixture.Controller.GetSnapshot();
        Assert.AreSame(candidate, snapshot.CandidateApplication);
        Assert.AreEqual(profile, fixture.Store.Current.GameProfiles.Single());
        Assert.IsNull(snapshot.ActiveSession);
        Assert.AreEqual(0, fixture.Runtime.StartCount);
    }

    [TestMethod]
    public void ManualProfile_IsPersistedWithoutLaunchingExecutable()
    {
        using TemporaryDirectory temp = new();
        string executablePath = Path.Combine(temp.Path, "Manual.exe");
        File.WriteAllText(executablePath, string.Empty);
        using TestApplicationFixture fixture = new(temp.Path, null);

        GameProfile profile = fixture.Controller.AddManualProfile(executablePath);

        Assert.AreEqual("Manual", profile.DisplayName);
        Assert.AreEqual(0, fixture.Runtime.StartCount);
        Assert.AreEqual(1, fixture.Store.Current.GameProfiles.Count);
    }

    [TestMethod]
    public void RemovingActiveProfile_RestoresBeforeRemovingIt()
    {
        using TemporaryDirectory temp = new();
        string executablePath = Path.Combine(temp.Path, "Active.exe");
        File.WriteAllText(executablePath, string.Empty);
        ForegroundApplicationSnapshot snapshot = Snapshot(executablePath);
        using TestApplicationFixture fixture = new(temp.Path, snapshot);
        fixture.Controller.AddManualProfile(executablePath);

        fixture.Controller.Poll();
        fixture.Controller.RemoveProfile(executablePath);

        Assert.AreEqual(1, fixture.Runtime.StartCount);
        Assert.AreEqual(1, fixture.Runtime.StopCount);
        Assert.AreEqual(0, fixture.Store.Current.GameProfiles.Count);
    }

    [TestMethod]
    public void InvokingApplicationProfile_UsesDisplayedValuesInOneAction()
    {
        using TemporaryDirectory temp = new();
        string executablePath = Path.Combine(temp.Path, "Invoked.exe");
        File.WriteAllText(executablePath, string.Empty);
        ForegroundApplicationSnapshot snapshot = Snapshot(executablePath);
        using TestApplicationFixture fixture = new(temp.Path, snapshot);

        Assert.IsTrue(fixture.Controller.CaptureInvokingApplication());
        GameProfile profile = fixture.Controller.CreateProfileForInvokingApplication(
            90,
            RestoreMode.Turbo,
            enabled: false);

        Assert.AreEqual(snapshot.ExecutablePath, profile.ExecutablePath);
        Assert.AreEqual(90, profile.TargetFps);
        Assert.AreEqual(RestoreMode.Turbo, profile.RestoreMode);
        Assert.IsFalse(profile.Enabled);
        Assert.AreEqual(0, fixture.Runtime.StartCount);
        Assert.AreEqual(1, fixture.Store.Current.GameProfiles.Count);
    }

    [TestMethod]
    public void StartWithWindows_PersistsConfigurationAndSynchronizesTask()
    {
        using TemporaryDirectory temp = new();
        var service = new FakeAutoStartService();
        using TestApplicationFixture fixture = new(temp.Path, null, service);

        AutoStartResult result = fixture.Controller.SetStartWithWindows(true);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(fixture.Store.Current.StartWithWindows);
        Assert.IsTrue(service.LastEnabled);
        Assert.IsFalse(string.IsNullOrWhiteSpace(service.LastExecutablePath));
    }

    [TestMethod]
    public void StartWithWindows_TaskSchedulerFailureIsNonFatal()
    {
        using TemporaryDirectory temp = new();
        var service = new FakeAutoStartService
        {
            Result = AutoStartResult.Failure("Task Scheduler unavailable.")
        };
        using TestApplicationFixture fixture = new(temp.Path, null, service);

        AutoStartResult result = fixture.Controller.SetStartWithWindows(true);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsFalse(fixture.Store.Current.StartWithWindows);
        Assert.AreEqual(
            "Task Scheduler unavailable.",
            fixture.Controller.GetSnapshot().AutoStartErrorMessage);
    }

    private static ForegroundApplicationSnapshot Snapshot(
        string executablePath,
        int processId = 42,
        DateTimeOffset? processStartTime = null) =>
        new(
            processId,
            processStartTime ?? DateTimeOffset.UtcNow,
            executablePath,
            Path.GetFileName(executablePath),
            Path.GetFileNameWithoutExtension(executablePath));

    private sealed class TestApplicationFixture : IDisposable
    {
        public TestApplicationFixture(
            string directory,
            ForegroundApplicationSnapshot? snapshot,
            IAutoStartService? autoStartService = null)
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
                autoStartService,
                LifetimeChecker);
        }

        public FakeForegroundDetector Detector { get; }
        public ConfigurationStore Store { get; }
        public FakeRuntime Runtime { get; }
        public AliveProcessChecker LifetimeChecker { get; }
        public AllyApplicationController Controller { get; }

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

        public GameProcessIdentity? LastIdentity { get; private set; }

        public ProcessLifetimeStatus Check(GameProcessIdentity identity) =>
            CheckCore(identity);

        private ProcessLifetimeStatus CheckCore(GameProcessIdentity identity)
        {
            LastIdentity = identity;
            return Status;
        }
    }

    private sealed class FakeRuntime : IAutoTdpRuntime
    {
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public AutoTdpRuntimeResult Start(GameProfile profile)
        {
            StartCount++;
            return AutoTdpRuntimeResult.Success();
        }

        public AutoTdpRuntimeResult StopAndRestore()
        {
            StopCount++;
            return AutoTdpRuntimeResult.Success();
        }

        public AutoTdpRuntimeSnapshot GetSnapshot() =>
            AutoTdpRuntimeSnapshot.Idle();
    }

    private sealed class FakeAutoStartService : IAutoStartService
    {
        public AutoStartResult Result { get; init; } = AutoStartResult.Success();

        public bool LastEnabled { get; private set; }

        public string? LastExecutablePath { get; private set; }

        public AutoStartResult Synchronize(bool enabled, string executablePath)
        {
            LastEnabled = enabled;
            LastExecutablePath = executablePath;
            return Result;
        }
    }
}
