using AllyAutoTDP.Application;
using AllyAutoTDP.AutoTdp;
using AllyAutoTDP.Configuration;
using AllyAutoTDP.Power;
using AllyAutoTDP.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AllyAutoTDP.Tests;

[TestClass]
public sealed class GameSessionManagerTests
{
    [TestMethod]
    public void ApplicationWithoutProfile_DoesNotStartRuntime()
    {
        FakeProfileLookup profiles = new();
        FakeRuntime runtime = new();
        using GameSessionManager manager = CreateManager(profiles, runtime);

        manager.ObserveForeground(Snapshot(@"C:\Games\Unknown.exe", 10));

        Assert.AreEqual(0, runtime.StartCount);
        Assert.AreEqual(GameSessionState.Ready, manager.State);
    }

    [TestMethod]
    public void DisabledProfile_DoesNotStartRuntime()
    {
        FakeProfileLookup profiles = new();
        GameProfile profile = Profile(@"C:\Games\GameA.exe");
        profile.Enabled = false;
        profiles.Add(profile);
        FakeRuntime runtime = new();
        using GameSessionManager manager = CreateManager(profiles, runtime);

        manager.ObserveForeground(Snapshot(profile.ExecutablePath, 10));

        Assert.AreEqual(0, runtime.StartCount);
    }

    [TestMethod]
    public void AutoTdpDisabled_DoesNotStartRuntime()
    {
        FakeProfileLookup profiles = new();
        GameProfile profile = Profile(@"C:\Games\GameA.exe");
        profiles.Add(profile);
        FakeRuntime runtime = new();
        using GameSessionManager manager = CreateManager(
            profiles,
            runtime,
            autoTdpEnabled: false);

        manager.ObserveForeground(Snapshot(profile.ExecutablePath, 10));

        Assert.AreEqual(0, runtime.StartCount);
        Assert.AreEqual(GameSessionState.Disabled, manager.State);
    }

    [TestMethod]
    public void KnownProfile_StartsOnlyOnceForSameProcessInstance()
    {
        FakeProfileLookup profiles = new();
        GameProfile profile = Profile(@"C:\Games\GameA.exe");
        profiles.Add(profile);
        FakeRuntime runtime = new();
        using GameSessionManager manager = CreateManager(profiles, runtime);
        ForegroundApplicationSnapshot observation = Snapshot(profile.ExecutablePath, 10);

        manager.ObserveForeground(observation);
        manager.ObserveForeground(observation);

        Assert.AreEqual(1, runtime.StartCount);
        Assert.AreEqual(GameSessionState.Active, manager.State);
        Assert.AreEqual(10, manager.ActiveSession!.ProcessIdentity.ProcessId);
    }

    [TestMethod]
    public void AltTabToUnknownApplication_PreservesActiveSession()
    {
        FakeProfileLookup profiles = new();
        GameProfile profile = Profile(@"C:\Games\GameA.exe");
        profiles.Add(profile);
        FakeRuntime runtime = new();
        using GameSessionManager manager = CreateManager(profiles, runtime);

        manager.ObserveForeground(Snapshot(profile.ExecutablePath, 10));
        manager.ObserveForeground(Snapshot(@"C:\Windows\explorer.exe", 11));

        Assert.AreEqual(1, runtime.StartCount);
        Assert.AreEqual(0, runtime.StopCount);
        Assert.IsNotNull(manager.ActiveSession);
        Assert.AreEqual(GameSessionState.Active, manager.State);
    }

    [TestMethod]
    public void TerminatedOrRecycledProcess_StopsAndRestores()
    {
        FakeProfileLookup profiles = new();
        GameProfile profile = Profile(@"C:\Games\GameA.exe");
        profiles.Add(profile);
        FakeRuntime runtime = new();
        FakeProcessLifetimeChecker lifetime = new();
        using GameSessionManager manager = CreateManager(
            profiles,
            runtime,
            lifetime);

        manager.ObserveForeground(Snapshot(profile.ExecutablePath, 10));
        lifetime.Status = ProcessLifetimeStatus.Terminated;
        manager.CheckActiveProcess();

        Assert.AreEqual(1, runtime.StopCount);
        Assert.IsNull(manager.ActiveSession);
        Assert.AreEqual(GameSessionState.Ready, manager.State);
    }

