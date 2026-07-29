using System.Collections.Generic;
using Godot;
using PhotoStealthPrototype.Photo;
using PhotoStealthPrototype.Stealth;

namespace PhotoStealthPrototype.UI;

/// <summary>
/// The album: the roll of film read back one frame at a time, with the score
/// breakdown that explains each grade.
/// </summary>
/// <remarks>
/// A separate CanvasLayer from <see cref="PhotoHud"/> and deliberately
/// <em>not</em> in <c>PhotoCamera.HudGroup</c>. The capture frame hides every hud
/// layer and then restores them all to visible — which would pop the album open
/// on its own. <see cref="PhotoCamera.IsCapturing"/> covers the other half of that
/// race: opening mid-grab would photograph this screen.
/// <para>
/// Pauses the tree while open, so studying a bad shot is not also a free look at
/// the patrol you are hiding from. That needs <c>ProcessMode.Always</c> to keep
/// receiving input, and it is what lets the pause double as an input lock —
/// everything else in the scene stops seeing keys entirely.
/// </para>
/// </remarks>
[GlobalClass]
public partial class AlbumScreen : CanvasLayer
{
    [Export] public PhotoCamera? Camera { get; set; }
    [Export] public PhotoMission? Mission { get; set; }

    /// <summary>Widest a strip slot may be. Slots shrink below this to fit the roll.</summary>
    [Export] public int MaxThumbnailWidth { get; set; } = 128;

    /// <summary>Gap between strip slots. Must match the scene's Strip separation.</summary>
    [Export] public int StripSeparation { get; set; } = 6;

    private Panel _panel = null!;
    private Label _header = null!;
    private TextureRect _large = null!;
    private Label _placeholder = null!;
    private Label _caption = null!;
    private Label _breakdown = null!;
    private HBoxContainer _strip = null!;

    private readonly List<Button> _thumbnails = new();
    private int _selected;
    private Input.MouseModeEnum _mouseModeBeforeOpen = Input.MouseModeEnum.Captured;

    /// <summary>Total horizontal padding the Layout container takes out of the Panel.</summary>
    private const float LayoutMargin = 40f;

    private static readonly Color Good = new(0.60f, 1.00f, 0.65f);
    private static readonly Color Warn = new(1.00f, 0.80f, 0.55f);
    private static readonly Color Idle = new(0.78f, 0.81f, 0.85f);
    private static readonly Color Faint = new(0.45f, 0.48f, 0.55f);

    public override void _Ready()
    {
        // Always, so the screen keeps taking input after it pauses the tree.
        ProcessMode = ProcessModeEnum.Always;

        _panel = GetNode<Panel>("Panel");
        _header = GetNode<Label>("Panel/Layout/Header");
        _large = GetNode<TextureRect>("Panel/Layout/Frame/Large");
        _placeholder = GetNode<Label>("Panel/Layout/Frame/Placeholder");
        _caption = GetNode<Label>("Panel/Layout/Caption");
        _breakdown = GetNode<Label>("Panel/Layout/Breakdown");
        _strip = GetNode<HBoxContainer>("Panel/Layout/Strip");

        Visible = false;

        if (Camera is not null)
        {
            // Fires twice per shot — once on the shutter, once when the image
            // arrives — so an album left open while a grab lands still fills in.
            Camera.PhotoTaken += OnPhotoTaken;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("review_photos"))
        {
            if (Visible)
            {
                Close();
            }
            else
            {
                Open();
            }

            return;
        }

        // Below here the album owns the keys. Guarded on Visible so a closed album
        // does not eat ui_cancel from the player's own mouse-release toggle; while
        // it is open the pause has already stopped everything else from listening.
        if (!Visible)
        {
            return;
        }

        if (@event.IsActionPressed("ui_cancel"))
        {
            Close();
            return;
        }

        // Running out of film is exactly when the player opens the album, so R has to
        // work from in here. Closing FIRST is load-bearing: SceneTree.Paused survives
        // ReloadCurrentScene, so restarting straight from the album would drop the
        // player into a frozen fresh scene.
        if (@event.IsActionPressed("restart"))
        {
            Close();
            (GetTree().GetFirstNodeInGroup(StealthDirector.GroupName) as StealthDirector)?.Restart();
            return;
        }

        if (@event.IsActionPressed("ui_right") || @event.IsActionPressed("ui_down"))
        {
            Select(_selected + 1);
            return;
        }

        if (@event.IsActionPressed("ui_left") || @event.IsActionPressed("ui_up"))
        {
            Select(_selected - 1);
        }
    }

    private void Open()
    {
        // A grab in flight is waiting on the next rendered frame with the hud
        // hidden. This layer is not hud, so showing it now would put the album
        // itself in the photo.
        if (Camera is null || Camera.IsCapturing)
        {
            return;
        }

        _mouseModeBeforeOpen = Input.MouseMode;

        Visible = true;
        SetGameHudVisible(false);
        GetTree().Paused = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;

        Rebuild();
        Select(Camera.Photos.Count - 1);
    }

    private void Close()
    {
        Visible = false;
        SetGameHudVisible(true);
        GetTree().Paused = false;

        // Restored rather than forced back to Captured: getting caught frees the
        // cursor on purpose, and closing the album must not quietly undo that.
        Input.MouseMode = _mouseModeBeforeOpen;
    }

    /// <summary>
    /// Clears the in-game HUD out of the way. The album panel does not cover the
    /// full screen, so without this the film counter and control hints keep
    /// showing around its edges and read as part of the album.
    /// </summary>
    /// <remarks>
    /// Safe to collide with <c>PhotoCamera</c>'s identical sweep only because the
    /// two can never interleave: a capture cannot start while the tree is paused,
    /// and <see cref="PhotoCamera.IsCapturing"/> blocks opening during one.
    /// </remarks>
    private void SetGameHudVisible(bool visible)
    {
        foreach (Node node in GetTree().GetNodesInGroup(PhotoCamera.HudGroup))
        {
            if (node is CanvasLayer layer)
            {
                layer.Visible = visible;
            }
        }
    }

