using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public enum CommandIssuingMode : byte
{
    RealTime,
    AlternatingTurns
}

public enum VoiceCommandVersion : byte
{
    [InspectorName("Legacy - Look To Select")]
    LegacyLookSelection,
    [InspectorName("New - Click Lock + Charge")]
    ConfirmedSelectionCharge
}

public enum MatchClockMode : byte
{
    Unlimited,
    Countdown
}

public enum TimeLimitResolution : byte
{
    Draw,
    RemainingPieces,
    CaptureScore,
    CombinedPieceAndCaptureScore
}

public enum RoyalUnitMode : byte
{
    BoardKing,
    PlayerCommander,
    BoardKingAndPlayerCommander
}

public enum RoyalEliminationRequirement : byte
{
    AnyRoyalLost,
    AllRoyalsLost
}

public enum MatchEndReason : byte
{
    None,
    RoyalEliminated,
    AllPiecesEliminated,
    CaptureScoreReached,
    TimeExpired,
    Manual
}

public enum PieceMovementMode : byte
{
    Free,
    ForwardOnly,
    ForwardAndBackward,
    StrafeOnly,
    Stationary
}

public enum PieceMovementControl : byte
{
    [InspectorName("Continuous (Legacy)")]
    Continuous,
    [InspectorName("Flick Impulse (Alkkagi)")]
    FlickImpulse
}

public enum CaptureScoringRule : byte
{
    [InspectorName("Periodic Score Per Piece")]
    PeriodicPerPiece,
    [InspectorName("Final Occupancy Piece Value")]
    FinalOccupancyValue
}

public enum PieceSelectionMouseButton : byte
{
    Left,
    Right,
    Middle
}

[Serializable]
public sealed class MatchClockSettings
{
    [SerializeField] private MatchClockMode mode = MatchClockMode.Countdown;
    [SerializeField, Min(1f)] private float durationSeconds = 60f;
    [SerializeField] private TimeLimitResolution timeLimitResolution =
        TimeLimitResolution.RemainingPieces;

    public MatchClockMode Mode => mode;
    public bool IsEnabled => mode == MatchClockMode.Countdown;
    public float DurationSeconds => Mathf.Max(1f, durationSeconds);
    public TimeLimitResolution TimeLimitResolution => timeLimitResolution;

    public void Validate()
    {
        durationSeconds = Mathf.Max(1f, durationSeconds);
    }
}

[Serializable]
public sealed class CommandEconomySettings
{
    private const byte LegacyRegeneratingPointsMode = 2;

    [SerializeField] private CommandIssuingMode mode = CommandIssuingMode.RealTime;

    [Header("음성 명령 버전")]
    [SerializeField] private VoiceCommandVersion voiceCommandVersion =
        VoiceCommandVersion.LegacyLookSelection;

    [Header("교대 턴제")]
    [SerializeField] private PlayerTeam firstTeam = PlayerTeam.White;
    [SerializeField] private bool advanceAfterAcceptedCommand = true;
    [SerializeField] private bool freezeInactiveTeamMovement;
    [SerializeField, Min(0f)] private float turnDurationSeconds;

    [Header("코스트 시스템")]
    [Tooltip("Independent master switch. It can be combined with real-time or alternating-turn commands.")]
    [SerializeField] private bool costSystemEnabled;
    [FormerlySerializedAs("startingPoints")]
    [SerializeField, Min(0f)] private float startingCost = 3f;
    [FormerlySerializedAs("maximumPoints")]
    [SerializeField, Min(0.01f)] private float maximumCost = 5f;
    [Tooltip("The recharge tick happens once per this many seconds.")]
    [SerializeField, Min(0.01f)] private float rechargeIntervalSeconds = 2f;
    [FormerlySerializedAs("pointsPerSecond")]
    [Tooltip("Cost restored on each recharge tick.")]
    [SerializeField, Min(0f)] private float rechargeAmount = 1f;

    [Header("명령별 코스트")]
    [FormerlySerializedAs("movementCommandCost")]
    [SerializeField, Min(0f)] private float moveForwardCost = 1f;
    [SerializeField, Min(0f)] private float moveBackwardCost = 1f;
    [SerializeField, Min(0f)] private float moveLeftCost = 1f;
    [SerializeField, Min(0f)] private float moveRightCost = 1f;
    [SerializeField, Min(0f)] private float moveUpperRightCost = 1f;
    [SerializeField, Min(0f)] private float moveUpperLeftCost = 1f;
    [SerializeField, Min(0f)] private float moveLowerRightCost = 1f;
    [SerializeField, Min(0f)] private float moveLowerLeftCost = 1f;
    [FormerlySerializedAs("turnCommandCost")]
    [SerializeField, Min(0f)] private float turnLeftCost = 0.5f;
    [SerializeField, Min(0f)] private float turnRightCost = 0.5f;
    [FormerlySerializedAs("stopCommandCost")]
    [SerializeField, Min(0f)] private float stopCost;
    [FormerlySerializedAs("skillCommandCost")]
    [SerializeField, Min(0f)] private float primarySkillCost = 2f;
    [SerializeField, Min(0f)] private float secondarySkillCost = 2f;
    [SerializeField, Min(0f)] private float chargeCost = 1f;

    [Header("신규 명령 방식 - 돌진 레이저")]
    [Tooltip("Maximum distance of the aiming laser, measured in board squares.")]
    [SerializeField, Min(1f)] private float chargeLaserRangeInSquares = 30f;
    [SerializeField, Min(0.01f)] private float chargeLaserVisibleSeconds = 0.2f;
    [SerializeField, Min(0.002f)] private float chargeLaserWidthInSquares = 0.025f;
    [SerializeField] private Color chargeLaserColor = new(1f, 0.35f, 0.08f, 0.95f);

    private bool UsesLegacyRegeneratingPointsMode =>
        (byte)mode == LegacyRegeneratingPointsMode;

    public CommandIssuingMode Mode => UsesLegacyRegeneratingPointsMode
        ? CommandIssuingMode.RealTime
        : mode;
    public VoiceCommandVersion VoiceCommandVersion => voiceCommandVersion;
    public bool CostSystemEnabled => costSystemEnabled ||
        UsesLegacyRegeneratingPointsMode;
    public PlayerTeam FirstTeam =>
        firstTeam == PlayerTeam.Black ? PlayerTeam.Black : PlayerTeam.White;
    public bool AdvanceAfterAcceptedCommand => advanceAfterAcceptedCommand;
    public bool FreezeInactiveTeamMovement => freezeInactiveTeamMovement;
    public float TurnDurationSeconds => Mathf.Max(0f, turnDurationSeconds);
    public float StartingCost => Mathf.Clamp(startingCost, 0f, MaximumCost);
    public float MaximumCost => Mathf.Max(0.01f, maximumCost);
    public float RechargeIntervalSeconds => Mathf.Max(0.01f, rechargeIntervalSeconds);
    public float RechargeAmount => Mathf.Max(0f, rechargeAmount);
    public float ChargeLaserRangeInSquares =>
        Mathf.Max(1f, chargeLaserRangeInSquares);
    public float ChargeLaserVisibleSeconds =>
        Mathf.Max(0.01f, chargeLaserVisibleSeconds);
    public float ChargeLaserWidthInSquares =>
        Mathf.Max(0.002f, chargeLaserWidthInSquares);
    public Color ChargeLaserColor => chargeLaserColor;

