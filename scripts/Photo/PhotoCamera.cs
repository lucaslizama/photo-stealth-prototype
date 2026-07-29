using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using PhotoStealthPrototype.Player;
using PhotoStealthPrototype.Stealth;

namespace PhotoStealthPrototype.Photo;

/// <summary>
/// The player's camera: aim, zoom, flash, and the shutter.
/// </summary>
/// <remarks>
/// Drives the first-person <c>Head/Camera</c> — zoom is just FOV, and because the
/// scorer reads the live projection, zooming improves coverage with no
/// special-casing anywhere.
/// <para>
/// The flash is the point where this system reaches back into stealth: it is the
/// only way to photograph a dark subject, and firing it spikes every nearby
/// guard's detection meter.
/// </para>
/// <para>
/// Film is finite and every press of the shutter spends a frame, hit or miss.
/// That is the whole pressure of the mode: a careless click is a real loss, so
/// framing has to happen before the click rather than by trial and error.
/// </para>
/// </remarks>
[GlobalClass]
public partial class PhotoCamera : Node
{
    [ExportGroup("Aim & zoom")]
    [Export] public float AimFov { get; set; } = 45.0f;
    [Export] public float MinFov { get; set; } = 18.0f;
    [Export] public float MaxFov { get; set; } = 60.0f;
    [Export] public float ZoomStep { get; set; } = 4.0f;
    [Export] public float FovBlendSpeed { get; set; } = 14.0f;

    // FlashStrength is the illumination a point-blank flash provides; it falls off
    // to nothing at FlashRange. AlertRadius/DetectionSpike are the stealth cost.
    [ExportGroup("Flash")]
    [Export] public float FlashStrength { get; set; } = 1.0f;
    [Export] public float FlashRange { get; set; } = 9.0f;
    [Export] public float FlashAlertRadius { get; set; } = 14.0f;
    [Export] public float FlashDetectionSpike { get; set; } = 0.7f;

    [ExportGroup("Framing")]
    [Export] public float MinFill { get; set; } = 0.002f;
    [Export] public float GoodFill { get; set; } = 0.04f;
    [Export] public float MaxFill { get; set; } = 0.5f;
    [Export] public float MinReadableLight { get; set; } = 0.18f;
    [Export] public float GoodLight { get; set; } = 0.62f;
    [Export(PropertyHint.Layers3DPhysics)] public uint OcclusionMask { get; set; } = 1;

    [ExportGroup("Capture")]
    [Export] public int StoredPhotoWidth { get; set; } = 640;
    [Export] public bool SavePngCopies { get; set; } = true;

    /// <summary>Exposures on the roll. Every shutter press spends one.</summary>
    [Export] public int FilmCapacity { get; set; } = 8;

    /// <summary>
    /// Optional explicit director. Left unset it is found via the
    /// <c>StealthDirector.GroupName</c> group — this node lives inside
    /// Player.tscn, so wiring it from Main.tscn would otherwise require enabling
    /// editable children on the instance.
    /// </summary>
    [ExportGroup("Wiring")]
    [Export] public StealthDirector? Director { get; set; }

    /// <summary>CanvasLayers in this group are hidden for the capture frame.</summary>
    public const string HudGroup = "hud";

    [Signal] public delegate void PhotoTakenEventHandler();
    [Signal] public delegate void FlashToggledEventHandler(bool on);

    /// <summary>Shutter pressed with an empty roll. The HUD says so out loud.</summary>
    [Signal] public delegate void OutOfFilmEventHandler();

    /// <summary>True while the viewfinder is up — the only state a photo can be taken in.</summary>
    public bool IsAiming => _player.View == ViewMode.FirstPerson;

    public bool FlashOn { get; private set; }
    public float CurrentFov => _camera?.Fov ?? AimFov;

    /// <summary>Exposures left on the roll.</summary>
    public int ShotsRemaining { get; private set; }

    public bool HasFilm => ShotsRemaining > 0;

    /// <summary>
    /// True while a frame grab is in flight. The album checks this before opening:
    /// it is not in the hud group, so opening it mid-grab would photograph it.
    /// </summary>
    public bool IsCapturing => _capturing;

