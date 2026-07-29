using Godot;
using PhotoStealthPrototype.Photo;

namespace PhotoStealthPrototype.UI;

/// <summary>
/// Viewfinder chrome, the shot list, the film counter, and per-shot feedback.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="StealthHud"/> so each script has one job, and
/// because <c>PhotoCamera</c> hides whole CanvasLayers for the capture frame —
/// having the photo UI be its own layer makes that a one-line toggle. Reading the
/// photos back is <see cref="AlbumScreen"/>'s job, which is a third layer for the
/// same reason plus one more: it must survive that restore-everything sweep.
/// <para>
/// Every Control here sets <c>MouseFilter.Ignore</c>. With the mouse captured it
/// sits at screen centre, so a viewfinder panel with the default STOP filter
/// would silently swallow the shutter click.
/// </para>
/// </remarks>
[GlobalClass]
public partial class PhotoHud : CanvasLayer
{
    [Export] public PhotoCamera? Camera { get; set; }
    [Export] public PhotoMission? Mission { get; set; }

    private Control _viewfinder = null!;
    private Label _zoomLabel = null!;
    private Label _flashLabel = null!;
    private Label _filmLabel = null!;
    private VBoxContainer _objectives = null!;
    private Label _lastShot = null!;
    private Label _completeLabel = null!;

    private static readonly Color Good = new(0.60f, 1.00f, 0.65f);
    private static readonly Color Warn = new(1.00f, 0.80f, 0.55f);
    private static readonly Color Bad = new(1.00f, 0.45f, 0.40f);
    private static readonly Color Idle = new(0.78f, 0.81f, 0.85f);

    public override void _Ready()
    {
        AddToGroup(PhotoCamera.HudGroup);

        _viewfinder = GetNode<Control>("Viewfinder");
        _zoomLabel = GetNode<Label>("Status/ZoomLabel");
        _flashLabel = GetNode<Label>("Status/FlashLabel");
        _filmLabel = GetNode<Label>("Status/FilmLabel");
        _objectives = GetNode<VBoxContainer>("Objectives");
        _lastShot = GetNode<Label>("LastShot");
        _completeLabel = GetNode<Label>("CompleteLabel");

        _viewfinder.Visible = false;
        _completeLabel.Visible = false;
        _lastShot.Text = string.Empty;

        // Forwarded rather than exported on the child: PhotoCamera lives in the
        // Player instance over in Main.tscn, which a NodePath inside this scene
        // cannot reach.
        GetNode<ViewfinderTargets>("Viewfinder/Targets").Camera = Camera;

        if (Camera is not null)
        {
            Camera.PhotoTaken += OnPhotoTaken;
            Camera.OutOfFilm += OnOutOfFilm;
        }

        if (Mission is not null)
        {
            Mission.ProgressChanged += RebuildObjectives;
            Mission.MissionComplete += () => _completeLabel.Visible = true;
        }

        RebuildObjectives();
    }

    public override void _Process(double delta)
    {
        if (Camera is null)
        {
            return;
        }

        _viewfinder.Visible = Camera.IsAiming;

        _zoomLabel.Text = Camera.IsAiming
            ? $"zoom {Camera.Zoom01:P0}  ({Camera.CurrentFov:0}°)"
            : "camera lowered  (hold RMB to raise)";

        _flashLabel.Text = Camera.FlashOn ? "flash ON — guards will notice" : "flash off  (F)";
        _flashLabel.Modulate = Camera.FlashOn ? Warn : Idle;

        // Drawn as the roll itself, not just a number: the point of the limit is
        // that the player feels it shrinking before they click, not after.
        int shot = Camera.FilmCapacity - Camera.ShotsRemaining;
        _filmLabel.Text = Camera.HasFilm
            ? $"film {new string('●', Camera.ShotsRemaining)}{new string('○', shot)}  "
              + $"{Camera.ShotsRemaining}/{Camera.FilmCapacity}"
            : "OUT OF FILM  (R to restart)";
        _filmLabel.Modulate = Camera.ShotsRemaining switch
        {
            0 => Bad,
            <= 2 => Warn,
            _ => Idle,
        };
    }

    private void OnPhotoTaken()
    {
        if (Camera is null)
        {
            return;
        }

        PhotoScore score = Camera.LastScore;
        string subject = score.Subject?.DisplayName ?? "nothing";

        _lastShot.Text =
            $"frame spent — {subject} {score.Quality:P0}   ({score.Diagnose()})\n"
            + $"coverage {score.Coverage:0.00}   centred {score.Centering:0.00}   "
            + $"visible {score.Visibility:0.00}   light {score.Lighting:0.00}   ·   Tab for album";

        bool passed = score.Subject is not null && score.Quality >= score.Subject.RequiredQuality;
        _lastShot.Modulate = passed ? Good : Warn;

        RebuildObjectives();
    }

    private void OnOutOfFilm()
    {
        _lastShot.Text = "out of film — no frames left on the roll   ·   R to restart";
        _lastShot.Modulate = Bad;
    }

    private void RebuildObjectives()
    {
        foreach (Node child in _objectives.GetChildren())
        {
            child.QueueFree();
        }

        if (Mission is null)
        {
            return;
        }

        _objectives.AddChild(MakeLabel(
            $"shot list — {Mission.CapturedCount}/{Mission.Subjects.Count}", Idle));

        foreach (PhotoSubject subject in Mission.Subjects)
        {
            string text = subject.Captured
                ? $"  [x] {subject.DisplayName}  {subject.BestQuality:P0}"
                : $"  [ ] {subject.DisplayName}  (best {subject.BestQuality:P0}, need {subject.RequiredQuality:P0})";

            _objectives.AddChild(MakeLabel(text, subject.Captured ? Good : Idle));
        }
    }

    private static Label MakeLabel(string text, Color colour) => new()
    {
        Text = text,
        Modulate = colour,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };
}
