using Godot;
using PhotoStealthPrototype.Player;

namespace PhotoStealthPrototype.Stealth;

/// <summary>
/// A volume that overrides how brightly lit the player counts as being.
/// </summary>
/// <remarks>
/// Deliberately decoupled from the visual lighting. Keeping it a separate
/// gameplay signal means a designer can make a bright-looking corner
/// mechanically dark (or vice versa) without fighting the renderer — and it
/// keeps detection deterministic, which real light sampling would not be.
/// </remarks>
[GlobalClass]
public partial class LightZone : Area3D
{
    [Export(PropertyHint.Range, "0,1,0.01")] public float Illumination { get; set; } = 1.0f;

    public override void _Ready()
    {
        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
    }

    private void OnBodyEntered(Node3D body)
    {
        if (body is PlayerController player)
        {
            player.Stealth.EnterLightZone(this);
        }
    }

    private void OnBodyExited(Node3D body)
    {
        if (body is PlayerController player)
        {
            player.Stealth.ExitLightZone(this);
        }
    }
}