    public float GetBaseCost(PieceVoiceCommand command)
    {
        return command switch
        {
            PieceVoiceCommand.MoveForward => moveForwardCost,
            PieceVoiceCommand.MoveBackward => moveBackwardCost,
            PieceVoiceCommand.MoveLeft => moveLeftCost,
            PieceVoiceCommand.MoveRight => moveRightCost,
            PieceVoiceCommand.MoveUpperRight => moveUpperRightCost,
            PieceVoiceCommand.MoveUpperLeft => moveUpperLeftCost,
            PieceVoiceCommand.MoveLowerRight => moveLowerRightCost,
            PieceVoiceCommand.MoveLowerLeft => moveLowerLeftCost,
            PieceVoiceCommand.TurnLeft => turnLeftCost,
            PieceVoiceCommand.TurnRight => turnRightCost,
            PieceVoiceCommand.Stop => stopCost,
            PieceVoiceCommand.SkillPrimary => primarySkillCost,
            PieceVoiceCommand.SkillSecondary => secondarySkillCost,
            PieceVoiceCommand.Charge => chargeCost,
            _ => 0f
        };
    }

    public void Validate()
    {
        if (firstTeam != PlayerTeam.White && firstTeam != PlayerTeam.Black)
        {
            firstTeam = PlayerTeam.White;
        }

        if (UsesLegacyRegeneratingPointsMode)
        {
            mode = CommandIssuingMode.RealTime;
            costSystemEnabled = true;
            rechargeIntervalSeconds = 1f;
        }

        turnDurationSeconds = Mathf.Max(0f, turnDurationSeconds);
        maximumCost = Mathf.Max(0.01f, maximumCost);
        startingCost = Mathf.Clamp(startingCost, 0f, maximumCost);
        rechargeIntervalSeconds = Mathf.Max(0.01f, rechargeIntervalSeconds);
        rechargeAmount = Mathf.Max(0f, rechargeAmount);
        moveForwardCost = Mathf.Max(0f, moveForwardCost);
        moveBackwardCost = Mathf.Max(0f, moveBackwardCost);
        moveLeftCost = Mathf.Max(0f, moveLeftCost);
        moveRightCost = Mathf.Max(0f, moveRightCost);
        moveUpperRightCost = Mathf.Max(0f, moveUpperRightCost);
        moveUpperLeftCost = Mathf.Max(0f, moveUpperLeftCost);
        moveLowerRightCost = Mathf.Max(0f, moveLowerRightCost);
        moveLowerLeftCost = Mathf.Max(0f, moveLowerLeftCost);
        turnLeftCost = Mathf.Max(0f, turnLeftCost);
        turnRightCost = Mathf.Max(0f, turnRightCost);
        stopCost = Mathf.Max(0f, stopCost);
        primarySkillCost = Mathf.Max(0f, primarySkillCost);
        secondarySkillCost = Mathf.Max(0f, secondarySkillCost);
        chargeCost = Mathf.Max(0f, chargeCost);
        chargeLaserRangeInSquares = Mathf.Max(1f, chargeLaserRangeInSquares);
        chargeLaserVisibleSeconds = Mathf.Max(0.01f, chargeLaserVisibleSeconds);
        chargeLaserWidthInSquares = Mathf.Max(0.002f, chargeLaserWidthInSquares);
    }
}

[Serializable]
public sealed class VictorySettings
{
    [Header("왕 유닛")]
    [SerializeField] private bool endWhenRoyalEliminated = true;
    [SerializeField] private RoyalUnitMode royalUnitMode = RoyalUnitMode.BoardKing;
    [SerializeField] private RoyalEliminationRequirement royalRequirement =
        RoyalEliminationRequirement.AnyRoyalLost;

    [Header("추가 승리 조건")]
    [SerializeField] private bool endWhenAllPiecesEliminated;
    [SerializeField] private bool endAtCaptureScore;
    [SerializeField, Min(0.01f)] private float captureScoreToWin = 100f;

    public bool EndWhenRoyalEliminated => endWhenRoyalEliminated;
    public RoyalUnitMode RoyalUnitMode => royalUnitMode;
    public RoyalEliminationRequirement RoyalRequirement => royalRequirement;
    public bool EndWhenAllPiecesEliminated => endWhenAllPiecesEliminated;
    public bool EndAtCaptureScore => endAtCaptureScore;
    public float CaptureScoreToWin => Mathf.Max(0.01f, captureScoreToWin);

    public bool UsesBoardKing =>
        royalUnitMode == RoyalUnitMode.BoardKing ||
        royalUnitMode == RoyalUnitMode.BoardKingAndPlayerCommander;

    public bool UsesPlayerCommander =>
        royalUnitMode == RoyalUnitMode.PlayerCommander ||
        royalUnitMode == RoyalUnitMode.BoardKingAndPlayerCommander;

    public void Validate()
    {
        captureScoreToWin = Mathf.Max(0.01f, captureScoreToWin);
    }
}

[Serializable]
public sealed class CollisionSettings
{
    [SerializeField, Range(0f, 1f)] private float restitution = 0.72f;
    [SerializeField, Range(0.1f, 5f)] private float impulseMultiplier = 1.15f;
    [SerializeField, Min(0.01f)] private float separationEpsilon = 0.0001f;

    [Header("기물 대 플레이어")]
    [SerializeField, Min(0.1f)] private float playerCollisionHeight = 0.6f;
    [SerializeField, Min(0f)] private float minimumPlayerImpactSpeed = 0.08f;
    [SerializeField, Range(0f, 1f)] private float minimumPlayerImpactAlignment = 0.25f;
    [SerializeField] private bool friendlyPiecesAreIntangible = true;

    public float Restitution => Mathf.Clamp01(restitution);
    public float ImpulseMultiplier => Mathf.Max(0.1f, impulseMultiplier);
    public float SeparationEpsilon => Mathf.Max(0.000001f, separationEpsilon);
    public float PlayerCollisionHeight => Mathf.Max(0.1f, playerCollisionHeight);
    public float MinimumPlayerImpactSpeed => Mathf.Max(0f, minimumPlayerImpactSpeed);
    public float MinimumPlayerImpactAlignment =>
        Mathf.Clamp01(minimumPlayerImpactAlignment);
    public bool FriendlyPiecesAreIntangible => friendlyPiecesAreIntangible;

