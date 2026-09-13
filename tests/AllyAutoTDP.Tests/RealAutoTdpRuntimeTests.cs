using AllyAutoTDP.Application;
using AllyAutoTDP.Configuration;
using AllyAutoTDP.Hardware;
using AllyAutoTDP.Hardware.AMD;
using AllyAutoTDP.Power;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AllyAutoTDP.Tests;

[TestClass]
public sealed class RealAutoTdpRuntimeTests
{
    [TestMethod]
    public void StartAndStop_UsesGlobalLimitsAndRestoresBeforeDispose()
    {
        List<string> events = [];
        var factory = new FakeHardwareFactory(events, PowerSourceKind.Battery);
        var runtime = new RealAutoTdpRuntime(
            new AllyDetectionResult("RC71", true, true, null),
            () => new PowerLimits
            {
                BatteryMinTdp = 6,
                BatteryMaxTdp = 25,
                AcMinTdp = 6,
                AcMaxTdp = 30
            },
            hardwareFactory: factory);

        AutoTdpRuntimeResult start = runtime.Start(Profile());
        Assert.IsTrue(start.IsSuccess, start.ErrorMessage);
        Assert.IsTrue(factory.Adl.StartCount > 0);
        Assert.IsTrue(
            factory.Acpi.FirstTdpWrite.Wait(TimeSpan.FromSeconds(2)),
            "AutoTDP did not issue its initial A0 write.");
        Assert.IsTrue(
            SpinWait.SpinUntil(
                () => runtime.GetSnapshot().Status.HasValue,
                TimeSpan.FromSeconds(2)),
            "AutoTDP did not publish an initial status.");
        Assert.IsTrue(runtime.GetSnapshot().IsRunning);

        AutoTdpRuntimeResult stop = runtime.StopAndRestore();

        Assert.IsTrue(stop.IsSuccess, stop.ErrorMessage);
        int stopFpsIndex = events.IndexOf("StopFPS");
        int restoreIndex = events.IndexOf("RestoreMode");
        int acpiDisposeIndex = events.IndexOf("AcpiDispose");
        Assert.IsTrue(stopFpsIndex >= 0);
        Assert.IsTrue(restoreIndex > stopFpsIndex);
        Assert.IsTrue(acpiDisposeIndex > restoreIndex);
        Assert.IsTrue(factory.Adl.Disposed);
        Assert.IsTrue(factory.Acpi.Disposed);
    }

    [TestMethod]
    public void Start_UsesProfileTargetAndRestoreModeWithoutProfileTdp()
    {
        var factory = new FakeHardwareFactory([], PowerSourceKind.Ac);
        var runtime = new RealAutoTdpRuntime(
            new AllyDetectionResult("RC71", true, true, null),
            () => new PowerLimits
            {
                BatteryMinTdp = 6,
                BatteryMaxTdp = 25,
                AcMinTdp = 6,
                AcMaxTdp = 30
            },
            hardwareFactory: factory);

        AutoTdpRuntimeResult start = runtime.Start(new GameProfile
        {
            ExecutablePath = @"C:\Games\Game.exe",
            DisplayName = "Game",
            Enabled = true,
            TargetFps = 90,
            RestoreMode = RestoreMode.Turbo
        });

        Assert.IsTrue(start.IsSuccess, start.ErrorMessage);
        AutoTdpRuntimeSnapshot snapshot = runtime.GetSnapshot();
        Assert.IsTrue(snapshot.IsRunning);
        Assert.IsTrue(runtime.StopAndRestore().IsSuccess);
        Assert.IsTrue(factory.Acpi.Writes.Any(write =>
            write.DeviceId == AsusModeReapply.PerformanceModeEndpoint &&
            write.Value == (int)RestoreMode.Turbo));
    }

    private static GameProfile Profile() => new()
    {
        ExecutablePath = @"C:\Games\Game.exe",
        DisplayName = "Game",
        Enabled = true,
        TargetFps = 60,
        RestoreMode = RestoreMode.Balanced
    };

    private sealed class FakeHardwareFactory : IAutoTdpHardwareFactory
    {
        public FakeHardwareFactory(List<string> events, PowerSourceKind source)
        {
            Adl = new FakeAdl(events);
            Acpi = new FakeAcpi(events);
            PowerSource = new FakePowerSource(source);
        }

        public FakeAdl Adl { get; }
        public FakeAcpi Acpi { get; }
        public FakePowerSource PowerSource { get; }

        public bool TryCreateAdl(out IAmdAdl? adl, out string? errorMessage)
        {
            adl = Adl;
            errorMessage = null;
            return true;
        }

        public IAsusAcpi CreateAcpi() => Acpi;

        public IPowerSourceReader CreatePowerSource() => PowerSource;
    }

    private sealed class FakePowerSource : IPowerSourceReader
    {
        private readonly PowerSourceKind _source;

        public FakePowerSource(PowerSourceKind source) => _source = source;

        public PowerSourceReading Read() => new(_source, null);
    }

    private sealed class FakeAdl : IAmdAdl
    {
        private readonly List<string> _events;

        public FakeAdl(List<string> events) => _events = events;

        public int StartCount { get; private set; }
        public bool Disposed { get; private set; }
        public bool IsInitialized => !Disposed;

        public AdlOperationResult StartFrameMetrics()
        {
            StartCount++;
            _events.Add("StartFPS");
            return new(true, 0, null);
        }

        public AdlFpsResult GetFrameMetrics() => new(true, 60, 0, null);

        public AdlOperationResult StopFrameMetrics()
        {
            _events.Add("StopFPS");
            return new(true, 0, null);
        }

        public AdlPowerResult GetIgpuAsicPower() => new(true, 12, 0, null);

        public void Dispose()
        {
            Disposed = true;
            _events.Add("AdlDispose");
        }
    }

    private sealed class FakeAcpi : IAsusAcpi
    {
        private readonly List<string> _events;

        public FakeAcpi(List<string> events) => _events = events;

        public List<(uint DeviceId, int Value)> Writes { get; } = [];
        public ManualResetEventSlim FirstTdpWrite { get; } = new();
        public bool IsConnected => !Disposed;
        public string? OpenError => null;
        public bool Disposed { get; private set; }

        public AcpiReadResult DeviceGet(uint deviceId) =>
            new(true, 12, null, null);

        public AcpiWriteResult DeviceSet(uint deviceId, int value)
        {
            Writes.Add((deviceId, value));
            if (deviceId == AllyPowerEndpoints.A0)
                FirstTdpWrite.Set();
            if (deviceId == AsusModeReapply.PerformanceModeEndpoint)
                _events.Add("RestoreMode");
            return new(true, 1, null, null);
        }

        public void Dispose()
        {
            Disposed = true;
            _events.Add("AcpiDispose");
        }
    }
}
