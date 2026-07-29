using Godot;

namespace PhotoStealthPrototype.Player;

/// <summary>
/// The player, across both views: a 3/4 top-down travelling mode and a
/// first-person viewfinder mode, plus the stance and motion data
/// <see cref="PlayerStealthProfile"/> turns into an exposure value for guards.
/// </summary>
/// <remarks>
/// Expects a <c>Head</c> Node3D (the first-person camera pivot), a
/// <c>Collider</c> CollisionShape3D holding a CapsuleShape3D, and optionally a
/// <c>Body</c> Node3D (the visible mesh, needed only by the top-down view) and a
/// <c>CameraRig</c> <see cref="PlayerCameraRig"/>.
/// <para>
/// This node is the single authority on which view is live: the rig and
/// <c>PhotoCamera</c> both react to <see cref="ViewChanged"/> rather than polling
/// the aim button themselves, so there is no way for them to disagree about it.
/// </para>
/// </remarks>
[GlobalClass]
public partial class PlayerController : CharacterBody3D
{
    /// <summary>Group other systems use to find the player without wiring.</summary>
    public const string GroupName = "player";

    /// <summary>Fired when the player raises or lowers the viewfinder.</summary>
    [Signal] public delegate void ViewChangedEventHandler(bool firstPerson);

    [ExportGroup("Movement")]
    [Export] public float CrouchSpeed { get; set; } = 1.8f;
    [Export] public float WalkSpeed { get; set; } = 3.6f;
    [Export] public float SprintSpeed { get; set; } = 6.0f;

    /// <summary>Speed ceiling while the viewfinder is up, over any stance speed.</summary>
    [Export] public float AimSpeed { get; set; } = 1.2f;
    [Export] public float Acceleration { get; set; } = 14.0f;
    [Export] public float Gravity { get; set; } = 24.0f;

    /// <summary>How fast the body swings toward its heading in top-down, rad/s-ish.</summary>
    [Export] public float TurnSpeed { get; set; } = 12.0f;

    [ExportGroup("Look")]
    [Export(PropertyHint.Range, "0.0005,0.01,0.0001")]
    public float MouseSensitivity { get; set; } = 0.0022f;
    [Export] public float MinPitchDegrees { get; set; } = -85.0f;
    [Export] public float MaxPitchDegrees { get; set; } = 85.0f;

    [ExportGroup("Stance")]
    [Export] public float StandHeight { get; set; } = 1.8f;
    [Export] public float CrouchHeight { get; set; } = 1.05f;
    [Export] public float StanceBlendSpeed { get; set; } = 10.0f;
    /// <summary>How far below the capsule's top the eye sits.</summary>
    [Export] public float EyeDropFromTop { get; set; } = 0.15f;

    public Stance CurrentStance { get; private set; } = Stance.Standing;

    /// <summary>Which camera is live, and therefore which control scheme is.</summary>
    public ViewMode View { get; private set; } = ViewMode.TopDown;

    /// <summary>Horizontal speed in m/s — the motion term of the exposure model.</summary>
    public float PlanarSpeed { get; private set; }

    public bool IsSprinting { get; private set; }

    /// <summary>
    /// When true, input-driven movement is ignored. Set once the run is lost so a
    /// discovered player cannot simply walk away from the guard that caught them.
    /// Cleared by reloading the scene on restart.
    /// </summary>
    public bool MovementLocked { get; set; }

    /// <summary>Camera pivot. Guards sight this point, not the capsule origin.</summary>
    public Node3D Head { get; private set; } = null!;

    public PlayerStealthProfile Stealth { get; private set; } = null!;

    private CollisionShape3D _collider = null!;
    private CapsuleShape3D _capsule = null!;
    private Node3D? _body;
    private PlayerCameraRig? _rig;
    private float _currentHeight;
    private float _pitch;

    /// <summary>
    /// Test override for the view mode: -1 follows input, 0 forces top-down, 1
    /// forces first-person. Headless probes cannot hold a mouse button, and the
    /// input path additionally requires a captured mouse they do not have.
    /// </summary>
    private int _viewOverride = -1;

    /// <summary>
    /// Latched crouch intent. Kept separate from the applied stance so that being
    /// stuck under geometry does not silently discard the player's choice.
    /// </summary>
    private bool _wantsCrouch;

