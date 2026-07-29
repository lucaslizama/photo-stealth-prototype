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

    /// <summary>
    /// Visual layer the beacon and name tag are drawn on. Excluded from the
    /// first-person camera's cull mask, so a label saying "wall plans" can hover
    /// over the plans in the travelling view without ever landing in a photo.
    /// </summary>
    public const uint MarkerVisualLayer = 4;

    [Export] public string DisplayName { get; set; } = "subject";

    /// <summary>Quality a photo must reach for this subject to count as done.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float RequiredQuality { get; set; } = 0.55f;

    /// <summary>Local half-extents of the subject's box. Drives framing and occlusion.</summary>
    [Export] public Vector3 Extents { get; set; } = new(0.4f, 0.4f, 0.05f);

    /// <summary>Light level used where no <c>LightZone</c> covers the subject.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float AmbientIllumination { get; set; } = 0.3f;

    // The beacon exists because a prop can look like a prop and still not read as
    // an objective. Built in code rather than placed per subject so it cannot drift
    // out of sync with DisplayName, and so any subject added later gets one free.
    [ExportGroup("Beacon")]
    [Export] public bool ShowBeacon { get; set; } = true;

    /// <summary>Gap between the top of the subject's box and the beacon.</summary>
    [Export] public float BeaconGap { get; set; } = 0.45f;
    [Export] public Color PendingColour { get; set; } = new(1.0f, 0.78f, 0.25f);
    [Export] public Color CapturedColour { get; set; } = new(0.45f, 1.0f, 0.55f);
    [Export] public float BobHeight { get; set; } = 0.09f;
    [Export] public float BobSpeed { get; set; } = 2.4f;
    [Export] public float SpinSpeed { get; set; } = 1.1f;

    public bool Captured { get; private set; }
    public float BestQuality { get; private set; }

    /// <summary>
    /// The body this subject is mounted on, if any. Occlusion rays must ignore it
    /// — a subject parented to a guard would otherwise be permanently hidden
    /// behind that guard's own collision capsule.
    /// </summary>
    public Rid? HostRid { get; private set; }

    private readonly Vector3[] _samples = new Vector3[9];
    private Node3D? _beacon;
    private StandardMaterial3D? _beaconMaterial;
    private Label3D? _nameTag;
    private float _beaconRestY;
    private float _time;

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

        if (ShowBeacon)
        {
            BuildBeacon();
        }
    }

    /// <summary>
    /// A downward-pointing pip plus the subject's name, floating above the prop.
    /// </summary>
    /// <remarks>
    /// Purely decorative: no collision anywhere on it, so it cannot block the
    /// occlusion rays that decide whether the subject is visible.
    /// </remarks>
    private void BuildBeacon()
    {
        _beaconRestY = Extents.Y + BeaconGap;

        _beaconMaterial = new StandardMaterial3D
        {
            AlbedoColor = PendingColour,

            // Unshaded so a marker in an unlit corner is still a marker. It is a
            // readability aid, not part of the scene's lighting.
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };

        _beacon = new Node3D { Name = "Beacon", Position = new Vector3(0f, _beaconRestY, 0f) };

        // Four radial segments: a square pyramid reads as a deliberate marker,
        // where a smooth cone just looks like more scenery.
        var pip = new MeshInstance3D
        {
            Name = "Pip",
            Mesh = new CylinderMesh
            {
                TopRadius = 0.19f,
                BottomRadius = 0.0f,
                Height = 0.34f,
                RadialSegments = 4,
                Rings = 0,
            },
            MaterialOverride = _beaconMaterial,
            Layers = MarkerVisualLayer,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        _nameTag = new Label3D
        {
            Name = "NameTag",
            Text = DisplayName,
            Position = new Vector3(0f, 0.4f, 0f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 96,

            // Sized to be read from the top-down camera's ~14m, which is the only
            // place it is ever seen — the first-person camera culls this layer.
            PixelSize = 0.0034f,
            Modulate = PendingColour,
            OutlineSize = 24,
            OutlineModulate = new Color(0f, 0f, 0f, 0.85f),
            Layers = MarkerVisualLayer,

            // Drawn through walls: from overhead the tag matters more than whatever
            // scenery happens to be in front of it.
            NoDepthTest = true,
            Shaded = false,
        };

        _beacon.AddChild(pip);
        _beacon.AddChild(_nameTag);
        AddChild(_beacon);
    }

    public override void _Process(double delta)
    {
        if (_beacon is null)
        {
            return;
        }

        _time += (float)delta;
        _beacon.Position = new Vector3(
            0f, _beaconRestY + (Mathf.Sin(_time * BobSpeed) * BobHeight), 0f);
        _beacon.Rotation = new Vector3(0f, _time * SpinSpeed, 0f);
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

        // Recoloured rather than removed: a passing shot is not necessarily the
        // shot the player wanted, and they may still have film to spend on it.
        if (_beaconMaterial is not null)
        {
            _beaconMaterial.AlbedoColor = Captured ? CapturedColour : PendingColour;
        }

        if (_nameTag is not null)
        {
            _nameTag.Modulate = Captured ? CapturedColour : PendingColour;
            _nameTag.Text = Captured ? $"{DisplayName}  ✓" : DisplayName;
        }
    }
}
