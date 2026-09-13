using AllyAutoTDP.Application;
using AllyAutoTDP.AutoTdp;
using AllyAutoTDP.Configuration;
using AllyAutoTDP.Hardware;
using AllyAutoTDP.Power;
using AllyAutoTDP.UI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AllyAutoTDP.Tests;

[TestClass]
public sealed class ApplicationBoundaryTests
{
    [TestMethod]
    public void GlobalPowerLimits_SelectBatteryAndAcRanges()
    {
        var limits = new PowerLimits
        {
            BatteryMinTdp = 7,
            BatteryMaxTdp = 21,
            AcMinTdp = 9,
            AcMaxTdp = 29
        };

        Assert.AreEqual(new TdpRange(7, 21), limits.GetEffectiveRange(
            PowerSourceKind.Battery));
        Assert.AreEqual(new TdpRange(9, 29), limits.GetEffectiveRange(
            PowerSourceKind.Ac));
        Assert.AreEqual(new TdpRange(7, 21), limits.GetEffectiveRange(
            PowerSourceKind.Unknown));
    }

    [TestMethod]
    public void FrenchStateMapping_ContainsNoInternalStateNames()
    {
        Assert.AreEqual("Désactivé", UiStateMapper.MapSessionState(
            GameSessionState.Disabled));
        Assert.AreEqual("Prêt", UiStateMapper.MapSessionState(
            GameSessionState.Ready));
        Assert.AreEqual("Actif", UiStateMapper.MapSessionState(
            GameSessionState.Active));
        Assert.AreEqual("Pause", UiStateMapper.MapSessionState(
            GameSessionState.Paused));
        Assert.AreEqual("Limite maximale", UiStateMapper.MapSessionState(
            GameSessionState.MaxLimit));
        Assert.AreEqual("Restauration", UiStateMapper.MapSessionState(
            GameSessionState.Restoring));
        Assert.AreEqual("Erreur", UiStateMapper.MapSessionState(
            GameSessionState.Error));
        Assert.AreEqual("Silencieux", UiStateMapper.MapRestoreMode(
            RestoreMode.Silent));
        Assert.AreEqual("Performance", UiStateMapper.MapRestoreMode(
            RestoreMode.Balanced));
        Assert.AreEqual("Turbo", UiStateMapper.MapRestoreMode(
            RestoreMode.Turbo));
    }

    [TestMethod]
    public void NamedMutex_DoesNotKillOrReplaceFirstInstance()
    {
        string name = $"AllyAutoTDP.Tests.{Guid.NewGuid():N}";
        Assert.IsTrue(SingleInstanceGuard.TryAcquire(out SingleInstanceGuard? first, name));
        using (first)
        {
            bool acquiredByOtherThread = Task.Run(() =>
            {
                bool acquired = SingleInstanceGuard.TryAcquire(
                    out SingleInstanceGuard? secondGuard,
                    name);
                secondGuard?.Dispose();
                return acquired;
            }).GetAwaiter().GetResult();
            Assert.IsFalse(acquiredByOtherThread);
        }

        Assert.IsTrue(SingleInstanceGuard.TryAcquire(out SingleInstanceGuard? second, name));
        second!.Dispose();
    }
}
