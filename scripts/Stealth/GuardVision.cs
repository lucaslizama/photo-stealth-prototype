using Godot;
using PhotoStealthPrototype.Player;

namespace PhotoStealthPrototype.Stealth;

/// <summary>
/// A guard's eye. Answers one question per physics frame: how strongly can this
/// guard see the player right now, as a 0..1+ value feeding the detection meter.
/// Returns 0 when the player is out of range, outside the cone, or occluded.
/// </summary>
/// <remarks>
/// Place this node at eye height on the guard, facing -Z like every other Godot
/// forward vector. The returned visibility already folds in the player's
/// exposure, so the guard itself only has to integrate it over time.
/// </remarks>
[GlobalClass]
public partial class GuardVision : Node3D
{
    [Export] public float ViewDistance { get; set; } = 16.0f;
    [Export] public float FovDegrees { get; set; } = 100.0f;

    /// <summary>Visibility multiplier at the very edge of the cone.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float EdgeFalloff { get; set; } = 0.35f;

    /// <summary>Within this distance the player reads at full clarity.</summary>
    [Export] public float FullClarityDistance { get; set; } = 4.0f;

    /// <summary>Physics layers that block sight. Defaults to layer 1 ("world").</summary>
    [Export(PropertyHint.Layers3DPhysics)] public uint OcclusionMask { get; set; } = 1;

    public bool HasLineOfSight { get; private set; }
    public float LastVisibility { get; private set; }

    private CollisionObject3D? _ownerBody;

    public override void _Ready()
    {
        for (Node? n = GetParent(); n is not null; n = n.GetParent())
        {
            if (n is CollisionObject3D body)
            {
                _ownerBody = body;
                break;
            }
        }
    }

    /// <summary>Visibility of <paramref name="player"/> this frame, 0 when unseen.</summary>
    public float Evaluate(PlayerController player)
    {
        HasLineOfSight = false;
        LastVisibility = 0f;

        Vector3 eye = GlobalPosition;
        Vector3 target = player.Head.GlobalPosition;
        Vector3 toTarget = target - eye;
        float distance = toTarget.Length();

        if (distance > ViewDistance || distance < 0.001f)
        {
            return 0f;
        }

        Vector3 direction = toTarget / distance;
        Vector3 forward = -GlobalTransform.Basis.Z.Normalized();
        float angle = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(forward.Dot(direction), -1f, 1f)));
        float halfFov = FovDegrees * 0.5f;

        if (angle > halfFov)
        {
            return 0f;
        }

        if (!HasClearLine(eye, target, player))
        {
            return 0f;
        }

        HasLineOfSight = true;

        // Centre of vision reads at full strength, peripheral vision at EdgeFalloff.
        float centred01 = 1f - (angle / Mathf.Max(halfFov, 0.001f));
        float angleFactor = Mathf.Lerp(EdgeFalloff, 1f, centred01);

        float fade = Mathf.Max(ViewDistance - FullClarityDistance, 0.01f);
        float distanceFactor = 1f - Mathf.Clamp((distance - FullClarityDistance) / fade, 0f, 1f);

        LastVisibility = angleFactor * distanceFactor * player.Stealth.Exposure;
        return LastVisibility;
    }

    /// <summary>
    /// True when nothing sits between the eye and the player. The player and the
    /// guard's own body are excluded, so any hit at all is a genuine occluder.
    /// </summary>
    private bool HasClearLine(Vector3 from, Vector3 to, PlayerController player)
    {
        var exclude = new Godot.Collections.Array<Rid> { player.GetRid() };
        if (_ownerBody is not null)
        {
            exclude.Add(_ownerBody.GetRid());
        }

        var query = PhysicsRayQueryParameters3D.Create(from, to, OcclusionMask);
        query.Exclude = exclude;

        return GetWorld3D().DirectSpaceState.IntersectRay(query).Count == 0;
    }
}
