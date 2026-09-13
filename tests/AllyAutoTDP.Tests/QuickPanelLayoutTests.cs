using AllyAutoTDP.UI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AllyAutoTDP.Tests;

[TestClass]
public sealed class QuickPanelLayoutTests
{
    [TestMethod]
    public void Calculate_UsesRightEdgeAndWorkingAreaAtRogAllyResolutions()
    {
        Rectangle fullHd = QuickPanelLayout.Calculate(
            new Rectangle(0, 0, 1920, 1040),
            96);
        Rectangle hd = QuickPanelLayout.Calculate(
            new Rectangle(0, 0, 1280, 680),
            96);

        Assert.AreEqual(0, fullHd.Top);
        Assert.AreEqual(1920, fullHd.Right);
        Assert.AreEqual(1040, fullHd.Bottom);
        Assert.AreEqual(1040, fullHd.Height);
        Assert.AreEqual(0, hd.Top);
        Assert.AreEqual(1280, hd.Right);
        Assert.AreEqual(680, hd.Bottom);
        Assert.AreEqual(680, hd.Height);
        Assert.IsTrue(fullHd.Width > 0);
        Assert.IsTrue(hd.Width > 0);
    }

    [TestMethod]
    public void Calculate_ScalesLogicalWidthWithDpi()
    {
        Rectangle standard = QuickPanelLayout.Calculate(
            new Rectangle(0, 0, 1920, 1080),
            96);
        Rectangle scaled = QuickPanelLayout.Calculate(
            new Rectangle(0, 0, 1920, 1080),
            144);

        Assert.IsTrue(scaled.Width > standard.Width);
        Assert.AreEqual(1080, scaled.Height);
        Assert.AreEqual(1920, scaled.Right);
    }

    [TestMethod]
    public void Calculate_UsesWorkingAreaOriginAndFullHeight()
    {
        Rectangle workingArea = new(100, 40, 1280, 680);

        Rectangle panel = QuickPanelLayout.Calculate(
            workingArea,
            96);

        Assert.AreEqual(workingArea.Top, panel.Top);
        Assert.AreEqual(workingArea.Bottom, panel.Bottom);
        Assert.AreEqual(workingArea.Right, panel.Right);
        Assert.AreEqual(workingArea.Height, panel.Height);
        Assert.IsTrue(panel.Left >= workingArea.Left);
    }

    [TestMethod]
    public void Calculate_PreservesAllWorkingAreaEdgesForTaskbarDirections()
    {
        Rectangle[] workingAreas =
        [
            new Rectangle(0, 0, 1920, 1040),
            new Rectangle(0, 40, 1920, 1040),
            new Rectangle(80, 0, 1840, 1080),
            new Rectangle(0, 0, 1840, 1080)
        ];

        foreach (Rectangle workingArea in workingAreas)
        {
            Rectangle panel = QuickPanelLayout.Calculate(workingArea, 96);

            Assert.AreEqual(workingArea.Right, panel.Right);
            Assert.AreEqual(workingArea.Top, panel.Top);
            Assert.AreEqual(workingArea.Bottom, panel.Bottom);
            Assert.IsTrue(panel.Left >= workingArea.Left);
        }
    }

    [TestMethod]
    public void Calculate_ClampsWidthWhenWorkingAreaIsNarrow()
    {
        Rectangle workingArea = new(300, 20, 120, 600);

        Rectangle panel = QuickPanelLayout.Calculate(workingArea, 144);

        Assert.AreEqual(workingArea.Right, panel.Right);
        Assert.AreEqual(workingArea.Top, panel.Top);
        Assert.AreEqual(workingArea.Bottom, panel.Bottom);
        Assert.IsTrue(panel.Width <= workingArea.Width);
    }

    [TestMethod]
    public void Calculate_ReturnsEmptyForInvalidWorkingArea()
    {
        Assert.AreEqual(
            Rectangle.Empty,
            QuickPanelLayout.Calculate(new Rectangle(0, 0, 0, 100), 96));
        Assert.AreEqual(
            Rectangle.Empty,
            QuickPanelLayout.Calculate(new Rectangle(0, 0, 100, 0), 96));
    }
}
