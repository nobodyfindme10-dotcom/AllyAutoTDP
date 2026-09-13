using Microsoft.VisualStudio.TestTools.UnitTesting;

using AllyAutoTDP.Hardware;

namespace AllyAutoTDP.Tests;

[TestClass]
public sealed class AllyDetectorTests
{
    [TestMethod]
    public void Rc71Model_IsFamilyAndWriteAuthorized()
    {
        Assert.IsTrue(AllyDetector.IsAllyFamilyModel("RC71L"));
        Assert.IsTrue(AllyDetector.IsWriteAuthorizedModel("RC71L"));
    }

    [TestMethod]
    public void Rc7OtherModel_IsFamilyButWriteBlocked()
    {
        Assert.IsTrue(AllyDetector.IsAllyFamilyModel("RC72LA"));
        Assert.IsFalse(AllyDetector.IsWriteAuthorizedModel("RC72LA"));
    }

    [TestMethod]
    public void ModelMatching_IsCaseInsensitive()
    {
        Assert.IsTrue(AllyDetector.IsAllyFamilyModel("rc71l"));
        Assert.IsTrue(AllyDetector.IsWriteAuthorizedModel("rc71l"));
    }

    [TestMethod]
    public void NonRc7Model_IsNotFamilyAndWriteBlocked()
    {
        Assert.IsFalse(AllyDetector.IsAllyFamilyModel("FAKE-MODEL"));
        Assert.IsFalse(AllyDetector.IsWriteAuthorizedModel("FAKE-MODEL"));
    }
}
