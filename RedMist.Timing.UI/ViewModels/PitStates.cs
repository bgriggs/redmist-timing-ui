namespace RedMist.Timing.UI.ViewModels;

public enum PitStates
{
    None,
    InPit,
    EnteredPit,
    ExitedPit,
    PitSF,
    // These two come only from a server's pit phase, never from the pit flags.
    InPitBox,
    ExitingPit
}
