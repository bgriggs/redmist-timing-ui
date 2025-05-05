using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignControlLogViewModel : ControlLogViewModel
{
    public DesignControlLogViewModel() : base(new Event(), new DesignHubClient(), new DesignEventClient(new DesignConfiguration()))
    {
        
    }
}
