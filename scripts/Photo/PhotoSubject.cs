using Godot;

namespace PhotoStealthPrototype.Photo;

/// <summary>
/// Something worth photographing. Supplies the sample points the scorer uses for
/// framing and occlusion, and owns its own pass mark.
/// </summary>
[GlobalClass]
public partial class PhotoSubject : Node3D
{
    public const string GroupName = "photo_subject";

    [Export] public string DisplayName { get; set; } = "subject";

    /// <summary>Quality a photo must reach for this subject to count as done.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float RequiredQuality { get; set; } = 0.55f;

    /// <summary>Local half-extents of the subject's box. Drives framing and occlusion.</summary>
    [Export] public Vector3 Extents { get; set; } = new(0.4f, 0.4f, 0.05f);

    /// <summary>Light level used where no <c>LightZone</c> covers the subject.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float AmbientIllumination { get; set; } = 0.3f;

    public bool Captured { get; private set; }
    public float BestQuality { get; private set; }

    /// <summary>
    /// The body this subject is mounted on, if any. Occlusion rays must ignore it
    /// — a subject parented to a guard would otherwise be permanently hidden
    /// behind that guard's own collision capsule.
    /// </summary>
    public Rid? HostRid { get; private set; }

    private readonly Vector3[] _samples = new Vector3[9];

    public override void _Ready()
    {
        AddToGroup(GroupName);

        for (Node? n = GetParent(); n is not null; n = n.GetParent())
        {
            if (n is CollisionObject3D body)
            {
                HostRid = body.GetRid();
                break;
            }
        }
    }

    /// <summary>
    /// Centre plus the eight box corners, in global space. Sampling the corners
    /// rather than just the centre is what lets a subject half-hidden behind a
    /// crate score partially instead of all-or-nothing.
    /// </summary>
    public Vector3[] GetSamplePoints()
    {
        Transform3D t = GlobalTransform;
        _samples[0] = t.Origin;

        int i = 1;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sy = -1; sy <= 1; sy += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    _samples[i++] = t * new Vector3(Extents.X * sx, Extents.Y * sy, Extents.Z * sz);
                }
            }
        }

        return _samples;
    }

    public void RecordQuality(float quality)
    {
        BestQuality = Mathf.Max(BestQuality, quality);

        if (quality >= RequiredQuality)
        {
            Captured = true;
        }
    }
}
