using System.Collections.Generic;
using Godot;
using PhotoStealthPrototype.Player;

namespace PhotoStealthPrototype.Stealth;

/// <summary>
/// Watches every guard in the scene, aggregates the tensest one, and owns the
/// caught / restart flow. Nothing else needs to know how many guards exist.
/// </summary>
[GlobalClass]
public partial class StealthDirector : Node
{
    /// <summary>Group used by nodes that need to find the director without wiring.</summary>
    public const string GroupName = "stealth_director";

    [Signal] public delegate void PlayerCaughtEventHandler();

    /// <summary>Highest detection meter across all guards, 0..1.</summary>
    public float HighestDetection { get; private set; }

    /// <summary>The guard currently closest to catching the player, if any.</summary>
    public GuardController? LeadGuard { get; private set; }

    public bool IsCaught { get; private set; }

    private readonly List<GuardController> _guards = new();

    public override void _Ready()
    {
        AddToGroup(GroupName);

        // Deferred: guards add themselves to the group in their own _Ready, and
        // sibling _Ready order is not guaranteed.
        CallDeferred(nameof(CollectGuards));
    }

    private void CollectGuards()
    {
        _guards.Clear();

        foreach (Node node in GetTree().GetNodesInGroup("guard"))
        {
            if (node is not GuardController guard)
            {
                continue;
            }

            _guards.Add(guard);
            guard.PlayerSpotted += OnPlayerSpotted;
        }

        if (_guards.Count == 0)
        {
            GD.PushWarning("StealthDirector found no guards — nothing will ever detect the player.");
        }
    }

    public override void _Process(double delta)
    {
        HighestDetection = 0f;
        LeadGuard = null;

        foreach (GuardController guard in _guards)
        {
            if (guard.Detection > HighestDetection)
            {
                HighestDetection = guard.Detection;
                LeadGuard = guard;
            }
        }

        if (Input.IsActionJustPressed("restart"))
        {
            Restart();
        }
    }

    /// <summary>
    /// Broadcast a disturbance to every guard within <paramref name="radius"/>,
    /// scaled down with distance so a flash in someone's face is dangerous and one
    /// across the room is a nudge. Returns how many guards reacted.
    /// </summary>
    /// <remarks>
    /// Deliberately ignores line of sight: a camera flash lights the whole room,
    /// and being safely tucked behind a crate is exactly the situation where the
    /// player should still get caught out by firing it.
    /// </remarks>
    public int ReportDisturbance(Vector3 position, float radius, float spike)
    {
        int notified = 0;

        foreach (GuardController guard in _guards)
        {
            float distance = guard.GlobalPosition.DistanceTo(position);
            if (distance > radius)
            {
                continue;
            }

            float falloff = 1f - (distance / Mathf.Max(radius, 0.01f));
            guard.NoticeDisturbance(position, spike * falloff);
            notified++;
        }

        return notified;
    }

    private void OnPlayerSpotted()
    {
        if (IsCaught)
        {
            return;
        }

        IsCaught = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;

        // Discovered means discovered — no walking away from the guard that caught
        // you. Releasing the mouse above already stops looking around, and the
        // shutter is gated on a captured mouse, so this is the last input to cut.
        if (GetTree().GetFirstNodeInGroup(PlayerController.GroupName) is PlayerController player)
        {
            player.MovementLocked = true;
        }

        EmitSignal(SignalName.PlayerCaught);
    }

    public void Restart()
    {
        Input.MouseMode = Input.MouseModeEnum.Captured;
        GetTree().ReloadCurrentScene();
    }
}
