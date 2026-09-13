using AllyAutoTDP.AutoTdp;
using AllyAutoTDP.Hardware;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AllyAutoTDP.Tests;

[TestClass]
public sealed class AutoTdpOptionsTests
{
    [TestMethod]
    public void BatteryDefaults_AreSixToTwentyFive()
    {
        Assert.IsTrue(
            AutoTdpOptions.TryParse(
                BaseArgs(),
                PowerSourceKind.Battery,
                out AutoTdpOptions? options,
                out string? error),
            error);

        TdpRange range = options!.GetEffectiveRange(PowerSourceKind.Battery);
        Assert.AreEqual(6, range.MinTdp);
        Assert.AreEqual(25, range.MaxTdp);
    }

    [TestMethod]
    public void AcDefaults_AreSixToThirty()
    {
        Assert.IsTrue(
            AutoTdpOptions.TryParse(
                BaseArgs(),
                PowerSourceKind.Ac,
                out AutoTdpOptions? options,
                out string? error),
            error);

        TdpRange range = options!.GetEffectiveRange(PowerSourceKind.Ac);
        Assert.AreEqual(6, range.MinTdp);
        Assert.AreEqual(30, range.MaxTdp);
    }

    [TestMethod]
    public void UserRange_CanBeNarrowed()
    {
        string[] args =
        [
            "--restore-mode", "balanced",
            "--target-fps", "60",
            "--min-tdp", "8",
            "--max-tdp", "20"
        ];

        Assert.IsTrue(
            AutoTdpOptions.TryParse(
                args,
                PowerSourceKind.Battery,
                out AutoTdpOptions? options,
                out string? error),
            error);

        TdpRange range = options!.GetEffectiveRange(PowerSourceKind.Battery);
        Assert.AreEqual(8, range.MinTdp);
        Assert.AreEqual(20, range.MaxTdp);
    }

    [TestMethod]
    public void BatteryRangeAboveTwentyFive_IsRejected()
    {
        string[] args =
        [
            "--restore-mode", "balanced",
            "--target-fps", "60",
            "--min-tdp", "6",
            "--max-tdp", "30"
        ];

        Assert.IsFalse(
            AutoTdpOptions.TryParse(
                args,
                PowerSourceKind.Battery,
                out _,
                out string? error));
        StringAssert.Contains(error ?? string.Empty, "25 W");
    }

    [TestMethod]
    public void AcRangeUpToThirty_IsAccepted()
    {
        string[] args =
        [
            "--restore-mode", "balanced",
            "--target-fps", "60",
            "--min-tdp", "10",
            "--max-tdp", "30"
        ];

        Assert.IsTrue(
            AutoTdpOptions.TryParse(
                args,
                PowerSourceKind.Ac,
                out AutoTdpOptions? options,
                out string? error),
            error);
        Assert.AreEqual(30, options!.GetEffectiveRange(PowerSourceKind.Ac).MaxTdp);
    }

    [TestMethod]
    public void TargetFps_UsesTheValidatedInitialSet()
    {
        string[] args = BaseArgs();
        args[3] = "55";

        Assert.IsFalse(
            AutoTdpOptions.TryParse(
                args,
                PowerSourceKind.Ac,
                out _,
                out string? error));
        StringAssert.Contains(error ?? string.Empty, "target FPS");
    }

    [TestMethod]
    public void ExplicitAcMaximum_IsCappedOnBatteryAndRestoredOnAc()
    {
        string[] args =
        [
            "--restore-mode", "balanced",
            "--target-fps", "60",
            "--max-tdp", "30"
        ];

        Assert.IsTrue(
            AutoTdpOptions.TryParse(
                args,
                PowerSourceKind.Ac,
                out AutoTdpOptions? options,
                out string? error),
            error);

        Assert.AreEqual(25, options!.GetEffectiveRange(PowerSourceKind.Battery).MaxTdp);
        Assert.AreEqual(30, options.GetEffectiveRange(PowerSourceKind.Ac).MaxTdp);
    }

    [TestMethod]
    public void UnknownSource_UsesConservativeTwentyFiveWattMaximum()
    {
        TdpRange range = TdpLimits.DefaultRange(PowerSourceKind.Unknown);

        Assert.AreEqual(6, range.MinTdp);
        Assert.AreEqual(25, range.MaxTdp);
        Assert.IsFalse(
            TdpLimits.TryValidate(
                PowerSourceKind.Unknown,
                6,
                30,
                out _));
    }

    private static string[] BaseArgs() =>
    [
        "--restore-mode", "balanced",
        "--target-fps", "60"
    ];
}
