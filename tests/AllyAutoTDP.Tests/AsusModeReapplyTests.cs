using Microsoft.VisualStudio.TestTools.UnitTesting;

using AllyAutoTDP.Hardware;
using AllyAutoTDP.Power;

namespace AllyAutoTDP.Tests;

[TestClass]
public sealed class AsusModeReapplyTests
{
    [TestMethod]
    public void MissingArgument_IsRejected()
    {
        Assert.IsFalse(RestoreModeParser.TryParse([], out _, out string? error));
        StringAssert.Contains(error ?? string.Empty, "--restore-mode");
    }

    [TestMethod]
    public void InvalidArgument_IsRejected()
    {
        Assert.IsFalse(
            RestoreModeParser.TryParse(
                ["--restore-mode", "performance"],
                out _,
                out string? error));
        StringAssert.Contains(error ?? string.Empty, "Invalid restore mode");
    }

    [TestMethod]
    public void UnknownArgument_IsRejected()
    {
        Assert.IsFalse(
            RestoreModeParser.TryParse(
                ["--unknown", "value"],
                out _,
                out string? error));
        StringAssert.Contains(error ?? string.Empty, "Unknown argument");
    }

    [TestMethod]
    public void RestoreModeValues_AreStrictlyMapped()
    {
        Assert.IsTrue(RestoreModeParser.TryParseValue("silent", out RestoreMode silent));
        Assert.AreEqual(RestoreMode.Silent, silent);
        Assert.AreEqual(2, (int)silent);

        Assert.IsTrue(RestoreModeParser.TryParseValue("balanced", out RestoreMode balanced));
        Assert.AreEqual(RestoreMode.Balanced, balanced);
        Assert.AreEqual(0, (int)balanced);

        Assert.IsTrue(RestoreModeParser.TryParseValue("turbo", out RestoreMode turbo));
        Assert.AreEqual(RestoreMode.Turbo, turbo);
        Assert.AreEqual(1, (int)turbo);
    }

    [TestMethod]
    public void Reapply_WritesOnlyPerformanceModeWithoutReadback()
    {
        var fake = new FakeAcpi();
        var reapply = new AsusModeReapply(fake, RestoreMode.Balanced);

        AsusModeReapplyResult result = reapply.Reapply();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(0, fake.ReadCount);
        CollectionAssert.AreEqual(
            new[] { (AsusModeReapply.PerformanceModeEndpoint, 0) },
            fake.Writes.ToArray());
    }

    [TestMethod]
    public void ReapplyFailure_IsReturned()
    {
        var fake = new FakeAcpi
        {
            NextWrite = new AcpiWriteResult(false, 7, null, "mode rejected")
        };
        var reapply = new AsusModeReapply(fake, RestoreMode.Silent);

        AsusModeReapplyResult result = reapply.Reapply();

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(7, result.ResultCode);
        StringAssert.Contains(result.FailureReason ?? string.Empty, "mode rejected");
    }

    private sealed class FakeAcpi : IAsusAcpi
    {
        public List<(uint DeviceId, int Value)> Writes { get; } = new();
        public AcpiWriteResult NextWrite { get; set; } = new(true, 1, null, null);
        public int ReadCount { get; private set; }
        public bool IsConnected { get; init; } = true;
        public string? OpenError => null;

        public AcpiReadResult DeviceGet(uint deviceId)
        {
            ReadCount++;
            return new AcpiReadResult(true, 0, null, null);
        }

        public AcpiWriteResult DeviceSet(uint deviceId, int value)
        {
            Writes.Add((deviceId, value));
            return NextWrite;
        }

        public void Dispose()
        {
        }
    }
}
