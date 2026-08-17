using System;
using System.Collections.Generic;
using Unity.Netcode;
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
    [InspectorName("1 - 시선 선택")]
    LegacyLookSelection,
    [InspectorName("2 - 클릭 확정 선택 + 돌진")]
    ConfirmedSelectionCharge,
    [InspectorName("3 - 플레이어 주변 자동 선택 + 돌진")]
    ProximityAutoSelectionCharge
}

public enum CostConsumptionVersion : byte
{
    [InspectorName("구버전 - 명령별 고정 코스트")]
    FixedPerCommand,
    [InspectorName("신버전 - 발화 시간 비례 코스트")]
    VoiceDurationCharge
}

public enum CommandRestrictionMode : byte
{
    [InspectorName("없음")]
    None,
    [InspectorName("코스트")]
    Cost,
    [InspectorName("쿨타임")]
    Cooldown
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

[Flags]
public enum PieceTraitFlags : byte
{
    None = 0,
    IgnoreFriendlyPieceCollisions = 1 << 0,
    FirstAttackingCollisionOnly = 1 << 1
}

/// <summary>
/// Permanent archetype traits. Collision and movement code consumes this common
/// description instead of branching on the chess-piece type, so the same values
/// can also be supplied by a temporary spell effect later.
/// </summary>
[Serializable]
public sealed class PieceTraitSettings
{
    [Header("돌진 특성")]
    [Tooltip("최종 돌진 거리에 곱해지는 값입니다. 1보다 작으면 같은 충전 세기에서도 이동 거리가 짧습니다.")]
    [SerializeField, Min(0.01f)] private float chargeDistanceMultiplier = 1f;
    [Tooltip("우클릭/음성 충전 곡선의 성장 속도입니다. 1보다 작으면 초반에 거리가 더 천천히 증가하지만 최대 충전에는 도달합니다.")]
    [SerializeField, Min(0.05f)] private float chargeGrowthRate = 1f;

    [Header("충돌 특성")]
    [Tooltip("이 기물이 상대를 향해 이동하며 충돌할 때 상대가 받는 충격 배율입니다. 질량과 독립적으로 적용됩니다.")]
    [SerializeField, Min(0f)] private float attackingImpactMultiplier = 1f;
    [Tooltip("켜면 공격 충격 배율을 이동 명령 후 처음 충돌한 한 대상에게만 적용합니다.")]
    [SerializeField] private bool firstAttackingCollisionOnly;
    [Tooltip("켜면 이 기물은 같은 팀의 다른 기물과 서로 통과합니다.")]
    [SerializeField] private bool ignoreFriendlyPieceCollisions;

    public float ChargeDistanceMultiplier => Mathf.Max(
        0.01f,
        chargeDistanceMultiplier);
    public float ChargeGrowthRate => Mathf.Max(0.05f, chargeGrowthRate);
    public float AttackingImpactMultiplier => Mathf.Max(
        0f,
        attackingImpactMultiplier);
    public bool FirstAttackingCollisionOnly => firstAttackingCollisionOnly;
    public bool IgnoreFriendlyPieceCollisions => ignoreFriendlyPieceCollisions;

    public float ShapeChargePower(float chargePower)
    {
        return Mathf.Pow(
            Mathf.Clamp01(chargePower),
            1f / ChargeGrowthRate);
    }

    public static PieceTraitSettings CreateDefault(ChessPieceType pieceType)
    {
        PieceTraitSettings settings = new();

        switch (pieceType)
        {
            case ChessPieceType.Rook:
                settings.chargeDistanceMultiplier = 0.7f;
                settings.chargeGrowthRate = 0.65f;
                settings.attackingImpactMultiplier = 0.65f;
                break;
            case ChessPieceType.Bishop:
                settings.attackingImpactMultiplier = 1.55f;
                settings.firstAttackingCollisionOnly = true;
                break;
            case ChessPieceType.Knight:
                settings.ignoreFriendlyPieceCollisions = true;
                break;
            case ChessPieceType.Queen:
                settings.attackingImpactMultiplier = 1.3f;
                settings.firstAttackingCollisionOnly = true;
                break;
        }

        return settings;
    }

    public void Validate()
    {
        chargeDistanceMultiplier = Mathf.Max(0.01f, chargeDistanceMultiplier);
        chargeGrowthRate = Mathf.Max(0.05f, chargeGrowthRate);
        attackingImpactMultiplier = Mathf.Max(0f, attackingImpactMultiplier);
    }
}

/// <summary>
/// Optional, networked, time-limited additions to an archetype's permanent
/// traits. No current piece starts with one; future voice-spell abilities can put
/// this value on a piece without changing the collision or charge systems.
/// </summary>
[Serializable]
public struct TemporaryPieceTraitModifiers :
    INetworkSerializable,
    IEquatable<TemporaryPieceTraitModifiers>
{
    public float MassMultiplier;
    public float ChargeDistanceMultiplier;
    public float ChargeGrowthRateMultiplier;
    public float AttackingImpactMultiplier;
    public PieceTraitFlags AddedFlags;
    public double ExpiresAtServerTime;

    public TemporaryPieceTraitModifiers(
        double expiresAtServerTime,
        float massMultiplier = 1f,
        float chargeDistanceMultiplier = 1f,
        float chargeGrowthRateMultiplier = 1f,
        float attackingImpactMultiplier = 1f,
        PieceTraitFlags addedFlags = PieceTraitFlags.None)
    {
        MassMultiplier = Mathf.Max(0.01f, massMultiplier);
        ChargeDistanceMultiplier = Mathf.Max(0.01f, chargeDistanceMultiplier);
        ChargeGrowthRateMultiplier = Mathf.Max(0.05f, chargeGrowthRateMultiplier);
        AttackingImpactMultiplier = Mathf.Max(0f, attackingImpactMultiplier);
        AddedFlags = addedFlags;
        ExpiresAtServerTime = expiresAtServerTime;
    }

    public bool IsActive(double serverTime)
    {
        return ExpiresAtServerTime > serverTime;
    }

    public float ResolvedMassMultiplier => MassMultiplier > 0f
        ? MassMultiplier
        : 1f;
    public float ResolvedChargeDistanceMultiplier =>
        ChargeDistanceMultiplier > 0f ? ChargeDistanceMultiplier : 1f;
    public float ResolvedChargeGrowthRateMultiplier =>
        ChargeGrowthRateMultiplier > 0f ? ChargeGrowthRateMultiplier : 1f;
    public float ResolvedAttackingImpactMultiplier =>
        Mathf.Max(0f, AttackingImpactMultiplier);

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref MassMultiplier);
        serializer.SerializeValue(ref ChargeDistanceMultiplier);
        serializer.SerializeValue(ref ChargeGrowthRateMultiplier);
        serializer.SerializeValue(ref AttackingImpactMultiplier);
        serializer.SerializeValue(ref AddedFlags);
        serializer.SerializeValue(ref ExpiresAtServerTime);
    }

    public bool Equals(TemporaryPieceTraitModifiers other)
    {
        return MassMultiplier.Equals(other.MassMultiplier) &&
               ChargeDistanceMultiplier.Equals(other.ChargeDistanceMultiplier) &&
               ChargeGrowthRateMultiplier.Equals(
                   other.ChargeGrowthRateMultiplier) &&
               AttackingImpactMultiplier.Equals(other.AttackingImpactMultiplier) &&
               AddedFlags == other.AddedFlags &&
               ExpiresAtServerTime.Equals(other.ExpiresAtServerTime);
    }
}

