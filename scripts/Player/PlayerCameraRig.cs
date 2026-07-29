using Godot;

namespace PhotoStealthPrototype.Player;

/// <summary>
/// The 3/4 overhead follow camera, and the switch between it and the
/// first-person viewfinder.
/// </summary>
/// <remarks>
/// Lives inside Player.tscn (like <c>PhotoCamera</c>) so the player scene stays
/// self-contained — nothing in Main.tscn has to know a camera rig exists.
/// <para>
/// Owning <em>both</em> cameras' <c>Current</c> flag is the point: exactly one is
/// ever live, and there is one place to look to find out which.
/// </para>
/// </remarks>
[GlobalClass]
public partial class PlayerCameraRig : Node3D
{
    // Pitch is the whole feel of the view, and it has a hard geometric
    // consequence: a wall of height h hides the player whenever they stand closer
    // to it than (h - FocusHeight) / tan(pitch) — 1.19m for a 3m wall at 58°, and
    // no amount of camera Distance changes that (it cancels out). Because YawDegrees
    // is fixed, only ever ONE wall can be between the camera and the player, so the
    // fix is to keep that wall off the top camera's cull mask. See CLAUDE.md.
    [ExportGroup("Framing")]
    [Export(PropertyHint.Range, "20,89,0.5")] public float PitchDegrees { get; set; } = 58.0f;
    [Export(PropertyHint.Range, "-180,180,1")] public float YawDegrees { get; set; }
    [Export] public float Distance { get; set; } = 14.0f;

    /// <summary>Height above the player's feet the camera aims at.</summary>
    [Export] public float FocusHeight { get; set; } = 1.1f;
    [Export] public float Fov { get; set; } = 50.0f;

    [ExportGroup("Follow")]
    [Export] public float FollowSpeed { get; set; } = 9.0f;

    /// <summary>
    /// Camera yaw in radians. <see cref="PlayerController"/> builds its top-down
    /// movement basis from this, which is what makes W mean "up the screen"
    /// regardless of where the body happens to be pointing.
    /// </summary>
    public float Yaw => Mathf.DegToRad(YawDegrees);

    private PlayerController _player = null!;
    private Camera3D _topCamera = null!;
    private Camera3D _firstPersonCamera = null!;
    private bool _snapped;

    public override void _Ready()
    {
        _player = GetParent<PlayerController>();
        _topCamera = GetNode<Camera3D>("TopCamera");

        // Godot readies children before parents, so PlayerController._Ready has
        // not run yet and its Head property is still null. Reach the first-person
        // camera by path instead of through the parent's cached reference.
        _firstPersonCamera = _player.GetNode<Camera3D>("Head/Camera");

        // The rig must not inherit the body's transform. In top-down the body
        // spins to face its own movement, and a camera bolted to that would swing
        // wildly with every change of direction.
        TopLevel = true;

        _player.ViewChanged += OnViewChanged;
        OnViewChanged(_player.View == ViewMode.FirstPerson);
    }

    /// <summary>
    /// Hard cut between the two cameras, deliberately not a blend: the shutter
    /// grabs the live viewport, so a camera mid-interpolation could hand back a
    /// frame that does not match the framing the scorer measured.
    /// </summary>
    private void OnViewChanged(bool firstPerson)
    {
        _firstPersonCamera.Current = firstPerson;
        _topCamera.Current = !firstPerson;
    }

    public override void _Process(double delta)
    {
        Vector3 focus = _player.GlobalPosition + (Vector3.Up * FocusHeight);

        // global_position is not propagated during _init and reads (0,0,0), so the
        // first usable follow target only exists once the tree is processing. Snap
        // to it rather than swooping in from the world origin.
        if (_snapped)
        {
            GlobalPosition = GlobalPosition.Lerp(focus, 1f - Mathf.Exp(-FollowSpeed * (float)delta));
        }
        else
        {
            GlobalPosition = focus;
            _snapped = true;
        }

        Rotation = new Vector3(0f, Yaw, 0f);

        // Rebuilt every frame from the exports rather than baked in _Ready, so
        // pitch/distance/fov can be dragged in the Inspector while the game runs.
        float pitch = Mathf.DegToRad(PitchDegrees);
        _topCamera.Position = new Vector3(0f, Mathf.Sin(pitch) * Distance, Mathf.Cos(pitch) * Distance);
        _topCamera.Rotation = new Vector3(-pitch, 0f, 0f);
        _topCamera.Fov = Fov;
    }
}