    public override void _Ready()
    {
        Head = GetNode<Node3D>("Head");
        _collider = GetNode<CollisionShape3D>("Collider");
        Stealth = GetNode<PlayerStealthProfile>("StealthProfile");

        // Both optional so a stripped-down scene (a headless probe rig, say) still
        // runs: the mesh is cosmetic and the rig only owns the top-down camera.
        _body = GetNodeOrNull<Node3D>("Body");
        _rig = GetNodeOrNull<PlayerCameraRig>("CameraRig");

        if (_collider.Shape is not CapsuleShape3D capsule)
        {
            GD.PushError($"{Name}/Collider must hold a CapsuleShape3D; crouching is disabled.");
            return;
        }

        // Duplicated so runtime height changes cannot leak back into the shared
        // resource — .tscn-embedded shapes are otherwise mutated in place.
        _capsule = (CapsuleShape3D)capsule.Duplicate();
        _collider.Shape = _capsule;

        _currentHeight = StandHeight;
        ApplyHeight(_currentHeight);

        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Mouse look belongs to the viewfinder only. In top-down the body's yaw is
        // owned by TurnTowardMotion, and letting the mouse fight it for control of
        // the same value would just make the body jitter.
        if (@event is InputEventMouseMotion motion)
        {
            if (View == ViewMode.FirstPerson && Input.MouseMode == Input.MouseModeEnum.Captured)
            {
                RotateY(-motion.Relative.X * MouseSensitivity);
                _pitch = Mathf.Clamp(
                    _pitch - (motion.Relative.Y * MouseSensitivity),
                    Mathf.DegToRad(MinPitchDegrees),
                    Mathf.DegToRad(MaxPitchDegrees));
                Head.Rotation = new Vector3(_pitch, 0f, 0f);
            }

            return;
        }

        // Toggled here rather than polled in _PhysicsProcess: IsActionJustPressed
        // is unreliable there, firing twice when two physics ticks share a frame
        // and getting missed when a frame has none. IsActionPressed ignores key
        // echo by default, so holding C does not flicker the stance.
        if (@event.IsActionPressed("crouch"))
        {
            _wantsCrouch = !_wantsCrouch;
            return;
        }

        if (@event.IsActionPressed("ui_cancel"))
        {
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
        }
    }

    /// <summary>
    /// Resolves the live view. Polled rather than edge-triggered because
    /// <c>aim_camera</c> is a hold, so there is no press/release pair to hang the
    /// mode off — and a release swallowed while the window was unfocused would
    /// otherwise leave the player stuck in the viewfinder.
    /// </summary>
    public override void _Process(double delta)
    {
        SetView(_viewOverride switch
        {
            0 => ViewMode.TopDown,
            1 => ViewMode.FirstPerson,

            // A captured mouse is part of the requirement: with the cursor freed
            // (Esc) or the run already lost, raising a camera you cannot aim would
            // just trap the player in a view they cannot steer.
            _ => !MovementLocked
                && Input.MouseMode == Input.MouseModeEnum.Captured
                && Input.IsActionPressed("aim_camera")
                    ? ViewMode.FirstPerson
                    : ViewMode.TopDown,
        });
    }

    /// <summary>Forces the view: -1 follows input, 0 top-down, 1 first-person. Test hook.</summary>
    public void ForceView(int mode) => _viewOverride = mode;

    private void SetView(ViewMode view)
    {
        if (view == View)
        {
            return;
        }

        View = view;

        // Hidden in first person because the camera sits inside the capsule and
        // would otherwise render the inside of the player's own head.
        if (_body is not null)
        {
            _body.Visible = view == ViewMode.TopDown;
        }

        EmitSignal(SignalName.ViewChanged, view == ViewMode.FirstPerson);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        if (MovementLocked)
        {
            HoldStill(dt);
            return;
        }

        UpdateStance(dt);

        Vector3 velocity = Velocity;
        if (IsOnFloor())
        {
            velocity.Y = 0f;
        }
        else
        {
            velocity.Y -= Gravity * dt;
        }

        // GetVector's Y axis runs forward-negative, which matches Godot's -Z forward.
        Vector2 input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");

        // The two views take the same stick and mean different things by it.
        // Top-down is screen-relative — W is "up the screen", built from the rig's
        // fixed yaw so it never depends on where the body is pointing. First person
        // is body-relative, i.e. ordinary FPS strafing.
        Basis moveBasis = View == ViewMode.FirstPerson
            ? Transform.Basis
            : Basis.FromEuler(new Vector3(0f, _rig?.Yaw ?? 0f, 0f));

        Vector3 wish = moveBasis * new Vector3(input.X, 0f, input.Y);
        wish.Y = 0f;
        if (wish.LengthSquared() > 1f)
        {
            wish = wish.Normalized();
        }

        if (View == ViewMode.TopDown)
        {
            TurnTowardMotion(wish, dt);
        }

        IsSprinting = Input.IsActionPressed("sprint")
            && View == ViewMode.TopDown
            && CurrentStance == Stance.Standing
            && wish.LengthSquared() > 0.01f;

        float topSpeed = CurrentStance == Stance.Crouching
            ? CrouchSpeed
            : IsSprinting ? SprintSpeed : WalkSpeed;

        // Raising the viewfinder is a deliberate, careful act: you can still shuffle
        // sideways to fix a framing, but not walk-and-shoot at patrol speed.
        if (View == ViewMode.FirstPerson)
        {
            topSpeed = Mathf.Min(topSpeed, AimSpeed);
        }

        Vector3 desired = wish * topSpeed;
        float blend = 1f - Mathf.Exp(-Acceleration * dt);
        velocity.X = Mathf.Lerp(velocity.X, desired.X, blend);
        velocity.Z = Mathf.Lerp(velocity.Z, desired.Z, blend);

        Velocity = velocity;
        MoveAndSlide();

        PlanarSpeed = new Vector2(Velocity.X, Velocity.Z).Length();
    }