    public void Validate()
    {
        restitution = Mathf.Clamp01(restitution);
        impulseMultiplier = Mathf.Clamp(impulseMultiplier, 0.1f, 5f);
        separationEpsilon = Mathf.Max(0.000001f, separationEpsilon);
        playerCollisionHeight = Mathf.Max(0.1f, playerCollisionHeight);
        minimumPlayerImpactSpeed = Mathf.Max(0f, minimumPlayerImpactSpeed);
        minimumPlayerImpactAlignment = Mathf.Clamp01(minimumPlayerImpactAlignment);
    }
}

[Serializable]
public sealed class PieceArchetypeSettings
{
    [SerializeField] private ChessPieceType pieceType = ChessPieceType.Pawn;
    [SerializeField] private bool acceptsCommands = true;
    [SerializeField] private PieceMovementMode movementMode = PieceMovementMode.Free;

    [Header("이동 방식")]
    [Tooltip("Continuous keeps moving until Stop. Flick Impulse applies one hit and then slows through friction.")]
    [SerializeField] private PieceMovementControl movementControl =
        PieceMovementControl.FlickImpulse;
    [Tooltip("Used only by Continuous (Legacy), in board squares per second.")]
    [SerializeField, Min(0f)] private float moveSpeed = 0.85f;
    [SerializeField, Min(0f)] private float turnSpeed = 90f;

    [Header("알까기 충격 이동")]
    [Tooltip("Initial speed for a quiet accepted voice command, in board squares per second.")]
    [SerializeField, Min(0f)] private float quietFlickSpeed = 1.5f;
    [Tooltip("Initial speed for a loud accepted voice command, in board squares per second.")]
    [SerializeField, Min(0f)] private float loudFlickSpeed = 5f;
    [Tooltip("Constant speed loss per second. Higher values stop the piece sooner.")]
    [SerializeField, Min(0f)] private float flickFriction = 3.5f;
    [Tooltip("Shapes how normalized voice dB becomes impulse speed. 1 is linear; above 1 rewards louder commands.")]
    [SerializeField, Min(0.01f)] private float flickLoudnessExponent = 1.35f;
    [Tooltip("When enabled, another command acts like another hit and adds to current momentum.")]
    [SerializeField] private bool accumulateFlickImpulses = true;
    [Tooltip("Safety cap applied after accumulated hits.")]
    [SerializeField, Min(0.01f)] private float maximumFlickSpeed = 8f;

    [Header("물리 반응")]
    [SerializeField, Min(0.01f)] private float mass = 1f;
    [SerializeField, Min(0.01f)] private float collisionRadius = 0.36f;
    [SerializeField, Min(0f)] private float knockbackDrag = 1.8f;
    [SerializeField, Min(0f)] private float ringOutDistance = 0.8f;
    [SerializeField, Min(0f)] private float commandCostMultiplier = 1f;

    [Header("점령전 점수")]
    [Tooltip("Periodic rule: this piece earns the amount whenever its independent timer completes while inside a zone.")]
    [SerializeField, Min(0.01f)] private float periodicCaptureIntervalSeconds = 1f;
    [SerializeField, Min(0f)] private float periodicCapturePoints = 1f;
    [Tooltip("Final Occupancy rule: this value is added if the piece is inside a zone when the match ends.")]
    [FormerlySerializedAs("captureWeight")]
    [SerializeField, Min(0f)] private float finalCaptureValue = 1f;
    [SerializeField] private List<ChessPieceAbility> abilities = new();

    public ChessPieceType PieceType => pieceType;
    public bool AcceptsCommands => acceptsCommands;
    public PieceMovementMode MovementMode => movementMode;
    public PieceMovementControl MovementControl => movementControl;
    public float MoveSpeed => Mathf.Max(0f, moveSpeed);
    public float TurnSpeed => Mathf.Max(0f, turnSpeed);
    public float FlickFriction => Mathf.Max(0f, flickFriction);
    public bool AccumulateFlickImpulses => accumulateFlickImpulses;
    public float MaximumFlickSpeed => Mathf.Max(0.01f, maximumFlickSpeed);
    public float Mass => Mathf.Max(0.01f, mass);
    public float CollisionRadius => Mathf.Max(0.01f, collisionRadius);
    public float KnockbackDrag => Mathf.Max(0f, knockbackDrag);
    public float RingOutDistance => Mathf.Max(0f, ringOutDistance);
    public float CommandCostMultiplier => Mathf.Max(0f, commandCostMultiplier);
    public float PeriodicCaptureIntervalSeconds =>
        Mathf.Max(0.01f, periodicCaptureIntervalSeconds);
    public float PeriodicCapturePoints => Mathf.Max(0f, periodicCapturePoints);
    public float FinalCaptureValue => Mathf.Max(0f, finalCaptureValue);
    public IReadOnlyList<ChessPieceAbility> Abilities => abilities;

    public float GetFlickSpeed(float normalizedVoiceLoudness)
    {
        float shapedLoudness = Mathf.Pow(
            Mathf.Clamp01(normalizedVoiceLoudness),
            Mathf.Max(0.01f, flickLoudnessExponent));
        return Mathf.Lerp(
            Mathf.Max(0f, quietFlickSpeed),
            Mathf.Max(quietFlickSpeed, loudFlickSpeed),
            shapedLoudness);
    }

    public static PieceArchetypeSettings CreateDefault(ChessPieceType pieceType)
    {
        PieceArchetypeSettings settings = new()
        {
            pieceType = pieceType,
            movementControl = PieceMovementControl.FlickImpulse,
            mass = pieceType switch
            {
                ChessPieceType.Pawn => 0.8f,
                ChessPieceType.Knight => 1.05f,
                ChessPieceType.Bishop => 0.95f,
                ChessPieceType.Rook => 1.35f,
                ChessPieceType.Queen => 1.15f,
                ChessPieceType.King => 1.4f,
                _ => 1f
            },
            ringOutDistance = pieceType == ChessPieceType.King ? 0f : 0.8f
        };
        return settings;
    }

    public static PieceArchetypeSettings CreateLegacyFallback(
        ChessPieceType pieceType,
        float moveSpeed,
        float turnSpeed,
        float mass,
        float collisionRadius,
        float knockbackDrag,
        float ringOutDistance)
    {
        PieceArchetypeSettings settings = CreateDefault(pieceType);
        settings.movementControl = PieceMovementControl.Continuous;
        settings.moveSpeed = moveSpeed;
        settings.turnSpeed = turnSpeed;
        settings.mass = mass;
        settings.collisionRadius = collisionRadius;
        settings.knockbackDrag = knockbackDrag;
        settings.ringOutDistance = pieceType == ChessPieceType.King
            ? 0f
            : ringOutDistance;
        settings.Validate();
        return settings;
    }

