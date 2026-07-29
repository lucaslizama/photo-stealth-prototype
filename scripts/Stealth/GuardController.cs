using Godot;
using PhotoStealthPrototype.Player;

namespace PhotoStealthPrototype.Stealth;

/// <summary>
/// A patrolling guard: integrates <see cref="GuardVision"/> into a detection
/// meter and drives the Patrol → Investigate → Alert / Search → Patrol cycle.
/// </summary>
/// <remarks>
/// The meter is deliberately gradual rather than a binary "seen" flag — a
/// glimpse across a lit room should cost the player something without instantly
/// ending the run, and the decay gives breaking line of sight a real payoff.
/// Expects a <c>Vision</c> child (GuardVision) at eye height.
/// </remarks>
[GlobalClass]
public partial class GuardController : CharacterBody3D
{
    [ExportGroup("Movement")]
    [Export] public float PatrolSpeed { get; set; } = 1.6f;
    [Export] public float InvestigateSpeed { get; set; } = 3.0f;
    [Export] public float TurnSpeed { get; set; } = 6.0f;
    [Export] public float Gravity { get; set; } = 24.0f;
    [Export] public float ArriveTolerance { get; set; } = 0.5f;

    // FillRate is meter units gained per second at visibility 1.0.
    [ExportGroup("Detection")]
    [Export] public float FillRate { get; set; } = 0.7f;
    [Export] public float DecayRate { get; set; } = 0.3f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float SuspicionThreshold { get; set; } = 0.45f;

    [ExportGroup("Behaviour")]
    [Export] public float WaypointPause { get; set; } = 1.5f;
    [Export] public float SearchDuration { get; set; } = 6.0f;
    [Export] public float SearchTurnSpeed { get; set; } = 1.4f;

    /// <summary>
    /// Longest a guard will keep walking toward a last known position before
    /// giving up and sweeping instead.
    /// </summary>
    [Export] public float InvestigateTimeout { get; set; } = 8.0f;

    [Export] public PatrolRoute? Route { get; set; }

    [Signal] public delegate void StateChangedEventHandler(int newState);
    [Signal] public delegate void PlayerSpottedEventHandler();

    public GuardState State { get; private set; } = GuardState.Patrol;

    /// <summary>Detection meter, 0..1. Reaching 1 means caught.</summary>
    public float Detection { get; private set; }

    public GuardVision Vision { get; private set; } = null!;

    private PlayerController? _player;
    private int _waypointIndex;
    private float _pauseTimer;
    private float _searchTimer;
    private float _investigateTimer;
    private Vector3 _lastKnownPosition;
    private bool _hasLastKnown;

    public override void _Ready()
    {
        Vision = GetNode<GuardVision>("Vision");
        AddToGroup("guard");
        _player = GetTree().GetFirstNodeInGroup(PlayerController.GroupName) as PlayerController;

        if (_player is null)
        {
            GD.PushWarning($"Guard '{Name}' found no node in the 'player' group — it will patrol blindly.");
        }
    }

    /// <summary>
    /// Something bright enough to register happened at <paramref name="position"/>
    /// — a camera flash, for now. Bumps the meter and points the guard at the
    /// spot; the existing state machine promotes it to Investigate on its own, so
    /// this needs no new state.
    /// </summary>
    public void NoticeDisturbance(Vector3 position, float spike)
    {
        if (State == GuardState.Alert)
        {
            return;
        }

        _lastKnownPosition = position;
        _hasLastKnown = true;
        Detection = Mathf.Min(Detection + spike, 1f);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        UpdateDetection(dt);
        UpdateState(dt);

        switch (State)
        {
            case GuardState.Patrol:
                TickPatrol(dt);
                break;
            case GuardState.Investigate:
                TickInvestigate(dt);
                break;
            case GuardState.Search:
                TickSearch(dt);
                break;
            case GuardState.Alert:
                Halt(dt);
                break;
        }
    }

    private void UpdateDetection(float dt)
    {
        if (State == GuardState.Alert || _player is null)
        {
            return;
        }

        float visibility = Vision.Evaluate(_player);

        if (visibility > 0f)
        {
            Detection = Mathf.Min(Detection + (visibility * FillRate * dt), 1f);
            _lastKnownPosition = _player.GlobalPosition;
            _hasLastKnown = true;
        }
        else
        {
            Detection = Mathf.Max(Detection - (DecayRate * dt), 0f);
        }
    }

