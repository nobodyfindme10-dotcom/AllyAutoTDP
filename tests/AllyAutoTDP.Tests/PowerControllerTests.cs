using Microsoft.VisualStudio.TestTools.UnitTesting;

using AllyAutoTDP.Application;
using AllyAutoTDP.Hardware;
using AllyAutoTDP.Power;

namespace AllyAutoTDP.Tests;

[TestClass]
public sealed class PowerControllerTests
{
    [TestMethod]
    public void FixedTdp_WritesA0ThenA3ThenC1()
    {
        var fake = new FakeAcpi();
        var controller = CreateController(fake);

        TdpWriteResult result = controller.SetFixedTdp(15);

        Assert.IsTrue(result.AllSucceeded);
        CollectionAssert.AreEqual(
            new[] { AllyPowerEndpoints.A0, AllyPowerEndpoints.A3, AllyPowerEndpoints.C1 },
            fake.Writes.Select(write => write.DeviceId).ToArray());
        CollectionAssert.AreEqual(
            new[] { 15, 15, 15 },
            fake.Writes.Select(write => write.Value).ToArray());
        Assert.AreEqual(0, fake.ReadCount);
    }

    [TestMethod]
    public void MissingRestoreMode_BlocksPowerWritesWithoutAcpiCalls()
    {
        var fake = new FakeAcpi();
        var controller = new AllyPowerController(fake, writeAuthorized: true);

        TdpWriteResult result = controller.SetFixedTdp(15);

        Assert.IsFalse(result.Accepted);
        StringAssert.Contains(result.RejectionReason ?? string.Empty, "restore-mode");
        Assert.AreEqual(0, fake.Writes.Count);
        Assert.AreEqual(0, fake.ReadCount);
    }

    [TestMethod]
    public void UnauthorizedWrite_IsBlockedWithoutAcpiCalls()
    {
        var fake = new FakeAcpi();
        var controller = new AllyPowerController(
            fake,
            writeAuthorized: false,
            restoreMode: RestoreMode.Balanced);

        TdpWriteResult result = controller.SetFixedTdp(15);

        Assert.IsFalse(result.Accepted);
        Assert.AreEqual(0, fake.Writes.Count);
    }

    [TestMethod]
    public void DisconnectedAcpi_IsBlockedWithoutAcpiCalls()
    {
        var fake = new FakeAcpi { IsConnected = false };
        var controller = CreateController(fake);

        TdpWriteResult result = controller.SetFixedTdp(15);

        Assert.IsFalse(result.Accepted);
        Assert.AreEqual(0, fake.Writes.Count);
    }

    [TestMethod]
    public void TdpOutsideFiveToThirty_IsRejectedWithoutAcpiCalls()
    {
        var fake = new FakeAcpi();
        var controller = CreateController(fake);

        Assert.IsFalse(controller.SetFixedTdp(4).Accepted);
        Assert.IsFalse(controller.SetFixedTdp(31).Accepted);
        Assert.AreEqual(0, fake.Writes.Count);
    }

    [TestMethod]
    public void ZeroReadback_DoesNotBlockFixedTdpWrite()
    {
        var fake = new FakeAcpi();
        var controller = CreateController(fake);

        PowerReadback readback = controller.ReadExperimental();
        TdpWriteResult result = controller.SetFixedTdp(15);

        Assert.AreEqual(0, readback.A0);
        Assert.AreEqual(0, readback.A3);
        Assert.AreEqual(0, readback.C1);
        Assert.IsTrue(result.AllSucceeded);
        Assert.AreEqual(3, fake.ReadCount);
        Assert.AreEqual(3, fake.Writes.Count);
    }

    [TestMethod]
    public void A0Failure_StopsSequenceAndReappliesRestoreMode()
    {
        var fake = new FakeAcpi();
        fake.SetOutcomes(AllyPowerEndpoints.A0, Failure("A0 failed"));
        var controller = CreateController(fake, RestoreMode.Balanced);

        TdpWriteResult result = controller.SetFixedTdp(15);

        Assert.IsFalse(result.AllSucceeded);
        Assert.IsTrue(result.RecoveryResult!.IsSuccess);
        CollectionAssert.AreEqual(
            new[] { AllyPowerEndpoints.A0, AsusModeReapply.PerformanceModeEndpoint },
            fake.Writes.Select(write => write.DeviceId).ToArray());
    }

