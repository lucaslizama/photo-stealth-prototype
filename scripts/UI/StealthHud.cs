using Godot;
using PhotoStealthPrototype.Player;
using PhotoStealthPrototype.Stealth;

namespace PhotoStealthPrototype.UI;

/// <summary>
/// Debug-grade readout for tuning the stealth model: detection meter, the lead
/// guard's state, and the player's live exposure breakdown.
/// </summary>
/// <remarks>
/// Deliberately shows the raw numbers rather than a diegetic indicator — the
/// point right now is to make the exposure model legible while tuning it, not
/// to ship a HUD.
/// </remarks>
[GlobalClass]
public partial class StealthHud : CanvasLayer
{
    [Export] public StealthDirector? Director { get; set; }
    [Export] public PlayerController? Player { get; set; }

    private ProgressBar _meter = null!;
    private Label _stateLabel = null!;
    private Label _exposureLabel = null!;
    private Label _caughtLabel = null!;

    public override void _Ready()
    {
        _meter = GetNode<ProgressBar>("Root/Meter");
        _stateLabel = GetNode<Label>("Root/StateLabel");
        _exposureLabel = GetNode<Label>("Root/ExposureLabel");
        _caughtLabel = GetNode<Label>("CaughtLabel");

        _caughtLabel.Visible = false;

        if (Director is not null)
        {
            Director.PlayerCaught += OnPlayerCaught;
        }
    }

    public override void _Process(double delta)
    {
        if (Director is not null)
        {
            _meter.Value = Director.HighestDetection * 100.0;
            _meter.Modulate = DetectionColour(Director.HighestDetection);

            GuardController? lead = Director.LeadGuard;
            _stateLabel.Text = lead is null
                ? "guards: idle"
                : $"{lead.Name}: {lead.State}  ({Director.HighestDetection:P0})"
                  + (lead.Vision.HasLineOfSight ? "  [LOS]" : string.Empty);
        }

        if (Player is not null)
        {
            PlayerStealthProfile stealth = Player.Stealth;
            string motion = Player.IsSprinting
                ? "sprinting"
                : Player.PlanarSpeed < 0.15f ? "still" : "walking";

            _exposureLabel.Text =
                $"exposure {stealth.Exposure:0.00}   "
                + $"{Player.CurrentStance.ToString().ToLowerInvariant()}, {motion}, "
                + $"light {stealth.Illumination:0.00}";
        }
    }

    private static Color DetectionColour(float detection) => detection switch
    {
        >= 1.0f => new Color(1.0f, 0.25f, 0.25f),
        >= 0.45f => new Color(1.0f, 0.75f, 0.2f),
        _ => new Color(0.55f, 0.85f, 1.0f),
    };

    private void OnPlayerCaught()
    {
        _caughtLabel.Visible = true;
    }
}