    private void UpdateState(float dt)
    {
        if (Detection >= 1f)
        {
            if (State != GuardState.Alert)
            {
                SetState(GuardState.Alert);
                EmitSignal(SignalName.PlayerSpotted);
            }

            return;
        }

        if (Detection >= SuspicionThreshold)
        {
            if (State != GuardState.Investigate)
            {
                _investigateTimer = InvestigateTimeout;
            }

            SetState(GuardState.Investigate);
            return;
        }

        if (State != GuardState.Investigate)
        {
            return;
        }

        // The meter drops below suspicion within a fraction of a second of
        // losing sight, so exiting Investigate on that alone would make the
        // state last a single frame. Commit to actually reaching the spot
        // (or timing out) before falling back to a sweep.
        _investigateTimer -= dt;

        Vector3 toSpot = _lastKnownPosition - GlobalPosition;
        toSpot.Y = 0f;
        bool arrived = _hasLastKnown && toSpot.Length() <= ArriveTolerance * 2f;

        if (arrived || _investigateTimer <= 0f)
        {
            _searchTimer = SearchDuration;
            SetState(GuardState.Search);
        }
    }

    private void SetState(GuardState next)
    {
        if (State == next)
        {
            return;
        }

        State = next;
        EmitSignal(SignalName.StateChanged, (int)next);
    }

    private void TickPatrol(float dt)
    {
        if (Route is null || Route.Count == 0)
        {
            Halt(dt);
            return;
        }

        if (_pauseTimer > 0f)
        {
            _pauseTimer -= dt;
            Halt(dt);
            return;
        }

        Vector3 target = Route.GetPoint(_waypointIndex);
        if (MoveTowards(target, PatrolSpeed, dt))
        {
            _waypointIndex++;
            _pauseTimer = WaypointPause;
        }
    }

    private void TickInvestigate(float dt)
    {
        if (!_hasLastKnown)
        {
            Halt(dt);
            return;
        }

        MoveTowards(_lastKnownPosition, InvestigateSpeed, dt);
    }

    private void TickSearch(float dt)
    {
        _searchTimer -= dt;
        if (_searchTimer <= 0f)
        {
            _hasLastKnown = false;
            SetState(GuardState.Patrol);
            return;
        }

        // Stand and scan rather than wander — wandering without navmesh just
        // walks guards into walls.
        RotateY(SearchTurnSpeed * dt);
        Halt(dt);
    }

    /// <summary>Moves toward <paramref name="target"/>; returns true once arrived.</summary>
    private bool MoveTowards(Vector3 target, float speed, float dt)
    {
        Vector3 flat = new(target.X - GlobalPosition.X, 0f, target.Z - GlobalPosition.Z);
        float distance = flat.Length();

        Vector3 velocity = Velocity;

        if (distance > ArriveTolerance)
        {
            Vector3 direction = flat / distance;
            velocity.X = direction.X * speed;
            velocity.Z = direction.Z * speed;
            FaceDirection(direction, dt);
        }
        else
        {
            velocity.X = 0f;
            velocity.Z = 0f;
        }

        ApplyGravity(ref velocity, dt);
        Velocity = velocity;
        MoveAndSlide();

        return distance <= ArriveTolerance;
    }

    private void Halt(float dt)
    {
        Vector3 velocity = Velocity;
        velocity.X = 0f;
        velocity.Z = 0f;
        ApplyGravity(ref velocity, dt);
        Velocity = velocity;
        MoveAndSlide();
    }

    private void ApplyGravity(ref Vector3 velocity, float dt)
    {
        if (IsOnFloor())
        {
            velocity.Y = 0f;
        }
        else
        {
            velocity.Y -= Gravity * dt;
        }
    }

    private void FaceDirection(Vector3 direction, float dt)
    {
        // Godot's forward is -Z, so the yaw that points -Z along `direction`
        // is atan2(-x, -z).
        float targetYaw = Mathf.Atan2(-direction.X, -direction.Z);
        float yaw = Mathf.LerpAngle(Rotation.Y, targetYaw, 1f - Mathf.Exp(-TurnSpeed * dt));
        Rotation = new Vector3(0f, yaw, 0f);
    }
}
