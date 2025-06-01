using RedMist.Timing.UI.ViewModels.InCarDriverMode;
using RedMist.TimingCommon.Models.InCarDriverMode;

namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignInCarPositionsViewModel : InCarPositionsViewModel
{
    public DesignInCarPositionsViewModel() : base(new DesignHubClient(), new DesignEventClient(new DesignConfiguration()))
    {
        ShowInClassOnly = false;
        var payload = new InCarPayload
        {
            PositionOverall = "10",
            PositionInClass = "2",
            Flag = TimingCommon.Models.Flags.Green,
        };

        var ahead = new CarStatus
        {
            Number = "123",
            Class = "GP1",
            Team = "Team ABC 123",
            TransponderId = 1234567890,
            CarType = "GT3",
            Driver = "John Doe",
            LastLap = "1:23.456",
            GainLoss = "0.123",
            Gap = "1.234",
        };

        var aheadOutOfClass = new CarStatus
        {
            Number = "234",
            Class = "GP2",
            Team = "Team something",
            TransponderId = 1234567891,
            CarType = "Miata",
            LastLap = "1:24.111",
            GainLoss = "1.123",
            Gap = "1.004",
            
        };

        var driver = new CarStatus
        {
            Number = "456",
            Class = "GP1",
            Team = "Team whatever",
            LastLap = "1:23.267",
            Driver = "Alice Smith",
        };

        var behind = new CarStatus
        {
            Number = "345",
            Class = "GP1",
            Team = "Team XYZ 345123123123123",
            TransponderId = 1234567892,
            CarType = "BMW 240ir",
            Driver = "Jane Doe",
            LastLap = "1:23.000",
            GainLoss = "-0.567",
            Gap = "2.345",
        };

        payload.Cars = [ahead, aheadOutOfClass, driver, behind];

        Receive(new Models.InCarPositionUpdate(payload));
    }
}
