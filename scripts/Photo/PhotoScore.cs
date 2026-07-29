using Godot;

namespace PhotoStealthPrototype.Photo;

/// <summary>
/// One evaluated shot, keeping every term rather than just the total so a
/// rejected photo can explain itself to the player ("well framed, but occluded").
/// </summary>
public readonly struct PhotoScore
{
    public PhotoSubject? Subject { get; init; }

    /// <summary>Final 0..1 grade: framing × visibility × lighting.</summary>
    public float Quality { get; init; }

    /// <summary>How well the subject fills the frame (0 = speck or cropped).</summary>
    public float Coverage { get; init; }

    /// <summary>1 at the centre of frame, 0 at a corner.</summary>
    public float Centering { get; init; }

    /// <summary>Fraction of sample points with a clear line from the lens.</summary>
    public float Visibility { get; init; }

    /// <summary>How readable the light level is, after any flash contribution.</summary>
    public float Lighting { get; init; }

    /// <summary>Raw illumination at the subject, including flash.</summary>
    public float Illumination { get; init; }

    /// <summary>False when the subject failed the hard on-screen gate.</summary>
    public bool InFrame { get; init; }

    /// <summary>A shot where the subject was not on screen at all.</summary>
    public static PhotoScore Miss(PhotoSubject? subject) => new() { Subject = subject };

    /// <summary>
    /// Godot-marshallable view of the score. Plain C# structs cannot cross into
    /// GDScript, and the scoring rubric is exactly the part worth driving from a
    /// headless test harness — this is how that harness reads results.
    /// </summary>
    public Godot.Collections.Dictionary ToDictionary() => new()
    {
        { "subject", Subject?.DisplayName ?? string.Empty },
        { "quality", Quality },
        { "coverage", Coverage },
        { "centering", Centering },
        { "visibility", Visibility },
        { "lighting", Lighting },
        { "illumination", Illumination },
        { "in_frame", InFrame },
        { "diagnosis", Diagnose() },
    };

    /// <summary>Short human-readable reason the shot scored badly, for the HUD.</summary>
    public string Diagnose() => this switch
    {
        { Subject: null } => "no subject in frame",
        { InFrame: false } => "subject not in frame",
        { Visibility: < 0.5f } => "subject obscured",
        { Lighting: < 0.4f } => "too dark — try the flash",
        { Coverage: < 0.4f } => "too far away — zoom or move closer",
        { Centering: < 0.5f } => "subject off-centre",
        _ => "good shot",
    };
}