    public void Validate()
    {
        moveSpeed = Mathf.Max(0f, moveSpeed);
        turnSpeed = Mathf.Max(0f, turnSpeed);
        quietFlickSpeed = Mathf.Max(0f, quietFlickSpeed);
        loudFlickSpeed = Mathf.Max(quietFlickSpeed, loudFlickSpeed);
        flickFriction = Mathf.Max(0f, flickFriction);
        flickLoudnessExponent = Mathf.Max(0.01f, flickLoudnessExponent);
        maximumFlickSpeed = Mathf.Max(0.01f, maximumFlickSpeed);
        mass = Mathf.Max(0.01f, mass);
        collisionRadius = Mathf.Max(0.01f, collisionRadius);
        knockbackDrag = Mathf.Max(0f, knockbackDrag);
        ringOutDistance = Mathf.Max(0f, ringOutDistance);
        commandCostMultiplier = Mathf.Max(0f, commandCostMultiplier);
        periodicCaptureIntervalSeconds = Mathf.Max(
            0.01f,
            periodicCaptureIntervalSeconds);
        periodicCapturePoints = Mathf.Max(0f, periodicCapturePoints);
        finalCaptureValue = Mathf.Max(0f, finalCaptureValue);
        abilities ??= new List<ChessPieceAbility>();
    }
}

[Serializable]
public sealed class InitialPiecePlacement
{
    [SerializeField] private bool enabled = true;
    [SerializeField] private PlayerTeam team = PlayerTeam.White;
    [SerializeField] private ChessPieceType pieceType = ChessPieceType.Pawn;
    [SerializeField] private Vector2 boardPosition;
    [SerializeField, Range(0f, 360f)] private float heading;

    public bool Enabled => enabled;
    public PlayerTeam Team => team;
    public ChessPieceType PieceType => pieceType;
    public Vector2 BoardPosition => boardPosition;
    public float Heading => Mathf.Repeat(heading, 360f);

    public InitialPiecePlacement()
    {
    }

    public InitialPiecePlacement(
        PlayerTeam team,
        ChessPieceType pieceType,
        Vector2 boardPosition,
        float heading = 0f)
    {
        enabled = true;
        this.team = team;
        this.pieceType = pieceType;
        this.boardPosition = boardPosition;
        this.heading = heading;
    }
}

[Serializable]
public sealed class BoardSetupSettings
{
    [SerializeField] private bool useCustomStartingPosition;
    [SerializeField] private List<InitialPiecePlacement> customPlacements = new();

    public bool UseCustomStartingPosition => useCustomStartingPosition;
    public IReadOnlyList<InitialPiecePlacement> CustomPlacements => customPlacements;

    public void Validate()
    {
        customPlacements ??= new List<InitialPiecePlacement>();
    }

    public void ReplaceWithEditableStandardPosition(
        IReadOnlyList<InitialPiecePlacement> standardPlacements)
    {
        customPlacements = new List<InitialPiecePlacement>(standardPlacements.Count);

        foreach (InitialPiecePlacement placement in standardPlacements)
        {
            customPlacements.Add(new InitialPiecePlacement(
                placement.Team,
                placement.PieceType,
                placement.BoardPosition,
                placement.Heading));
        }

        useCustomStartingPosition = true;
    }
}

[Serializable]
public sealed class CaptureZoneSettings
{
    [SerializeField] private bool enabled = true;
    [SerializeField] private string displayName = "Centre";
    [SerializeField] private Vector2 boardPosition = new(3.5f, 3.5f);
    [SerializeField, Min(0.05f)] private float radiusInSquares = 1f;

    [Header("런타임 점령 원 표시")]
    [SerializeField] private bool showFilledCircle = true;
    [SerializeField] private Color fillColor = new(0.1f, 0.75f, 1f, 0.16f);
    [SerializeField] private Color outlineColor = new(0.15f, 0.9f, 1f, 0.95f);
    [SerializeField, Range(16, 128)] private int circleSegments = 64;
    [SerializeField, Min(0.005f)] private float outlineWidthInSquares = 0.035f;
    [SerializeField, Min(0f)] private float heightOffsetInSquares = 0.015f;

    public bool Enabled => enabled;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName)
        ? "Capture Zone"
        : displayName;
    public Vector2 BoardPosition => boardPosition;
    public float RadiusInSquares => Mathf.Max(0.05f, radiusInSquares);
    public bool ShowFilledCircle => showFilledCircle;
    public Color FillColor => fillColor;
    public Color OutlineColor => outlineColor;
    public int CircleSegments => Mathf.Clamp(circleSegments, 16, 128);
    public float OutlineWidthInSquares => Mathf.Max(0.005f, outlineWidthInSquares);
    public float HeightOffsetInSquares => Mathf.Max(0f, heightOffsetInSquares);

    public void Validate()
    {
        radiusInSquares = Mathf.Max(0.05f, radiusInSquares);
        circleSegments = Mathf.Clamp(circleSegments, 16, 128);
        outlineWidthInSquares = Mathf.Max(0.005f, outlineWidthInSquares);
        heightOffsetInSquares = Mathf.Max(0f, heightOffsetInSquares);
    }
}

[Serializable]
public sealed class CaptureModeSettings
{
    [Tooltip("Master switch. When disabled, no circle is drawn and no capture score is evaluated.")]
    [SerializeField] private bool enabled;
    [SerializeField] private CaptureScoringRule scoringRule =
        CaptureScoringRule.PeriodicPerPiece;
    [Tooltip("At the match time limit, use capture score instead of the clock's normal resolution rule.")]
    [SerializeField] private bool resolveWinnerAtTimeLimit = true;
    [Tooltip("Periodic rule: leaving a circle discards that piece's partial interval. Otherwise it resumes when the piece returns.")]
    [SerializeField] private bool resetPeriodicTimerWhenLeaving = true;
    [SerializeField] private List<CaptureZoneSettings> zones = new()
    {
        new CaptureZoneSettings()
    };

    public bool Enabled => enabled;
    public CaptureScoringRule ScoringRule => scoringRule;
    public bool ResolveWinnerAtTimeLimit => resolveWinnerAtTimeLimit;
    public bool ResetPeriodicTimerWhenLeaving => resetPeriodicTimerWhenLeaving;
    public IReadOnlyList<CaptureZoneSettings> Zones => zones;

    public void Validate()
    {
        zones ??= new List<CaptureZoneSettings>();

        foreach (CaptureZoneSettings zone in zones)
        {
            zone?.Validate();
        }
    }
}

