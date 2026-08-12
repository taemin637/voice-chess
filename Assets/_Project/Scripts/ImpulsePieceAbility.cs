using UnityEngine;

[CreateAssetMenu(
    fileName = "ImpulsePieceAbility",
    menuName = "Voice Chess/Piece Abilities/Impulse")]
public sealed class ImpulsePieceAbility : ChessPieceAbility
{
    [SerializeField, Min(0f)] private float impulseSpeedInSquares = 3f;
    [SerializeField, Range(-180f, 180f)] private float relativeAngle;
    [SerializeField] private bool scaleWithVoiceLoudness = true;
    [SerializeField] private bool stopCurrentMovement;
    [SerializeField, Range(-360f, 360f)] private float headingChange;

    public override bool TryExecute(
        ref NetworkChessPieceState piece,
        in ChessPieceAbilityContext context,
        out string rejection)
    {
        float radians = (piece.VoiceHeading + relativeAngle) * Mathf.Deg2Rad;
        float sine = Mathf.Sin(radians);
        float cosine = Mathf.Cos(radians);
        Vector2 direction = new(
            context.TeamForward.x * cosine + context.TeamForward.y * sine,
            -context.TeamForward.x * sine + context.TeamForward.y * cosine);
        float loudnessMultiplier = scaleWithVoiceLoudness
            ? Mathf.Lerp(0.5f, 1.5f, context.CommandLoudness)
            : 1f;
        Vector2 impulse = direction * impulseSpeedInSquares * loudnessMultiplier;

        piece.KnockbackFileVelocity += impulse.x;
        piece.KnockbackRankVelocity += impulse.y;
        piece.VoiceHeading = Mathf.Repeat(piece.VoiceHeading + headingChange, 360f);

        if (stopCurrentMovement)
        {
            piece.VoiceMoveAxis = 0;
            piece.VoiceTurnAxis = 0;
        }

        rejection = string.Empty;
        return true;
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        impulseSpeedInSquares = Mathf.Max(0f, impulseSpeedInSquares);
        relativeAngle = Mathf.Clamp(relativeAngle, -180f, 180f);
        headingChange = Mathf.Clamp(headingChange, -360f, 360f);
    }
}