public readonly struct ResolvedPieceTraits
{
    public readonly float MassMultiplier;
    public readonly float ChargeDistanceMultiplier;
    public readonly float ChargeGrowthRate;
    public readonly float AttackingImpactMultiplier;
    public readonly bool FirstAttackingCollisionOnly;
    public readonly bool IgnoreFriendlyPieceCollisions;

    public ResolvedPieceTraits(
        PieceTraitSettings permanentTraits,
        in TemporaryPieceTraitModifiers temporaryTraits,
        double serverTime)
    {
        permanentTraits ??= new PieceTraitSettings();
        bool hasTemporaryTraits = temporaryTraits.IsActive(serverTime);
        MassMultiplier = hasTemporaryTraits
            ? temporaryTraits.ResolvedMassMultiplier
            : 1f;
        ChargeDistanceMultiplier = permanentTraits.ChargeDistanceMultiplier *
            (hasTemporaryTraits
                ? temporaryTraits.ResolvedChargeDistanceMultiplier
                : 1f);
        ChargeGrowthRate = permanentTraits.ChargeGrowthRate *
            (hasTemporaryTraits
                ? temporaryTraits.ResolvedChargeGrowthRateMultiplier
                : 1f);
        AttackingImpactMultiplier = permanentTraits.AttackingImpactMultiplier *
            (hasTemporaryTraits
                ? temporaryTraits.ResolvedAttackingImpactMultiplier
                : 1f);
        PieceTraitFlags addedFlags = hasTemporaryTraits
            ? temporaryTraits.AddedFlags
            : PieceTraitFlags.None;
        FirstAttackingCollisionOnly =
            permanentTraits.FirstAttackingCollisionOnly ||
            (addedFlags & PieceTraitFlags.FirstAttackingCollisionOnly) != 0;
        IgnoreFriendlyPieceCollisions =
            permanentTraits.IgnoreFriendlyPieceCollisions ||
            (addedFlags & PieceTraitFlags.IgnoreFriendlyPieceCollisions) != 0;
    }

    public float ShapeChargePower(float chargePower)
    {
        return Mathf.Pow(
            Mathf.Clamp01(chargePower),
            1f / Mathf.Max(0.05f, ChargeGrowthRate));
    }
}

public enum CaptureScoringRule : byte
{
    [InspectorName("초당 기물별 점수")]
    PeriodicPerPiece,
    [InspectorName("종료 시 기물 가치 합산")]
    FinalOccupancyValue
}

public enum CaptureModeVersion : byte
{
    [InspectorName("구버전 - 고정 점령 원")]
    LegacyConfiguredZones,
    [InspectorName("신버전 - 랜덤 라운드 점령전")]
    RandomRoundControl
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
    [InspectorName("최대 확정 선택 기물 수")]
    [Tooltip("신규 명령 방식에서 동시에 확정 선택할 수 있는 기물 수입니다.")]
    [SerializeField, Range(1, 3)] private int maximumConfirmedSelections = 3;

    [Header("3번 선택 방식")]
    [Tooltip("플레이어를 중심으로 이 반지름 안에 있는 아군 기물을 모두 자동 선택합니다. 체스 칸 단위입니다.")]
    [SerializeField, Min(0.05f)]
    private float proximitySelectionRadiusInSquares = 1.5f;

    [Header("교대 턴제")]
    [SerializeField] private PlayerTeam firstTeam = PlayerTeam.White;
    [SerializeField] private bool advanceAfterAcceptedCommand = true;
    [SerializeField] private bool freezeInactiveTeamMovement;
    [SerializeField, Min(0f)] private float turnDurationSeconds;

    [Header("명령 제한 방식")]
    [Tooltip("없음, 충전식 코스트, 명령 후 쿨타임 중 하나를 선택합니다.")]
    [FormerlySerializedAs("costSystemEnabled")]
    [SerializeField] private CommandRestrictionMode commandRestrictionMode =
        CommandRestrictionMode.Cost;
    [Tooltip("쿨타임 방식에서 명령 성공 후 다음 명령까지 기다리는 시간입니다.")]
    [SerializeField, Min(0.01f)] private float commandCooldownSeconds = 2f;
    [Tooltip("남은 명령 쿨타임이 이 시간 이하일 때 다음 기물을 미리 선택할 수 있습니다. 0이면 쿨타임이 완전히 끝난 뒤에만 선택할 수 있습니다.")]
    [InspectorName("쿨타임 종료 전 선택 허용 시간 (초)")]
    [SerializeField, Min(0f)]
    private float commandCooldownSelectionLeadTimeSeconds = 0.5f;
    [Tooltip("중앙 조준점에 표시되는 원형 쿨타임 게이지의 지름입니다. 화면 픽셀 단위입니다.")]
    [SerializeField, Min(16f)]
    private float commandCooldownReticleDiameterPixels = 72f;

    [Header("기물별 이동 쿨타임")]
    [Tooltip("켜면 이동 또는 돌진 명령을 받은 기물은 개별 쿨타임 동안 다시 이동 명령을 받을 수 없습니다.")]
    [SerializeField] private bool pieceMovementCooldownEnabled = true;
    [Tooltip("기물별 설정에서 이동 쿨타임을 0초로 두었을 때 사용하는 기본 시간입니다.")]
    [InspectorName("기본 이동 쿨타임 (초)")]
    [SerializeField, Min(0.01f)] private float pieceMovementCooldownSeconds = 10f;
    [FormerlySerializedAs("startingPoints")]
    [SerializeField, Min(0f)] private float startingCost = 3f;
    [FormerlySerializedAs("maximumPoints")]
    [SerializeField, Min(0.01f)] private float maximumCost = 5f;
    [Tooltip("The recharge tick happens once per this many seconds.")]
    [SerializeField, Min(0.01f)] private float rechargeIntervalSeconds = 2f;
    [FormerlySerializedAs("pointsPerSecond")]
    [Tooltip("Cost restored on each recharge tick.")]
    [SerializeField, Min(0f)] private float rechargeAmount = 1f;

    [Header("코스트 소모 버전")]
    [SerializeField] private CostConsumptionVersion costConsumptionVersion =
        CostConsumptionVersion.FixedPerCommand;

