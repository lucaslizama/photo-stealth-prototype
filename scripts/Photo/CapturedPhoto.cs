using Godot;

namespace PhotoStealthPrototype.Photo;

/// <summary>One photo the player actually took, kept for the album.</summary>
/// <remarks>
/// A flattened snapshot rather than a reference to the live <see cref="PhotoScore"/>
/// or its subject: the album has to keep telling the truth about a shot taken ten
/// seconds ago, and the subject's own <c>BestQuality</c> has moved on by then.
/// </remarks>
public sealed class CapturedPhoto
{
    public CapturedPhoto(int frameNumber, PhotoScore score)
    {
        FrameNumber = frameNumber;
        SubjectName = score.Subject?.DisplayName ?? "nothing";
        Quality = score.Quality;
        Diagnosis = score.Diagnose();
        Coverage = score.Coverage;
        Centering = score.Centering;
        Visibility = score.Visibility;
        Lighting = score.Lighting;
        RequiredQuality = score.Subject?.RequiredQuality ?? 0f;
        Passed = score.Subject is not null && score.Quality >= score.Subject.RequiredQuality;
    }

    /// <summary>Which exposure on the roll this was, 1-based.</summary>
    public int FrameNumber { get; }

    public string SubjectName { get; }
    public float Quality { get; }
    public string Diagnosis { get; }

    public float Coverage { get; }
    public float Centering { get; }
    public float Visibility { get; }
    public float Lighting { get; }

    /// <summary>The pass mark this shot was measured against, 0 for a miss.</summary>
    public float RequiredQuality { get; }

    /// <summary>True when this shot cleared its subject's pass mark.</summary>
    public bool Passed { get; }

    /// <summary>
    /// The captured pixels, or null when the frame could not be grabbed (no
    /// rendering device, e.g. headless). Filled in a frame after the shot, since
    /// the grab is deliberately not allowed to block the shutter.
    /// </summary>
    public ImageTexture? Texture { get; internal set; }
}
