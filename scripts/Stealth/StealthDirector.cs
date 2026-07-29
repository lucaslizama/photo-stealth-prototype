using System.Collections.Generic;
using Godot;

namespace PhotoStealthPrototype.Stealth;

/// <summary>
/// Watches every guard in the scene, aggregates the tensest one, and owns the
/// caught / restart flow. Nothing else needs to know how many guards exist.
/// </summary>
[GlobalClass]
public partial class StealthDirector : Node
{
    [Signal] public delegate void PlayerCaughtEventHandler();

    /// <summary>Highest detection meter across all guards, 0..1.</summary>
    public float HighestDetection { get; private set; }

    /// <summary>The guard currently closest to catching the player, if any.</summary>
    public GuardController? LeadGuard { get; private set; }

    public bool IsCaught { get; private set; }

    private readonly List<GuardController> _guards = new();

    public override void _Ready()
    {
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

    private void OnPlayerSpotted()
    {
        if (IsCaught)
        {
            return;
        }

        IsCaught = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        EmitSignal(SignalName.PlayerCaught);
    }

    public void Restart()
    {
        Input.MouseMode = Input.MouseModeEnum.Captured;
        GetTree().ReloadCurrentScene();
    }
}
