namespace PhotoStealthPrototype.Stealth;

/// <summary>Behaviour states a guard cycles through as the detection meter moves.</summary>
public enum GuardState
{
    /// <summary>Walking the patrol route, unaware.</summary>
    Patrol,

    /// <summary>Something registered — moving to the last known player position.</summary>
    Investigate,

    /// <summary>Meter is full. The player has been caught.</summary>
    Alert,

    /// <summary>Lost the trail — sweeping the area before giving up.</summary>
    Search,
}
