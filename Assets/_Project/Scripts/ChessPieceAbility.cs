using UnityEngine;

public readonly struct ChessPieceAbilityContext
{
    public readonly PlayerTeam Team;
    public readonly float CommandLoudness;
    public readonly Vector2 TeamForward;
    public readonly double ServerTime;

    public ChessPieceAbilityContext(
        PlayerTeam team,
        float commandLoudness,
        Vector2 teamForward,
        double serverTime = 0d)
    {
        Team = team;
        CommandLoudness = Mathf.Clamp01(commandLoudness);
        TeamForward = teamForward.sqrMagnitude > 0f
            ? teamForward.normalized
            : Vector2.up;
        ServerTime = serverTime;
    }
}

/// <summary>
/// Inspector-assignable extension point for future piece skills. Implementations run
/// on the authoritative server and may modify the networked piece state.
/// </summary>
public abstract class ChessPieceAbility : ScriptableObject
{
    [SerializeField] private string displayName = "Piece Ability";
    [SerializeField] private PieceVoiceCommand trigger = PieceVoiceCommand.SkillPrimary;
    [SerializeField, Min(0f)] private float additionalCommandCost;
    [SerializeField, Min(0f)] private float cooldownSeconds;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName)
        ? name
        : displayName;
    public PieceVoiceCommand Trigger => trigger;
    public float AdditionalCommandCost => Mathf.Max(0f, additionalCommandCost);
    public float CooldownSeconds => Mathf.Max(0f, cooldownSeconds);

    public abstract bool TryExecute(
        ref NetworkChessPieceState piece,
        in ChessPieceAbilityContext context,
        out string rejection);

    protected virtual void OnValidate()
    {
        additionalCommandCost = Mathf.Max(0f, additionalCommandCost);
        cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
    }
}
