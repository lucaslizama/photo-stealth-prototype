using System.Collections.Generic;
using Godot;

namespace PhotoStealthPrototype.Stealth;

/// <summary>
/// Shared illumination lookup.
/// </summary>
/// <remarks>
/// <see cref="LightZone"/> reports the <em>player</em> entering it via
/// <c>BodyEntered</c>, which is fine for the player but useless for anything
/// that is not a physics body — photo subjects, for instance. Those need to ask
/// "how lit is this world point?" directly, so the zones also sit on a dedicated
/// physics layer that can be point-queried.
/// </remarks>
public static class LightField
{
    /// <summary>Physics layer the zones occupy (layer 4) so points can be queried.</summary>
    public const uint LightLayer = 8;

    /// <summary>Brightest zone containing <paramref name="point"/>, else <paramref name="ambient"/>.</summary>
    public static float SampleIllumination(World3D world, Vector3 point, float ambient)
    {
        var query = new PhysicsPointQueryParameters3D
        {
            Position = point,
            CollideWithAreas = true,
            CollideWithBodies = false,
            CollisionMask = LightLayer,
        };

        Godot.Collections.Array<Godot.Collections.Dictionary> hits =
            world.DirectSpaceState.IntersectPoint(query, 16);

        float best = ambient;
        foreach (Godot.Collections.Dictionary hit in hits)
        {
            if (hit.ContainsKey("collider") && hit["collider"].As<GodotObject>() is LightZone zone)
            {
                best = Mathf.Max(best, zone.Illumination);
            }
        }

        return best;
    }

    /// <summary>
    /// Brightest zone wins, falling back to <paramref name="ambient"/>. Standing
    /// where two lights cross should not average out to something safe.
    /// </summary>
    public static float Combine(IEnumerable<LightZone> zones, float ambient)
    {
        float best = ambient;
        foreach (LightZone zone in zones)
        {
            best = Mathf.Max(best, zone.Illumination);
        }

        return best;
    }
}