    [TestMethod]
    public void GameBObservedOnce_DoesNotTransition()
    {
        FakeProfileLookup profiles = AddTwoProfiles();
        FakeRuntime runtime = new();
        using GameSessionManager manager = CreateManager(profiles, runtime);
        ForegroundApplicationSnapshot gameA = Snapshot(@"C:\Games\GameA.exe", 10);
        ForegroundApplicationSnapshot gameB = Snapshot(@"C:\Games\GameB.exe", 20);

        manager.ObserveForeground(gameA);
        manager.ObserveForeground(gameB);

        Assert.AreEqual(1, runtime.StartCount);
        Assert.AreEqual(0, runtime.StopCount);
        Assert.AreEqual(gameA.ProcessId, manager.ActiveSession!.ProcessIdentity.ProcessId);
    }

    [TestMethod]
    public void GameBObservedTwice_RestoresAThenStartsB()
    {
        FakeProfileLookup profiles = AddTwoProfiles();
        FakeRuntime runtime = new();
        using GameSessionManager manager = CreateManager(profiles, runtime);
        ForegroundApplicationSnapshot gameA = Snapshot(@"C:\Games\GameA.exe", 10);
        ForegroundApplicationSnapshot gameB = Snapshot(@"C:\Games\GameB.exe", 20);

        manager.ObserveForeground(gameA);
        manager.ObserveForeground(gameB);
        manager.ObserveForeground(gameB);

        Assert.AreEqual(2, runtime.StartCount);
        Assert.AreEqual(1, runtime.StopCount);
        Assert.AreEqual(gameB.ProcessId, manager.ActiveSession!.ProcessIdentity.ProcessId);
        Assert.AreEqual(GameSessionState.Active, manager.State);
    }

    [TestMethod]
    public void GameARestoreFailure_PreventsGameBStartAndSetsError()
    {
        FakeProfileLookup profiles = AddTwoProfiles();
        FakeRuntime runtime = new()
        {
            StopResult = AutoTdpRuntimeResult.Failure("restore failed"),
        };
        using GameSessionManager manager = CreateManager(profiles, runtime);

        ForegroundApplicationSnapshot gameA = Snapshot(@"C:\Games\GameA.exe", 10);
        ForegroundApplicationSnapshot gameB = Snapshot(@"C:\Games\GameB.exe", 20);
        manager.ObserveForeground(gameA);
        manager.ObserveForeground(gameB);
        manager.ObserveForeground(gameB);

        Assert.AreEqual(1, runtime.StartCount);
        Assert.AreEqual(1, runtime.StopCount);
        Assert.AreEqual(GameSessionState.Error, manager.State);
        Assert.AreEqual(10, manager.ActiveSession!.ProcessIdentity.ProcessId);
    }

    [TestMethod]
    public async Task ConcurrentTransitions_AreSerialized()
    {
        FakeProfileLookup profiles = AddTwoProfiles();
        FakeRuntime runtime = new() { DelayMilliseconds = 40 };
        using GameSessionManager manager = CreateManager(profiles, runtime);
        ForegroundApplicationSnapshot gameA = Snapshot(@"C:\Games\GameA.exe", 10);
        ForegroundApplicationSnapshot gameB = Snapshot(@"C:\Games\GameB.exe", 20);
        manager.ObserveForeground(gameA);

        Task first = Task.Run(() => manager.ObserveForeground(gameB));
        Task second = Task.Run(() => manager.ObserveForeground(gameB));
        await Task.WhenAll(first, second);

        Assert.AreEqual(2, runtime.StartCount);
        Assert.AreEqual(1, runtime.StopCount);
        Assert.AreEqual(1, runtime.MaxConcurrentCalls);
    }

    [TestMethod]
    public void DisablingAutoTdp_RestoresActiveSession()
    {
        FakeProfileLookup profiles = new();
        GameProfile profile = Profile(@"C:\Games\GameA.exe");
        profiles.Add(profile);
        FakeRuntime runtime = new();
        using GameSessionManager manager = CreateManager(profiles, runtime);

        manager.ObserveForeground(Snapshot(profile.ExecutablePath, 10));
        manager.SetAutoTdpEnabled(false);

        Assert.AreEqual(1, runtime.StopCount);
        Assert.IsFalse(manager.AutoTdpEnabled);
        Assert.IsNull(manager.ActiveSession);
        Assert.AreEqual(GameSessionState.Disabled, manager.State);
    }

