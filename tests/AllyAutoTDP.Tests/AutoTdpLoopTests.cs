using AllyAutoTDP.AutoTdp;
using AllyAutoTDP.Application;
using AllyAutoTDP.Hardware;
using AllyAutoTDP.Hardware.AMD;
using AllyAutoTDP.Power;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AllyAutoTDP.Tests;

[TestClass]
public sealed class AutoTdpLoopTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void UnchangedTdp_IsNotWrittenEveryTick_ThenRefreshesAtTwoSeconds()
    {
        var fake = new FakeAdl();
        var writes = new List<int>();
        using AutoTdpLoop loop = CreateLoop(
            fake,
            PowerSourceKind.Ac,
            12,
            writes.Add);

        AutoTdpTickResult first = loop.ProcessSample(
            FpsReading.Valid(60),
            PowerReading.Valid(12),
            Ac(PowerSourceKind.Ac),
            Start);
        AutoTdpTickResult second = loop.ProcessSample(
            FpsReading.Valid(60),
            PowerReading.Valid(12),
            Ac(PowerSourceKind.Ac),
            Start.AddMilliseconds(300));
        AutoTdpTickResult refresh = loop.ProcessSample(
            FpsReading.Valid(60),
            PowerReading.Valid(12),
            Ac(PowerSourceKind.Ac),
            Start.AddMilliseconds(2000));

        Assert.IsTrue(first.WriteAttempted);
        Assert.IsFalse(second.WriteAttempted);
        Assert.IsTrue(refresh.WriteAttempted);
        Assert.IsTrue(refresh.RefreshWrite);
        Assert.AreEqual(2, writes.Count);
    }

    [TestMethod]
    public void SuccessZero_PreservesTdpAndContinuesRefresh()
    {
        var fake = new FakeAdl();
        var writes = new List<int>();
        using AutoTdpLoop loop = CreateLoop(
            fake,
            PowerSourceKind.Ac,
            12,
            writes.Add);

        AutoTdpTickResult first = loop.ProcessSample(
            FpsReading.Valid(60),
            PowerReading.Valid(12),
            Ac(PowerSourceKind.Ac),
            Start);
        AutoTdpTickResult paused = loop.ProcessSample(
            FpsReading.Invalid(0, "SUCCESS_ZERO"),
            PowerReading.Invalid(null, "FPS paused"),
            Ac(PowerSourceKind.Ac),
            Start.AddMilliseconds(300));
        AutoTdpTickResult refresh = loop.ProcessSample(
            FpsReading.Invalid(0, "SUCCESS_ZERO"),
            PowerReading.Invalid(null, "FPS paused"),
            Ac(PowerSourceKind.Ac),
            Start.AddMilliseconds(2000));

        Assert.IsTrue(first.WriteAttempted);
        Assert.IsFalse(paused.ShouldStop);
        Assert.IsFalse(paused.WriteAttempted);
        Assert.AreEqual(AutoTdpState.FpsInvalid, paused.Status.State);
        Assert.AreEqual(12, paused.Status.CurrentTdp);
        Assert.IsTrue(refresh.WriteAttempted);
        Assert.IsTrue(refresh.RefreshWrite);
        Assert.AreEqual(12, refresh.Status.CurrentTdp);
        Assert.AreEqual(2, writes.Count);
        Assert.AreEqual(12, writes[^1]);
    }

    [TestMethod]
    public void CalculatedTdpChange_IsWrittenImmediately()
    {
        var fake = new FakeAdl();
        var writes = new List<int>();
        using AutoTdpLoop loop = CreateLoop(
            fake,
            PowerSourceKind.Ac,
            12,
            writes.Add);

        for (int index = 0; index < 8; index++)
        {
            loop.ProcessSample(
                FpsReading.Valid(60),
                PowerReading.Valid(1),
                Ac(PowerSourceKind.Ac),
                Start.AddMilliseconds(index * 300));
        }

        int writesBeforeIncrease = writes.Count;
        AutoTdpTickResult result = loop.ProcessSample(
            FpsReading.Valid(54),
            PowerReading.Valid(1),
            Ac(PowerSourceKind.Ac),
            Start.AddMilliseconds(2400));

        Assert.IsTrue(result.WriteAttempted);
        Assert.AreEqual(writesBeforeIncrease + 1, writes.Count);
        Assert.AreEqual(12, writes[^1]);
    }

    [TestMethod]
    public void AcToBattery_ClampsAboveTwentyFiveAndWritesImmediately()
    {
        var fake = new FakeAdl();
        var writes = new List<int>();
        using AutoTdpLoop loop = CreateLoop(
            fake,
            PowerSourceKind.Ac,
            30,
            writes.Add);

        for (int index = 0; index < 10; index++)
        {
            loop.ProcessSample(
                FpsReading.Valid(60),
                PowerReading.Valid(1),
                Ac(PowerSourceKind.Ac),
                Start.AddMilliseconds(index * 300));
        }

        Assert.AreEqual(28, writes[^1]);
        AutoTdpTickResult result = loop.ProcessSample(
            FpsReading.Valid(60),
            PowerReading.Valid(1),
            Ac(PowerSourceKind.Battery),
            Start.AddMilliseconds(3000));

        Assert.IsTrue(result.WriteAttempted);
        Assert.AreEqual(25, writes[^1]);
        Assert.AreEqual(25, result.Status.CurrentTdp);
        Assert.AreEqual(25, result.Status.EffectiveMax);
    }

    [TestMethod]
    public void AcToBattery_WhenAlreadyAtTwentyFive_DoesNotWriteOnlyForSourceChange()
    {
        var fake = new FakeAdl();
        var writes = new List<int>();
        using AutoTdpLoop loop = CreateLoop(
            fake,
            PowerSourceKind.Ac,
            25,
            writes.Add);

        loop.ProcessSample(
            FpsReading.Valid(60),
            PowerReading.Valid(12),
            Ac(PowerSourceKind.Ac),
            Start);
        AutoTdpTickResult result = loop.ProcessSample(
            FpsReading.Valid(60),
            PowerReading.Valid(12),
            Ac(PowerSourceKind.Battery),
            Start.AddMilliseconds(300));

        Assert.IsFalse(result.WriteAttempted);
        Assert.AreEqual(1, writes.Count);
        Assert.AreEqual(25, result.Status.CurrentTdp);
    }

    [TestMethod]
    public void BatteryToAc_RaisesEffectiveMaximumWithoutAutomaticIncrease()
    {
        var fake = new FakeAdl();
        var writes = new List<int>();
        using AutoTdpLoop loop = CreateLoop(
            fake,
            PowerSourceKind.Battery,
            null,
            writes.Add);

        loop.ProcessSample(
            FpsReading.Valid(60),
            PowerReading.Valid(12),
            Ac(PowerSourceKind.Battery),
            Start);
        AutoTdpTickResult result = loop.ProcessSample(
            FpsReading.Valid(60),
            PowerReading.Valid(12),
            Ac(PowerSourceKind.Ac),
            Start.AddMilliseconds(300));

        Assert.IsFalse(result.WriteAttempted);
        Assert.AreEqual(1, writes.Count);
        Assert.AreEqual(25, result.Status.CurrentTdp);
        Assert.AreEqual(30, result.Status.EffectiveMax);
    }

    [TestMethod]
    public void UnknownPowerSource_UsesTwentyFiveWattSafetyCap()
    {
        var fake = new FakeAdl();
        var writes = new List<int>();
        using AutoTdpLoop loop = CreateLoop(
            fake,
            PowerSourceKind.Ac,
            30,
            writes.Add);

        loop.ProcessSample(
            FpsReading.Valid(60),
            PowerReading.Valid(12),
            Ac(PowerSourceKind.Ac),
            Start);
        AutoTdpTickResult result = loop.ProcessSample(
            FpsReading.Valid(60),
            PowerReading.Valid(12),
            new(PowerSourceKind.Unknown, "source unknown"),
            Start.AddMilliseconds(300));

        Assert.IsTrue(result.WriteAttempted);
        Assert.AreEqual(25, result.Status.CurrentTdp);
        Assert.AreEqual(25, result.Status.EffectiveMax);
        Assert.AreEqual(25, writes[^1]);
    }

    [TestMethod]
    public void FpsTimeout_StopsFrameMetricsBeforeRestoreMode()
    {
        var fakeAdl = new FakeAdl
        {
            FrameResult = new(false, 0, 17, "FPS unavailable")
        };
        var fakeAcpi = new FakeAcpi(fakeAdl);
        var controller = new AllyPowerController(
            fakeAcpi,
            writeAuthorized: true,
            restoreMode: RestoreMode.Balanced);
        var options = CreateOptions(PowerSourceKind.Ac, 12);
        var autoController = new AutoTdpController(
            60,
            new TdpRange(6, 12),
            TimeSpan.FromMilliseconds(100));
        using var fps = new AmdFrameMetricsReader(fakeAdl);
        using var power = new AmdPowerReader(fakeAdl);
        using var loop = new AutoTdpLoop(
            autoController,
            options,
            fps,
            power,
            new FakePowerSource(PowerSourceKind.Ac),
            controller.SetFixedTdp,
            controller.Restore);

        loop.Start();
        Assert.IsTrue(
            SpinWait.SpinUntil(
                () => !loop.IsRunning,
                TimeSpan.FromSeconds(2)));

        Assert.IsNotNull(loop.LastRestoreResult);
        Assert.IsTrue(loop.LastRestoreResult!.IsComplete);
        Assert.AreEqual(1, fakeAdl.StopCount);
        Assert.IsTrue(
            fakeAcpi.Writes.Any(
                write => write.DeviceId == AsusModeReapply.PerformanceModeEndpoint));
        Assert.IsFalse(fakeAcpi.RestoreBeforeMetricsStop);
    }

    [TestMethod]
    public void WriteError_StopsLoopAndRequestsRestore()
    {
        var fakeAdl = new FakeAdl();
        int restoreCalls = 0;
        using var fps = new AmdFrameMetricsReader(fakeAdl);
        using var power = new AmdPowerReader(fakeAdl);
        var options = CreateOptions(PowerSourceKind.Ac, 12);
        var controller = new AutoTdpController(60, new TdpRange(6, 12));
        using var loop = new AutoTdpLoop(
            controller,
            options,
            fps,
            power,
            new FakePowerSource(PowerSourceKind.Ac),
            _ => FailedWrite(),
            () =>
            {
                restoreCalls++;
                return RestoreSuccess();
            });

        loop.Start();
        Assert.IsTrue(
            SpinWait.SpinUntil(
                () => !loop.IsRunning,
                TimeSpan.FromSeconds(2)));

        Assert.AreEqual(1, restoreCalls);
        Assert.AreEqual(1, fakeAdl.StopCount);
    }

    [TestMethod]
    public void Quit_StopsLoopAndStopFpsPrecedesRestoreMode()
    {
        var fakeAdl = new FakeAdl
        {
            FrameResult = new(true, 60, 0, null)
        };
        var fakeAcpi = new FakeAcpi(fakeAdl);
        var controller = new AllyPowerController(
            fakeAcpi,
            writeAuthorized: true,
            restoreMode: RestoreMode.Balanced);
        var options = CreateOptions(PowerSourceKind.Ac, 12);
        var autoController = new AutoTdpController(60, new TdpRange(6, 12));
        using var fps = new AmdFrameMetricsReader(fakeAdl);
        using var power = new AmdPowerReader(fakeAdl);
        using var loop = new AutoTdpLoop(
            autoController,
            options,
            fps,
            power,
            new FakePowerSource(PowerSourceKind.Ac),
            controller.SetFixedTdp,
            controller.Restore);
        using var session = new RuntimeSession(controller, loop);

        Assert.IsTrue(
            SpinWait.SpinUntil(
                () => fakeAdl.StartCount > 0,
                TimeSpan.FromSeconds(2)));
        RestoreResult result = session.Quit();

        Assert.IsTrue(result.IsComplete);
        Assert.AreEqual(1, fakeAdl.StopCount);
        Assert.IsFalse(fakeAcpi.RestoreBeforeMetricsStop);
        Assert.AreEqual(
            AsusModeReapply.PerformanceModeEndpoint,
            fakeAcpi.Writes[^1].DeviceId);
    }

    private static AutoTdpLoop CreateLoop(
        FakeAdl fake,
        PowerSourceKind source,
        int? max,
        Action<int> onWrite)
    {
        AutoTdpOptions options = CreateOptions(source, max);
        TdpRange range = options.GetEffectiveRange(source);
        var controller = new AutoTdpController(options.TargetFps, range);
        var fps = new AmdFrameMetricsReader(fake);
        var power = new AmdPowerReader(fake);
        return new AutoTdpLoop(
            controller,
            options,
            fps,
            power,
            new FakePowerSource(source),
            watts =>
            {
                onWrite(watts);
                return SuccessWrite(watts);
            },
            RestoreSuccess,
            null);
    }

    private static AutoTdpOptions CreateOptions(
        PowerSourceKind source,
        int? max) =>
        new(
            RestoreMode.Balanced,
            60,
            currentSource => currentSource == PowerSourceKind.Ac
                ? new TdpRange(6, max ?? 30)
                : new TdpRange(6, 25),
            source);

    private static PowerSourceReading Ac(PowerSourceKind source) =>
        new(source, source == PowerSourceKind.Unknown ? "unknown" : null);

    private static TdpWriteResult SuccessWrite(int watts) =>
        new(
            true,
            null,
            [
                new EndpointWriteResult("A0", watts, 1, true, false, null),
                new EndpointWriteResult("A3", watts, 1, true, false, null),
                new EndpointWriteResult("C1", watts, 1, true, false, null)
            ]);

    private static TdpWriteResult FailedWrite() =>
        new(
            true,
            null,
            [new EndpointWriteResult("A0", 12, 7, false, false, "failed")]);

    private static RestoreResult RestoreSuccess() =>
        new(new AsusModeReapplyResult(RestoreMode.Balanced, true, 1, null));

    private sealed class FakePowerSource : IPowerSourceReader
    {
        private readonly PowerSourceKind _source;

        public FakePowerSource(PowerSourceKind source)
        {
            _source = source;
        }

        public PowerSourceReading Read() => Ac(_source);
    }

    private sealed class FakeAdl : IAmdAdl
    {
        public AdlFpsResult FrameResult { get; set; } =
            new(true, 60, 0, null);
        public AdlPowerResult PowerResult { get; set; } =
            new(true, 12, 0, null);
        public bool IsInitialized => true;
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public bool MetricsStopped { get; private set; }

        public AdlOperationResult StartFrameMetrics()
        {
            StartCount++;
            return new(true, 0, null);
        }

        public AdlFpsResult GetFrameMetrics() => FrameResult;

        public AdlOperationResult StopFrameMetrics()
        {
            StopCount++;
            MetricsStopped = true;
            return new(true, 0, null);
        }

        public AdlPowerResult GetIgpuAsicPower() => PowerResult;

        public void Dispose()
        {
        }
    }

    private sealed class FakeAcpi : IAsusAcpi
    {
        private readonly FakeAdl _adl;

        public FakeAcpi(FakeAdl adl)
        {
            _adl = adl;
        }

        public List<(uint DeviceId, int Value)> Writes { get; } = [];
        public bool IsConnected => true;
        public string? OpenError => null;
        public bool RestoreBeforeMetricsStop { get; private set; }

        public AcpiReadResult DeviceGet(uint deviceId) =>
            new(true, 0, null, null);

        public AcpiWriteResult DeviceSet(uint deviceId, int value)
        {
            if (deviceId == AsusModeReapply.PerformanceModeEndpoint &&
                !_adl.MetricsStopped)
            {
                RestoreBeforeMetricsStop = true;
            }

            Writes.Add((deviceId, value));
            return new(true, 1, null, null);
        }

        public void Dispose()
        {
        }
    }
}
