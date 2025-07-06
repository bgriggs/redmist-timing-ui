using RedMist.Timing.UI.ViewModels.CarDetails;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Tests.ViewModels.CarDetails;

[TestClass]
public sealed class LapViewModelTests
{
    [TestMethod]
    public void RaceTime_Std_Test()
    {
        var cp = new CarPosition { TotalTime = "01:00:15.989" };
        var lapVm = new LapViewModel(cp);
        var rt = lapVm.RaceTime;
        Assert.AreEqual("1:00:15", rt);
    }

    [TestMethod]
    public void RaceTime_24_Test()
    {
        var cp = new CarPosition { TotalTime = "13:00:15.989" };
        var lapVm = new LapViewModel(cp);
        Assert.AreEqual("13:00:15", lapVm.RaceTime);
    }

    [TestMethod]
    public void RaceTime_MultiDay_Test()
    {
        var cp = new CarPosition { TotalTime = "48:00:15.989" };
        var lapVm = new LapViewModel(cp);
        var rt = lapVm.RaceTime;
        Assert.AreEqual("48:00:15", lapVm.RaceTime);
    }

    [TestMethod]
    public void RaceTime_Empty_Test()
    {
        var cp = new CarPosition { TotalTime = "" };
        var lapVm = new LapViewModel(cp);
        Assert.AreEqual("", lapVm.RaceTime);
    }

    [TestMethod]
    public void RaceTime_Null_Test()
    {
        var cp = new CarPosition { TotalTime = null };
        var lapVm = new LapViewModel(cp);
        Assert.AreEqual("", lapVm.RaceTime);
    }
}