    private static GameSessionManager CreateManager(
        FakeProfileLookup profiles,
        FakeRuntime runtime,
        FakeProcessLifetimeChecker? lifetime = null,
        bool autoTdpEnabled = true) =>
        new(
            profiles,
            runtime,
            lifetime ?? new FakeProcessLifetimeChecker(),
            autoTdpEnabled);

    private static FakeProfileLookup AddTwoProfiles()
    {
        FakeProfileLookup profiles = new();
        profiles.Add(Profile(@"C:\Games\GameA.exe"));
        profiles.Add(Profile(@"C:\Games\GameB.exe"));
        return profiles;
    }

    private static GameProfile Profile(string executablePath) => new()
    {
        ExecutablePath = ExecutablePathIdentity.Normalize(executablePath),
        DisplayName = Path.GetFileNameWithoutExtension(executablePath),
        Enabled = true,
        TargetFps = 60,
        RestoreMode = RestoreMode.Balanced,
    };

    private static ForegroundApplicationSnapshot Snapshot(
        string executablePath,
        int processId) =>
        new(
            processId,
            DateTimeOffset.UtcNow,
            executablePath,
            Path.GetFileName(executablePath),
            Path.GetFileNameWithoutExtension(executablePath));

    private sealed class FakeProfileLookup : IGameProfileLookup
    {
        private readonly Dictionary<string, GameProfile> _profiles = new(
            StringComparer.OrdinalIgnoreCase);

        public void Add(GameProfile profile) =>
            _profiles[profile.ExecutablePath] = profile;

        public GameProfile? FindByExecutablePath(string executablePath)
        {
            if (!ExecutablePathIdentity.TryNormalize(
                    executablePath,
                    out string normalizedPath,
                    out _))
            {
                return null;
            }

            return _profiles.TryGetValue(normalizedPath, out GameProfile? profile)
                ? profile
                : null;
        }
    }

    private sealed class FakeRuntime : IAutoTdpRuntime
    {
        private int _currentCalls;
        private int _maxConcurrentCalls;

        public List<GameProfile> StartedProfiles { get; } = [];

        public int DelayMilliseconds { get; init; }

        public AutoTdpRuntimeResult StartResult { get; init; } =
            AutoTdpRuntimeResult.Success();

        public AutoTdpRuntimeResult StopResult { get; init; } =
            AutoTdpRuntimeResult.Success();

        public int StartCount => StartedProfiles.Count;

        public int StopCount { get; private set; }

        public int MaxConcurrentCalls => _maxConcurrentCalls;

        public AutoTdpRuntimeResult Start(GameProfile profile)
        {
            EnterRuntime();
            try
            {
                if (DelayMilliseconds > 0)
                {
                    Thread.Sleep(DelayMilliseconds);
                }

                StartedProfiles.Add(profile);
                return StartResult;
            }
            finally
            {
                ExitRuntime();
            }
        }

        public AutoTdpRuntimeResult StopAndRestore()
        {
            EnterRuntime();
            try
            {
                if (DelayMilliseconds > 0)
                {
                    Thread.Sleep(DelayMilliseconds);
                }

                StopCount++;
                return StopResult;
            }
            finally
            {
                ExitRuntime();
            }
        }

        public AutoTdpRuntimeSnapshot GetSnapshot() =>
            AutoTdpRuntimeSnapshot.Idle();

        private void EnterRuntime()
        {
            int current = Interlocked.Increment(ref _currentCalls);
            int observedMaximum;
            do
            {
                observedMaximum = _maxConcurrentCalls;
                if (current <= observedMaximum)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(
                       ref _maxConcurrentCalls,
                       current,
                       observedMaximum) != observedMaximum);
        }

        private void ExitRuntime()
        {
            Interlocked.Decrement(ref _currentCalls);
        }
    }

    private sealed class FakeProcessLifetimeChecker : IProcessLifetimeChecker
    {
        public ProcessLifetimeStatus Status { get; set; } = ProcessLifetimeStatus.Alive;

        public ProcessLifetimeStatus Check(GameProcessIdentity identity) => Status;
    }
}
