using Microsoft.VisualStudio.TestTools.UnitTesting;

using AllyAutoTDP.Hardware;
using AllyAutoTDP.Power;

namespace AllyAutoTDP.Tests;

[TestClass]
public sealed class PowerWatchTests
{
    [TestMethod]
    public void WatchUsesFiveHundredMillisecondDefaultInterval()
    {
        Assert.AreEqual(500, PowerWatch.DefaultInterval.TotalMilliseconds);
    }

    [TestMethod]
    public void WatchReadbackNeverCallsDeviceSet()
    {
        var fake = new FakeAcpi();
        fake.Reads[AllyPowerEndpoints.A0] = new AcpiReadResult(true, 15, null, null);
        fake.Reads[AllyPowerEndpoints.A3] = new AcpiReadResult(true, 15, null, null);
        fake.Reads[AllyPowerEndpoints.C1] = new AcpiReadResult(true, 20, null, null);
        var controller = new AllyPowerController(fake, writeAuthorized: true);
        var watch = new PowerWatch(controller);

        watch.PollOnce();
        fake.Reads[AllyPowerEndpoints.A0] = new AcpiReadResult(true, 10, null, null);
        watch.PollOnce();

        Assert.AreEqual(0, fake.WriteCount);
    }

    private sealed class FakeAcpi : IAsusAcpi
    {
        public Dictionary<uint, AcpiReadResult> Reads { get; } = new();
        public int WriteCount { get; private set; }
        public bool IsConnected => true;
        public string? OpenError => null;

        public AcpiReadResult DeviceGet(uint deviceId) =>
            Reads.TryGetValue(deviceId, out AcpiReadResult result)
                ? result
                : new AcpiReadResult(false, null, null, "not configured");

        public AcpiWriteResult DeviceSet(uint deviceId, int value)
        {
            WriteCount++;
            return new AcpiWriteResult(true, 1, null, null);
        }

        public void Dispose()
        {
        }
    }
}