    [Header("신규 코스트 방식 - 발화 시간")]
    [Tooltip("발화 중 코스트가 한 번 줄어들 때의 단위입니다. 0.1이나 0.01처럼 설정할 수 있습니다.")]
    [SerializeField, Min(0.001f)] private float voiceChargeCostStep = 0.05f;
    [Tooltip("위 코스트 단위가 소모되는 시간 간격입니다.")]
    [SerializeField, Min(0.01f)] private float voiceChargeSecondsPerCostStep = 0.05f;
    [Tooltip("코스트와 돌진 세기 계산에 사용할 최대 유효 발화 시간입니다.")]
    [SerializeField, Min(0.1f)] private float voiceChargeMaximumDurationSeconds = 3f;

    [Header("신규 돌진 - 거리 판정")]
    [SerializeField, Min(0f)] private float voiceChargeMinimumDistanceInSquares = 0.75f;
    [SerializeField, Min(0.01f)] private float voiceChargeMaximumDistanceInSquares = 8f;
    [InspectorName("마우스 돌진 최소 거리")]
    [Tooltip("우클릭 돌진을 누르기 시작했을 때의 거리입니다. 음성 돌진 최소 거리와 최대 음량의 초기 추가 거리 사이에서 조절됩니다.")]
    [SerializeField, Min(0f)] private float mouseChargeMinimumDistanceInSquares = 1f;
    [Tooltip("최대 음량으로 말하기 시작했을 때 최소 거리에 즉시 더해지는 거리입니다. 이후 발화 시간에 따라 최대 거리까지 계속 증가합니다.")]
    [SerializeField, Min(0f)] private float voiceChargeMaximumInitialLoudnessDistanceInSquares = 1f;
    [Tooltip("돌진이 시간에 따라 충전되는 기본 비중입니다. 이 값이 클수록 작은 목소리도 발화 길이만큼 안정적으로 충전됩니다.")]
    [SerializeField, Min(0f)] private float voiceChargeDurationWeight = 0.4f;
    [Tooltip("발화 시간 충전 곡선입니다. 1은 선형, 1보다 작으면 초반에 빠르게 자라고, 1보다 크면 후반에 빠르게 자랍니다.")]
    [SerializeField, Min(0.05f)] private float voiceChargeDurationExponent = 0.6f;
    [Tooltip("목소리 크기가 충전 효율에 미치는 비중입니다. 음량은 즉시 거리를 더하지 않고 말하는 동안의 충전 효율을 높입니다.")]
    [SerializeField, Min(0f)] private float voiceChargeLoudnessWeight = 0.35f;
    [Tooltip("돌진용 음량 곡선입니다. 1은 선형이며, 1보다 높이면 작은 목소리를 더 약하게 판정하면서 최대 음량은 그대로 유지합니다.")]
    [SerializeField, Min(0.01f)] private float voiceChargeLoudnessExponent = 1.75f;
    [Tooltip("발음 정확도가 충전 거리를 얼마나 강하게 감점할지 정합니다. 0이면 거리에 영향이 없고, 1이면 발음 점수를 그대로 곱합니다.")]
    [SerializeField, Range(0f, 1f)] private float voiceChargePronunciationWeight = 0.25f;
    [Tooltip("발음 정확도 안에서 Azure 음성 신뢰도가 차지하는 비중입니다.")]
    [SerializeField, Range(0f, 1f)] private float voiceChargeAzureConfidenceWeight = 0.6f;

    [Header("신규 돌진 - 실시간 화살표")]
    [SerializeField, Min(0.002f)] private float voiceChargeArrowWidthInSquares = 0.055f;
    [SerializeField, Min(0.005f)] private float voiceChargeArrowHeightInSquares = 0.12f;
    [SerializeField, Range(0.05f, 0.8f)] private float voiceChargeArrowHeadLengthRatio = 0.2f;
    [InspectorName("아군 차징 화살표 색")]
    [Tooltip("현재 화면을 보는 플레이어와 같은 팀 기물의 차징 화살표 색입니다.")]
    [SerializeField] private Color voiceChargeArrowColor =
        new(0.1f, 0.85f, 1f, 0.95f);
    [InspectorName("적군 차징 화살표 색")]
    [Tooltip("현재 화면을 보는 플레이어와 다른 팀 기물의 차징 화살표 색입니다.")]
    [SerializeField] private Color remoteVoiceChargeArrowColor =
        new(1f, 0.3f, 0.15f, 0.95f);

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
    [InspectorName("돌진 최소 코스트")]
    [Tooltip("돌진 한 번의 최소 코스트입니다. 1보다 낮게 설정할 수 없습니다.")]
    [SerializeField, Min(1f)] private float chargeCost = 1f;

