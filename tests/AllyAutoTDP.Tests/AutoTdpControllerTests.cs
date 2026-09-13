using AllyAutoTDP.AutoTdp;
using AllyAutoTDP.Hardware.AMD;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AllyAutoTDP.Tests;

[TestClass]
public sealed class AutoTdpControllerTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void LowFps_ChangesTdpUpImmediately()
    {
        var controller = new AutoTdpController(60, new TdpRange(6, 12));
        DecreaseOnce(controller);

        AutoTdpDecision result = controller.Tick(
            FpsReading.Valid(54),
            PowerReading.Valid(10),
            Start);

        Assert.AreEqual(AutoTdpAction.Increase, result.Action);
        Assert.AreEqual(12, controller.CurrentTdp);
    }

    [TestMethod]
    public void HighFps_RequiresEightSamplesBeforeDecrease()
    {
        var controller = new AutoTdpController(60, new TdpRange(6, 12));

        for (int index = 0; index < 7; index++)
        {
            AutoTdpDecision result = controller.Tick(
                FpsReading.Valid(60),
                PowerReading.Valid(5),
                Start.AddMilliseconds(index * 300));
            Assert.AreEqual(AutoTdpAction.None, result.Action);
        }

        AutoTdpDecision eighth = controller.Tick(
            FpsReading.Valid(60),
            PowerReading.Valid(5),
            Start.AddMilliseconds(2100));

        Assert.AreEqual(AutoTdpAction.Decrease, eighth.Action);
        Assert.AreEqual(11, controller.CurrentTdp);
    }

    [TestMethod]
    public void HighFps_WithPowerEqualToCurrentTdp_AllowsDecrease()
    {
        var controller = new AutoTdpController(60, new TdpRange(6, 12));

        AutoTdpDecision result = default;
        for (int index = 0; index < 8; index++)
        {
            result = controller.Tick(
                FpsReading.Valid(60),
                PowerReading.Valid(12),
                Start.AddMilliseconds(index * 300));
        }

        Assert.AreEqual(AutoTdpAction.Decrease, result.Action);
        Assert.AreEqual(11, controller.CurrentTdp);
        Assert.AreEqual(7, controller.DownSamples);
    }

    [TestMethod]
    public void Decrease_FirstActionIsImmediate_ThenCooldownBlocksSecondAction()
    {
        var controller = new AutoTdpController(60, new TdpRange(6, 12));
        for (int index = 0; index < 7; index++)
        {
            controller.Tick(
                FpsReading.Valid(60),
                PowerReading.Valid(1),
                Start.AddMilliseconds(index * 300));
        }

        AutoTdpDecision first = controller.Tick(
            FpsReading.Valid(60),
            PowerReading.Valid(1),
            Start.AddMilliseconds(2100));
        AutoTdpDecision blocked = controller.Tick(
            FpsReading.Valid(60),
            PowerReading.Valid(1),
            Start.AddMilliseconds(2400));

        Assert.AreEqual(AutoTdpAction.Decrease, first.Action);
        Assert.AreEqual(11, first.CurrentTdp);
        Assert.AreEqual(AutoTdpAction.None, blocked.Action);
        Assert.AreEqual(11, blocked.CurrentTdp);
        Assert.AreEqual(8, controller.DownSamples);
        StringAssert.Contains(blocked.Reason, "decrease cooldown active");
    }

    [TestMethod]
    public void Decrease_IsAllowedAgainAtCooldownExpiry()
    {
        var controller = new AutoTdpController(60, new TdpRange(6, 12));
        for (int index = 0; index < 7; index++)
        {
            controller.Tick(
                FpsReading.Valid(60),
                PowerReading.Valid(1),
                Start.AddMilliseconds(index * 300));
        }

        controller.Tick(
            FpsReading.Valid(60),
            PowerReading.Valid(1),
            Start.AddMilliseconds(2100));
        AutoTdpDecision result = controller.Tick(
            FpsReading.Valid(60),
            PowerReading.Valid(1),
            Start.AddMilliseconds(2700));

        Assert.AreEqual(AutoTdpAction.Decrease, result.Action);
        Assert.AreEqual(10, result.CurrentTdp);
    }

    [TestMethod]
    public void DecreaseCooldown_DoesNotBlockIncrease()
    {
        var controller = new AutoTdpController(60, new TdpRange(6, 12));
        for (int index = 0; index < 7; index++)
        {
            controller.Tick(
                FpsReading.Valid(60),
                PowerReading.Valid(1),
                Start.AddMilliseconds(index * 300));
        }

        controller.Tick(
            FpsReading.Valid(60),
            PowerReading.Valid(1),
            Start.AddMilliseconds(2100));
        AutoTdpDecision result = controller.Tick(
            FpsReading.Valid(54),
            PowerReading.Valid(1),
            Start.AddMilliseconds(2400));

        Assert.AreEqual(AutoTdpAction.Increase, result.Action);
        Assert.AreEqual(12, result.CurrentTdp);
    }

    [TestMethod]
    public void HighFps_WithPowerAboveCurrentTdp_BlocksAndCapsCounter()
    {
        var controller = new AutoTdpController(60, new TdpRange(6, 12));

        AutoTdpDecision result = default;
        for (int index = 0; index < 20; index++)
        {
            result = controller.Tick(
                FpsReading.Valid(60),
                PowerReading.Valid(13),
                Start.AddMilliseconds(index * 300));
        }

        Assert.AreEqual(AutoTdpAction.None, result.Action);
        Assert.AreEqual(AutoTdpState.Stable, result.State);
        Assert.AreEqual(12, controller.CurrentTdp);
        Assert.AreEqual(8, controller.DownSamples);
    }

    [TestMethod]
    public void StableBand_DecreasesCounterByOne()
    {
        var controller = new AutoTdpController(60, new TdpRange(6, 12));

        for (int index = 0; index < 7; index++)
        {
            controller.Tick(
                FpsReading.Valid(60),
                PowerReading.Valid(1),
                Start.AddMilliseconds(index * 300));
        }

        AutoTdpDecision result = controller.Tick(
            FpsReading.Valid(55),
            PowerReading.Valid(1),
            Start.AddMilliseconds(2100));

        Assert.AreEqual(AutoTdpAction.None, result.Action);
        Assert.AreEqual(AutoTdpState.Stable, result.State);
        Assert.AreEqual(6, controller.DownSamples);
    }

    [TestMethod]
    public void LowFps_StillResetsDecreaseCounter()
    {
        var controller = new AutoTdpController(60, new TdpRange(6, 20));

        for (int index = 0; index < 8; index++)
        {
            controller.Tick(
                FpsReading.Valid(60),
                PowerReading.Valid(13),
                Start.AddMilliseconds(index * 300));
        }

        AutoTdpDecision result = controller.Tick(
            FpsReading.Valid(54),
            PowerReading.Valid(13),
            Start.AddMilliseconds(2100));

        Assert.AreEqual(AutoTdpAction.Increase, result.Action);
        Assert.AreEqual(0, controller.DownSamples);
    }

    [TestMethod]
    public void LowFps_FirstIncreaseIsImmediateThenCooldownsSubsequentIncreases()
    {
        var controller = new AutoTdpController(60, new TdpRange(6, 20));

        for (int index = 0; index < 8; index++)
        {
            controller.Tick(
                FpsReading.Valid(60),
                PowerReading.Valid(1),
                Start.AddMilliseconds(index * 300));
        }
        controller.Tick(
            FpsReading.Valid(60),
            PowerReading.Valid(1),
            Start.AddMilliseconds(2700));

        AutoTdpDecision first = controller.Tick(
            FpsReading.Valid(54),
            PowerReading.Valid(1),
            Start.AddMilliseconds(3000));
        AutoTdpDecision blocked = controller.Tick(
            FpsReading.Valid(54),
            PowerReading.Valid(1),
            Start.AddMilliseconds(3300));
        AutoTdpDecision allowed = controller.Tick(
            FpsReading.Valid(54),
            PowerReading.Valid(1),
            Start.AddMilliseconds(3600));

        Assert.AreEqual(AutoTdpAction.Increase, first.Action);
        Assert.AreEqual(19, first.CurrentTdp);
        Assert.AreEqual(AutoTdpAction.None, blocked.Action);
        StringAssert.Contains(blocked.Reason, "increase cooldown active");
        Assert.AreEqual(19, blocked.CurrentTdp);
        Assert.AreEqual(AutoTdpAction.Increase, allowed.Action);
        Assert.AreEqual(20, allowed.CurrentTdp);
    }

    [TestMethod]
    public void Decrease_StopsAtMinimumClamp()
    {
        var controller = new AutoTdpController(60, new TdpRange(6, 12));

        for (int index = 0; index < 100; index++)
        {
            controller.Tick(
                FpsReading.Valid(60),
                PowerReading.Valid(1),
                Start.AddMilliseconds(index * 300));
        }

        Assert.AreEqual(6, controller.CurrentTdp);
        Assert.IsTrue(controller.CurrentTdp >= controller.MinTdp);
    }

    [TestMethod]
    public void LowFpsAtMaximum_ReportsMaxLimitWithoutWriteAction()
    {
        var controller = new AutoTdpController(60, new TdpRange(6, 12));

        AutoTdpDecision result = controller.Tick(
            FpsReading.Valid(45),
            PowerReading.Valid(10),
            Start);

        Assert.AreEqual(AutoTdpAction.None, result.Action);
        Assert.AreEqual(AutoTdpState.MaxLimit, result.State);
        Assert.AreEqual(12, result.EffectiveMax);
    }

    [TestMethod]
    public void InvalidPower_BlocksDecrease()
    {
        var controller = new AutoTdpController(60, new TdpRange(6, 12));

        AutoTdpDecision result = default;
        for (int index = 0; index < 8; index++)
        {
            result = controller.Tick(
                FpsReading.Valid(60),
                PowerReading.Invalid(null, "power unavailable"),
                Start.AddMilliseconds(index * 300));
        }

        Assert.AreEqual(AutoTdpState.PowerInvalid, result.State);
        Assert.AreEqual(12, controller.CurrentTdp);
    }

    [TestMethod]
    public void InvalidFps_PreservesTdpThenStopsAfterThirtySeconds()
    {
        var controller = new AutoTdpController(60, new TdpRange(6, 12));

        AutoTdpDecision first = controller.Tick(
            FpsReading.Invalid(17, "ADL error"),
            PowerReading.Valid(10),
            Start);
        AutoTdpDecision temporary = controller.Tick(
            FpsReading.Invalid(17, "ADL error"),
            PowerReading.Valid(10),
            Start.AddSeconds(29));
        AutoTdpDecision lost = controller.Tick(
            FpsReading.Invalid(17, "ADL error"),
            PowerReading.Valid(10),
            Start.AddSeconds(30));

        Assert.AreEqual(AutoTdpState.FpsInvalid, first.State);
        Assert.AreEqual(AutoTdpState.FpsInvalid, temporary.State);
        Assert.IsFalse(temporary.ShouldStop);
        Assert.AreEqual(12, temporary.CurrentTdp);
        Assert.AreEqual(AutoTdpState.FpsSourceLost, lost.State);
        Assert.IsTrue(lost.ShouldStop);
        Assert.AreEqual(12, lost.CurrentTdp);
    }

    [TestMethod]
    public void SuccessZero_PausesAndResetsDecreaseCounter_ThenResumes()
    {
        var controller = new AutoTdpController(60, new TdpRange(6, 12));

        for (int index = 0; index < 7; index++)
        {
            controller.Tick(
                FpsReading.Valid(60),
                PowerReading.Valid(1),
                Start.AddMilliseconds(index * 300));
        }

        Assert.AreEqual(7, controller.DownSamples);
        int pausedTdp = controller.CurrentTdp;
        AutoTdpDecision paused = default;
        for (int index = 0; index <= 5; index++)
        {
            paused = controller.Tick(
                FpsReading.Invalid(0, "SUCCESS_ZERO"),
                PowerReading.Valid(1),
                Start.AddSeconds(2 + index));

            Assert.IsFalse(paused.ShouldStop);
            Assert.AreEqual(pausedTdp, controller.CurrentTdp);
        }

        Assert.AreEqual(AutoTdpState.FpsInvalid, paused.State);
        Assert.AreEqual(0, controller.DownSamples);

        AutoTdpDecision resumed = default;
        for (int index = 0; index < 8; index++)
        {
            resumed = controller.Tick(
                FpsReading.Valid(60),
                PowerReading.Valid(1),
                Start.AddSeconds(8).AddMilliseconds(index * 300));
        }

        Assert.AreEqual(AutoTdpAction.Decrease, resumed.Action);
        Assert.AreEqual(11, controller.CurrentTdp);
    }

    [TestMethod]
    public void UpdatingEffectiveMaximum_ClampsCurrentTdpImmediately()
    {
        var controller = new AutoTdpController(60, new TdpRange(6, 30));

        controller.UpdateLimits(new TdpRange(6, 25));

        Assert.AreEqual(25, controller.CurrentTdp);
        Assert.AreEqual(25, controller.EffectiveMax);
        Assert.AreEqual(AutoTdpState.SafetyClamped, controller.State);
    }

    [TestMethod]
    public void FixedRange_AllowsMinimumEqualToMaximum()
    {
        var controller = new AutoTdpController(60, new TdpRange(12, 12));

        AutoTdpDecision result = controller.Tick(
            FpsReading.Valid(30),
            PowerReading.Valid(1),
            Start);

        Assert.AreEqual(12, controller.CurrentTdp);
        Assert.AreEqual(12, result.EffectiveMax);
        Assert.AreEqual(AutoTdpState.MaxLimit, result.State);
    }

    private static void DecreaseOnce(AutoTdpController controller)
    {
        for (int index = 0; index < 8; index++)
        {
            controller.Tick(
                FpsReading.Valid(60),
                PowerReading.Valid(1),
                Start.AddMilliseconds(index * 300));
        }
    }
}
