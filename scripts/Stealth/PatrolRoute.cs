using System.Collections.Generic;
using Godot;

namespace PhotoStealthPrototype.Stealth;

/// <summary>
/// An ordered set of patrol waypoints, authored as child Node3Ds (Marker3D is
/// the natural choice). Positions are snapshotted at _Ready, so the route can
/// live anywhere in the scene without the guard caring about its transform.
/// </summary>
[GlobalClass]
public partial class PatrolRoute : Node3D
{
    /// <summary>Loop back to the first point, rather than walking the route in reverse.</summary>
    [Export] public bool Loop { get; set; } = true;

    private readonly List<Vector3> _points = new();

    public int Count => _points.Count;

    public override void _Ready()
    {
        foreach (Node child in GetChildren())
        {
            if (child is Node3D marker)
            {
                _points.Add(marker.GlobalPosition);
            }
        }

        if (_points.Count == 0)
        {
            GD.PushWarning($"PatrolRoute '{Name}' has no Node3D children — guards using it will idle.");
        }
    }

    /// <summary>
    /// Position for step <paramref name="index"/>. Loops or ping-pongs past the
    /// end depending on <see cref="Loop"/>, so callers can just keep incrementing.
    /// </summary>
    public Vector3 GetPoint(int index)
    {
        if (_points.Count == 0)
        {
            return GlobalPosition;
        }

        if (Loop)
        {
            return _points[Mathf.PosMod(index, _points.Count)];
        }

        // Ping-pong: 0,1,2,1,0,1,2... over a period of 2n-2.
        int period = Mathf.Max((_points.Count * 2) - 2, 1);
        int step = Mathf.PosMod(index, period);
        return _points[step < _points.Count ? step : period - step];
    }
}
