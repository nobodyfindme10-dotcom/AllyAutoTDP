using AllyAutoTDP.Hardware;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AllyAutoTDP.Tests;

[TestClass]
public sealed class WindowsPowerSourceTests
{
    [TestMethod]
    public void OfflineStatus_IsBattery()
    {
        Assert.IsTrue(
            WindowsPowerSource.TryInterpretAcLineStatus(
                0,
                out PowerSourceKind source,
                out string? error),
            error);
        Assert.AreEqual(PowerSourceKind.Battery, source);
    }

    [TestMethod]
    public void OnlineStatus_IsAc()
    {
        Assert.IsTrue(
            WindowsPowerSource.TryInterpretAcLineStatus(
                1,
                out PowerSourceKind source,
                out string? error),
            error);
        Assert.AreEqual(PowerSourceKind.Ac, source);
    }

    [TestMethod]
    public void UnknownStatus_IsConservativeUnknown()
    {
        Assert.IsFalse(
            WindowsPowerSource.TryInterpretAcLineStatus(
                255,
                out PowerSourceKind source,
                out string? error));
        Assert.AreEqual(PowerSourceKind.Unknown, source);
        StringAssert.Contains(error ?? string.Empty, "unknown");
    }
}