    [TestMethod]
    public void A3Failure_DoesNotWriteC1AndReappliesRestoreMode()
    {
        var fake = new FakeAcpi();
        fake.SetOutcomes(AllyPowerEndpoints.A3, Failure("A3 failed"));
        var controller = CreateController(fake, RestoreMode.Silent);

        TdpWriteResult result = controller.SetFixedTdp(15);

        Assert.IsTrue(result.RecoveryResult!.IsSuccess);
        CollectionAssert.AreEqual(
            new[]
            {
                AllyPowerEndpoints.A0,
                AllyPowerEndpoints.A3,
                AsusModeReapply.PerformanceModeEndpoint
            },
            fake.Writes.Select(write => write.DeviceId).ToArray());
        Assert.IsFalse(fake.Writes.Any(write => write.DeviceId == AllyPowerEndpoints.C1));
    }

    [TestMethod]
    public void C1Failure_ReappliesRestoreModeWithoutRollback()
    {
        var fake = new FakeAcpi();
        fake.SetOutcomes(AllyPowerEndpoints.C1, Failure("C1 failed"));
        var controller = CreateController(fake, RestoreMode.Turbo);

        TdpWriteResult result = controller.SetFixedTdp(15);

        Assert.IsTrue(result.RecoveryResult!.IsSuccess);
        CollectionAssert.AreEqual(
            new[]
            {
                AllyPowerEndpoints.A0,
                AllyPowerEndpoints.A3,
                AllyPowerEndpoints.C1,
                AsusModeReapply.PerformanceModeEndpoint
            },
            fake.Writes.Select(write => write.DeviceId).ToArray());
    }

    [TestMethod]
    public void RecoveryFailure_FaultsControllerAndBlocksNewTdpWrites()
    {
        var fake = new FakeAcpi();
        fake.SetOutcomes(AllyPowerEndpoints.A3, Failure("A3 failed"));
        fake.SetOutcomes(AsusModeReapply.PerformanceModeEndpoint, Failure("mode failed"));
        var controller = CreateController(fake);

        TdpWriteResult failed = controller.SetFixedTdp(15);
        int writesAfterFailure = fake.Writes.Count;
        TdpWriteResult blocked = controller.SetFixedTdp(16);

        Assert.IsFalse(failed.RecoveryResult!.IsSuccess);
        Assert.AreEqual(PowerControllerState.Faulted, controller.State);
        Assert.IsFalse(blocked.Accepted);
        Assert.AreEqual(writesAfterFailure, fake.Writes.Count);
    }

    [TestMethod]
    public void Restore_ReappliesOnlyPerformanceModeWithoutReadback()
    {
        var fake = new FakeAcpi();
        var controller = CreateController(fake, RestoreMode.Turbo);

        RestoreResult result = controller.Restore();

        Assert.IsTrue(result.IsComplete);
        Assert.AreEqual(RestoreMode.Turbo, result.ModeResult!.Mode);
        Assert.AreEqual(0, fake.ReadCount);
        CollectionAssert.AreEqual(
            new[] { (AsusModeReapply.PerformanceModeEndpoint, 1) },
            fake.Writes.ToArray());
    }

    [TestMethod]
    public void ManualRestore_StopsAndJoinsWatchBeforeReapply()
    {
        var fake = new FakeAcpi { ReadDelayMilliseconds = 5 };
        using var sampleReceived = new ManualResetEventSlim();
        fake.ReadSampleCompleted = sampleReceived;
        var controller = CreateController(fake);
        using var session = new RuntimeSession(controller, new PowerWatch(controller));

        session.ToggleWatch(_ => { });
        Assert.IsTrue(sampleReceived.Wait(TimeSpan.FromSeconds(2)));

        RestoreResult result = session.Restore();

        Assert.IsTrue(result.IsComplete);
        Assert.IsFalse(session.WatchRunning);
        Assert.IsFalse(fake.WriteBeforeWatchEnded);
        CollectionAssert.AreEqual(
            new[] { AsusModeReapply.PerformanceModeEndpoint },
            fake.Writes.Select(write => write.DeviceId).ToArray());
    }

