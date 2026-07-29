using Godot;
using PhotoStealthPrototype.Stealth;

namespace PhotoStealthPrototype.Photo;

/// <summary>
/// Grades a shot of one subject through a live camera.
/// </summary>
/// <remarks>
/// The three terms multiply rather than sum: a beautifully framed photo of
/// something behind a crate is worth nothing, and so is a perfectly visible
/// subject in pitch darkness. Multiplying makes any single failure fatal, which
/// is both the behaviour we want and easy to reason about while tuning.
/// Static and dependency-light on purpose — this is the part worth testing
/// headlessly by parking a camera at known transforms.
/// </remarks>
public static class PhotoScorer
{
    public static PhotoScore Evaluate(
        Camera3D camera,
        PhotoSubject subject,
        in PhotoScoringSettings settings,
        Godot.Collections.Array<Rid> exclude)
    {
        Vector2 viewport = camera.GetViewport().GetVisibleRect().Size;
        if (viewport.X < 1f || viewport.Y < 1f)
        {
            return PhotoScore.Miss(subject);
        }

        Vector3[] samples = subject.GetSamplePoints();
        Vector3 centre = samples[0];

        // Hard gate: the subject's centre must actually be on screen. Without
        // this, UnprojectPosition happily returns coordinates for things behind
        // the camera and a subject at your back would score.
        if (camera.IsPositionBehind(centre))
        {
            return PhotoScore.Miss(subject);
        }

        Vector2 centreScreen = camera.UnprojectPosition(centre);
        if (centreScreen.X < 0f || centreScreen.Y < 0f
            || centreScreen.X > viewport.X || centreScreen.Y > viewport.Y)
        {
            return PhotoScore.Miss(subject);
        }

        // Screen-space bounds across every sample in front of the lens.
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        Vector2 sum = Vector2.Zero;
        int projected = 0;

        foreach (Vector3 point in samples)
        {
            if (camera.IsPositionBehind(point))
            {
                continue;
            }

            Vector2 s = camera.UnprojectPosition(point);
            minX = Mathf.Min(minX, s.X);
            minY = Mathf.Min(minY, s.Y);
            maxX = Mathf.Max(maxX, s.X);
            maxY = Mathf.Max(maxY, s.Y);
            sum += s;
            projected++;
        }

        if (projected == 0)
        {
            return PhotoScore.Miss(subject);
        }

        Vector2 centroid = sum / projected;

        float boxArea = Mathf.Max(maxX - minX, 0f) * Mathf.Max(maxY - minY, 0f);
        float fill = boxArea / (viewport.X * viewport.Y);
        float coverage = ScoreFill(fill, settings);

        Vector2 half = viewport * 0.5f;
        float offset = (centroid - half).Length() / Mathf.Max(half.Length(), 0.001f);
        float centering = Mathf.Clamp(1f - offset, 0f, 1f);

        float visibility = ScoreVisibility(camera, subject, samples, settings, exclude);

        float illumination = LightField.SampleIllumination(
            camera.GetWorld3D(), centre, subject.AmbientIllumination);

        if (settings.FlashOn)
        {
            // Flash falls off with distance, so it cannot rescue a subject you
            // are sniping from across the room.
            float distance = camera.GlobalPosition.DistanceTo(centre);
            float falloff = 1f - Mathf.Clamp(distance / Mathf.Max(settings.FlashRange, 0.01f), 0f, 1f);
            illumination = Mathf.Max(illumination, settings.FlashStrength * falloff);
        }

        float lightSpan = Mathf.Max(settings.GoodLight - settings.MinReadableLight, 0.01f);
        float lighting = Mathf.Clamp((illumination - settings.MinReadableLight) / lightSpan, 0f, 1f);

        // Coverage dominates: a pin-sharp centred speck is still a bad photo, and a
        // 60/40 split let a distant subject coast to ~0.4 on centring alone.
        float framing = (0.7f * coverage) + (0.3f * centering);

        return new PhotoScore
        {
            Subject = subject,
            InFrame = true,
            Coverage = coverage,
            Centering = centering,
            Visibility = visibility,
            Lighting = lighting,
            Illumination = illumination,
            Quality = framing * visibility * lighting,
        };
    }

    /// <summary>
    /// Fraction of sample points with a clear line from the lens. The subject's
    /// own host body is excluded, or anything mounted on a guard would read as
    /// permanently hidden behind that guard.
    /// </summary>
    private static float ScoreVisibility(
        Camera3D camera,
        PhotoSubject subject,
        Vector3[] samples,
        in PhotoScoringSettings settings,
        Godot.Collections.Array<Rid> exclude)
    {
        var ignore = new Godot.Collections.Array<Rid>(exclude);
        if (subject.HostRid is { } host)
        {
            ignore.Add(host);
        }

        PhysicsDirectSpaceState3D space = camera.GetWorld3D().DirectSpaceState;
        Vector3 lens = camera.GlobalPosition;
        int clear = 0;

        foreach (Vector3 point in samples)
        {
            var query = PhysicsRayQueryParameters3D.Create(lens, point, settings.OcclusionMask);
            query.Exclude = ignore;

            if (space.IntersectRay(query).Count == 0)
            {
                clear++;
            }
        }

        return (float)clear / samples.Length;
    }

    /// <summary>
    /// Coverage is scored in a band, not monotonically: a subject filling 2% of
    /// frame is a distant speck, and one filling 90% is cropped. Both are bad
    /// photos.
    /// </summary>
    private static float ScoreFill(float fill, in PhotoScoringSettings settings)
    {
        if (fill <= settings.MinFill)
        {
            return 0f;
        }

        if (fill < settings.GoodFill)
        {
            return (fill - settings.MinFill) / Mathf.Max(settings.GoodFill - settings.MinFill, 0.0001f);
        }

        if (fill <= settings.MaxFill)
        {
            return 1f;
        }

        float over = (fill - settings.MaxFill) / Mathf.Max(1f - settings.MaxFill, 0.0001f);
        return Mathf.Lerp(1f, 0.45f, Mathf.Clamp(over, 0f, 1f));
    }
}
