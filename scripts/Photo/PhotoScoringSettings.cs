namespace PhotoStealthPrototype.Photo;

/// <summary>
/// Tuning values for <see cref="PhotoScorer"/>. Lives as a struct so the scorer
/// can stay static and testable while <c>PhotoCamera</c> owns the exported knobs.
/// </summary>
public readonly struct PhotoScoringSettings
{
    /// <summary>Frame fraction below which the subject is a distant speck.</summary>
    public float MinFill { get; init; }

    /// <summary>Frame fraction at which framing reads as good.</summary>
    public float GoodFill { get; init; }

    /// <summary>Frame fraction beyond which the subject starts getting cropped.</summary>
    public float MaxFill { get; init; }

    /// <summary>Illumination below which the subject is unreadably dark.</summary>
    public float MinReadableLight { get; init; }

    /// <summary>Illumination at which the subject is well lit.</summary>
    public float GoodLight { get; init; }

    public bool FlashOn { get; init; }
    public float FlashStrength { get; init; }
    public float FlashRange { get; init; }

    /// <summary>Physics layers that block sight of the subject.</summary>
    public uint OcclusionMask { get; init; }

    public static PhotoScoringSettings Default => new()
    {
        MinFill = 0.002f,
        GoodFill = 0.04f,
        MaxFill = 0.5f,
        MinReadableLight = 0.18f,
        GoodLight = 0.62f,
        FlashOn = false,
        FlashStrength = 1.0f,
        FlashRange = 9.0f,
        OcclusionMask = 1,
    };
}