[Serializable]
public sealed class PlayerCommanderSettings
{
    [Header("팀 및 아바타")]
    [SerializeField, Min(1)] private int maximumPlayersPerTeam = 2;
    [SerializeField, Min(0.1f)] private float avatarHeightInSquares = 0.68f;
    [SerializeField, Min(0.01f)] private float avatarRadiusInSquares = 0.16f;
    [SerializeField, Range(1f, 60f)] private float poseUpdatesPerSecond = 20f;
    [SerializeField, Min(0f)] private float maximumPoseHeightInSquares = 4f;
    [SerializeField] private Color whiteAvatarColor = new(0.92f, 0.95f, 1f, 1f);
    [SerializeField] private Color blackAvatarColor = new(0.08f, 0.12f, 0.2f, 1f);
    [SerializeField] private Color unassignedAvatarColor = new(0.45f, 0.5f, 0.55f, 1f);

    [Header("1인칭 이동")]
    [SerializeField, Min(0.1f)] private float moveSpeedInSquares = 5f;
    [SerializeField, Min(0.01f)] private float mouseSensitivity = 0.08f;
    [SerializeField, Range(-89f, 0f)] private float minimumPitch = -75f;
    [SerializeField, Range(0f, 89f)] private float maximumPitch = 75f;
    [SerializeField, Range(0.05f, 1f)] private float eyeHeightAsPieceFraction = 0.5f;
    [SerializeField, Range(0.01f, 1f)] private float collisionRadiusInSquares = 0.16f;
    [SerializeField, Min(0.1f)] private float jumpSpeedInSquares = 4.2f;
    [SerializeField, Min(0.1f)] private float gravityInSquares = 15f;
    [SerializeField, Min(0f)] private float knockbackDrag = 2.2f;

    [Header("명령 입력")]
    [SerializeField] private Key endTurnKey = Key.Enter;
    [SerializeField] private PieceSelectionMouseButton confirmSelectionButton =
        PieceSelectionMouseButton.Left;

    public int MaximumPlayersPerTeam => Mathf.Max(1, maximumPlayersPerTeam);
    public float AvatarHeightInSquares => Mathf.Max(0.1f, avatarHeightInSquares);
    public float AvatarRadiusInSquares => Mathf.Max(0.01f, avatarRadiusInSquares);
    public float PoseSendInterval => 1f / Mathf.Clamp(poseUpdatesPerSecond, 1f, 60f);
    public float MaximumPoseHeightInSquares => Mathf.Max(0f, maximumPoseHeightInSquares);
    public Color WhiteAvatarColor => whiteAvatarColor;
    public Color BlackAvatarColor => blackAvatarColor;
    public Color UnassignedAvatarColor => unassignedAvatarColor;
    public float MoveSpeedInSquares => Mathf.Max(0.1f, moveSpeedInSquares);
    public float MouseSensitivity => Mathf.Max(0.01f, mouseSensitivity);
    public float MinimumPitch => Mathf.Clamp(minimumPitch, -89f, 0f);
    public float MaximumPitch => Mathf.Clamp(maximumPitch, 0f, 89f);
    public float EyeHeightAsPieceFraction =>
        Mathf.Clamp(eyeHeightAsPieceFraction, 0.05f, 1f);
    public float CollisionRadiusInSquares => Mathf.Max(0.01f, collisionRadiusInSquares);
    public float JumpSpeedInSquares => Mathf.Max(0.1f, jumpSpeedInSquares);
    public float GravityInSquares => Mathf.Max(0.1f, gravityInSquares);
    public float KnockbackDrag => Mathf.Max(0f, knockbackDrag);
    public Key EndTurnKey => endTurnKey;
    public PieceSelectionMouseButton ConfirmSelectionButton => confirmSelectionButton;
    public void Validate()
    {
        maximumPlayersPerTeam = Mathf.Max(1, maximumPlayersPerTeam);
        avatarHeightInSquares = Mathf.Max(0.1f, avatarHeightInSquares);
        avatarRadiusInSquares = Mathf.Max(0.01f, avatarRadiusInSquares);
        poseUpdatesPerSecond = Mathf.Clamp(poseUpdatesPerSecond, 1f, 60f);
        maximumPoseHeightInSquares = Mathf.Max(0f, maximumPoseHeightInSquares);
        moveSpeedInSquares = Mathf.Max(0.1f, moveSpeedInSquares);
        mouseSensitivity = Mathf.Max(0.01f, mouseSensitivity);
        minimumPitch = Mathf.Clamp(minimumPitch, -89f, 0f);
        maximumPitch = Mathf.Clamp(maximumPitch, 0f, 89f);
        eyeHeightAsPieceFraction = Mathf.Clamp(eyeHeightAsPieceFraction, 0.05f, 1f);
        collisionRadiusInSquares = Mathf.Max(0.01f, collisionRadiusInSquares);
        jumpSpeedInSquares = Mathf.Max(0.1f, jumpSpeedInSquares);
        gravityInSquares = Mathf.Max(0.1f, gravityInSquares);
        knockbackDrag = Mathf.Max(0f, knockbackDrag);
    }
}

[Serializable]
public sealed class BoardPresentationSettings
{
    [Header("기물 프리팹")]
    [SerializeField] private ChessPiecePrefabSet whitePieces = new();
    [SerializeField] private ChessPiecePrefabSet blackPieces = new();

    [Header("보드 좌표와 간격")]
    [SerializeField] private ChessBoardAnchor anchor = ChessBoardAnchor.BoardCenter;
    [SerializeField] private ChessPlacementPlane placementPlane =
        ChessPlacementPlane.WorldHorizontal;
    [SerializeField] private float layoutYawOffset;
    [SerializeField, Min(0.001f)] private float fileSpacing = 1f;
    [SerializeField, Min(0.001f)] private float rankSpacing = 1f;
    [SerializeField] private float heightOffset;
    [SerializeField, Min(0f)] private float boardBorderWidthInSquares =
        ChessPieceSpawner.DefaultBoardBorderWidthInSquares;

    [Header("기물 방향과 장외 연출")]
    [SerializeField] private Vector3 whiteRotationOffset;
    [SerializeField] private Vector3 blackRotationOffset = new(0f, 180f, 0f);
    [SerializeField, Min(0.1f)] private float ringOutVisualDistance = 0.8f;
    [SerializeField, Min(0.1f)] private float ringOutDropDistance = 2.5f;
    [SerializeField, Range(0f, 120f)] private float ringOutTiltAngle = 82f;

    [Header("선택 표시")]
    [SerializeField] private Color selectionMarkerColor = new(1f, 0.8f, 0f, 1f);
    [SerializeField, Range(16, 96)] private int selectionMarkerSegments = 48;
    [SerializeField] private Color voiceHoverMarkerColor =
        new(0.1f, 0.45f, 1f, 1f);
    [SerializeField] private Color confirmedVoiceMarkerColor =
        new(1f, 0.38f, 0.05f, 1f);

