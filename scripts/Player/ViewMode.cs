namespace PhotoStealthPrototype.Player;

/// <summary>
/// Which camera the player is looking through.
/// </summary>
/// <remarks>
/// These are two different control schemes, not two framings of one. Top-down
/// moves relative to the screen and turns the body toward its own motion;
/// first-person hands yaw to the mouse so a shot can actually be aimed. Code that
/// branches on this is branching on "which set of controls is live".
/// </remarks>
public enum ViewMode
{
    /// <summary>3/4 overhead follow camera. The travelling and scouting view.</summary>
    TopDown,

    /// <summary>Through the viewfinder. The only view a photo can be taken from.</summary>
    FirstPerson,
}