    /// <summary>
    /// Drops all horizontal motion but keeps gravity, so a player frozen mid-air
    /// still settles onto the floor instead of hanging there. Stance is left
    /// wherever it was — being caught should not make you stand up.
    /// </summary>
    private void HoldStill(float dt)
    {
        Vector3 velocity = Velocity;
        velocity.X = 0f;
        velocity.Z = 0f;

        if (IsOnFloor())
        {
            velocity.Y = 0f;
        }
        else
        {
            velocity.Y -= Gravity * dt;
        }

        Velocity = velocity;
        MoveAndSlide();

        PlanarSpeed = 0f;
        IsSprinting = false;
    }

    /// <summary>
    /// Points the body along its own movement. This is the <em>only</em> yaw control
    /// in top-down, and first person inherits whatever it leaves behind — so the
    /// direction you were last walking is the direction you start framing from.
    /// </summary>
    private void TurnTowardMotion(Vector3 wish, float dt)
    {
        if (wish.LengthSquared() < 0.01f)
        {
            return;
        }

        // Yaw convention: 0 faces -Z, so forward is (-sin y, 0, -cos y) and
        // recovering y from a forward vector means atan2(-x, -z), not atan2(x, z).
        float target = Mathf.Atan2(-wish.X, -wish.Z);

        // LerpAngle rather than Lerp so turning from +179° to -179° takes the 2°
        // route instead of spinning the long way round.
        Rotation = new Vector3(
            0f,
            Mathf.LerpAngle(Rotation.Y, target, 1f - Mathf.Exp(-TurnSpeed * dt)),
            0f);
    }

    private void UpdateStance(float dt)
    {
        if (_capsule is null)
        {
            return;
        }

        bool crouch = _wantsCrouch;

        // Refuse to stand up into geometry — otherwise the growing capsule
        // resolves the overlap by launching the player through the ceiling.
        // _wantsCrouch keeps the "stand up" intent, so the player rises on their
        // own once they clear the low space rather than having to press C twice.
        if (!crouch && CurrentStance == Stance.Crouching && IsBlockedAbove())
        {
            crouch = true;
        }

        CurrentStance = crouch ? Stance.Crouching : Stance.Standing;

        float target = CurrentStance == Stance.Crouching ? CrouchHeight : StandHeight;
        _currentHeight = Mathf.Lerp(_currentHeight, target, 1f - Mathf.Exp(-StanceBlendSpeed * dt));
        ApplyHeight(_currentHeight);
    }

    private void ApplyHeight(float height)
    {
        _capsule.Height = height;
        _collider.Position = new Vector3(0f, height * 0.5f, 0f);
        Head.Position = new Vector3(0f, Mathf.Max(height - EyeDropFromTop, 0.2f), 0f);

        // Squashed rather than animated: with no rig, scaling the whole mesh group
        // is what makes the stance legible from overhead, where the eye-height drop
        // that sells it in first person is invisible.
        if (_body is not null)
        {
            _body.Scale = new Vector3(1f, height / StandHeight, 1f);
        }
    }

    /// <summary>
    /// Single upward ray from the crouched centre. Adequate for grey-box
    /// geometry; thin ledges would need a shape cast to catch reliably.
    /// </summary>
    private bool IsBlockedAbove()
    {
        Vector3 from = GlobalPosition + (Vector3.Up * CrouchHeight * 0.5f);
        Vector3 to = GlobalPosition + (Vector3.Up * (StandHeight + 0.1f));

        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };

        return GetWorld3D().DirectSpaceState.IntersectRay(query).Count > 0;
    }
}