    [Header("기물 방향 안내선")]
    [SerializeField] private Color whiteHeadingArrowColor =
        new(0.1f, 0.85f, 1f, 0.95f);
    [SerializeField] private Color blackHeadingArrowColor =
        new(1f, 0.3f, 0.15f, 0.95f);
    [SerializeField, Range(0.3f, 1f)] private float headingArrowLengthInSquares = 0.72f;
    [SerializeField, Range(0.02f, 0.15f)] private float headingArrowWidthInSquares = 0.065f;
    [SerializeField, Range(0.005f, 0.1f)] private float headingArrowHeightInSquares = 0.025f;
    [SerializeField] private bool generateOnStart = true;

    public ChessPiecePrefabSet WhitePieces => whitePieces;
    public ChessPiecePrefabSet BlackPieces => blackPieces;
    public ChessBoardAnchor Anchor => anchor;
    public ChessPlacementPlane PlacementPlane => placementPlane;
    public float LayoutYawOffset => layoutYawOffset;
    public float FileSpacing => Mathf.Max(0.001f, fileSpacing);
    public float RankSpacing => Mathf.Max(0.001f, rankSpacing);
    public float HeightOffset => heightOffset;
    public float BoardBorderWidthInSquares => Mathf.Max(0f, boardBorderWidthInSquares);
    public Vector3 WhiteRotationOffset => whiteRotationOffset;
    public Vector3 BlackRotationOffset => blackRotationOffset;
    public float RingOutVisualDistance => Mathf.Max(0.1f, ringOutVisualDistance);
    public float RingOutDropDistance => Mathf.Max(0.1f, ringOutDropDistance);
    public float RingOutTiltAngle => Mathf.Clamp(ringOutTiltAngle, 0f, 120f);
    public Color SelectionMarkerColor => selectionMarkerColor;
    public int SelectionMarkerSegments => Mathf.Clamp(selectionMarkerSegments, 16, 96);
    public Color VoiceHoverMarkerColor => voiceHoverMarkerColor;
    public Color ConfirmedVoiceMarkerColor => confirmedVoiceMarkerColor;
    public Color WhiteHeadingArrowColor => whiteHeadingArrowColor;
    public Color BlackHeadingArrowColor => blackHeadingArrowColor;
    public float HeadingArrowLengthInSquares =>
        Mathf.Clamp(headingArrowLengthInSquares, 0.3f, 1f);
    public float HeadingArrowWidthInSquares =>
        Mathf.Clamp(headingArrowWidthInSquares, 0.02f, 0.15f);
    public float HeadingArrowHeightInSquares =>
        Mathf.Clamp(headingArrowHeightInSquares, 0.005f, 0.1f);
    public bool GenerateOnStart => generateOnStart;

    public void Validate()
    {
        whitePieces ??= new ChessPiecePrefabSet();
        blackPieces ??= new ChessPiecePrefabSet();
        fileSpacing = Mathf.Max(0.001f, fileSpacing);
        rankSpacing = Mathf.Max(0.001f, rankSpacing);
        boardBorderWidthInSquares = Mathf.Max(0f, boardBorderWidthInSquares);
        ringOutVisualDistance = Mathf.Max(0.1f, ringOutVisualDistance);
        ringOutDropDistance = Mathf.Max(0.1f, ringOutDropDistance);
        ringOutTiltAngle = Mathf.Clamp(ringOutTiltAngle, 0f, 120f);
        selectionMarkerSegments = Mathf.Clamp(selectionMarkerSegments, 16, 96);
        headingArrowLengthInSquares = Mathf.Clamp(
            headingArrowLengthInSquares,
            0.3f,
            1f);
        headingArrowWidthInSquares = Mathf.Clamp(
            headingArrowWidthInSquares,
            0.02f,
            0.15f);
        headingArrowHeightInSquares = Mathf.Clamp(
            headingArrowHeightInSquares,
            0.005f,
            0.1f);
    }
}

[Serializable]
public sealed class VoiceRecognitionSettings
{
    [Header("음성 인식 판정")]
    [SerializeField, Range(0f, 1f)] private float minimumConfidence = 0.55f;
    [SerializeField, Range(-80f, 0f)] private float quietCommandDecibels = -45f;
    [SerializeField, Range(-80f, 0f)] private float loudCommandDecibels = -12f;
    [SerializeField, Min(0f)] private float minimumCommandReach = 1f;
    [SerializeField, Min(0.01f)] private float maximumCommandReach = 12f;

    [Header("자동 음성 감지")]
    [SerializeField, Min(0.01f)] private float voiceStartHoldSeconds = 0.08f;
    [SerializeField, Min(0.01f)] private float voiceEndSilenceSeconds = 0.12f;
    [SerializeField, Min(0f)] private float targetSwitchBoundarySilenceSeconds = 0.04f;
    [SerializeField, Min(0f)] private float minimumTargetSwitchUtteranceSeconds = 0.3f;
    [SerializeField, Min(0.1f)] private float maximumAutomaticUtteranceSeconds = 3f;
    [SerializeField, Min(0.1f)] private float noiseCalibrationSeconds = 1.5f;
    [SerializeField, Min(0.01f)] private float speechBoundaryAverageSeconds = 0.2f;

    [Header("플레이어별 음성 설정 기본값")]
    [Tooltip("플레이어가 UI에서 저장한 값이 있으면 그 값이 우선합니다.")]
    [SerializeField] private VoiceInputMode defaultInputMode = VoiceInputMode.Automatic;
    [SerializeField, Range(0f, 1f)] private float defaultVoiceSensitivity = 0.55f;
    [SerializeField] private bool defaultAutomaticNoiseCalibration = true;
    [SerializeField, Range(-80f, -10f)] private float defaultNoiseFloorDecibels = -55f;
    [SerializeField] private Key pushToTalkKey = Key.V;

    public float MinimumConfidence => Mathf.Clamp01(minimumConfidence);
    public float QuietCommandDecibels => Mathf.Clamp(quietCommandDecibels, -80f, 0f);
    public float LoudCommandDecibels => Mathf.Clamp(
        loudCommandDecibels,
        QuietCommandDecibels + 0.01f,
        0f);
    public float MinimumCommandReach => Mathf.Max(0f, minimumCommandReach);
    public float MaximumCommandReach => Mathf.Max(MinimumCommandReach + 0.01f, maximumCommandReach);
    public float VoiceStartHoldSeconds => Mathf.Max(0.01f, voiceStartHoldSeconds);
    public float VoiceEndSilenceSeconds => Mathf.Max(0.01f, voiceEndSilenceSeconds);
    public float TargetSwitchBoundarySilenceSeconds =>
        Mathf.Max(0f, targetSwitchBoundarySilenceSeconds);
    public float MinimumTargetSwitchUtteranceSeconds =>
        Mathf.Max(0f, minimumTargetSwitchUtteranceSeconds);
    public float MaximumAutomaticUtteranceSeconds =>
        Mathf.Max(0.1f, maximumAutomaticUtteranceSeconds);
    public float NoiseCalibrationSeconds => Mathf.Max(0.1f, noiseCalibrationSeconds);
    public float SpeechBoundaryAverageSeconds => Mathf.Max(0.01f, speechBoundaryAverageSeconds);
    public VoiceInputMode DefaultInputMode => defaultInputMode;
    public float DefaultVoiceSensitivity => Mathf.Clamp01(defaultVoiceSensitivity);
    public bool DefaultAutomaticNoiseCalibration => defaultAutomaticNoiseCalibration;
    public float DefaultNoiseFloorDecibels => Mathf.Clamp(defaultNoiseFloorDecibels, -80f, -10f);
    public Key PushToTalkKey => pushToTalkKey;

