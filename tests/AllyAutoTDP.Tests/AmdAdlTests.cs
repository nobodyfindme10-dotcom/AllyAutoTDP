using AllyAutoTDP.Hardware.AMD;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AllyAutoTDP.Tests;

[TestClass]
public sealed class AmdAdlTests
{
    [TestMethod]
    public void FrameMetricsError_IsUnavailableAndNotZeroFps()
    {
        var fake = new FakeAdl
        {
            FrameResult = new(false, 0, 17, "frame metrics failed")
        };
        using var reader = new AmdFrameMetricsReader(fake);

        Assert.IsTrue(reader.StartFPS().IsSuccess);
        FpsReading result = reader.GetFPS();

        Assert.IsFalse(result.IsValid);
        Assert.IsNull(result.Value);
        Assert.AreEqual(17, result.ErrorCode);
    }

    [TestMethod]
    public void ZeroAndNonFiniteFps_AreUnavailable()
    {
        var fake = new FakeAdl();
        using var reader = new AmdFrameMetricsReader(fake);
        reader.StartFPS();

        fake.FrameResult = new(true, 0, 0, null);
        Assert.IsFalse(reader.GetFPS().IsValid);

        fake.FrameResult = new(true, float.NaN, 0, null);
        Assert.IsFalse(reader.GetFPS().IsValid);

        fake.FrameResult = new(true, float.PositiveInfinity, 0, null);
        Assert.IsFalse(reader.GetFPS().IsValid);
    }

    [TestMethod]
    public void PlausibleFpsBoundary_IsAccepted()
    {
        var fake = new FakeAdl();
        using var reader = new AmdFrameMetricsReader(fake);
        reader.StartFPS();

        fake.FrameResult = new(true, 999, 0, null);
        Assert.IsTrue(reader.GetFPS().IsValid);

        fake.FrameResult = new(true, 1000, 0, null);
        Assert.IsTrue(reader.GetFPS().IsValid);
    }

    [TestMethod]
    public void AbovePlausibleFpsLimit_IsUnavailable()
    {
        var fake = new FakeAdl();
        using var reader = new AmdFrameMetricsReader(fake);
        reader.StartFPS();

        fake.FrameResult = new(true, 1001, 0, null);
        Assert.IsFalse(reader.GetFPS().IsValid);

        fake.FrameResult = new(true, float.MaxValue, 0, null);
        Assert.IsFalse(reader.GetFPS().IsValid);
    }

    [TestMethod]
    public void ZeroPower_IsUnavailable()
    {
        var fake = new FakeAdl
        {
            PowerResult = new(true, 0, 0, null)
        };
        using var reader = new AmdPowerReader(fake);

        PowerReading result = reader.GetPower();

        Assert.IsFalse(result.IsValid);
        Assert.IsNull(result.Watts);
    }

    [TestMethod]
    public void FpsAndPowerReaders_ShareOneAdlSession()
    {
        var fake = new FakeAdl
        {
            FrameResult = new(true, 60, 0, null),
            PowerResult = new(true, 12, 0, null)
        };
        using (var fps = new AmdFrameMetricsReader(fake))
        using (var power = new AmdPowerReader(fake))
        {
            Assert.IsTrue(fps.StartFPS().IsSuccess);
            Assert.IsTrue(fps.GetFPS().IsValid);
            Assert.IsTrue(power.GetPower().IsValid);
        }

        Assert.AreEqual(1, fake.StartCount);
        Assert.AreEqual(1, fake.StopCount);
        Assert.AreEqual(0, fake.DisposeCount);
        fake.Dispose();
        fake.Dispose();
        Assert.AreEqual(1, fake.DisposeCount);
    }

    [TestMethod]
    public void ReaderDoesNotReadAfterDispose()
    {
        var fake = new FakeAdl();
        var reader = new AmdFrameMetricsReader(fake);
        reader.Dispose();

        FpsReading result = reader.GetFPS();

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual(0, fake.FrameReadCount);
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
        public int FrameReadCount { get; private set; }
        public int DisposeCount { get; private set; }

        public AdlOperationResult StartFrameMetrics()
        {
            StartCount++;
            return new(true, 0, null);
        }

        public AdlFpsResult GetFrameMetrics()
        {
            FrameReadCount++;
            return FrameResult;
        }

        public AdlOperationResult StopFrameMetrics()
        {
            StopCount++;
            return new(true, 0, null);
        }

        public AdlPowerResult GetIgpuAsicPower() => PowerResult;

        public void Dispose()
        {
            if (DisposeCount == 0)
                DisposeCount++;
        }
    }
}