    [TestMethod]
    public void Quit_DoesNotCloseUntilRestoreModeReapplySucceeds()
    {
        var fake = new FakeAcpi();
        fake.SetOutcomes(
            AsusModeReapply.PerformanceModeEndpoint,
            Failure("temporary mode failure"),
            Success());
        var controller = CreateController(fake);
        using var session = new RuntimeSession(controller, new PowerWatch(controller));

        RestoreResult first = session.Quit();

        Assert.IsFalse(first.IsComplete);
        Assert.IsFalse(session.QuitRequested);
        Assert.AreEqual(RuntimeSessionState.ShutdownPending, session.State);
        Assert.AreEqual(PowerControllerState.ShutdownPending, controller.State);

        RestoreResult second = session.Quit();

        Assert.IsTrue(second.IsComplete);
        Assert.IsTrue(session.QuitRequested);
        Assert.AreEqual(RuntimeSessionState.Closed, session.State);
        Assert.AreEqual(PowerControllerState.Closed, controller.State);
        CollectionAssert.AreEqual(
            new[]
            {
                AsusModeReapply.PerformanceModeEndpoint,
                AsusModeReapply.PerformanceModeEndpoint
            },
            fake.Writes.Select(write => write.DeviceId).ToArray());
    }

    [TestMethod]
    public void ClosedController_RejectsRestoreAndTdpWithoutAcpiCalls()
    {
        var fake = new FakeAcpi();
        var controller = CreateController(fake);
        controller.CompleteShutdown();

        TdpWriteResult write = controller.SetFixedTdp(15);
        RestoreResult restore = controller.Restore();

        Assert.IsFalse(write.Accepted);
        Assert.IsFalse(restore.IsComplete);
        Assert.AreEqual(0, fake.Writes.Count);
        Assert.AreEqual(PowerControllerState.Closed, controller.State);
    }

    private static AllyPowerController CreateController(
        FakeAcpi fake,
        RestoreMode mode = RestoreMode.Balanced) =>
        new(fake, writeAuthorized: true, restoreMode: mode);

    private static AcpiWriteResult Success() => new(true, 1, null, null);

    private static AcpiWriteResult Failure(string message) => new(false, 0, null, message);

    private sealed class FakeAcpi : IAsusAcpi
    {
        private readonly object _gate = new();
        private int _activeReads;
        private int _readCallCount;

        public Dictionary<uint, AcpiReadResult> Reads { get; } = new();
        public Dictionary<uint, Queue<AcpiWriteResult>> Outcomes { get; } = new();
        public List<(uint DeviceId, int Value)> Writes { get; } = new();
        public bool IsConnected { get; set; } = true;
        public string? OpenError => null;
        public int ReadCount => Volatile.Read(ref _readCallCount);
        public int ReadDelayMilliseconds { get; set; }
        public ManualResetEventSlim? ReadSampleCompleted { get; set; }
        public bool WriteBeforeWatchEnded { get; private set; }

        public void SetOutcomes(uint deviceId, params AcpiWriteResult[] outcomes) =>
            Outcomes[deviceId] = new Queue<AcpiWriteResult>(outcomes);

        public AcpiReadResult DeviceGet(uint deviceId)
        {
            Interlocked.Increment(ref _readCallCount);
            Interlocked.Increment(ref _activeReads);
            try
            {
                ReadSampleCompleted?.Set();
                if (ReadDelayMilliseconds > 0)
                    Thread.Sleep(ReadDelayMilliseconds);

                return Reads.TryGetValue(deviceId, out AcpiReadResult result)
                    ? result
                    : new AcpiReadResult(true, 0, null, null);
            }
            finally
            {
                Interlocked.Decrement(ref _activeReads);
            }
        }

        public AcpiWriteResult DeviceSet(uint deviceId, int value)
        {
            if (Volatile.Read(ref _activeReads) != 0)
                WriteBeforeWatchEnded = true;

            lock (_gate)
            {
                Writes.Add((deviceId, value));
                if (Outcomes.TryGetValue(deviceId, out Queue<AcpiWriteResult>? outcomes) &&
                    outcomes.Count > 0)
                    return outcomes.Dequeue();
            }

            return Success();
        }

        public void Dispose()
        {
        }
    }
}