    public void Validate()
    {
        minimumConfidence = Mathf.Clamp01(minimumConfidence);
        quietCommandDecibels = Mathf.Clamp(quietCommandDecibels, -80f, 0f);
        loudCommandDecibels = Mathf.Clamp(
            loudCommandDecibels,
            quietCommandDecibels + 0.01f,
            0f);
        minimumCommandReach = Mathf.Max(0f, minimumCommandReach);
        maximumCommandReach = Mathf.Max(minimumCommandReach + 0.01f, maximumCommandReach);
        voiceStartHoldSeconds = Mathf.Max(0.01f, voiceStartHoldSeconds);
        voiceEndSilenceSeconds = Mathf.Max(0.01f, voiceEndSilenceSeconds);
        targetSwitchBoundarySilenceSeconds = Mathf.Max(0f, targetSwitchBoundarySilenceSeconds);
        minimumTargetSwitchUtteranceSeconds = Mathf.Max(0f, minimumTargetSwitchUtteranceSeconds);
        maximumAutomaticUtteranceSeconds = Mathf.Max(0.1f, maximumAutomaticUtteranceSeconds);
        noiseCalibrationSeconds = Mathf.Max(0.1f, noiseCalibrationSeconds);
        speechBoundaryAverageSeconds = Mathf.Max(0.01f, speechBoundaryAverageSeconds);
        defaultVoiceSensitivity = Mathf.Clamp01(defaultVoiceSensitivity);
        defaultNoiseFloorDecibels = Mathf.Clamp(defaultNoiseFloorDecibels, -80f, -10f);
    }
}

[Serializable]
public sealed class InterfaceAndSessionSettings
{
    [Header("기준 해상도")]
    [SerializeField, Min(320f)] private float designWidth = 1600f;
    [SerializeField, Min(180f)] private float designHeight = 900f;

    [Header("대전 세션")]
    [SerializeField, Min(2)] private int maximumSessionPlayers = 4;
    [SerializeField, Min(1)] private int sessionListPollingIntervalSeconds = 5;

    [Header("인게임 HUD 배치")]
    [SerializeField] private Rect matchTimerPanel = new(675f, 30f, 250f, 78f);
    [SerializeField] private Rect costPanel = new(30f, 30f, 360f, 142f);
    [SerializeField] private Rect captureScorePanel = new(575f, 174f, 450f, 46f);
    [SerializeField] private Key pauseMenuKey = Key.Escape;

    public float DesignWidth => Mathf.Max(320f, designWidth);
    public float DesignHeight => Mathf.Max(180f, designHeight);
    public int MaximumSessionPlayers => Mathf.Max(2, maximumSessionPlayers);
    public int SessionListPollingIntervalSeconds =>
        Mathf.Max(1, sessionListPollingIntervalSeconds);
    public Rect MatchTimerPanel => EnsurePositiveSize(matchTimerPanel);
    public Rect CostPanel => EnsurePositiveSize(costPanel);
    public Rect CaptureScorePanel => EnsurePositiveSize(captureScorePanel);
    public Key PauseMenuKey => pauseMenuKey;

    public void Validate()
    {
        designWidth = Mathf.Max(320f, designWidth);
        designHeight = Mathf.Max(180f, designHeight);
        maximumSessionPlayers = Mathf.Max(2, maximumSessionPlayers);
        sessionListPollingIntervalSeconds = Mathf.Max(1, sessionListPollingIntervalSeconds);
        matchTimerPanel = EnsurePositiveSize(matchTimerPanel);
        costPanel = EnsurePositiveSize(costPanel);
        captureScorePanel = EnsurePositiveSize(captureScorePanel);
    }

    private static Rect EnsurePositiveSize(Rect value)
    {
        value.width = Mathf.Max(1f, value.width);
        value.height = Mathf.Max(1f, value.height);
        return value;
    }
}

[Serializable]
public sealed class MatchPresentationSettings
{
    [SerializeField] private bool playRoyalDeathCinematic = true;
    [SerializeField, Min(0.1f)] private float royalDeathDuration = 3f;
    [SerializeField, Min(0.1f)] private float cameraDistanceInSquares = 2.6f;
    [SerializeField, Min(0.1f)] private float cameraHeightInSquares = 1.15f;
    [SerializeField, Min(0.1f)] private float dropDistanceInSquares = 4f;
    [SerializeField, Min(0f)] private float outwardDistanceInSquares = 1.1f;
    [SerializeField, Range(0f, 180f)] private float tiltAngle = 110f;
    [SerializeField, Range(1f, 179f)] private float cameraFieldOfView = 48f;

    public bool PlayRoyalDeathCinematic => playRoyalDeathCinematic;
    public float RoyalDeathDuration => Mathf.Max(0.1f, royalDeathDuration);
    public float CameraDistanceInSquares => Mathf.Max(0.1f, cameraDistanceInSquares);
    public float CameraHeightInSquares => Mathf.Max(0.1f, cameraHeightInSquares);
    public float DropDistanceInSquares => Mathf.Max(0.1f, dropDistanceInSquares);
    public float OutwardDistanceInSquares => Mathf.Max(0f, outwardDistanceInSquares);
    public float TiltAngle => Mathf.Clamp(tiltAngle, 0f, 180f);
    public float CameraFieldOfView => Mathf.Clamp(cameraFieldOfView, 1f, 179f);

    public void Validate()
    {
        royalDeathDuration = Mathf.Max(0.1f, royalDeathDuration);
        cameraDistanceInSquares = Mathf.Max(0.1f, cameraDistanceInSquares);
        cameraHeightInSquares = Mathf.Max(0.1f, cameraHeightInSquares);
        dropDistanceInSquares = Mathf.Max(0.1f, dropDistanceInSquares);
        outwardDistanceInSquares = Mathf.Max(0f, outwardDistanceInSquares);
        tiltAngle = Mathf.Clamp(tiltAngle, 0f, 180f);
        cameraFieldOfView = Mathf.Clamp(cameraFieldOfView, 1f, 179f);
    }
}

