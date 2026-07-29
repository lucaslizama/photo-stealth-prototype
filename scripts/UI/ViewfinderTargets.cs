using Godot;
using PhotoStealthPrototype.Photo;

namespace PhotoStealthPrototype.UI;

/// <summary>
/// Brackets every subject visible through the viewfinder and names it, with the
/// grade it would score right now.
/// </summary>
/// <remarks>
/// Answers "is this the thing I am supposed to photograph?" at the moment the
/// question is actually being asked. Lives under the Viewfinder Control, so it
/// inherits that node's visibility and appears only while aiming — and being inside
/// a <c>hud</c>-group CanvasLayer, it is hidden for the capture frame like the rest
/// of the HUD, so the brackets never end up in the photo.
/// </remarks>
[GlobalClass]
public partial class ViewfinderTargets : Control
{
    [Export] public PhotoCamera? Camera { get; set; }

    /// <summary>
    /// Show the numeric grade next to the name. On by default: with a finite roll,
    /// hiding it makes the player spend frames to discover what the scorer already
    /// knows. Turn it off for a harsher, judge-it-yourself run.
    /// </summary>
    [Export] public bool ShowLiveQuality { get; set; } = true;

    /// <summary>Below this grade a subject is bracketed but marked as a bad shot.</summary>
    [Export] public float CornerLength { get; set; } = 14.0f;
    [Export] public float Thickness { get; set; } = 2.0f;

    private static readonly Color Pass = new(0.55f, 1.00f, 0.60f);
    private static readonly Color Partial = new(1.00f, 0.80f, 0.35f);
    private static readonly Color Fail = new(1.00f, 0.45f, 0.42f);

    public override void _Ready()
    {
        // With the mouse captured the cursor sits at screen centre, so anything but
        // Ignore here would swallow the shutter click.
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        if (Camera is not null && Camera.LiveScores.Count > 0)
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (Camera is null)
        {
            return;
        }

        Font font = GetThemeDefaultFont();
        int fontSize = GetThemeDefaultFontSize();

        foreach (PhotoScore score in Camera.LiveScores)
        {
            if (!score.InFrame || score.Subject is null)
            {
                continue;
            }

            Rect2 rect = ToLocalRect(score.ScreenRect);
            Color colour = ColourFor(score);

            DrawCorners(rect, colour);

            string label = score.Subject.DisplayName;
            if (ShowLiveQuality)
            {
                label += $"   {score.Quality:P0}";
            }

            if (score.Subject.Captured)
            {
                label += "  ✓";
            }

            // Name above the box, the reason it is not a keeper below it.
            DrawString(
                font, new Vector2(rect.Position.X, rect.Position.Y - 6f), label,
                HorizontalAlignment.Left, -1, fontSize, colour);

            if (score.Quality < score.Subject.RequiredQuality)
            {
                DrawString(
                    font, new Vector2(rect.Position.X, rect.End.Y + fontSize + 2f), score.Diagnose(),
                    HorizontalAlignment.Left, -1, fontSize, colour);
            }
        }
    }

    /// <summary>
    /// Converts the scorer's viewport-pixel rect into this Control's coordinates.
    /// </summary>
    /// <remarks>
    /// These are not the same space. <c>window/stretch/mode="canvas_items"</c> means
    /// the 3D view renders at the real window size while the HUD works in the
    /// 1152x648 design space — so <c>UnprojectPosition</c>'s pixels are larger than
    /// this node's. Both cover exactly the same visible (letterboxed) area though, so
    /// normalising by the viewport size and rescaling by this node's own size is
    /// correct at any resolution, and needs no knowledge of the scale factor.
    /// </remarks>
    private Rect2 ToLocalRect(Rect2 viewportRect)
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        if (viewport.X < 1f || viewport.Y < 1f)
        {
            return viewportRect;
        }

        Vector2 scale = Size / viewport;
        return new Rect2(viewportRect.Position * scale, viewportRect.Size * scale);
    }

    private Color ColourFor(PhotoScore score)
    {
        if (score.Subject is null)
        {
            return Fail;
        }

        if (score.Quality >= score.Subject.RequiredQuality)
        {
            return Pass;
        }

        return score.Quality > 0.01f ? Partial : Fail;
    }

    /// <summary>
    /// Corner ticks rather than a full outline: a closed box over the subject fights
    /// with judging the photo, which is the one thing the viewfinder exists for.
    /// </summary>
    private void DrawCorners(Rect2 rect, Color colour)
    {
        // Clamped so a subject larger than its own bracket corners still reads as a
        // bracket instead of four overlapping crosses.
        float len = Mathf.Min(CornerLength, Mathf.Min(rect.Size.X, rect.Size.Y) * 0.4f);
        if (len < 2f)
        {
            return;
        }

        Vector2 tl = rect.Position;
        var tr = new Vector2(rect.End.X, rect.Position.Y);
        var bl = new Vector2(rect.Position.X, rect.End.Y);
        Vector2 br = rect.End;

        DrawLine(tl, tl + new Vector2(len, 0f), colour, Thickness);
        DrawLine(tl, tl + new Vector2(0f, len), colour, Thickness);

        DrawLine(tr, tr - new Vector2(len, 0f), colour, Thickness);
        DrawLine(tr, tr + new Vector2(0f, len), colour, Thickness);

        DrawLine(bl, bl + new Vector2(len, 0f), colour, Thickness);
        DrawLine(bl, bl - new Vector2(0f, len), colour, Thickness);

        DrawLine(br, br - new Vector2(len, 0f), colour, Thickness);
        DrawLine(br, br - new Vector2(0f, len), colour, Thickness);
    }
}
