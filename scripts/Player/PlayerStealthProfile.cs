using System.Collections.Generic;
using Godot;
using PhotoStealthPrototype.Stealth;

namespace PhotoStealthPrototype.Player;

/// <summary>
/// Collapses stance, motion and surrounding light into a single 0..1+ "exposure"
/// value. Guards multiply their detection rate by it, so this is the one knob
/// that makes crouching in shadow meaningfully safer than sprinting through a
/// lit room.
/// </summary>
/// <remarks>
/// Exposure is deliberately allowed above 1.0: sprinting through bright light
/// should be *worse* than the baseline, not merely capped at it.
/// </remarks>
[GlobalClass]
public partial class PlayerStealthProfile : Node
{
    [ExportGroup("Stance")]
    [Export] public float StandingFactor { get; set; } = 1.0f;
    [Export] public float CrouchingFactor { get; set; } = 0.55f;

    [ExportGroup("Motion")]
    [Export] public float StillFactor { get; set; } = 0.7f;
    [Export] public float WalkingFactor { get; set; } = 1.0f;
    [Export] public float SprintingFactor { get; set; } = 1.4f;

    // AmbientIllumination is the light level used when the player is inside no
    // LightZone. Dark/LitMultiplier bracket the exposure scaling it drives.
    [ExportGroup("Light")]
    [Export(PropertyHint.Range, "0,1,0.01")] public float AmbientIllumination { get; set; } = 0.35f;
    [Export] public float DarkMultiplier { get; set; } = 0.4f;
    [Export] public float LitMultiplier { get; set; } = 1.25f;

    /// <summary>Current exposure. 1.0 is "standing, walking, average light".</summary>
    public float Exposure { get; private set; } = 1.0f;

    /// <summary>Resolved 0..1 light level at the player's feet.</summary>
    public float Illumination { get; private set; }

    private PlayerController _player = null!;
    private readonly List<LightZone> _zones = new();

    public override void _Ready()
    {
        _player = GetParent<PlayerController>();
        Illumination = AmbientIllumination;
    }

    /// <summary>Called by <see cref="LightZone"/> as the player enters it.</summary>
    public void EnterLightZone(LightZone zone)
    {
        if (!_zones.Contains(zone))
        {
            _zones.Add(zone);
        }
    }

    /// <summary>Called by <see cref="LightZone"/> as the player leaves it.</summary>
    public void ExitLightZone(LightZone zone) => _zones.Remove(zone);

    public override void _Process(double delta)
    {
        // Brightest overlapping zone wins — standing where two lights cross
        // should not average out to something safe.
        Illumination = AmbientIllumination;
        foreach (LightZone zone in _zones)
        {
            Illumination = Mathf.Max(Illumination, zone.Illumination);
        }

        float stance = _player.CurrentStance == Stance.Crouching ? CrouchingFactor : StandingFactor;

        float speed01 = Mathf.Clamp(_player.PlanarSpeed / Mathf.Max(_player.WalkSpeed, 0.01f), 0f, 1f);
        float motion = _player.IsSprinting
            ? SprintingFactor
            : Mathf.Lerp(StillFactor, WalkingFactor, speed01);

        float light = Mathf.Lerp(DarkMultiplier, LitMultiplier, Illumination);

        Exposure = Mathf.Max(stance * motion * light, 0.02f);
    }
}