    private void OnPhotoTaken()
    {
        if (Visible)
        {
            Rebuild();
            Select(_selected);
        }
    }

    private void Rebuild()
    {
        foreach (Node child in _strip.GetChildren())
        {
            _strip.RemoveChild(child);
            child.QueueFree();
        }

        if (Camera is null)
        {
            return;
        }

        int shot = Camera.Photos.Count;
        string objectives = Mission is null
            ? string.Empty
            : $"     shot list {Mission.CapturedCount}/{Mission.Subjects.Count}";

        _header.Text = $"ALBUM      {shot} of {Camera.FilmCapacity} frames used{objectives}";
        _header.Modulate = Camera.HasFilm ? Idle : Warn;

        _thumbnails.Clear();
        float slot = SlotWidth(Camera.FilmCapacity);

        for (int i = 0; i < Camera.Photos.Count; i++)
        {
            _strip.AddChild(MakeThumbnail(Camera.Photos[i], i, slot));
        }

        // Unshot frames are drawn too. The budget is the mechanic, so it should be
        // visible as film left on the roll rather than only as a number.
        for (int i = 0; i < Camera.ShotsRemaining; i++)
        {
            _strip.AddChild(MakeEmptySlot(slot));
        }
    }

    /// <summary>
    /// Slot width that fits a whole roll of <paramref name="slots"/> frames.
    /// </summary>
    /// <remarks>
    /// Derived rather than fixed because the strip always holds exactly
    /// <c>FilmCapacity</c> slots, and that is an export. At the default 8 the fixed
    /// 128 happened to overflow the panel by ~46px — and a VBoxContainer forced
    /// wider than its parent Panel drags its other children off-centre with it, so
    /// the symptom showed up as the large photo sitting to the right rather than as
    /// anything obviously wrong with the strip.
    /// </remarks>
    private float SlotWidth(int slots)
    {
        if (slots <= 0)
        {
            return MaxThumbnailWidth;
        }

        // The Panel is not a container, so its width comes from its own anchors and
        // is unaffected by whatever the strip is currently demanding.
        float available = _panel.Size.X - LayoutMargin - (StripSeparation * (slots - 1));
        return Mathf.Clamp(available / slots, 32f, MaxThumbnailWidth);
    }

    /// <summary>
    /// One frame on the strip: a clickable image with its grade underneath.
    /// </summary>
    /// <remarks>
    /// The pass/fail colour goes on the label, never on the button — modulating the
    /// button tints the photo inside it, and a green wash over the very image the
    /// player is trying to judge defeats the point of showing it.
    /// </remarks>
    private VBoxContainer MakeThumbnail(CapturedPhoto photo, int index, float width)
    {
        var button = new Button
        {
            CustomMinimumSize = new Vector2(width, width * 9f / 16f),
            Icon = photo.Texture,
            ExpandIcon = true,
            ToggleMode = true,
            ButtonPressed = index == _selected,
            TooltipText = $"frame {photo.FrameNumber} — {photo.SubjectName}",
        };

        button.Pressed += () => Select(index);
        _thumbnails.Add(button);

        var label = new Label
        {
            Text = $"{photo.FrameNumber}  {photo.Quality:P0}",
            Modulate = photo.Passed ? Good : Warn,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(width, 0f) };
        box.AddChild(button);
        box.AddChild(label);
        return box;
    }

    private Panel MakeEmptySlot(float width) => new()
    {
        CustomMinimumSize = new Vector2(width, (width * 9f / 16f) + 22f),
        Modulate = new Color(1f, 1f, 1f, 0.15f),
    };

    private void Select(int index)
    {
        if (Camera is null || Camera.Photos.Count == 0)
        {
            _large.Texture = null;
            _placeholder.Visible = true;
            _placeholder.Text = "no frames shot yet\n\nhold RMB to raise the camera, click to shoot";
            _caption.Text = string.Empty;
            _breakdown.Text = string.Empty;
            return;
        }

        _selected = Mathf.Clamp(index, 0, Camera.Photos.Count - 1);
        CapturedPhoto photo = Camera.Photos[_selected];

        _large.Texture = photo.Texture;

        // A shot with no pixels is not an error worth hiding: headless runs have no
        // rendering device at all, and saying so beats an unexplained blank panel.
        _placeholder.Visible = photo.Texture is null;
        _placeholder.Text = "(no image — nothing was rendered for this frame)";

        _caption.Text = photo.SubjectName == "nothing"
            ? $"frame {photo.FrameNumber} — wasted, {photo.Diagnosis}"
            : $"frame {photo.FrameNumber} — {photo.SubjectName}   {photo.Quality:P0}"
              + $"   {(photo.Passed ? "PASS" : $"needed {photo.RequiredQuality:P0}")}";
        _caption.Modulate = photo.Passed ? Good : Warn;

        _breakdown.Text =
            $"coverage {photo.Coverage:0.00}    centred {photo.Centering:0.00}    "
            + $"visible {photo.Visibility:0.00}    light {photo.Lighting:0.00}\n{photo.Diagnosis}";
        _breakdown.Modulate = Faint;

        // The strip's toggle state is not self-maintaining — the buttons are not in
        // a ButtonGroup, so the previously selected one stays pressed otherwise.
        for (int i = 0; i < _thumbnails.Count; i++)
        {
            _thumbnails[i].ButtonPressed = i == _selected;
        }
    }
}