    /// <summary>Zoom as 0..1, where 1 is fully zoomed in. For the HUD.</summary>
    public float Zoom01 => Mathf.Clamp(1f - ((_aimFov - MinFov) / Mathf.Max(MaxFov - MinFov, 0.01f)), 0f, 1f);

    public PhotoScore LastScore { get; private set; }
    public IReadOnlyList<CapturedPhoto> Photos => _photos;

    /// <summary>
    /// Every subject's score as of this frame, refreshed only while aiming. Lets the
    /// viewfinder bracket and name what the lens is on before a frame is spent on it
    /// — which is what keeps a finite roll fair rather than a guessing game.
    /// </summary>
    public IReadOnlyList<PhotoScore> LiveScores => _liveScores;

    private PlayerController _player = null!;
    private Camera3D _camera = null!;
    private OmniLight3D? _flashLight;
    private readonly List<CapturedPhoto> _photos = new();
    private readonly List<PhotoScore> _liveScores = new();
    private readonly List<PhotoScore> _shutterScores = new();
    private float _aimFov;
    private bool _capturing;

    public override void _Ready()
    {
        _player = GetParent<PlayerController>();

        // Godot readies children before parents, so PlayerController._Ready has
        // NOT run yet and its Head property is still null. Reach the camera by
        // path instead of through the parent's cached reference.
        _camera = _player.GetNode<Camera3D>("Head/Camera");

        // Real light, fired only for the capture frame. Without it the flash would
        // be a pure scoring abstraction and a "95%" dark-room shot would come back
        // as a black image — the score and the photo have to agree.
        _flashLight = _player.GetNodeOrNull<OmniLight3D>("Head/FlashLight");
        if (_flashLight is not null)
        {
            _flashLight.Visible = false;
            _flashLight.OmniRange = FlashRange;
        }

        _aimFov = AimFov;
        _camera.Fov = _aimFov;
        ShotsRemaining = FilmCapacity;

        _player.ViewChanged += OnViewChanged;

        // Same reason, one level up: the director is a later sibling of Player, so
        // it has not joined its group yet. Resolve after the tree settles.
        CallDeferred(nameof(ResolveDirector));
    }

    /// <summary>
    /// Snaps the lens to the dialled-in zoom as the viewfinder comes up. Blending
    /// in from a wider FOV would look nicer but means the first fraction of a
    /// second of every raise is a frame the player did not choose — and the scorer
    /// reads the live projection, so a fast shutter would be graded on it.
    /// </summary>
    private void OnViewChanged(bool firstPerson)
    {
        if (firstPerson)
        {
            _camera.Fov = _aimFov;
        }
    }

    private void ResolveDirector()
    {
        Director ??= GetTree().GetFirstNodeInGroup(StealthDirector.GroupName) as StealthDirector;

        if (Director is null)
        {
            GD.PushWarning("PhotoCamera found no StealthDirector — the flash will not alert guards.");
        }
    }

    public override void _Process(double delta)
    {
        // Only ever chases the zoom setting: the first-person camera is on screen
        // exclusively while aiming, so there is no lowered "hip" FOV to return to.
        // The blend is what makes wheel zoom feel like a lens rather than a step.
        _camera.Fov = Mathf.Lerp(_camera.Fov, _aimFov, 1f - Mathf.Exp(-FovBlendSpeed * (float)delta));

        // Only while aiming: this raycasts every subject's sample points, and there
        // is nothing to draw brackets on when the viewfinder is down.
        if (IsAiming)
        {
            ScoreAllSubjects(_liveScores);
        }
        else if (_liveScores.Count > 0)
        {
            _liveScores.Clear();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("toggle_flash"))
        {
            FlashOn = !FlashOn;
            EmitSignal(SignalName.FlashToggled, FlashOn);
            return;
        }

        // Zoom only while the viewfinder is up, so the wheel stays free otherwise.
        if (IsAiming && @event.IsActionPressed("zoom_in"))
        {
            _aimFov = Mathf.Max(_aimFov - ZoomStep, MinFov);
            return;
        }

        if (IsAiming && @event.IsActionPressed("zoom_out"))
        {
            _aimFov = Mathf.Min(_aimFov + ZoomStep, MaxFov);
            return;
        }

        if (@event.IsActionPressed("shutter") && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            TakePhoto();
        }
    }