[CreateAssetMenu(
    fileName = "GameModeConfiguration",
    menuName = "Voice Chess/Game Mode Configuration")]
public sealed class GameModeConfiguration : ScriptableObject
{
    [SerializeField] private string displayName = "Classic Voice Chess";
    [SerializeField] private MatchClockSettings clock = new();
    [SerializeField] private CommandEconomySettings commands = new();
    [SerializeField] private VictorySettings victory = new();
    [SerializeField] private CollisionSettings collisions = new();
    [SerializeField] private PlayerCommanderSettings players = new();
    [SerializeField] private BoardSetupSettings boardSetup = new();
    [SerializeField] private List<PieceArchetypeSettings> pieceArchetypes =
        CreateDefaultArchetypes();
    [SerializeField] private CaptureModeSettings captureMode = new();
    [SerializeField] private MatchPresentationSettings presentation = new();
    [SerializeField] private BoardPresentationSettings boardPresentation = new();
    [SerializeField] private VoiceRecognitionSettings voiceRecognition = new();
    [SerializeField] private InterfaceAndSessionSettings interfaceAndSession = new();

    private static readonly IReadOnlyList<InitialPiecePlacement> StandardPlacements =
        CreateStandardPlacements();
    [NonSerialized] private readonly Dictionary<ChessPieceType, PieceArchetypeSettings>
        _pieceLookup = new();

    public string DisplayName => string.IsNullOrWhiteSpace(displayName)
        ? name
        : displayName;
    public MatchClockSettings Clock => clock;
    public CommandEconomySettings Commands => commands;
    public VictorySettings Victory => victory;
    public CollisionSettings Collisions => collisions;
    public PlayerCommanderSettings Players => players;
    public BoardSetupSettings BoardSetup => boardSetup;
    public CaptureModeSettings CaptureMode => captureMode;
    public MatchPresentationSettings Presentation => presentation;
    public BoardPresentationSettings BoardPresentation => boardPresentation;
    public VoiceRecognitionSettings VoiceRecognition => voiceRecognition;
    public InterfaceAndSessionSettings InterfaceAndSession => interfaceAndSession;
    public IReadOnlyList<InitialPiecePlacement> InitialPlacements =>
        boardSetup.UseCustomStartingPosition
            ? boardSetup.CustomPlacements
            : StandardPlacements;

    public PieceArchetypeSettings GetPiece(ChessPieceType pieceType)
    {
        if (_pieceLookup.Count != pieceArchetypes.Count)
        {
            RebuildPieceLookup();
        }

        if (_pieceLookup.TryGetValue(pieceType, out PieceArchetypeSettings settings))
        {
            return settings;
        }

        return PieceArchetypeSettings.CreateDefault(pieceType);
    }

    public int GetInitialPieceCount(PlayerTeam team)
    {
        int count = 0;

        foreach (InitialPiecePlacement placement in InitialPlacements)
        {
            if (placement != null && placement.Enabled && placement.Team == team)
            {
                count++;
            }
        }

        return count;
    }

    public void MakeStandardPositionEditable()
    {
        EnsureChildren();
        boardSetup.ReplaceWithEditableStandardPosition(StandardPlacements);
    }

    public void ResetPieceArchetypesToDefaults()
    {
        pieceArchetypes = CreateDefaultArchetypes();
        RebuildPieceLookup();
    }

    private void OnEnable()
    {
        EnsureChildren();
        RebuildPieceLookup();
    }

    private void OnValidate()
    {
        EnsureChildren();
        clock.Validate();
        commands.Validate();
        victory.Validate();
        collisions.Validate();
        players.Validate();
        boardSetup.Validate();
        captureMode.Validate();
        presentation.Validate();
        boardPresentation.Validate();
        voiceRecognition.Validate();
        interfaceAndSession.Validate();

        foreach (PieceArchetypeSettings archetype in pieceArchetypes)
        {
            archetype?.Validate();
        }

        RebuildPieceLookup();
    }

    private void EnsureChildren()
    {
        clock ??= new MatchClockSettings();
        commands ??= new CommandEconomySettings();
        victory ??= new VictorySettings();
        collisions ??= new CollisionSettings();
        players ??= new PlayerCommanderSettings();
        boardSetup ??= new BoardSetupSettings();
        pieceArchetypes ??= CreateDefaultArchetypes();
        captureMode ??= new CaptureModeSettings();
        presentation ??= new MatchPresentationSettings();
        boardPresentation ??= new BoardPresentationSettings();
        voiceRecognition ??= new VoiceRecognitionSettings();
        interfaceAndSession ??= new InterfaceAndSessionSettings();
    }

    private void RebuildPieceLookup()
    {
        _pieceLookup.Clear();

        foreach (PieceArchetypeSettings settings in pieceArchetypes)
        {
            if (settings != null && settings.PieceType != ChessPieceType.None)
            {
                _pieceLookup[settings.PieceType] = settings;
            }
        }
    }

    private static List<PieceArchetypeSettings> CreateDefaultArchetypes()
    {
        return new List<PieceArchetypeSettings>
        {
            PieceArchetypeSettings.CreateDefault(ChessPieceType.Pawn),
            PieceArchetypeSettings.CreateDefault(ChessPieceType.Knight),
            PieceArchetypeSettings.CreateDefault(ChessPieceType.Bishop),
            PieceArchetypeSettings.CreateDefault(ChessPieceType.Rook),
            PieceArchetypeSettings.CreateDefault(ChessPieceType.Queen),
            PieceArchetypeSettings.CreateDefault(ChessPieceType.King)
        };
    }

    private static IReadOnlyList<InitialPiecePlacement> CreateStandardPlacements()
    {
        ChessPieceType[] backRank =
        {
            ChessPieceType.Rook,
            ChessPieceType.Knight,
            ChessPieceType.Bishop,
            ChessPieceType.Queen,
            ChessPieceType.King,
            ChessPieceType.Bishop,
            ChessPieceType.Knight,
            ChessPieceType.Rook
        };
        List<InitialPiecePlacement> placements = new(32);

        for (int file = 0; file < 8; file++)
        {
            placements.Add(new InitialPiecePlacement(
                PlayerTeam.White, backRank[file], new Vector2(file, 0f)));
            placements.Add(new InitialPiecePlacement(
                PlayerTeam.White, ChessPieceType.Pawn, new Vector2(file, 1f)));
            placements.Add(new InitialPiecePlacement(
                PlayerTeam.Black, ChessPieceType.Pawn, new Vector2(file, 6f)));
            placements.Add(new InitialPiecePlacement(
                PlayerTeam.Black, backRank[file], new Vector2(file, 7f)));
        }

        return placements;
    }
}
