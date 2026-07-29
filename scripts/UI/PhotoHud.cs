using Godot;
using PhotoStealthPrototype.Photo;

namespace PhotoStealthPrototype.UI;

/// <summary>
/// Viewfinder chrome, the shot list, per-shot feedback, and the review gallery.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="StealthHud"/> so each script has one job, and
/// because <c>PhotoCamera</c> hides whole CanvasLayers for the capture frame —
/// having the photo UI be its own layer makes that a one-line toggle.
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
    [Export] public int GalleryColumns { get; set; } = 3;
    [Export] public int ThumbnailWidth { get; set; } = 240;

    private Control _viewfinder = null!;
    private Label _zoomLabel = null!;
    private Label _flashLabel = null!;
    private VBoxContainer _objectives = null!;
    private Label _lastShot = null!;
    private Label _completeLabel = null!;
    private Control _gallery = null!;
    private GridContainer _grid = null!;

    private static readonly Color Good = new(0.60f, 1.00f, 0.65f);
    private static readonly Color Warn = new(1.00f, 0.80f, 0.55f);
    private static readonly Color Idle = new(0.78f, 0.81f, 0.85f);

    public override void _Ready()
    {
        AddToGroup(PhotoCamera.HudGroup);

        _viewfinder = GetNode<Control>("Viewfinder");
        _zoomLabel = GetNode<Label>("Status/ZoomLabel");
        _flashLabel = GetNode<Label>("Status/FlashLabel");
        _objectives = GetNode<VBoxContainer>("Objectives");
        _lastShot = GetNode<Label>("LastShot");
        _completeLabel = GetNode<Label>("CompleteLabel");
        _gallery = GetNode<Control>("Gallery");
        _grid = GetNode<GridContainer>("Gallery/Grid");

        _grid.Columns = GalleryColumns;
        _viewfinder.Visible = false;
        _gallery.Visible = false;
        _completeLabel.Visible = false;
        _lastShot.Text = string.Empty;

        if (Camera is not null)
        {
            Camera.PhotoTaken += OnPhotoTaken;
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
            : "camera lowered  (hold RMB)";

        _flashLabel.Text = Camera.FlashOn ? "flash ON — guards will notice" : "flash off  (F)";
        _flashLabel.Modulate = Camera.FlashOn ? Warn : Idle;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("review_photos"))
        {
            return;
        }

        _gallery.Visible = !_gallery.Visible;

        if (_gallery.Visible)
        {
            RebuildGallery();
        }
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
            $"{subject} — {score.Quality:P0}   ({score.Diagnose()})\n"
            + $"coverage {score.Coverage:0.00}   centred {score.Centering:0.00}   "
            + $"visible {score.Visibility:0.00}   light {score.Lighting:0.00}";

        bool passed = score.Subject is not null && score.Quality >= score.Subject.RequiredQuality;
        _lastShot.Modulate = passed ? Good : Warn;

        RebuildObjectives();

        if (_gallery.Visible)
        {
            RebuildGallery();
        }
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

    private void RebuildGallery()
    {
        foreach (Node child in _grid.GetChildren())
        {
            child.QueueFree();
        }

        if (Camera is null)
        {
            return;
        }

        if (Camera.Photos.Count == 0)
        {
            _grid.AddChild(MakeLabel("no photos yet — hold RMB and click to shoot", Idle));
            return;
        }

        foreach (CapturedPhoto photo in Camera.Photos)
        {
            var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };

            if (photo.Texture is not null)
            {
                box.AddChild(new TextureRect
                {
                    Texture = photo.Texture,
                    CustomMinimumSize = new Vector2(ThumbnailWidth, ThumbnailWidth * 9f / 16f),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                });
            }
            else
            {
                box.AddChild(MakeLabel("(no image captured)", Warn));
            }

            box.AddChild(MakeLabel($"{photo.SubjectName} — {photo.Quality:P0}", Idle));
            box.AddChild(MakeLabel(photo.Diagnosis, Warn));
            _grid.AddChild(box);
        }
    }

    private static Label MakeLabel(string text, Color colour) => new()
    {
        Text = text,
        Modulate = colour,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };
}
