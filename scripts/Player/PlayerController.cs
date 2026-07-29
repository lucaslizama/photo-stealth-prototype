using Godot;

namespace PhotoStealthPrototype.Player;

/// <summary>
/// First-person player: mouse look, walk/sprint/crouch movement, and the stance
/// and motion data <see cref="PlayerStealthProfile"/> turns into an exposure
/// value for guards to read.
/// </summary>
/// <remarks>
/// Expects two children: a <c>Head</c> Node3D (the camera pivot, and later the
/// camera's viewfinder mount) and a <c>Collider</c> CollisionShape3D holding a
/// CapsuleShape3D.
/// </remarks>
[GlobalClass]
public partial class PlayerController : CharacterBody3D
{
    [ExportGroup("Movement")]
    [Export] public float CrouchSpeed { get; set; } = 1.8f;
    [Export] public float WalkSpeed { get; set; } = 3.6f;
    [Export] public float SprintSpeed { get; set; } = 6.0f;
    [Export] public float Acceleration { get; set; } = 14.0f;
    [Export] public float Gravity { get; set; } = 24.0f;

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

    /// <summary>Horizontal speed in m/s — the motion term of the exposure model.</summary>
    public float PlanarSpeed { get; private set; }

    public bool IsSprinting { get; private set; }

    /// <summary>Camera pivot. Guards sight this point, not the capsule origin.</summary>
    public Node3D Head { get; private set; } = null!;

    public PlayerStealthProfile Stealth { get; private set; } = null!;

    private CollisionShape3D _collider = null!;
    private CapsuleShape3D _capsule = null!;
    private float _currentHeight;
    private float _pitch;

    public override void _Ready()
    {
        Head = GetNode<Node3D>("Head");
        _collider = GetNode<CollisionShape3D>("Collider");
        Stealth = GetNode<PlayerStealthProfile>("StealthProfile");

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
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            RotateY(-motion.Relative.X * MouseSensitivity);
            _pitch = Mathf.Clamp(
                _pitch - (motion.Relative.Y * MouseSensitivity),
                Mathf.DegToRad(MinPitchDegrees),
                Mathf.DegToRad(MaxPitchDegrees));
            Head.Rotation = new Vector3(_pitch, 0f, 0f);
            return;
        }

        if (@event.IsActionPressed("ui_cancel"))
        {
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

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
        Vector3 wish = (Transform.Basis * new Vector3(input.X, 0f, input.Y));
        wish.Y = 0f;
        if (wish.LengthSquared() > 1f)
        {
            wish = wish.Normalized();
        }

        IsSprinting = Input.IsActionPressed("sprint")
            && CurrentStance == Stance.Standing
            && wish.LengthSquared() > 0.01f;

        float topSpeed = CurrentStance == Stance.Crouching
            ? CrouchSpeed
            : IsSprinting ? SprintSpeed : WalkSpeed;

        Vector3 desired = wish * topSpeed;
        float blend = 1f - Mathf.Exp(-Acceleration * dt);
        velocity.X = Mathf.Lerp(velocity.X, desired.X, blend);
        velocity.Z = Mathf.Lerp(velocity.Z, desired.Z, blend);

        Velocity = velocity;
        MoveAndSlide();

        PlanarSpeed = new Vector2(Velocity.X, Velocity.Z).Length();
    }

    private void UpdateStance(float dt)
    {
        if (_capsule is null)
        {
            return;
        }

        bool wantsCrouch = Input.IsActionPressed("crouch");

        // Refuse to stand up into geometry — otherwise the growing capsule
        // resolves the overlap by launching the player through the ceiling.
        if (!wantsCrouch && CurrentStance == Stance.Crouching && IsBlockedAbove())
        {
            wantsCrouch = true;
        }

        CurrentStance = wantsCrouch ? Stance.Crouching : Stance.Standing;

        float target = CurrentStance == Stance.Crouching ? CrouchHeight : StandHeight;
        _currentHeight = Mathf.Lerp(_currentHeight, target, 1f - Mathf.Exp(-StanceBlendSpeed * dt));
        ApplyHeight(_currentHeight);
    }

    private void ApplyHeight(float height)
    {
        _capsule.Height = height;
        _collider.Position = new Vector3(0f, height * 0.5f, 0f);
        Head.Position = new Vector3(0f, Mathf.Max(height - EyeDropFromTop, 0.2f), 0f);
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
