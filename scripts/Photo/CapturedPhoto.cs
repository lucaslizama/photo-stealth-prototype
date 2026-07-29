using Godot;

namespace PhotoStealthPrototype.Photo;

/// <summary>One photo the player actually took, kept for the review gallery.</summary>
public sealed class CapturedPhoto
{
    public CapturedPhoto(string subjectName, float quality, string diagnosis)
    {
        SubjectName = subjectName;
        Quality = quality;
        Diagnosis = diagnosis;
    }

    public string SubjectName { get; }
    public float Quality { get; }
    public string Diagnosis { get; }

    /// <summary>
    /// The captured pixels, or null when the frame could not be grabbed (no
    /// rendering device, e.g. headless). Filled in a frame after the shot, since
    /// the grab is deliberately not allowed to block the shutter.
    /// </summary>
    public ImageTexture? Texture { get; internal set; }
}