    [Header("신규 명령 방식 - 돌진 레이저")]
    [Tooltip("레이캐스트 판정은 항상 유지됩니다. 이 옵션은 명령 확정 순간 카메라에서 목표까지 보이는 레이저 선만 표시합니다.")]
    [SerializeField] private bool showChargeRaycastLaser;
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
    public int MaximumConfirmedSelections =>
        Mathf.Clamp(maximumConfirmedSelections, 1, 3);
    public float ProximitySelectionRadiusInSquares =>
        Mathf.Max(0.05f, proximitySelectionRadiusInSquares);
    public CostConsumptionVersion CostConsumptionVersion => costConsumptionVersion;
    public bool UsesVoiceDurationCost =>
        costConsumptionVersion == CostConsumptionVersion.VoiceDurationCharge;
    public bool UsesVoiceChargeScaling =>
        UsesVoiceDurationCost || CooldownSystemEnabled;
    public CommandRestrictionMode RestrictionMode =>
        UsesLegacyRegeneratingPointsMode
            ? CommandRestrictionMode.Cost
            : commandRestrictionMode;
    public bool CostSystemEnabled =>
        RestrictionMode == CommandRestrictionMode.Cost;
    public bool CooldownSystemEnabled =>
        RestrictionMode == CommandRestrictionMode.Cooldown;
    public float CommandCooldownSeconds => Mathf.Max(0.01f, commandCooldownSeconds);
    public float CommandCooldownSelectionLeadTimeSeconds =>
        Mathf.Max(0f, commandCooldownSelectionLeadTimeSeconds);
    public float CommandCooldownReticleDiameterPixels =>
        Mathf.Max(16f, commandCooldownReticleDiameterPixels);
    public bool PieceMovementCooldownEnabled => pieceMovementCooldownEnabled;
    public float PieceMovementCooldownSeconds =>
        Mathf.Max(0.01f, pieceMovementCooldownSeconds);
    public PlayerTeam FirstTeam =>
        firstTeam == PlayerTeam.Black ? PlayerTeam.Black : PlayerTeam.White;
    public bool AdvanceAfterAcceptedCommand => advanceAfterAcceptedCommand;
    public bool FreezeInactiveTeamMovement => freezeInactiveTeamMovement;
    public float TurnDurationSeconds => Mathf.Max(0f, turnDurationSeconds);
    public float StartingCost => Mathf.Clamp(startingCost, 0f, MaximumCost);
    public float MaximumCost => Mathf.Max(0.01f, maximumCost);
    public float RechargeIntervalSeconds => Mathf.Max(0.01f, rechargeIntervalSeconds);
    public float RechargeAmount => Mathf.Max(0f, rechargeAmount);
    public float VoiceChargeCostStep => Mathf.Max(0.001f, voiceChargeCostStep);
    public float VoiceChargeSecondsPerCostStep =>
        Mathf.Max(0.01f, voiceChargeSecondsPerCostStep);
    public float VoiceChargeMaximumDurationSeconds =>
        Mathf.Max(0.1f, voiceChargeMaximumDurationSeconds);
    public float VoiceChargeMinimumDistanceInSquares =>
        Mathf.Max(0f, voiceChargeMinimumDistanceInSquares);
    public float VoiceChargeMaximumDistanceInSquares => Mathf.Max(
        VoiceChargeMinimumDistanceInSquares + 0.01f,
        voiceChargeMaximumDistanceInSquares);
    public float VoiceChargeMaximumInitialLoudnessDistanceInSquares =>
        Mathf.Clamp(
            voiceChargeMaximumInitialLoudnessDistanceInSquares,
            0f,
            VoiceChargeMaximumDistanceInSquares -
            VoiceChargeMinimumDistanceInSquares);
    public float MouseChargeMinimumDistanceInSquares => Mathf.Clamp(
        mouseChargeMinimumDistanceInSquares,
        VoiceChargeMinimumDistanceInSquares,
        Mathf.Min(
            VoiceChargeMaximumDistanceInSquares,
            VoiceChargeMinimumDistanceInSquares +
            VoiceChargeMaximumInitialLoudnessDistanceInSquares));
    public float VoiceChargeLoudnessExponent =>
        Mathf.Max(0.01f, voiceChargeLoudnessExponent);
    public float VoiceChargeDurationExponent =>
        Mathf.Max(0.05f, voiceChargeDurationExponent);
    public float VoiceChargeArrowWidthInSquares =>
        Mathf.Max(0.002f, voiceChargeArrowWidthInSquares);
    public float VoiceChargeArrowHeightInSquares =>
        Mathf.Max(0.005f, voiceChargeArrowHeightInSquares);
    public float VoiceChargeArrowHeadLengthRatio =>
        Mathf.Clamp(voiceChargeArrowHeadLengthRatio, 0.05f, 0.8f);
    public Color FriendlyVoiceChargeArrowColor => voiceChargeArrowColor;
    public Color EnemyVoiceChargeArrowColor => remoteVoiceChargeArrowColor;
    public float ChargeLaserRangeInSquares =>
        Mathf.Max(1f, chargeLaserRangeInSquares);
    public bool ShowChargeRaycastLaser => showChargeRaycastLaser;
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
            PieceVoiceCommand.Charge => Mathf.Max(1f, chargeCost),
            _ => 0f
        };
    }

    public float GetVoiceChargeCost(
        float voicedDurationSeconds,
        int selectedPieceCount = 1)
    {
        float duration = Mathf.Min(
            Mathf.Max(0f, voicedDurationSeconds),
            VoiceChargeMaximumDurationSeconds);
        int steps = duration > 0f
            ? Mathf.CeilToInt(duration / VoiceChargeSecondsPerCostStep)
            : 0;
        float durationCostPerPiece = Mathf.Min(
            MaximumCost,
            steps * VoiceChargeCostStep);
        float durationCost = Mathf.Min(
            MaximumCost,
            durationCostPerPiece * Mathf.Max(1, selectedPieceCount));
        return Mathf.Max(GetBaseCost(PieceVoiceCommand.Charge), durationCost);
    }

    public float GetVoiceChargePronunciationScore(
        float azureConfidence,
        float textSimilarity)
    {
        float azureWeight = Mathf.Clamp01(voiceChargeAzureConfidenceWeight);
        return Mathf.Clamp01(Mathf.Lerp(
            textSimilarity,
            azureConfidence,
            azureWeight));
    }

    public float GetVoiceChargePower(
        float voicedDurationSeconds,
        float normalizedLoudness,
        float pronunciationScore)
    {
        float durationScore = Mathf.Pow(
            Mathf.Clamp01(
                voicedDurationSeconds / VoiceChargeMaximumDurationSeconds),
            VoiceChargeDurationExponent);
        float durationWeight = Mathf.Max(0f, voiceChargeDurationWeight);
        float loudnessWeight = Mathf.Max(0f, voiceChargeLoudnessWeight);
        float chargeWeight = durationWeight + loudnessWeight;

        if (chargeWeight <= 0.0001f)
        {
            return 0f;
        }

        float normalizedLoudnessValue = Mathf.Pow(
            Mathf.Clamp01(normalizedLoudness),
            VoiceChargeLoudnessExponent);
        float distanceRange = VoiceChargeMaximumDistanceInSquares -
            VoiceChargeMinimumDistanceInSquares;
        float maximumInitialPower = distanceRange > 0.0001f
            ? VoiceChargeMaximumInitialLoudnessDistanceInSquares / distanceRange
            : 0f;
        float initialLoudnessPower = normalizedLoudnessValue * maximumInitialPower;

        // Loudness provides a bounded initial hit, then also controls how
        // efficiently elapsed speech time fills the remaining distance.
        float loudnessEfficiency =
            (durationWeight +
             normalizedLoudnessValue * loudnessWeight) /
            chargeWeight;
        float durationPower = durationScore * loudnessEfficiency;
        float chargePower = initialLoudnessPower +
            (1f - initialLoudnessPower) * durationPower;
        float pronunciationImpact = Mathf.Clamp01(
            voiceChargePronunciationWeight);
        float pronunciationMultiplier = Mathf.Lerp(
            1f,
            Mathf.Clamp01(pronunciationScore),
            pronunciationImpact);
        return Mathf.Clamp01(chargePower * pronunciationMultiplier);
    }

    public float GetVoiceChargeDistance(float chargePower)
    {
        return Mathf.Lerp(
            VoiceChargeMinimumDistanceInSquares,
            VoiceChargeMaximumDistanceInSquares,
            Mathf.Clamp01(chargePower));
    }

    public float GetMouseChargeNormalizedLoudness(float heldDurationSeconds)
    {
        float initialDistanceRange =
            VoiceChargeMaximumInitialLoudnessDistanceInSquares;
        float startingLoudness = 0f;

        if (initialDistanceRange > 0.0001f)
        {
            float startingLoudnessPower = Mathf.InverseLerp(
                VoiceChargeMinimumDistanceInSquares,
                VoiceChargeMinimumDistanceInSquares + initialDistanceRange,
                MouseChargeMinimumDistanceInSquares);
            startingLoudness = Mathf.Pow(
                startingLoudnessPower,
                1f / VoiceChargeLoudnessExponent);
        }

        float holdProgress = Mathf.Clamp01(
            Mathf.Max(0f, heldDurationSeconds) /
            VoiceChargeMaximumDurationSeconds);
        return Mathf.Lerp(startingLoudness, 1f, holdProgress);
    }

    public void Validate()
    {
        if (firstTeam != PlayerTeam.White && firstTeam != PlayerTeam.Black)
        {
            firstTeam = PlayerTeam.White;
        }

        maximumConfirmedSelections = Mathf.Clamp(
            maximumConfirmedSelections,
            1,
            3);
        proximitySelectionRadiusInSquares = Mathf.Max(
            0.05f,
            proximitySelectionRadiusInSquares);

        if (UsesLegacyRegeneratingPointsMode)
        {
            mode = CommandIssuingMode.RealTime;
            commandRestrictionMode = CommandRestrictionMode.Cost;
            rechargeIntervalSeconds = 1f;
        }

        turnDurationSeconds = Mathf.Max(0f, turnDurationSeconds);
        commandCooldownSeconds = Mathf.Max(0.01f, commandCooldownSeconds);
        commandCooldownSelectionLeadTimeSeconds = Mathf.Max(
            0f,
            commandCooldownSelectionLeadTimeSeconds);
        commandCooldownReticleDiameterPixels = Mathf.Max(
            16f,
            commandCooldownReticleDiameterPixels);
        pieceMovementCooldownSeconds = Mathf.Max(
            0.01f,
            pieceMovementCooldownSeconds);
        maximumCost = Mathf.Max(0.01f, maximumCost);
        startingCost = Mathf.Clamp(startingCost, 0f, maximumCost);
        rechargeIntervalSeconds = Mathf.Max(0.01f, rechargeIntervalSeconds);
        rechargeAmount = Mathf.Max(0f, rechargeAmount);
        voiceChargeCostStep = Mathf.Max(0.001f, voiceChargeCostStep);
        voiceChargeSecondsPerCostStep = Mathf.Max(
            0.01f,
            voiceChargeSecondsPerCostStep);
        voiceChargeMaximumDurationSeconds = Mathf.Max(
            0.1f,
            voiceChargeMaximumDurationSeconds);
        voiceChargeMinimumDistanceInSquares = Mathf.Max(
            0f,
            voiceChargeMinimumDistanceInSquares);
        voiceChargeMaximumDistanceInSquares = Mathf.Max(
            voiceChargeMinimumDistanceInSquares + 0.01f,
            voiceChargeMaximumDistanceInSquares);
        voiceChargeMaximumInitialLoudnessDistanceInSquares = Mathf.Clamp(
            voiceChargeMaximumInitialLoudnessDistanceInSquares,
            0f,
            voiceChargeMaximumDistanceInSquares -
            voiceChargeMinimumDistanceInSquares);
        mouseChargeMinimumDistanceInSquares = Mathf.Clamp(
            mouseChargeMinimumDistanceInSquares,
            voiceChargeMinimumDistanceInSquares,
            Mathf.Min(
                voiceChargeMaximumDistanceInSquares,
                voiceChargeMinimumDistanceInSquares +
                voiceChargeMaximumInitialLoudnessDistanceInSquares));
        voiceChargeDurationWeight = Mathf.Max(0f, voiceChargeDurationWeight);
        voiceChargeDurationExponent = Mathf.Max(
            0.05f,
            voiceChargeDurationExponent);
        voiceChargeLoudnessWeight = Mathf.Max(0f, voiceChargeLoudnessWeight);
        voiceChargeLoudnessExponent = Mathf.Max(
            0.01f,
            voiceChargeLoudnessExponent);
        voiceChargePronunciationWeight = Mathf.Clamp01(
            voiceChargePronunciationWeight);
        voiceChargeAzureConfidenceWeight = Mathf.Clamp01(
            voiceChargeAzureConfidenceWeight);
        voiceChargeArrowWidthInSquares = Mathf.Max(
            0.002f,
            voiceChargeArrowWidthInSquares);
        voiceChargeArrowHeightInSquares = Mathf.Max(
            0.005f,
            voiceChargeArrowHeightInSquares);
        voiceChargeArrowHeadLengthRatio = Mathf.Clamp(
            voiceChargeArrowHeadLengthRatio,
            0.05f,
            0.8f);
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
        chargeCost = Mathf.Max(1f, chargeCost);
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
    [Header("기물 대 기물 - 기본 충돌")]
    [Tooltip("충돌 후 서로 튕겨 나가는 탄성입니다. 1이면 완전 탄성, 0이면 비탄성입니다.")]
    [SerializeField, Range(0f, 1f)] private float restitution = 0.9f;
    [Tooltip("모든 기물 충돌에 적용되는 기본 충격 배율입니다. 에너지 증폭을 막으려면 1 근처를 권장합니다.")]
    [SerializeField, Range(0.1f, 5f)] private float impulseMultiplier = 1f;
    [Tooltip("플레이어 명령으로 움직인 기물의 첫 직접 충돌에 적용되는 추가 전달 배율입니다.")]
    [SerializeField, Range(0.1f, 3f)] private float directImpactMultiplier = 1.35f;
    [Tooltip("충돌로 밀려난 기물이 다음 기물에 충격을 전달할 때 단계마다 곱해지는 값입니다.")]
    [SerializeField, Range(0f, 1f)] private float chainTransferMultiplier = 0.5f;
    [Tooltip("대상이 충돌로 얻을 수 있는 바깥 방향 속도의 상한입니다. 공격자의 접촉 속도에 이 값을 곱합니다.")]
    [SerializeField, Range(0.1f, 3f)] private float maximumTransferredSpeedRatio = 1.25f;
    [SerializeField, Min(0.01f)] private float separationEpsilon = 0.0001f;

    [Header("기물 대 플레이어")]
    [SerializeField, Min(0.1f)] private float playerCollisionHeight = 0.6f;
    [SerializeField, Min(0f)] private float minimumPlayerImpactSpeed = 0.08f;
    [SerializeField, Range(0f, 1f)] private float minimumPlayerImpactAlignment = 0.25f;
    [SerializeField] private bool friendlyPiecesAreIntangible = true;

    public float Restitution => Mathf.Clamp01(restitution);
    public float ImpulseMultiplier => Mathf.Max(0.1f, impulseMultiplier);
    public float DirectImpactMultiplier => Mathf.Max(
        0.1f,
        directImpactMultiplier);
    public float ChainTransferMultiplier => Mathf.Clamp01(
        chainTransferMultiplier);
    public float MaximumTransferredSpeedRatio => Mathf.Max(
        0.1f,
        maximumTransferredSpeedRatio);
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
        directImpactMultiplier = Mathf.Clamp(directImpactMultiplier, 0.1f, 3f);
        chainTransferMultiplier = Mathf.Clamp01(chainTransferMultiplier);
        maximumTransferredSpeedRatio = Mathf.Clamp(
            maximumTransferredSpeedRatio,
            0.1f,
            3f);
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

    [Header("이동 명령 쿨타임")]
    [Tooltip("이 종류의 기물이 이동 또는 돌진한 뒤 다시 이동 명령을 받기까지의 시간입니다. 0으로 두면 Commands의 기본 이동 쿨타임을 사용합니다.")]
    [SerializeField, Min(0f)] private float movementCooldownSeconds;

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

    [Header("고유 특성")]
    [SerializeField] private PieceTraitSettings traits = new();

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
    public float ResolveMovementCooldownSeconds(float defaultSeconds)
    {
        return movementCooldownSeconds > 0f
            ? movementCooldownSeconds
            : Mathf.Max(0.01f, defaultSeconds);
    }
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
    public PieceTraitSettings Traits => traits ?? PieceTraitSettings.CreateDefault(
        pieceType);
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
                ChessPieceType.Pawn => 1f,
                ChessPieceType.Knight => 1.5f,
                ChessPieceType.Bishop => 1.25f,
                ChessPieceType.Rook => 2f,
                ChessPieceType.Queen => 1.6f,
                ChessPieceType.King => 1.4f,
                _ => 1f
            },
            traits = PieceTraitSettings.CreateDefault(pieceType),
            ringOutDistance = pieceType == ChessPieceType.King ? 0f : 0.8f
        };

        if (pieceType == ChessPieceType.Rook)
        {
            settings.quietFlickSpeed = 1.2f;
            settings.loudFlickSpeed = 4f;
        }

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
        movementCooldownSeconds = Mathf.Max(0f, movementCooldownSeconds);
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
        traits ??= PieceTraitSettings.CreateDefault(pieceType);
        traits.Validate();
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
    [Header("공통 스위치")]
    [Tooltip("끄면 점령 원, 점령 점수 계산, 점령전 승리 판정을 모두 사용하지 않습니다.")]
    [SerializeField] private bool enabled;
    [Tooltip("고정 원 점수 방식과 랜덤 라운드 점령전 중에서 선택합니다.")]
    [SerializeField] private CaptureModeVersion version =
        CaptureModeVersion.LegacyConfiguredZones;

    [Header("구버전 - 고정 점령 원")]
    [SerializeField] private CaptureScoringRule scoringRule =
        CaptureScoringRule.PeriodicPerPiece;
    [Tooltip("제한 시간이 끝났을 때 기본 시간 종료 규칙 대신 점령 점수로 승자를 정합니다.")]
    [SerializeField] private bool resolveWinnerAtTimeLimit = true;
    [Tooltip("초당 점수 규칙에서 기물이 원 밖으로 나가면 진행 중이던 개별 타이머를 초기화합니다.")]
    [SerializeField] private bool resetPeriodicTimerWhenLeaving = true;
    [SerializeField] private List<CaptureZoneSettings> zones = new()
    {
        new CaptureZoneSettings()
    };

    [Header("신버전 - 랜덤 라운드 규칙")]
    [Tooltip("한 점령 원을 보여준 뒤 판정할 때까지의 시간입니다.")]
    [SerializeField, Min(0.1f)] private float randomRoundDurationSeconds = 5f;
    [Tooltip("한 라운드 판정 후 다음 미리보기 원이 나타날 때까지의 시간입니다. 0이면 즉시 이어집니다.")]
    [SerializeField, Min(0f)] private float randomRoundIntervalSeconds = 0.35f;
    [Tooltip("이 점수에 먼저 도달한 팀이 즉시 승리합니다.")]
    [SerializeField, Min(1)] private int randomRoundScoreToWin = 3;
    [Tooltip("랜덤 점령 원의 최소 반지름입니다. 체스 칸 단위입니다.")]
    [SerializeField, Min(0.05f)] private float randomRadiusMinimumInSquares = 1.15f;
    [Tooltip("랜덤 점령 원의 최대 반지름입니다. 최소 반지름과 같게 두면 크기가 고정됩니다.")]
    [SerializeField, Min(0.05f)] private float randomRadiusMaximumInSquares = 1.15f;
    [Tooltip("점령 원이 보드 외곽에서 추가로 떨어질 여백입니다. 체스 칸 단위입니다.")]
    [SerializeField, Min(0f)] private float randomPositionPaddingInSquares;
    [Tooltip("켜면 원 전체가 보드 안에 들어오도록 반지름만큼 가장자리를 제외합니다. 끄면 원의 중심이 가장자리와 코너를 포함한 보드 전체에서 생성됩니다.")]
    [SerializeField] private bool randomKeepEntireCircleInsideBoard;
    [Tooltip("직전 원과 강제로 떨어뜨릴 최소 거리입니다. 진짜 독립 균등 랜덤을 원하면 0으로 둡니다. 값이 크면 두 구역을 오가는 것처럼 느껴질 수 있습니다.")]
    [SerializeField, Min(0f)] private float randomMinimumCentreDistanceInSquares;
    [Tooltip("거리까지 거의 같은 상황을 무승부로 볼 오차 범위입니다.")]
    [SerializeField, Min(0f)] private float randomDistanceTieToleranceInSquares = 0.001f;
    [Tooltip("0이면 매 경기 무작위입니다. 0이 아닌 값은 같은 랜덤 배치를 재현하는 테스트용 시드입니다.")]
    [SerializeField] private int randomSeed;

    [Header("신버전 - 미리보기 원 표시")]
    [SerializeField] private bool randomShowFilledCircle = true;
    [SerializeField] private Color randomFillColor =
        new(0.1f, 0.75f, 1f, 0.1f);
    [Tooltip("아직 차오르지 않은 원 테두리의 색입니다.")]
    [SerializeField] private Color randomFaintOutlineColor =
        new(0.15f, 0.9f, 1f, 0.22f);
    [Tooltip("0%에서 100%까지 차오르는 진한 테두리의 색입니다.")]
    [SerializeField] private Color randomProgressOutlineColor =
        new(0.15f, 0.9f, 1f, 0.98f);
    [SerializeField, Range(16, 128)] private int randomCircleSegments = 96;
    [SerializeField, Min(0.005f)] private float randomOutlineWidthInSquares = 0.055f;
    [SerializeField, Min(0f)] private float randomHeightOffsetInSquares = 0.02f;
    [Tooltip("진한 게이지가 차기 시작하는 각도입니다. -90은 화면 기준 위쪽에서 시작합니다.")]
    [SerializeField, Range(-180f, 180f)] private float randomProgressStartAngleDegrees = -90f;

    [Header("점령전 - 킹 부활")]
    [Tooltip("점령전에서는 킹 사망으로 경기를 끝내지 않고 지정 시간이 지난 뒤 부활시킵니다.")]
    [SerializeField] private bool respawnEliminatedKings = true;
    [Tooltip("킹 사망 후 다시 나타날 때까지의 시간입니다.")]
    [SerializeField, Min(0.1f)] private float kingRespawnDelaySeconds = 10f;
    [Tooltip("랜덤 부활 위치를 보드 가장자리에서 이만큼 안쪽으로 제한합니다. 체스 칸 단위입니다.")]
    [SerializeField, Range(0f, 3.49f)] private float kingRespawnEdgePaddingInSquares = 0.35f;
    [Tooltip("다른 기물이나 플레이어와 겹치지 않도록 확보하려는 추가 거리입니다. 공간이 부족하면 가장 덜 겹치는 후보를 사용합니다.")]
    [SerializeField, Min(0f)] private float kingRespawnClearanceInSquares = 0.15f;
    [Tooltip("죽어 있는 동안 보드를 수직으로 내려다보는 카메라 높이입니다. 체스 칸 단위입니다.")]
    [SerializeField, Min(1f)] private float kingRespawnCameraHeightInSquares = 10f;
    [Tooltip("화면 중앙에 표시하는 부활 카운트다운 숫자의 크기입니다.")]
    [SerializeField, Range(24, 240)] private int kingRespawnCountdownFontSize = 112;
    [SerializeField] private Color kingRespawnCountdownColor = Color.white;

    public bool Enabled => enabled;
    public CaptureModeVersion Version => version;
    public CaptureScoringRule ScoringRule => scoringRule;
    public bool ResolveWinnerAtTimeLimit => resolveWinnerAtTimeLimit;
    public bool ResetPeriodicTimerWhenLeaving => resetPeriodicTimerWhenLeaving;
    public IReadOnlyList<CaptureZoneSettings> Zones => zones;
    public float RandomRoundDurationSeconds =>
        Mathf.Max(0.1f, randomRoundDurationSeconds);
    public float RandomRoundIntervalSeconds =>
        Mathf.Max(0f, randomRoundIntervalSeconds);
    public int RandomRoundScoreToWin => Mathf.Max(1, randomRoundScoreToWin);
    public float RandomRadiusMinimumInSquares =>
        Mathf.Max(0.05f, Mathf.Min(
            randomRadiusMinimumInSquares,
            randomRadiusMaximumInSquares));
    public float RandomRadiusMaximumInSquares =>
        Mathf.Max(RandomRadiusMinimumInSquares, randomRadiusMaximumInSquares);
    public float RandomPositionPaddingInSquares =>
        Mathf.Max(0f, randomPositionPaddingInSquares);
    public bool RandomKeepEntireCircleInsideBoard =>
        randomKeepEntireCircleInsideBoard;
    public float RandomMinimumCentreDistanceInSquares =>
        Mathf.Max(0f, randomMinimumCentreDistanceInSquares);
    public float RandomDistanceTieToleranceInSquares =>
        Mathf.Max(0f, randomDistanceTieToleranceInSquares);
    public int RandomSeed => randomSeed;
    public bool RandomShowFilledCircle => randomShowFilledCircle;
    public Color RandomFillColor => randomFillColor;
    public Color RandomFaintOutlineColor => randomFaintOutlineColor;
    public Color RandomProgressOutlineColor => randomProgressOutlineColor;
    public int RandomCircleSegments => Mathf.Clamp(randomCircleSegments, 16, 128);
    public float RandomOutlineWidthInSquares =>
        Mathf.Max(0.005f, randomOutlineWidthInSquares);
    public float RandomHeightOffsetInSquares =>
        Mathf.Max(0f, randomHeightOffsetInSquares);
    public float RandomProgressStartAngleDegrees => randomProgressStartAngleDegrees;
    public bool RespawnEliminatedKings => respawnEliminatedKings;
    public float KingRespawnDelaySeconds =>
        Mathf.Max(0.1f, kingRespawnDelaySeconds);
    public float KingRespawnEdgePaddingInSquares =>
        Mathf.Clamp(kingRespawnEdgePaddingInSquares, 0f, 3.49f);
    public float KingRespawnClearanceInSquares =>
        Mathf.Max(0f, kingRespawnClearanceInSquares);
    public float KingRespawnCameraHeightInSquares =>
        Mathf.Max(1f, kingRespawnCameraHeightInSquares);
    public int KingRespawnCountdownFontSize =>
        Mathf.Clamp(kingRespawnCountdownFontSize, 24, 240);
    public Color KingRespawnCountdownColor => kingRespawnCountdownColor;

    public void Validate()
    {
        zones ??= new List<CaptureZoneSettings>();

        randomRoundDurationSeconds = Mathf.Max(0.1f, randomRoundDurationSeconds);
        randomRoundIntervalSeconds = Mathf.Max(0f, randomRoundIntervalSeconds);
        randomRoundScoreToWin = Mathf.Max(1, randomRoundScoreToWin);
        randomRadiusMinimumInSquares =
            Mathf.Max(0.05f, randomRadiusMinimumInSquares);
        randomRadiusMaximumInSquares = Mathf.Max(
            randomRadiusMinimumInSquares,
            randomRadiusMaximumInSquares);
        randomPositionPaddingInSquares =
            Mathf.Max(0f, randomPositionPaddingInSquares);
        randomMinimumCentreDistanceInSquares =
            Mathf.Max(0f, randomMinimumCentreDistanceInSquares);
        randomDistanceTieToleranceInSquares =
            Mathf.Max(0f, randomDistanceTieToleranceInSquares);
        randomCircleSegments = Mathf.Clamp(randomCircleSegments, 16, 128);
        randomOutlineWidthInSquares =
            Mathf.Max(0.005f, randomOutlineWidthInSquares);
        randomHeightOffsetInSquares = Mathf.Max(0f, randomHeightOffsetInSquares);
        kingRespawnDelaySeconds = Mathf.Max(0.1f, kingRespawnDelaySeconds);
        kingRespawnEdgePaddingInSquares = Mathf.Clamp(
            kingRespawnEdgePaddingInSquares,
            0f,
            3.49f);
        kingRespawnClearanceInSquares =
            Mathf.Max(0f, kingRespawnClearanceInSquares);
        kingRespawnCameraHeightInSquares =
            Mathf.Max(1f, kingRespawnCameraHeightInSquares);
        kingRespawnCountdownFontSize = Mathf.Clamp(
            kingRespawnCountdownFontSize,
            24,
            240);

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

    [Header("플레이어 킹 시작 위치와 시점")]
    [Tooltip("켜면 초기 기물 배치에 등록된 자기 팀 킹의 칸에서 시작합니다.")]
    [SerializeField] private bool useBoardKingPlacementAsPlayerStart = true;
    [Tooltip("초기 배치에서 흰색 킹을 찾지 못했을 때 사용할 보드 좌표입니다.")]
    [SerializeField] private Vector2 whitePlayerKingFallbackStart = new(4f, 0f);
    [Tooltip("초기 배치에서 검은색 킹을 찾지 못했을 때 사용할 보드 좌표입니다.")]
    [SerializeField] private Vector2 blackPlayerKingFallbackStart = new(4f, 7f);
    [Tooltip("킹 모델 전체 높이 중 카메라가 놓일 비율입니다. 0.82는 머리 부근입니다.")]
    [SerializeField, Range(0.05f, 1f)]
    private float playerKingEyeHeightAsModelFraction = 0.82f;
    [Tooltip("흰색 플레이어 킹의 시작 시야 각도입니다. 0도는 흰색 진영에서 검은색 진영 방향입니다.")]
    [SerializeField, Range(0f, 360f)] private float whitePlayerKingStartYaw;
    [Tooltip("검은색 플레이어 킹의 시작 시야 각도입니다. 180도는 흰색 진영 방향입니다.")]
    [SerializeField, Range(0f, 360f)] private float blackPlayerKingStartYaw = 180f;
    [Tooltip("플레이어 킹이 자기 팀 기물에도 막히고, 움직이는 아군 기물에 밀려나게 합니다.")]
    [SerializeField] private bool playerKingCollidesWithFriendlyPieces = true;
    [Tooltip("플레이어 킹이 경기장 경계를 넘어가면 아래로 떨어질 수 있게 합니다.")]
    [SerializeField] private bool playerKingCanFallOffBoard = true;
    [Tooltip("플레이어 킹이 장외에서 아래로 떨어지는 중력입니다. 초당 제곱 칸 단위입니다.")]
    [SerializeField, Min(0.1f)] private float playerKingFallGravityInSquares = 15f;
    [Tooltip("보드 아래로 이 깊이만큼 떨어지면 플레이어 킹을 장외 사망 처리합니다.")]
    [SerializeField, Min(0.1f)] private float playerKingEliminationDepthInSquares = 2.5f;
    [Tooltip("장외 낙하 중 네트워크 좌표에 허용할 최대 수평 거리입니다.")]
    [SerializeField, Min(0.1f)]
    private float playerKingMaximumOutOfBoundsDistanceInSquares = 4f;

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
    public bool UseBoardKingPlacementAsPlayerStart =>
        useBoardKingPlacementAsPlayerStart;
    public float PlayerKingEyeHeightAsModelFraction =>
        Mathf.Clamp(playerKingEyeHeightAsModelFraction, 0.05f, 1f);
    public bool PlayerKingCollidesWithFriendlyPieces =>
        playerKingCollidesWithFriendlyPieces;
    public bool PlayerKingCanFallOffBoard => playerKingCanFallOffBoard;
    public float PlayerKingFallGravityInSquares =>
        Mathf.Max(0.1f, playerKingFallGravityInSquares);
    public float PlayerKingEliminationDepthInSquares =>
        Mathf.Max(0.1f, playerKingEliminationDepthInSquares);
    public float PlayerKingMaximumOutOfBoundsDistanceInSquares =>
        Mathf.Max(0.1f, playerKingMaximumOutOfBoundsDistanceInSquares);
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

    public Vector2 GetPlayerKingFallbackStart(PlayerTeam team)
    {
        return team == PlayerTeam.Black
            ? blackPlayerKingFallbackStart
            : whitePlayerKingFallbackStart;
    }

    public float GetPlayerKingStartYaw(PlayerTeam team)
    {
        return Mathf.Repeat(
            team == PlayerTeam.Black
                ? blackPlayerKingStartYaw
                : whitePlayerKingStartYaw,
            360f);
    }

    public void Validate()
    {
        maximumPlayersPerTeam = Mathf.Max(1, maximumPlayersPerTeam);
        avatarHeightInSquares = Mathf.Max(0.1f, avatarHeightInSquares);
        avatarRadiusInSquares = Mathf.Max(0.01f, avatarRadiusInSquares);
        poseUpdatesPerSecond = Mathf.Clamp(poseUpdatesPerSecond, 1f, 60f);
        maximumPoseHeightInSquares = Mathf.Max(0f, maximumPoseHeightInSquares);
        playerKingEyeHeightAsModelFraction = Mathf.Clamp(
            playerKingEyeHeightAsModelFraction,
            0.05f,
            1f);
        whitePlayerKingStartYaw = Mathf.Repeat(whitePlayerKingStartYaw, 360f);
        blackPlayerKingStartYaw = Mathf.Repeat(blackPlayerKingStartYaw, 360f);
        playerKingFallGravityInSquares = Mathf.Max(
            0.1f,
            playerKingFallGravityInSquares);
        playerKingEliminationDepthInSquares = Mathf.Max(
            0.1f,
            playerKingEliminationDepthInSquares);
        playerKingMaximumOutOfBoundsDistanceInSquares = Mathf.Max(
            0.1f,
            playerKingMaximumOutOfBoundsDistanceInSquares);
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
    [InspectorName("아군 방향 화살표 색")]
    [Tooltip("현재 화면을 보는 플레이어와 같은 팀 기물의 방향 화살표 색입니다.")]
    [SerializeField] private Color whiteHeadingArrowColor =
        new(0.1f, 0.85f, 1f, 0.95f);
    [InspectorName("적군 방향 화살표 색")]
    [Tooltip("현재 화면을 보는 플레이어와 다른 팀 기물의 방향 화살표 색입니다.")]
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
    public Color FriendlyHeadingArrowColor => whiteHeadingArrowColor;
    public Color EnemyHeadingArrowColor => blackHeadingArrowColor;
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
    [SerializeField] private Rect captureScorePanel = new(945f, 30f, 450f, 78f);
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
public sealed class EditorSoloTestSettings
{
    [Tooltip("Unity 에디터에서 Play를 누르면 로컬 Host를 만들고 즉시 경기를 시작합니다. 빌드에는 적용되지 않습니다.")]
    [SerializeField] private bool enabled = true;
    [SerializeField] private PlayerTeam playerTeam = PlayerTeam.White;
    [SerializeField, Min(1f)] private float startupTimeoutSeconds = 10f;

    public bool Enabled => enabled;
    public PlayerTeam PlayerTeam => playerTeam == PlayerTeam.Black
        ? PlayerTeam.Black
        : PlayerTeam.White;
    public float StartupTimeoutSeconds => Mathf.Max(1f, startupTimeoutSeconds);

    public void Validate()
    {
        if (playerTeam != PlayerTeam.White && playerTeam != PlayerTeam.Black)
        {
            playerTeam = PlayerTeam.White;
        }

        startupTimeoutSeconds = Mathf.Max(1f, startupTimeoutSeconds);
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
    [SerializeField] private EditorSoloTestSettings editorSoloTest = new();

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
    public EditorSoloTestSettings EditorSoloTest => editorSoloTest;
    public IReadOnlyList<InitialPiecePlacement> InitialPlacements =>
        boardSetup.UseCustomStartingPosition
            ? boardSetup.CustomPlacements
            : StandardPlacements;

    public bool ShouldSpawnBoardPiece(InitialPiecePlacement placement)
    {
        return placement != null &&
            !(victory.RoyalUnitMode == RoyalUnitMode.PlayerCommander &&
              placement.PieceType == ChessPieceType.King);
    }

    public bool TryGetConfiguredKingPosition(
        PlayerTeam team,
        out Vector2 boardPosition)
    {
        foreach (InitialPiecePlacement placement in InitialPlacements)
        {
            if (placement != null &&
                placement.Enabled &&
                placement.Team == team &&
                placement.PieceType == ChessPieceType.King)
            {
                boardPosition = placement.BoardPosition;
                return true;
            }
        }

        boardPosition = default;
        return false;
    }

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
            if (placement != null &&
                placement.Enabled &&
                placement.Team == team &&
                ShouldSpawnBoardPiece(placement))
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
        editorSoloTest.Validate();

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
        editorSoloTest ??= new EditorSoloTestSettings();
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