    /// <summary>
    /// Spends a frame of film, scores every subject, banks the best one, and fires
    /// the flash consequence. Returns false when the shutter did not fire.
    /// </summary>
    /// <remarks>
    /// Everything that affects game state happens synchronously here; the image
    /// grab is kicked off as best-effort and fills the photo in later. An earlier
    /// version awaited the grab before recording anything, which meant that on a
    /// build with no rendering device (headless, CI) the await never resolved and
    /// the camera silently stopped working after a single shot.
    /// </remarks>
    public bool TakePhoto()
    {
        // The viewfinder has to be up. The shot is framed, scored and grabbed
        // through the first-person lens, and in top-down that camera is not even
        // the one on screen — a shutter there would hand back a photo of a view
        // the player never saw.
        if (!IsAiming)
        {
            return false;
        }

        if (!HasFilm)
        {
            EmitSignal(SignalName.OutOfFilm);
            return false;
        }

        // Spent before the shot is graded, and spent whatever the grade turns out
        // to be. A wasted frame is the cost of a careless click.
        ShotsRemaining--;

        PhotoScore best = ScoreBestSubject();
        LastScore = best;
        best.Subject?.RecordQuality(best.Quality);

        if (FlashOn)
        {
            Director?.ReportDisturbance(_player.GlobalPosition, FlashAlertRadius, FlashDetectionSpike);
        }

        // Appended, not inserted at 0: the album is a roll of film read in the
        // order it was shot, and frame numbers have to agree with that order.
        var photo = new CapturedPhoto(FilmCapacity - ShotsRemaining, best);
        _photos.Add(photo);

        EmitSignal(SignalName.PhotoTaken);

        _ = AttachFrameAsync(photo);
        return true;
    }

    /// <summary>Best-scoring subject currently framed, or a miss if none are.</summary>
    /// <remarks>
    /// Scores afresh rather than reusing <see cref="LiveScores"/>: the shutter has to
    /// grade the instant it was pressed, not whatever the last drawn frame measured.
    /// </remarks>
    public PhotoScore ScoreBestSubject()
    {
        ScoreAllSubjects(_shutterScores);

        PhotoScore best = PhotoScore.Miss(null);

        foreach (PhotoScore score in _shutterScores)
        {
            // Prefer the better shot; when nothing scores, still prefer a subject
            // that was at least on screen so the feedback can be specific.
            bool better = score.Quality > best.Quality
                || (Mathf.IsEqualApprox(score.Quality, best.Quality) && score.InFrame && !best.InFrame);

            if (better)
            {
                best = score;
            }
        }

        return best;
    }

    /// <summary>Grades every subject in the scene into <paramref name="into"/>.</summary>
    private void ScoreAllSubjects(List<PhotoScore> into)
    {
        into.Clear();

        PhotoScoringSettings settings = BuildSettings();
        var exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };

        foreach (Node node in GetTree().GetNodesInGroup(PhotoSubject.GroupName))
        {
            if (node is PhotoSubject subject)
            {
                into.Add(PhotoScorer.Evaluate(_camera, subject, settings, exclude));
            }
        }
    }

    /// <summary>
    /// Dictionary view of the current best shot, for the debug HUD and for the
    /// headless test harness that drives the scoring rubric.
    /// </summary>
    public Godot.Collections.Dictionary DescribeBestShot() => ScoreBestSubject().ToDictionary();

    /// <summary>
    /// Marshallable view of the album, newest frame last. <see cref="Photos"/> is an
    /// <c>IReadOnlyList</c>, which does not cross into GDScript — reading it from a
    /// probe fails with "Invalid access to property". This is how a probe reads it.
    /// </summary>
    public Godot.Collections.Array DescribeAlbum()
    {
        var album = new Godot.Collections.Array();

        foreach (CapturedPhoto photo in _photos)
        {
            album.Add(new Godot.Collections.Dictionary
            {
                { "frame", photo.FrameNumber },
                { "subject", photo.SubjectName },
                { "quality", photo.Quality },
                { "passed", photo.Passed },
                { "diagnosis", photo.Diagnosis },
                { "has_image", photo.Texture is not null },
            });
        }

        return album;
    }

    /// <summary>Forces the lens to a given FOV, bypassing the zoom blend. Test hook.</summary>
    public void ForceFov(float fov)
    {
        _aimFov = Mathf.Clamp(fov, MinFov, MaxFov);
        _camera.Fov = fov;
    }

    /// <summary>Reloads the roll to full. Test hook.</summary>
    public void RefillFilm() => ShotsRemaining = FilmCapacity;

    /// <summary>Turns the flash on or off without going through input. Test hook.</summary>
    public void SetFlash(bool on)
    {
        FlashOn = on;
        EmitSignal(SignalName.FlashToggled, on);
    }

    private PhotoScoringSettings BuildSettings() => new()
    {
        MinFill = MinFill,
        GoodFill = GoodFill,
        MaxFill = MaxFill,
        MinReadableLight = MinReadableLight,
        GoodLight = GoodLight,
        FlashOn = FlashOn,
        FlashStrength = FlashStrength,
        FlashRange = FlashRange,
        OcclusionMask = OcclusionMask,
    };

    /// <summary>
    /// Grabs the player's own viewport with the HUD hidden and attaches the result
    /// to <paramref name="photo"/>. Grabbing the real viewport (rather than
    /// re-rendering through an off-screen SubViewport) means the photo is exactly
    /// what the player framed.
    /// </summary>
    private async Task AttachFrameAsync(CapturedPhoto photo)
    {
        // No rendering device means no pixels, and awaiting FramePostDraw there
        // would never resolve. Bail before touching the HUD.
        if (_capturing || DisplayServer.GetName() == "headless")
        {
            return;
        }

        _capturing = true;

        try
        {
            SetHudVisible(false);

            if (FlashOn && _flashLight is not null)
            {
                _flashLight.Visible = true;
            }

            // A frame has to actually render with the HUD off before the viewport
            // texture reflects it.
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            Image? image = _camera.GetViewport().GetTexture()?.GetImage();

            SetHudVisible(true);

            if (image is null || image.IsEmpty())
            {
                return;
            }

            if (StoredPhotoWidth > 0 && image.GetWidth() > StoredPhotoWidth)
            {
                int height = Mathf.Max(1, (int)(image.GetHeight() * (StoredPhotoWidth / (float)image.GetWidth())));
                image.Resize(StoredPhotoWidth, height, Image.Interpolation.Bilinear);
            }

            if (SavePngCopies)
            {
                SavePng(image, photo);
            }

            photo.Texture = ImageTexture.CreateFromImage(image);
            EmitSignal(SignalName.PhotoTaken);
        }
        finally
        {
            // Restored here too: an exception mid-grab must not leave the HUD
            // hidden, the flash stuck on, or the camera permanently jammed.
            SetHudVisible(true);

            if (_flashLight is not null)
            {
                _flashLight.Visible = false;
            }

            _capturing = false;
        }
    }

    private void SetHudVisible(bool visible)
    {
        foreach (Node node in GetTree().GetNodesInGroup(HudGroup))
        {
            if (node is CanvasLayer layer)
            {
                layer.Visible = visible;
            }
        }
    }

    private void SavePng(Image image, CapturedPhoto photo)
    {
        const string directory = "user://photos";
        DirAccess.MakeDirRecursiveAbsolute(directory);

        string safe = photo.SubjectName.Replace(' ', '-').Replace('/', '-');
        int percent = Mathf.RoundToInt(photo.Quality * 100f);

        // Named by frame number, so the roll restarts each run and files are
        // overwritten rather than piling up.
        Error error = image.SavePng($"{directory}/photo_{photo.FrameNumber:D2}_{safe}_{percent}.png");
        if (error != Error.Ok)
        {
            GD.PushWarning($"Could not save photo PNG: {error}");
        }
    }
}
