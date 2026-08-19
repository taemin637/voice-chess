using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum ChessPieceType : byte
{
    None,
    Pawn,
    Knight,
    Bishop,
    Rook,
    Queen,
    King
}

public struct NetworkChessPieceState :
    INetworkSerializable,
    IEquatable<NetworkChessPieceState>
{
    public const byte InactiveCollisionChainDepth = byte.MaxValue;

    public ushort Id;
    public PlayerTeam OwnerTeam;
    public ChessPieceType PieceType;
    public float BoardFile;
    public float BoardRank;
    public float VoiceHeading;
    public float VoiceMoveHeadingOffset;
    public sbyte VoiceMoveAxis;
    public sbyte VoiceTurnAxis;
    public float VoiceMoveLoudness;
    public float VoiceTurnLoudness;
    public float VoiceChargeDistanceRemaining;
    public float KnockbackFileVelocity;
    public float KnockbackRankVelocity;
    public double MovementCooldownEndServerTime;
    public TemporaryPieceTraitModifiers TemporaryTraits;
    public bool FirstAttackingCollisionAvailable;
    public byte CollisionChainDepth;

    public NetworkChessPieceState(
        ushort id,
        PlayerTeam ownerTeam,
        ChessPieceType pieceType,
        float boardFile,
        float boardRank)
    {
        Id = id;
        OwnerTeam = ownerTeam;
        PieceType = pieceType;
        BoardFile = boardFile;
        BoardRank = boardRank;
        VoiceHeading = 0f;
        VoiceMoveHeadingOffset = 0f;
        VoiceMoveAxis = 0;
        VoiceTurnAxis = 0;
        VoiceMoveLoudness = 0.5f;
        VoiceTurnLoudness = 0.5f;
        VoiceChargeDistanceRemaining = 0f;
        KnockbackFileVelocity = 0f;
        KnockbackRankVelocity = 0f;
        MovementCooldownEndServerTime = 0d;
        TemporaryTraits = default;
        FirstAttackingCollisionAvailable = false;
        CollisionChainDepth = InactiveCollisionChainDepth;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref Id);
        serializer.SerializeValue(ref OwnerTeam);
        serializer.SerializeValue(ref PieceType);
        serializer.SerializeValue(ref BoardFile);
        serializer.SerializeValue(ref BoardRank);
        serializer.SerializeValue(ref VoiceHeading);
        serializer.SerializeValue(ref VoiceMoveHeadingOffset);
        serializer.SerializeValue(ref VoiceMoveAxis);
        serializer.SerializeValue(ref VoiceTurnAxis);
        serializer.SerializeValue(ref VoiceMoveLoudness);
        serializer.SerializeValue(ref VoiceTurnLoudness);
        serializer.SerializeValue(ref VoiceChargeDistanceRemaining);
        serializer.SerializeValue(ref KnockbackFileVelocity);
        serializer.SerializeValue(ref KnockbackRankVelocity);
        serializer.SerializeValue(ref MovementCooldownEndServerTime);
        serializer.SerializeValue(ref TemporaryTraits);
        serializer.SerializeValue(ref FirstAttackingCollisionAvailable);
        serializer.SerializeValue(ref CollisionChainDepth);
    }

    public bool Equals(NetworkChessPieceState other)
    {
        return Id == other.Id &&
               OwnerTeam == other.OwnerTeam &&
               PieceType == other.PieceType &&
               BoardFile.Equals(other.BoardFile) &&
               BoardRank.Equals(other.BoardRank) &&
               VoiceHeading.Equals(other.VoiceHeading) &&
               VoiceMoveHeadingOffset.Equals(other.VoiceMoveHeadingOffset) &&
               VoiceMoveAxis == other.VoiceMoveAxis &&
               VoiceTurnAxis == other.VoiceTurnAxis &&
               VoiceMoveLoudness.Equals(other.VoiceMoveLoudness) &&
               VoiceTurnLoudness.Equals(other.VoiceTurnLoudness) &&
               VoiceChargeDistanceRemaining.Equals(other.VoiceChargeDistanceRemaining) &&
               KnockbackFileVelocity.Equals(other.KnockbackFileVelocity) &&
               KnockbackRankVelocity.Equals(other.KnockbackRankVelocity) &&
               MovementCooldownEndServerTime.Equals(
                   other.MovementCooldownEndServerTime) &&
               TemporaryTraits.Equals(other.TemporaryTraits) &&
               FirstAttackingCollisionAvailable ==
               other.FirstAttackingCollisionAvailable &&
               CollisionChainDepth == other.CollisionChainDepth;
    }
}

[DisallowMultipleComponent]
public sealed partial class NetworkChessGame : NetworkBehaviour
{
    private const int StartingPiecesPerTeam = 16;

    private readonly struct LocalVoiceGazeSample
    {
        public readonly float Time;
        public readonly int PieceId;
        public readonly float DistanceInSquares;
        public readonly bool HasChargeAim;
        public readonly Vector2 ChargeAimBoardPosition;

        public LocalVoiceGazeSample(
            float time,
            int pieceId,
            float distanceInSquares,
            bool hasChargeAim,
            Vector2 chargeAimBoardPosition)
        {
            Time = time;
            PieceId = pieceId;
            DistanceInSquares = distanceInSquares;
            HasChargeAim = hasChargeAim;
            ChargeAimBoardPosition = chargeAimBoardPosition;
        }
    }

    [SerializeField] private ChessPieceSpawner pieceSpawner;

    [Header("경기 규칙")]
    [SerializeField, Min(1f)] private float matchDurationSeconds = 60f;

    [Header("음성 자유 이동")]
    [SerializeField, Min(0.05f)] private float voiceMoveSpeed = 0.85f;
    [SerializeField, Min(5f)] private float voiceTurnSpeed = 90f;
    [SerializeField, Range(0.05f, 1f)] private float quietVoiceSpeedMultiplier = 0.25f;
    [SerializeField, Range(1f, 3f)] private float loudVoiceSpeedMultiplier = 1.75f;

    [Header("기물 충돌 및 장외")]
    [Tooltip("Collision radius measured in chess-square units.")]
    [SerializeField, Range(0.2f, 0.49f)] private float pieceCollisionRadius = 0.36f;
    [Tooltip("How strongly pieces bounce apart. 0 is inelastic, 1 is fully elastic.")]
    [SerializeField, Range(0f, 1f)] private float collisionRestitution = 0.9f;
    [Tooltip("Extra multiplier for the impulse produced by a collision.")]
    [SerializeField, Range(0.5f, 2f)] private float collisionImpulseMultiplier = 1f;
    [Tooltip("How quickly collision knockback loses speed.")]
    [SerializeField, Min(0f)] private float knockbackDrag = 1.8f;
    [Tooltip("Distance past the board edge before a piece is removed.")]
    [SerializeField, Min(0.1f)] private float ringOutDistance = 0.8f;
    [Tooltip("Players above this height (in square units) have jumped over pieces.")]
    [SerializeField, Min(0.1f)] private float playerPieceCollisionHeight = 0.6f;
    [Tooltip("Minimum piece speed required before it can knock a player back.")]
    [SerializeField, Min(0f)] private float minimumPlayerImpactSpeed = 0.08f;
    [Tooltip("How closely the contact must face the piece's travel direction. 0 is any forward contact, 1 is head-on only.")]
    [SerializeField, Range(0f, 1f)] private float minimumPlayerImpactAlignment = 0.25f;

    [Header("킹 사망 연출")]
    [SerializeField, Min(1f)] private float kingDeathCinematicDuration = 3f;
    [SerializeField, Min(0.5f)] private float kingDeathCameraDistanceInSquares = 2.6f;
    [SerializeField, Min(0.1f)] private float kingDeathCameraHeightInSquares = 1.15f;
    [SerializeField, Min(0.1f)] private float kingDeathDropDistanceInSquares = 4f;
    [SerializeField, Min(0f)] private float kingDeathOutwardDistanceInSquares = 1.1f;
    [SerializeField, Range(0f, 180f)] private float kingDeathTiltAngle = 110f;
    [SerializeField, Range(25f, 80f)] private float kingDeathCameraFieldOfView = 48f;

    private readonly NetworkList<NetworkChessPieceState> _pieces = new(
        readPerm: NetworkVariableReadPermission.Everyone,
        writePerm: NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<PlayerTeam> _winner = new(
        PlayerTeam.Unassigned,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _isGameOver = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _matchTimerRunning = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<double> _matchEndServerTime = new(
        0d,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _gameOverPresentationReady = new(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly List<LocalVoiceGazeSample> _localVoiceGazeHistory = new();
    private int _localVoiceTargetPieceId = -1;
    private float _localCommanderFile = 3.5f;
    private float _localCommanderRank = 3.5f;
    private bool _visualRefreshPending;
    private bool _kingDeathCinematicActive;
    private float _kingDeathCinematicStartTime;
    private float _kingDeathPieceHeight;
    private float _kingDeathOriginalFieldOfView;
    private GameObject _kingDeathVisual;
    private Camera _kingDeathCamera;
    private Vector3 _kingDeathStartPosition;
    private Vector3 _kingDeathOutward;
    private Vector3 _kingDeathCameraStartPosition;
    private Quaternion _kingDeathStartRotation;
    private Quaternion _kingDeathCameraStartRotation;

    public PlayerTeam Winner => _winner.Value;
    public bool IsGameOver => _isGameOver.Value;
    public bool IsGameOverPresentationReady => _gameOverPresentationReady.Value;
    public float MatchDurationSeconds => gameMode != null
        ? gameMode.Clock.DurationSeconds
        : matchDurationSeconds;
    public float RemainingTime
    {
        get
        {
            if (!IsMatchClockEnabled)
            {
                return 0f;
            }

            if (!_matchTimerRunning.Value || NetworkManager == null)
            {
                return _isGameOver.Value ? 0f : MatchDurationSeconds;
            }

            return Mathf.Max(
                0f,
                (float)(_matchEndServerTime.Value - NetworkManager.ServerTime.Time));
        }
    }

    public bool HasLocalVoiceSelection =>
        _localVoiceTargetPieceId >= 0 &&
        FindPieceIndexById((ushort)_localVoiceTargetPieceId) >= 0;

    public override void OnNetworkSpawn()
    {
        if (IsServer && _pieces.Count == 0)
        {
            InitializePieces(startMatchTimer: false);
        }

        if (pieceSpawner == null)
        {
            pieceSpawner = FindFirstObjectByType<ChessPieceSpawner>();
        }

        _pieces.OnListChanged += HandlePiecesChanged;
        _isGameOver.OnValueChanged += HandleGameOverChanged;
        _visualRefreshPending = true;
        RebuildCaptureZoneVisuals();
    }

    public override void OnNetworkDespawn()
    {
        _pieces.OnListChanged -= HandlePiecesChanged;
        _isGameOver.OnValueChanged -= HandleGameOverChanged;
        CleanupCaptureZoneVisuals();
        CleanupLocalChargeLaser();
        CleanupKingDeathCinematic();
        ClearLocalVoiceTarget();
    }

    /// <summary>
    /// Restores every piece to its starting square and clears the winner.
    /// Match flow calls this on the server before a new round begins.
    /// </summary>
    public bool ResetGame()
    {
        if (!IsSpawned || !IsServer)
        {
            return false;
        }

        InitializePieces(startMatchTimer: true);
        return true;
    }

    private void FixedUpdate()
    {
        if (!IsSpawned || !IsServer)
        {
            return;
        }

        if (_isGameOver.Value)
        {
            return;
        }

        float deltaTime = Time.fixedDeltaTime;
        List<NetworkChessPieceState> simulatedPieces = new(_pieces.Count);
        List<Vector2> commandedVelocities = new(_pieces.Count);
        List<Vector2> collisionVelocities = new(_pieces.Count);

        for (int index = 0; index < _pieces.Count; index++)
        {
            NetworkChessPieceState piece = _pieces[index];

            PieceArchetypeSettings pieceSettings = GetPieceSettings(piece.PieceType);

            if (piece.VoiceTurnAxis != 0 && !ShouldFreezePieceMovement(piece.OwnerTeam))
            {
                float turnSpeed = pieceSettings.TurnSpeed * GetVoiceSpeedMultiplier(
                    piece.VoiceTurnLoudness);
                piece.VoiceHeading = Mathf.Repeat(
                    piece.VoiceHeading +
                    piece.VoiceTurnAxis * turnSpeed * deltaTime,
                    360f);
            }

            Vector2 commandedVelocity = GetCommandedVelocity(piece);
            Vector2 knockbackVelocity = new(
                piece.KnockbackFileVelocity,
                piece.KnockbackRankVelocity);
            knockbackVelocity = DeceleratePhysicalVelocity(
                pieceSettings,
                knockbackVelocity,
                deltaTime);

            if (knockbackVelocity.sqrMagnitude < 0.0001f)
            {
                knockbackVelocity = Vector2.zero;

                if (piece.VoiceChargeDistanceRemaining <= 0.0001f &&
                    commandedVelocity.sqrMagnitude < 0.0001f)
                {
                    piece.FirstAttackingCollisionAvailable = false;
                    piece.CollisionChainDepth =
                        NetworkChessPieceState.InactiveCollisionChainDepth;
                }
            }

            bool stopChargeKnockbackAfterStep = false;

            if (piece.VoiceChargeDistanceRemaining > 0f)
            {
                bool usesContinuousMovement =
                    pieceSettings.MovementControl == PieceMovementControl.Continuous;
                Vector2 chargeVelocity = usesContinuousMovement
                    ? commandedVelocity
                    : knockbackVelocity;
                float maximumStepSpeed = piece.VoiceChargeDistanceRemaining /
                    Mathf.Max(0.0001f, deltaTime);
                chargeVelocity = Vector2.ClampMagnitude(
                    chargeVelocity,
                    maximumStepSpeed);
                float chargedStepDistance = chargeVelocity.magnitude * deltaTime;
                piece.VoiceChargeDistanceRemaining = Mathf.Max(
                    0f,
                    piece.VoiceChargeDistanceRemaining - chargedStepDistance);

                if (usesContinuousMovement)
                {
                    commandedVelocity = chargeVelocity;
                }
                else
                {
                    knockbackVelocity = chargeVelocity;
                }

                if (piece.VoiceChargeDistanceRemaining <= 0.0001f ||
                    chargeVelocity.sqrMagnitude < 0.0001f)
                {
                    piece.VoiceChargeDistanceRemaining = 0f;

                    if (usesContinuousMovement)
                    {
                        piece.VoiceMoveAxis = 0;
                    }
                    else
                    {
                        stopChargeKnockbackAfterStep = true;
                    }
                }
            }

            Vector2 position = new(piece.BoardFile, piece.BoardRank);
            position += (commandedVelocity + knockbackVelocity) * deltaTime;

            piece.BoardFile = position.x;
            piece.BoardRank = position.y;
            piece.KnockbackFileVelocity = stopChargeKnockbackAfterStep
                ? 0f
                : knockbackVelocity.x;
            piece.KnockbackRankVelocity = stopChargeKnockbackAfterStep
                ? 0f
                : knockbackVelocity.y;
            simulatedPieces.Add(piece);
            commandedVelocities.Add(commandedVelocity);
            collisionVelocities.Add(commandedVelocity + knockbackVelocity);
        }

        ResolvePieceCollisions(
            simulatedPieces,
            commandedVelocities,
            collisionVelocities);
        RemoveRingedOutPieces(simulatedPieces);
        ApplySimulatedState(simulatedPieces);
        ServerUpdateRuleState(deltaTime);
    }

    private void LateUpdate()
    {
        if (_visualRefreshPending && pieceSpawner != null)
        {
            _visualRefreshPending = false;
            List<NetworkChessPieceState> visualStates = new(_pieces.Count);

            for (int index = 0; index < _pieces.Count; index++)
            {
                visualStates.Add(_pieces[index]);
            }

            pieceSpawner.RebuildFromNetworkState(visualStates);

            if (_localVoiceTargetPieceId >= 0 &&
                FindPieceIndexById((ushort)_localVoiceTargetPieceId) < 0)
            {
                ClearLocalVoiceTarget();
            }
        }

        if (pieceSpawner != null)
        {
            double serverTime = NetworkManager != null && NetworkManager.IsListening
                ? NetworkManager.ServerTime.Time
                : 0d;
            pieceSpawner.UpdateMovementCooldownVisuals(
                serverTime,
                IsPieceMovementCooldownEnabled,
                GetPieceMovementCooldownDuration);
        }

        UpdateKingDeathCinematic();
        UpdateLocalChargeLaserVisual();
        UpdateRemoteVoiceChargePreviews();
        UpdateRandomCaptureZoneVisual();
    }

    private void InitializePieces(bool startMatchTimer)
    {
        _matchTimerRunning.Value = false;
        InitializeConfiguredPieces();
        _gameOverPresentationReady.Value = true;
        _winner.Value = PlayerTeam.Unassigned;
        _isGameOver.Value = false;
        InitializeRuleState(startMatchTimer);
    }

    private void HandlePiecesChanged(
        NetworkListEvent<NetworkChessPieceState> changeEvent)
    {
        _visualRefreshPending = true;
    }

    private void HandleGameOverChanged(bool wasGameOver, bool isGameOver)
    {
        if (wasGameOver && !isGameOver)
        {
            CleanupKingDeathCinematic();
            ClearLocalVoiceTarget();
        }
    }

    public int GetRemainingPieceCount(PlayerTeam team)
    {
        int count = 0;

        for (int index = 0; index < _pieces.Count; index++)
        {
            if (_pieces[index].OwnerTeam == team)
            {
                count++;
            }
        }

        return count;
    }

    public int GetKilledPieceCount(PlayerTeam team)
    {
        PlayerTeam opponent = GetOpponent(team);
        return Mathf.Max(
            0,
            GetConfiguredInitialPieceCount(opponent) - GetRemainingPieceCount(opponent));
    }

    private void FinishMatchByPieceCount()
    {
        int whiteKills = GetKilledPieceCount(PlayerTeam.White);
        int blackKills = GetKilledPieceCount(PlayerTeam.Black);
        PlayerTeam winner = PlayerTeam.Unassigned;

        if (whiteKills > blackKills)
        {
            winner = PlayerTeam.White;
        }
        else if (blackKills > whiteKills)
        {
            winner = PlayerTeam.Black;
        }

        EndMatch(winner, MatchEndReason.TimeExpired);
    }

    private Vector2 GetCommandedVelocity(NetworkChessPieceState piece)
    {
        PieceArchetypeSettings settings = GetPieceSettings(piece.PieceType);

        if (piece.VoiceMoveAxis == 0 ||
            settings.MovementControl != PieceMovementControl.Continuous ||
            ShouldFreezePieceMovement(piece.OwnerTeam) ||
            settings.MovementMode == PieceMovementMode.Stationary)
        {
            return Vector2.zero;
        }

        float relativeHeading = Mathf.DeltaAngle(0f, piece.VoiceMoveHeadingOffset);

        if (settings.MovementMode == PieceMovementMode.ForwardOnly &&
            Mathf.Abs(relativeHeading) > 45f)
        {
            return Vector2.zero;
        }

        if (settings.MovementMode == PieceMovementMode.ForwardAndBackward &&
            Mathf.Abs(relativeHeading) > 45f &&
            Mathf.Abs(Mathf.Abs(relativeHeading) - 180f) > 45f)
        {
            return Vector2.zero;
        }

        if (settings.MovementMode == PieceMovementMode.StrafeOnly &&
            Mathf.Abs(Mathf.Abs(relativeHeading) - 90f) > 45f)
        {
            return Vector2.zero;
        }

        float moveSpeed = settings.MoveSpeed * GetVoiceSpeedMultiplier(
            piece.VoiceMoveLoudness);
        return GetVoiceMoveDirection(
                   piece.OwnerTeam,
                   piece.VoiceHeading + piece.VoiceMoveHeadingOffset) *
               (piece.VoiceMoveAxis * moveSpeed);
    }

    private double GetCurrentServerTime()
    {
        return NetworkManager != null
            ? NetworkManager.ServerTime.Time
            : Time.timeAsDouble;
    }

    private ResolvedPieceTraits ResolvePieceTraits(
        in NetworkChessPieceState piece)
    {
        return new ResolvedPieceTraits(
            GetPieceSettings(piece.PieceType).Traits,
            piece.TemporaryTraits,
            GetCurrentServerTime());
    }

    private void ResolvePieceCollisions(
        List<NetworkChessPieceState> pieces,
        List<Vector2> commandedVelocities,
        List<Vector2> collisionVelocities)
    {
        for (int leftIndex = 0; leftIndex < pieces.Count - 1; leftIndex++)
        {
            for (int rightIndex = leftIndex + 1; rightIndex < pieces.Count; rightIndex++)
            {
                NetworkChessPieceState left = pieces[leftIndex];
                NetworkChessPieceState right = pieces[rightIndex];
                PieceArchetypeSettings leftSettings = GetPieceSettings(
                    left.PieceType);
                PieceArchetypeSettings rightSettings = GetPieceSettings(
                    right.PieceType);
                ResolvedPieceTraits leftTraits = ResolvePieceTraits(left);
                ResolvedPieceTraits rightTraits = ResolvePieceTraits(right);

                if (left.OwnerTeam == right.OwnerTeam &&
                    (leftTraits.IgnoreFriendlyPieceCollisions ||
                     rightTraits.IgnoreFriendlyPieceCollisions))
                {
                    continue;
                }

                Vector2 leftPosition = new(left.BoardFile, left.BoardRank);
                Vector2 rightPosition = new(right.BoardFile, right.BoardRank);
                Vector2 separation = rightPosition - leftPosition;
                float minimumDistance =
                    leftSettings.CollisionRadius + rightSettings.CollisionRadius;
                float minimumDistanceSquared = minimumDistance * minimumDistance;
                float distanceSquared = separation.sqrMagnitude;

                if (distanceSquared >= minimumDistanceSquared)
                {
                    continue;
                }

                float distance = Mathf.Sqrt(distanceSquared);
                Vector2 normal = distance > ResolveSeparationEpsilon()
                    ? separation / distance
                    : GetStableCollisionNormal(left.Id, right.Id);
                float leftInverseMass = 1f /
                    (leftSettings.Mass * leftTraits.MassMultiplier);
                float rightInverseMass = 1f /
                    (rightSettings.Mass * rightTraits.MassMultiplier);
                float inverseMassSum = leftInverseMass + rightInverseMass;
                float penetration = minimumDistance - distance;
                Vector2 correction = normal * (penetration / inverseMassSum);

                leftPosition -= correction * leftInverseMass;
                rightPosition += correction * rightInverseMass;

                Vector2 leftVelocity = collisionVelocities[leftIndex];
                Vector2 rightVelocity = collisionVelocities[rightIndex];
                float closingSpeed = Vector2.Dot(rightVelocity - leftVelocity, normal);

                if (closingSpeed < 0f)
                {
                    Vector2 leftVelocityBeforeCollision = leftVelocity;
                    Vector2 rightVelocityBeforeCollision = rightVelocity;
                    byte leftChainDepth = left.CollisionChainDepth;
                    byte rightChainDepth = right.CollisionChainDepth;
                    bool leftIsAttacking = Vector2.Dot(leftVelocity, normal) >
                        0.0001f;
                    bool rightIsAttacking = Vector2.Dot(rightVelocity, -normal) >
                        0.0001f;
                    float leftTraitImpact = ResolveAttackingImpactMultiplier(
                        left,
                        leftTraits,
                        leftIsAttacking);
                    float rightTraitImpact = ResolveAttackingImpactMultiplier(
                        right,
                        rightTraits,
                        rightIsAttacking);
                    float leftAttackingImpact = leftIsAttacking
                        ? leftTraitImpact * ResolveMomentumTransferMultiplier(
                            leftChainDepth)
                        : 1f;
                    float rightAttackingImpact = rightIsAttacking
                        ? rightTraitImpact * ResolveMomentumTransferMultiplier(
                            rightChainDepth)
                        : 1f;
                    float impulseMagnitude =
                        -(1f + ResolveRestitution()) * closingSpeed /
                        inverseMassSum * ResolveCollisionImpulseMultiplier();
                    Vector2 impulse = normal * impulseMagnitude;
                    leftVelocity -= impulse * leftInverseMass *
                        rightAttackingImpact;
                    rightVelocity += impulse * rightInverseMass *
                        leftAttackingImpact;

                    if (rightIsAttacking)
                    {
                        ClampTransferredCollisionSpeed(
                            ref leftVelocity,
                            leftVelocityBeforeCollision,
                            rightVelocityBeforeCollision,
                            -normal,
                            rightTraitImpact *
                            ResolveTransferredSpeedRatio(rightChainDepth));
                    }

                    if (leftIsAttacking)
                    {
                        ClampTransferredCollisionSpeed(
                            ref rightVelocity,
                            rightVelocityBeforeCollision,
                            leftVelocityBeforeCollision,
                            normal,
                            leftTraitImpact *
                            ResolveTransferredSpeedRatio(leftChainDepth));
                    }

                    ConsumeFirstAttackingCollision(
                        ref left,
                        leftTraits,
                        leftIsAttacking);
                    ConsumeFirstAttackingCollision(
                        ref right,
                        rightTraits,
                        rightIsAttacking);
                    AdvanceCollisionChain(
                        ref left,
                        ref right,
                        leftChainDepth,
                        rightChainDepth,
                        leftIsAttacking,
                        rightIsAttacking);

                    Vector2 leftKnockback = leftVelocity - commandedVelocities[leftIndex];
                    Vector2 rightKnockback = rightVelocity - commandedVelocities[rightIndex];
                    left.KnockbackFileVelocity = leftKnockback.x;
                    left.KnockbackRankVelocity = leftKnockback.y;
                    right.KnockbackFileVelocity = rightKnockback.x;
                    right.KnockbackRankVelocity = rightKnockback.y;
                    collisionVelocities[leftIndex] = leftVelocity;
                    collisionVelocities[rightIndex] = rightVelocity;
                }

                left.BoardFile = leftPosition.x;
                left.BoardRank = leftPosition.y;
                right.BoardFile = rightPosition.x;
                right.BoardRank = rightPosition.y;
                pieces[leftIndex] = left;
                pieces[rightIndex] = right;
            }
        }
    }

    private float ResolveMomentumTransferMultiplier(byte chainDepth)
    {
        int resolvedDepth = chainDepth ==
            NetworkChessPieceState.InactiveCollisionChainDepth
                ? 0
                : chainDepth;
        return ResolveDirectImpactMultiplier() * Mathf.Pow(
            ResolveChainTransferMultiplier(),
            resolvedDepth);
    }

    private float ResolveTransferredSpeedRatio(byte chainDepth)
    {
        int resolvedDepth = chainDepth ==
            NetworkChessPieceState.InactiveCollisionChainDepth
                ? 0
                : chainDepth;
        return ResolveMaximumTransferredSpeedRatio() * Mathf.Pow(
            ResolveChainTransferMultiplier(),
            resolvedDepth);
    }

    private static void ClampTransferredCollisionSpeed(
        ref Vector2 receiverVelocity,
        Vector2 receiverVelocityBeforeCollision,
        Vector2 sourceVelocityBeforeCollision,
        Vector2 outwardDirection,
        float maximumSpeedRatio)
    {
        float sourceApproachSpeed = Mathf.Max(
            0f,
            Vector2.Dot(sourceVelocityBeforeCollision, outwardDirection));
        float existingOutwardSpeed = Vector2.Dot(
            receiverVelocityBeforeCollision,
            outwardDirection);
        float maximumOutwardSpeed = Mathf.Max(
            existingOutwardSpeed,
            sourceApproachSpeed * Mathf.Max(0f, maximumSpeedRatio));
        float resolvedOutwardSpeed = Vector2.Dot(
            receiverVelocity,
            outwardDirection);

        if (resolvedOutwardSpeed > maximumOutwardSpeed)
        {
            receiverVelocity -= outwardDirection *
                (resolvedOutwardSpeed - maximumOutwardSpeed);
        }
    }

    private static void AdvanceCollisionChain(
        ref NetworkChessPieceState left,
        ref NetworkChessPieceState right,
        byte leftChainDepth,
        byte rightChainDepth,
        bool leftIsAttacking,
        bool rightIsAttacking)
    {
        if (leftIsAttacking)
        {
            byte nextDepth = GetNextCollisionChainDepth(leftChainDepth);
            left.CollisionChainDepth = nextDepth;

            if (!rightIsAttacking)
            {
                right.CollisionChainDepth = nextDepth;
            }
        }

        if (rightIsAttacking)
        {
            byte nextDepth = GetNextCollisionChainDepth(rightChainDepth);
            right.CollisionChainDepth = nextDepth;

            if (!leftIsAttacking)
            {
                left.CollisionChainDepth = nextDepth;
            }
        }
    }

    private static byte GetNextCollisionChainDepth(byte chainDepth)
    {
        if (chainDepth == NetworkChessPieceState.InactiveCollisionChainDepth)
        {
            return 1;
        }

        return (byte)Mathf.Min(chainDepth + 1, byte.MaxValue - 1);
    }

    private static float ResolveAttackingImpactMultiplier(
        in NetworkChessPieceState piece,
        in ResolvedPieceTraits traits,
        bool isAttacking)
    {
        if (!isAttacking ||
            (traits.FirstAttackingCollisionOnly &&
             !piece.FirstAttackingCollisionAvailable))
        {
            return 1f;
        }

        return traits.AttackingImpactMultiplier;
    }

    private static void ConsumeFirstAttackingCollision(
        ref NetworkChessPieceState piece,
        in ResolvedPieceTraits traits,
        bool isAttacking)
    {
        if (isAttacking && traits.FirstAttackingCollisionOnly)
        {
            piece.FirstAttackingCollisionAvailable = false;
        }
    }

    private void RemoveRingedOutPieces(List<NetworkChessPieceState> pieces)
    {
        for (int index = pieces.Count - 1; index >= 0; index--)
        {
            if (!IsRingedOut(pieces[index]))
            {
                continue;
            }

            HandleRingOut(pieces[index]);
            pieces.RemoveAt(index);
        }
    }

    private void ApplySimulatedState(List<NetworkChessPieceState> simulatedPieces)
    {
        for (int index = _pieces.Count - 1; index >= simulatedPieces.Count; index--)
        {
            _pieces.RemoveAt(index);
        }

        for (int index = 0; index < simulatedPieces.Count; index++)
        {
            if (!_pieces[index].Equals(simulatedPieces[index]))
            {
                _pieces[index] = simulatedPieces[index];
            }
        }
    }

    private bool IsRingedOut(NetworkChessPieceState piece)
    {
        float boardMinimumEdge = pieceSpawner != null
            ? pieceSpawner.GroundMinimumCoordinate
            : -0.5f - ChessPieceSpawner.DefaultBoardBorderWidthInSquares;
        float boardMaximumEdge = pieceSpawner != null
            ? pieceSpawner.GroundMaximumCoordinate
            : 7.5f + ChessPieceSpawner.DefaultBoardBorderWidthInSquares;
        float removalDistance = GetPieceSettings(piece.PieceType).RingOutDistance;
        return piece.BoardFile < boardMinimumEdge - removalDistance ||
               piece.BoardFile > boardMaximumEdge + removalDistance ||
               piece.BoardRank < boardMinimumEdge - removalDistance ||
               piece.BoardRank > boardMaximumEdge + removalDistance;
    }

    private void HandleRingOut(NetworkChessPieceState piece)
    {
        HandleConfiguredPieceRingOut(piece);
    }

    [Rpc(
        SendTo.Everyone,
        InvokePermission = RpcInvokePermission.Server)]
    private void BeginKingDeathCinematicRpc(NetworkChessPieceState kingState)
    {
        BeginKingDeathCinematic(kingState);
    }

    private void BeginKingDeathCinematic(NetworkChessPieceState kingState)
    {
        CleanupKingDeathCinematic();

        if (pieceSpawner == null)
        {
            pieceSpawner = FindFirstObjectByType<ChessPieceSpawner>();
        }

        if (pieceSpawner == null)
        {
            if (IsServer)
            {
                _gameOverPresentationReady.Value = true;
            }

            return;
        }

        Vector2 boardPosition = new(kingState.BoardFile, kingState.BoardRank);
        Vector2 edgePosition = new(
            Mathf.Clamp(
                boardPosition.x,
                pieceSpawner.GroundMinimumCoordinate,
                pieceSpawner.GroundMaximumCoordinate),
            Mathf.Clamp(
                boardPosition.y,
                pieceSpawner.GroundMinimumCoordinate,
                pieceSpawner.GroundMaximumCoordinate));
        Vector2 outward = boardPosition - edgePosition;

        if (outward.sqrMagnitude < 0.0001f)
        {
            outward = boardPosition - new Vector2(3.5f, 3.5f);
        }

        if (outward.sqrMagnitude < 0.0001f)
        {
            outward = Vector2.up;
        }

        outward.Normalize();
        _kingDeathOutward = (
            pieceSpawner.BoardRight * outward.x +
            pieceSpawner.BoardForward * outward.y).normalized;
        _kingDeathVisual = pieceSpawner.DetachKingForDeathCinematic(kingState);
        _kingDeathStartPosition = _kingDeathVisual != null
            ? _kingDeathVisual.transform.position
            : pieceSpawner.GetBoardWorldPosition(edgePosition.x, edgePosition.y);
        _kingDeathStartRotation = _kingDeathVisual != null
            ? _kingDeathVisual.transform.rotation
            : Quaternion.identity;
        _kingDeathPieceHeight = GetVisualHeightAlongAxis(
            _kingDeathVisual,
            pieceSpawner.BoardUp);

        float squareSize = Mathf.Min(
            pieceSpawner.FileSpacing,
            pieceSpawner.RankSpacing);

        if (_kingDeathPieceHeight <= 0.001f)
        {
            _kingDeathPieceHeight = squareSize;
        }

        _kingDeathCamera = Camera.main;

        if (_kingDeathCamera != null)
        {
            _kingDeathCameraStartPosition = _kingDeathCamera.transform.position;
            _kingDeathCameraStartRotation = _kingDeathCamera.transform.rotation;
            _kingDeathOriginalFieldOfView = _kingDeathCamera.fieldOfView;
        }

        _kingDeathCinematicStartTime = Time.unscaledTime;
        _kingDeathCinematicActive = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void UpdateKingDeathCinematic()
    {
        if (!_kingDeathCinematicActive || pieceSpawner == null)
        {
            return;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        float duration = ResolvePresentationDuration();
        float progress = Mathf.Clamp01(
            (Time.unscaledTime - _kingDeathCinematicStartTime) / duration);
        float easedProgress = Mathf.SmoothStep(0f, 1f, progress);
        float squareSize = Mathf.Min(
            pieceSpawner.FileSpacing,
            pieceSpawner.RankSpacing);
        Vector3 boardUp = pieceSpawner.BoardUp;
        Vector3 currentPosition =
            _kingDeathStartPosition +
            _kingDeathOutward *
            (ResolvePresentationOutwardDistance() * squareSize * easedProgress) -
            boardUp *
            (ResolvePresentationDropDistance() * squareSize * progress * progress);

        if (_kingDeathVisual != null)
        {
            Vector3 tiltAxis = Vector3.Cross(_kingDeathOutward, boardUp).normalized;
            Quaternion tilt = Quaternion.AngleAxis(
                ResolvePresentationTiltAngle() * easedProgress,
                tiltAxis);
            _kingDeathVisual.transform.SetPositionAndRotation(
                currentPosition,
                tilt * _kingDeathStartRotation);
        }

        if (_kingDeathCamera != null)
        {
            Vector3 focusPoint = currentPosition +
                boardUp * (_kingDeathPieceHeight * 0.45f);
            Vector3 desiredCameraPosition = currentPosition -
                _kingDeathOutward *
                (ResolvePresentationCameraDistance() * squareSize) +
                boardUp *
                (ResolvePresentationCameraHeight() * squareSize);
            Quaternion desiredCameraRotation = Quaternion.LookRotation(
                focusPoint - desiredCameraPosition,
                boardUp);
            float cameraBlend = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(progress / 0.18f));

            _kingDeathCamera.transform.SetPositionAndRotation(
                Vector3.Lerp(
                    _kingDeathCameraStartPosition,
                    desiredCameraPosition,
                    cameraBlend),
                Quaternion.Slerp(
                    _kingDeathCameraStartRotation,
                    desiredCameraRotation,
                    cameraBlend));
            _kingDeathCamera.fieldOfView = Mathf.Lerp(
                _kingDeathOriginalFieldOfView,
                ResolvePresentationFieldOfView(),
                cameraBlend);
        }

        if (progress < 1f)
        {
            return;
        }

        _kingDeathCinematicActive = false;

        if (_kingDeathVisual != null)
        {
            Destroy(_kingDeathVisual);
            _kingDeathVisual = null;
        }

        if (IsServer)
        {
            _gameOverPresentationReady.Value = true;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void CleanupKingDeathCinematic()
    {
        _kingDeathCinematicActive = false;

        if (_kingDeathVisual != null)
        {
            Destroy(_kingDeathVisual);
            _kingDeathVisual = null;
        }

        if (_kingDeathCamera != null)
        {
            _kingDeathCamera.fieldOfView = _kingDeathOriginalFieldOfView;
            _kingDeathCamera = null;
        }
    }

    private static float GetVisualHeightAlongAxis(
        GameObject visual,
        Vector3 axis)
    {
        if (visual == null)
        {
            return 0f;
        }

        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>();
        float minimum = float.PositiveInfinity;
        float maximum = float.NegativeInfinity;

        foreach (Renderer visualRenderer in renderers)
        {
            Bounds bounds = visualRenderer.bounds;
            float centre = Vector3.Dot(bounds.center, axis);
            Vector3 extents = bounds.extents;
            float projectedExtent =
                Mathf.Abs(axis.x) * extents.x +
                Mathf.Abs(axis.y) * extents.y +
                Mathf.Abs(axis.z) * extents.z;
            minimum = Mathf.Min(minimum, centre - projectedExtent);
            maximum = Mathf.Max(maximum, centre + projectedExtent);
        }

        return renderers.Length > 0 ? maximum - minimum : 0f;
    }

    private static Vector2 GetStableCollisionNormal(ushort leftId, ushort rightId)
    {
        float angle = ((leftId * 37 + rightId * 17) % 360) * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }

    /// <summary>
    /// Resolves a local player capsule against configured pieces without applying the
    /// opposite impulse to the pieces. This deliberately makes the much lighter
    /// player take almost all of the bounce while the server-owned piece motion
    /// remains authoritative.
    /// </summary>
    public bool ResolvePlayerPieceCollisions(
        PlayerTeam playerTeam,
        float playerBottomHeight,
        float playerRadius,
        ref Vector2 playerPosition,
        ref Vector2 playerVelocity)
    {
        return ResolvePlayerPieceCollisions(
            playerTeam,
            playerBottomHeight,
            playerRadius,
            false,
            ref playerPosition,
            ref playerVelocity);
    }

    public bool ResolvePlayerPieceCollisions(
        PlayerTeam playerTeam,
        float playerBottomHeight,
        float playerRadius,
        bool forceFriendlyPieceCollision,
        ref Vector2 playerPosition,
        ref Vector2 playerVelocity)
    {
        if (!IsSpawned ||
            playerTeam == PlayerTeam.Unassigned ||
            playerBottomHeight >= ResolvePlayerCollisionHeight())
        {
            return false;
        }

        bool collided = false;
        for (int index = 0; index < _pieces.Count; index++)
        {
            NetworkChessPieceState piece = _pieces[index];

            if (piece.OwnerTeam == playerTeam &&
                !forceFriendlyPieceCollision &&
                AreFriendlyPiecesIntangible())
            {
                continue;
            }

            Vector2 piecePosition = new(piece.BoardFile, piece.BoardRank);
            Vector2 separation = playerPosition - piecePosition;
            float minimumDistance =
                GetPieceSettings(piece.PieceType).CollisionRadius +
                Mathf.Max(0.01f, playerRadius);
            float minimumDistanceSquared = minimumDistance * minimumDistance;
            float distanceSquared = separation.sqrMagnitude;

            if (distanceSquared >= minimumDistanceSquared)
            {
                continue;
            }

            float distance = Mathf.Sqrt(distanceSquared);
            Vector2 normal;

            if (distance > ResolveSeparationEpsilon())
            {
                normal = separation / distance;
            }
            else
            {
                Vector2 pieceVelocityAtCentre = GetCommandedVelocity(piece) +
                    new Vector2(
                        piece.KnockbackFileVelocity,
                        piece.KnockbackRankVelocity);
                normal = pieceVelocityAtCentre.sqrMagnitude > 0.0001f
                    ? pieceVelocityAtCentre.normalized
                    : GetStableCollisionNormal(piece.Id, (ushort)(piece.Id + 97));
            }

            // Resolve all penetration on the player, modelling the piece as much
            // heavier than the player.
            playerPosition += normal * (minimumDistance - distance);

            Vector2 pieceVelocity = GetCommandedVelocity(piece) +
                new Vector2(
                    piece.KnockbackFileVelocity,
                    piece.KnockbackRankVelocity);
            float pieceSpeed = pieceVelocity.magnitude;
            float impactAlignment = pieceSpeed > 0.0001f
                ? Vector2.Dot(pieceVelocity / pieceSpeed, normal)
                : 0f;

            // Only the piece's own forward motion can create knockback. Walking
            // into a stationary piece, approaching a moving piece from behind,
            // or hitting its side still resolves overlap but never bounces the
            // player away.
            if (pieceSpeed >= ResolveMinimumPlayerImpactSpeed() &&
                impactAlignment >= ResolveMinimumPlayerImpactAlignment())
            {
                float pieceImpactSpeed = pieceSpeed * impactAlignment;
                float targetOutwardSpeed = pieceImpactSpeed *
                    (1f + ResolveRestitution()) *
                    ResolveCollisionImpulseMultiplier();
                float currentOutwardSpeed = Vector2.Dot(playerVelocity, normal);

                if (currentOutwardSpeed < targetOutwardSpeed)
                {
                    playerVelocity += normal *
                        (targetOutwardSpeed - currentOutwardSpeed);
                }
            }

            collided = true;
        }

        return collided;
    }

    public void UpdateLocalVoiceGazeTarget(
        ushort? pieceId,
        float commanderFile,
        float commanderRank)
    {
        if (pieceId.HasValue)
        {
            int pieceIndex = FindPieceIndexById(pieceId.Value);

            if (pieceIndex < 0 ||
                !CanLocallySelectPiece(_pieces[pieceIndex], out _))
            {
                pieceId = null;
            }
        }

        _localCommanderFile = commanderFile;
        _localCommanderRank = commanderRank;
        _localHoveredPieceId = pieceId.HasValue ? pieceId.Value : -1;

        if (UsesManualConfirmedSelection)
        {
            PruneLocalConfirmedSelections();

            _localVoiceTargetPieceId = _localConfirmedPieceId;
            pieceSpawner?.SetVoiceSelectionTarget(
                _localHoveredPieceId >= 0 &&
                !_localConfirmedPieceIds.Contains((ushort)_localHoveredPieceId)
                    ? (ushort)_localHoveredPieceId
                    : null);
        }
        else if (UsesProximityAutoSelection)
        {
            UpdateLocalProximitySelection(commanderFile, commanderRank);
            _localHoveredPieceId = -1;
        }
        else
        {
            ClearLocalConfirmedSelection();
            HideLocalProximitySelectionRange();
            _localVoiceTargetPieceId = _localHoveredPieceId;
            pieceSpawner?.SetVoiceSelectionTarget(pieceId);
        }

        RecordLocalVoiceGazeSample();
    }

    public bool TryGetLocalVoiceTargetSnapshot(
        out ushort pieceId,
        out float distanceInSquares)
    {
        return TryGetLocalVoiceTargetSnapshotAt(
            Time.unscaledTime,
            out pieceId,
            out distanceInSquares);
    }

    public bool TryGetLocalVoiceTargetSnapshotAt(
        float sampleTime,
        out ushort pieceId,
        out float distanceInSquares)
    {
        return TryGetLocalVoiceCommandSnapshotAt(
            sampleTime,
            out pieceId,
            out distanceInSquares,
            out _,
            out _);
    }

    public bool TryGetLocalVoiceCommandSnapshot(
        out ushort pieceId,
        out float distanceInSquares,
        out bool hasChargeAim,
        out Vector2 chargeAimBoardPosition)
    {
        return TryGetLocalVoiceCommandSnapshotAt(
            Time.unscaledTime,
            out pieceId,
            out distanceInSquares,
            out hasChargeAim,
            out chargeAimBoardPosition);
    }

    public bool TryGetLocalVoiceCommandSnapshotAt(
        float sampleTime,
        out ushort pieceId,
        out float distanceInSquares,
        out bool hasChargeAim,
        out Vector2 chargeAimBoardPosition)
    {
        pieceId = 0;
        distanceInSquares = 0f;
        hasChargeAim = false;
        chargeAimBoardPosition = default;

        if (_localVoiceGazeHistory.Count == 0)
        {
            return false;
        }

        LocalVoiceGazeSample sample = _localVoiceGazeHistory[0];

        for (int index = _localVoiceGazeHistory.Count - 1; index >= 0; index--)
        {
            if (_localVoiceGazeHistory[index].Time <= sampleTime)
            {
                sample = _localVoiceGazeHistory[index];
                break;
            }
        }

        if (sample.PieceId < 0 ||
            FindPieceIndexById((ushort)sample.PieceId) < 0)
        {
            return false;
        }

        pieceId = (ushort)sample.PieceId;
        distanceInSquares = sample.DistanceInSquares;
        hasChargeAim = sample.HasChargeAim;
        chargeAimBoardPosition = sample.ChargeAimBoardPosition;
        return true;
    }

    private void RecordLocalVoiceGazeSample()
    {
        float distance = 0f;

        if (_localVoiceTargetPieceId >= 0)
        {
            int pieceIndex = FindPieceIndexById((ushort)_localVoiceTargetPieceId);

            if (pieceIndex >= 0)
            {
                NetworkChessPieceState piece = _pieces[pieceIndex];
                distance = Vector2.Distance(
                    new Vector2(_localCommanderFile, _localCommanderRank),
                    new Vector2(piece.BoardFile, piece.BoardRank));
            }
        }

        float now = Time.unscaledTime;
        _localVoiceGazeHistory.Add(new LocalVoiceGazeSample(
            now,
            _localVoiceTargetPieceId,
            distance,
            _localChargeAimValid,
            _localChargeAimBoardPosition));

        while (_localVoiceGazeHistory.Count > 0 &&
               now - _localVoiceGazeHistory[0].Time > 2f)
        {
            _localVoiceGazeHistory.RemoveAt(0);
        }
    }

    public void ShowLocalVoiceFailure(ushort? pieceId)
    {
        if (pieceId.HasValue)
        {
            pieceSpawner?.ShowVoiceQuestionMark(pieceId.Value);
        }
    }

    public void ShowLocalVoiceCommandTarget(
        ushort? pieceId,
        float duration = -1f)
    {
        pieceSpawner?.SetVoiceCommandTarget(pieceId, duration);
    }

    public void HoldLocalVoiceCommandTarget(float duration = 1f)
    {
        pieceSpawner?.HoldVoiceCommandTarget(duration);
    }

    public bool TryExecuteLocalVoiceCommand(
        ushort pieceId,
        float targetDistanceInSquares,
        float commandReachInSquares,
        float commandLoudness,
        PieceVoiceCommand command,
        bool hasChargeAim,
        Vector2 chargeAimBoardPosition,
        out string rejection)
    {
        return TryExecuteLocalVoiceCommand(
            pieceId,
            targetDistanceInSquares,
            commandReachInSquares,
            commandLoudness,
            command,
            hasChargeAim,
            chargeAimBoardPosition,
            0f,
            1f,
            out rejection);
    }

    public bool TryExecuteLocalVoiceCommand(
        ushort pieceId,
        float targetDistanceInSquares,
        float commandReachInSquares,
        float commandLoudness,
        PieceVoiceCommand command,
        bool hasChargeAim,
        Vector2 chargeAimBoardPosition,
        float voicedDurationSeconds,
        float pronunciationScore,
        out string rejection)
    {
        rejection = string.Empty;

        if (!IsSpawned || NetworkManager == null || !NetworkManager.IsListening)
        {
            rejection = "게임 네트워크가 아직 준비되지 않았습니다.";
            return false;
        }

        if (!TryGetLocalPlayer(out NetworkPlayer localPlayer))
        {
            rejection = "로컬 플레이어를 찾지 못했습니다.";
            return false;
        }

        if (localPlayer.Team != PlayerTeam.White &&
            localPlayer.Team != PlayerTeam.Black)
        {
            rejection = "먼저 팀을 선택해 주세요.";
            return false;
        }

        int pieceIndex = FindPieceIndexById(pieceId);

        if (pieceIndex < 0 || _pieces[pieceIndex].OwnerTeam != localPlayer.Team)
        {
            rejection = "자기 팀의 말만 명령할 수 있습니다.";
            return false;
        }

        voicedDurationSeconds = Mathf.Clamp(
            voicedDurationSeconds,
            0f,
            gameMode?.Commands.VoiceChargeMaximumDurationSeconds ?? 3f);
        pronunciationScore = Mathf.Clamp01(pronunciationScore);

        if (!CanIssueCommand(
                localPlayer.Team,
                _pieces[pieceIndex].PieceType,
                command,
                out rejection,
                voicedDurationSeconds,
                localPlayer))
        {
            return false;
        }

        if (!CanIssuePieceMovementCommand(
                _pieces[pieceIndex],
                command,
                out rejection))
        {
            return false;
        }

        if (IsPieceMovementCommand(command) &&
            !CanLocallySelectPiece(_pieces[pieceIndex], out rejection))
        {
            return false;
        }

        if (command != PieceVoiceCommand.Stop &&
            _isGameOver.Value)
        {
            rejection = "게임이 이미 끝났습니다.";
            return false;
        }

        if (command == PieceVoiceCommand.Charge && !hasChargeAim)
        {
            rejection = "돌진 레이저가 기물, 플레이어, 보드 또는 경기장 벽에 닿지 않았습니다.";
            return false;
        }

        if (command == PieceVoiceCommand.Charge &&
            UsesChargeSelectionCommand &&
            _localConfirmedPieceIds.Count > 0)
        {
            int selectionCount = _localConfirmedPieceIds.Count;

            for (int selectionIndex = 0;
                 selectionIndex < selectionCount;
                 selectionIndex++)
            {
                int selectedPieceIndex = FindPieceIndexById(
                    _localConfirmedPieceIds[selectionIndex]);

                if (selectedPieceIndex < 0 ||
                    !CanLocallySelectPiece(
                        _pieces[selectedPieceIndex],
                        out rejection))
                {
                    return false;
                }
            }

            float totalCost = GetVoiceChargeCost(
                _pieces[pieceIndex].PieceType,
                voicedDurationSeconds,
                selectionCount);

            if (IsCostSystemEnabled &&
                GetCommandPoints(localPlayer.Team) + 0.0001f < totalCost)
            {
                rejection =
                    $"명령 코스트가 부족합니다. 필요 {totalCost:F1}, " +
                    $"보유 {GetCommandPoints(localPlayer.Team):F1}";
                return false;
            }

            if (UsesProximityAutoSelection)
            {
                RequestProximityChargeCommandRpc(
                    Mathf.Clamp01(commandLoudness),
                    chargeAimBoardPosition.x,
                    chargeAimBoardPosition.y,
                    voicedDurationSeconds,
                    pronunciationScore);
            }
            else
            {
                ushort first = _localConfirmedPieceIds[0];
                ushort second = selectionCount > 1
                    ? _localConfirmedPieceIds[1]
                    : ushort.MaxValue;
                ushort third = selectionCount > 2
                    ? _localConfirmedPieceIds[2]
                    : ushort.MaxValue;
                RequestMultiChargeCommandRpc(
                    first,
                    second,
                    third,
                    (byte)selectionCount,
                    Mathf.Clamp01(commandLoudness),
                    chargeAimBoardPosition.x,
                    chargeAimBoardPosition.y,
                    voicedDurationSeconds,
                    pronunciationScore);
            }

            if (gameMode?.Commands.ShowChargeRaycastLaser ?? false)
            {
                ShowLocalChargeLaser(chargeAimBoardPosition);
            }

            PredictLocalMovementCooldown(_localConfirmedPieceIds);
            ClearLocalSelectionAfterMovementCommand();

            return true;
        }

        if (command != PieceVoiceCommand.Charge &&
            commandReachInSquares + 0.05f < targetDistanceInSquares)
        {
            rejection =
                "목소리가 말까지 닿지 않습니다. 거리 " +
                targetDistanceInSquares.ToString("F1") +
                "칸 / 전달 " +
                commandReachInSquares.ToString("F1") +
                "칸";
            ShowLocalVoiceFailure(pieceId);
            return false;
        }

        RequestVoiceCommandRpc(
            pieceId,
            command,
            Mathf.Clamp01(commandLoudness),
            hasChargeAim,
            chargeAimBoardPosition.x,
            chargeAimBoardPosition.y,
            voicedDurationSeconds,
            pronunciationScore);

        if (command == PieceVoiceCommand.Charge &&
            (gameMode?.Commands.ShowChargeRaycastLaser ?? false))
        {
            ShowLocalChargeLaser(chargeAimBoardPosition);
        }

        if (IsPieceMovementCommand(command))
        {
            PredictLocalMovementCooldown(pieceId);
            ClearLocalSelectionAfterMovementCommand();
        }

        return true;
    }

    [Rpc(
        SendTo.Server,
        InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestMultiChargeCommandRpc(
        ushort firstPieceId,
        ushort secondPieceId,
        ushort thirdPieceId,
        byte requestedCount,
        float commandLoudness,
        float chargeTargetFile,
        float chargeTargetRank,
        float voicedDurationSeconds,
        float pronunciationScore,
        RpcParams rpcParams = default)
    {
        if (!UsesManualConfirmedSelection ||
            !NetworkPlayer.TryGetByClientId(
                rpcParams.Receive.SenderClientId,
                out NetworkPlayer player) ||
            player.IsEliminated ||
            pieceSpawner == null ||
            _isGameOver.Value)
        {
            return;
        }

        int count = Mathf.Clamp(
            requestedCount,
            1,
            gameMode?.Commands.MaximumConfirmedSelections ?? 3);
        ushort[] requestedIds = { firstPieceId, secondPieceId, thirdPieceId };
        List<int> pieceIndices = new(count);

        for (int requestIndex = 0; requestIndex < count; requestIndex++)
        {
            ushort requestedId = requestedIds[requestIndex];

            for (int previous = 0; previous < requestIndex; previous++)
            {
                if (requestedIds[previous] == requestedId)
                {
                    return;
                }
            }

            int index = FindPieceIndexById(requestedId);

            if (index < 0 ||
                _pieces[index].OwnerTeam != player.Team ||
                !CanIssueCommand(
                    player.Team,
                    _pieces[index].PieceType,
                    PieceVoiceCommand.Charge,
                    out _,
                    voicedDurationSeconds,
                    player) ||
                !CanIssuePieceMovementCommand(
                    _pieces[index],
                    PieceVoiceCommand.Charge,
                    out _))
            {
                return;
            }

            pieceIndices.Add(index);
        }

        ExecuteChargeCommandForPieces(
            player,
            pieceIndices,
            commandLoudness,
            chargeTargetFile,
            chargeTargetRank,
            voicedDurationSeconds,
            pronunciationScore);
    }

    [Rpc(
        SendTo.Server,
        InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestProximityChargeCommandRpc(
        float commandLoudness,
        float chargeTargetFile,
        float chargeTargetRank,
        float voicedDurationSeconds,
        float pronunciationScore,
        RpcParams rpcParams = default)
    {
        if (!UsesProximityAutoSelection ||
            !NetworkPlayer.TryGetByClientId(
                rpcParams.Receive.SenderClientId,
                out NetworkPlayer player) ||
            player.IsEliminated ||
            pieceSpawner == null ||
            _isGameOver.Value)
        {
            return;
        }

        Vector3 avatarPose = player.AvatarBoardPose;
        Vector2 commanderPosition = new(avatarPose.x, avatarPose.z);
        float radius = gameMode?.Commands.ProximitySelectionRadiusInSquares ?? 1.5f;
        float radiusSquared = radius * radius;
        List<int> pieceIndices = new();

        for (int index = 0; index < _pieces.Count; index++)
        {
            NetworkChessPieceState piece = _pieces[index];
            Vector2 offset = new(
                piece.BoardFile - commanderPosition.x,
                piece.BoardRank - commanderPosition.y);

            if (piece.OwnerTeam != player.Team ||
                offset.sqrMagnitude > radiusSquared)
            {
                continue;
            }

            if (!CanIssueCommand(
                    player.Team,
                    piece.PieceType,
                    PieceVoiceCommand.Charge,
                    out _,
                    voicedDurationSeconds,
                    player))
            {
                return;
            }

            if (!CanIssuePieceMovementCommand(
                    piece,
                    PieceVoiceCommand.Charge,
                    out _))
            {
                continue;
            }

            pieceIndices.Add(index);
        }

        if (pieceIndices.Count == 0)
        {
            return;
        }

        ExecuteChargeCommandForPieces(
            player,
            pieceIndices,
            commandLoudness,
            chargeTargetFile,
            chargeTargetRank,
            voicedDurationSeconds,
            pronunciationScore);
    }

    private void ExecuteChargeCommandForPieces(
        NetworkPlayer player,
        IReadOnlyList<int> pieceIndices,
        float commandLoudness,
        float chargeTargetFile,
        float chargeTargetRank,
        float voicedDurationSeconds,
        float pronunciationScore)
    {
        if (player == null || pieceIndices == null || pieceIndices.Count == 0)
        {
            return;
        }

        voicedDurationSeconds = Mathf.Clamp(
            voicedDurationSeconds,
            0f,
            gameMode?.Commands.VoiceChargeMaximumDurationSeconds ?? 3f);
        commandLoudness = Mathf.Clamp01(commandLoudness);
        pronunciationScore = Mathf.Clamp01(pronunciationScore);
        PieceArchetypeSettings representativeSettings = GetPieceSettings(
            _pieces[pieceIndices[0]].PieceType);
        float acceptedCost = GetVoiceChargeCost(
            _pieces[pieceIndices[0]].PieceType,
            voicedDurationSeconds,
            pieceIndices.Count);

        if (IsCostSystemEnabled &&
            GetCommandPoints(player.Team) + 0.0001f < acceptedCost)
        {
            return;
        }

        Vector2 target = new(
            Mathf.Clamp(
                chargeTargetFile,
                pieceSpawner.GroundMinimumCoordinate,
                pieceSpawner.GroundMaximumCoordinate),
            Mathf.Clamp(
                chargeTargetRank,
                pieceSpawner.GroundMinimumCoordinate,
                pieceSpawner.GroundMaximumCoordinate));

        for (int index = 0; index < pieceIndices.Count; index++)
        {
            int pieceIndex = pieceIndices[index];
            NetworkChessPieceState piece = _pieces[pieceIndex];
            PieceArchetypeSettings pieceSettings = GetPieceSettings(
                piece.PieceType);
            Vector2 direction = target - new Vector2(
                piece.BoardFile,
                piece.BoardRank);

            if (direction.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            piece.VoiceHeading = GetVoiceHeadingForDirection(
                piece.OwnerTeam,
                direction.normalized);

            // Every selected piece travels directly to the shared aim point.
            ApplyVoiceChargedMovement(
                ref piece,
                pieceSettings,
                chargePower: 1f,
                chargeDistance: direction.magnitude);

            StartPieceMovementCooldown(ref piece);

            _pieces[pieceIndex] = piece;
        }

        AcceptCommand(
            player.Team,
            representativeSettings,
            PieceVoiceCommand.Charge,
            acceptedCost,
            player);
    }

    [Rpc(
        SendTo.Server,
        InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestVoiceCommandRpc(
        ushort pieceId,
        PieceVoiceCommand command,
        float commandLoudness,
        bool hasChargeAim,
        float chargeTargetFile,
        float chargeTargetRank,
        float voicedDurationSeconds,
        float pronunciationScore,
        RpcParams rpcParams = default)
    {
        if (!NetworkPlayer.TryGetByClientId(
                rpcParams.Receive.SenderClientId,
                out NetworkPlayer player))
        {
            return;
        }

        int pieceIndex = FindPieceIndexById(pieceId);

        if (pieceIndex < 0)
        {
            return;
        }

        NetworkChessPieceState piece = _pieces[pieceIndex];

        PieceArchetypeSettings pieceSettings = GetPieceSettings(piece.PieceType);

        if (player.IsEliminated ||
            piece.OwnerTeam != player.Team ||
            (command != PieceVoiceCommand.Stop &&
             _isGameOver.Value) ||
             !CanIssueCommand(
                 player.Team,
                 piece.PieceType,
                 command,
                 out _,
                 voicedDurationSeconds,
                 player) ||
             !CanIssuePieceMovementCommand(piece, command, out _))
        {
            return;
        }

        commandLoudness = Mathf.Clamp01(commandLoudness);
        voicedDurationSeconds = Mathf.Clamp(
            voicedDurationSeconds,
            0f,
            gameMode?.Commands.VoiceChargeMaximumDurationSeconds ?? 3f);
        pronunciationScore = Mathf.Clamp01(pronunciationScore);
        float acceptedCost = GetCommandCost(
            pieceSettings,
            command,
            voicedDurationSeconds);

        if (command == PieceVoiceCommand.Charge)
        {
            if (!hasChargeAim || pieceSpawner == null)
            {
                return;
            }

            Vector2 target = new(
                Mathf.Clamp(
                    chargeTargetFile,
                    pieceSpawner.GroundMinimumCoordinate,
                    pieceSpawner.GroundMaximumCoordinate),
                Mathf.Clamp(
                    chargeTargetRank,
                    pieceSpawner.GroundMinimumCoordinate,
                    pieceSpawner.GroundMaximumCoordinate));
            Vector2 direction = target - new Vector2(
                piece.BoardFile,
                piece.BoardRank);

            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            piece.VoiceHeading = GetVoiceHeadingForDirection(
                piece.OwnerTeam,
                direction.normalized);
            ApplyVoiceChargedMovement(
                ref piece,
                pieceSettings,
                chargePower: 1f,
                chargeDistance: direction.magnitude);
        }
        else if (TryGetMovementHeadingOffset(command, out float movementHeadingOffset))
        {
            ApplyMovementCommand(
                ref piece,
                pieceSettings,
                movementHeadingOffset,
                commandLoudness);
        }
        else
        {
            switch (command)
            {
            case PieceVoiceCommand.Stop:
                piece.VoiceMoveAxis = 0;
                piece.VoiceTurnAxis = 0;
                piece.VoiceChargeDistanceRemaining = 0f;
                piece.FirstAttackingCollisionAvailable = false;
                piece.CollisionChainDepth =
                    NetworkChessPieceState.InactiveCollisionChainDepth;

                if (pieceSettings.MovementControl == PieceMovementControl.FlickImpulse)
                {
                    piece.KnockbackFileVelocity = 0f;
                    piece.KnockbackRankVelocity = 0f;
                }

                break;
            case PieceVoiceCommand.TurnLeft:
                piece.VoiceTurnAxis = -1;
                piece.VoiceTurnLoudness = commandLoudness;
                break;
            case PieceVoiceCommand.TurnRight:
                piece.VoiceTurnAxis = 1;
                piece.VoiceTurnLoudness = commandLoudness;
                break;
            case PieceVoiceCommand.SkillPrimary:
            case PieceVoiceCommand.SkillSecondary:
                if (!TryExecuteAbility(
                        ref piece,
                        pieceSettings,
                        command,
                        commandLoudness))
                {
                    return;
                }

                break;
            default:
                return;
            }
        }

        if (IsPieceMovementCommand(command))
        {
            StartPieceMovementCooldown(ref piece);
        }

        _pieces[pieceIndex] = piece;
        AcceptCommand(
            player.Team,
            pieceSettings,
            command,
            acceptedCost,
            player);
    }

    private bool TryGetLocalPlayer(out NetworkPlayer localPlayer)
    {
        localPlayer = null;

        return NetworkManager != null &&
               NetworkManager.IsListening &&
               NetworkPlayer.TryGetByClientId(
                   NetworkManager.LocalClientId,
                   out localPlayer);
    }

    private int FindPieceIndexById(ushort pieceId)
    {
        for (int index = 0; index < _pieces.Count; index++)
        {
            if (_pieces[index].Id == pieceId)
            {
                return index;
            }
        }

        return -1;
    }

    private void ClearLocalVoiceTarget()
    {
        _localVoiceTargetPieceId = -1;
        _localHoveredPieceId = -1;
        _localVoiceGazeHistory.Clear();
        _localPredictedMovementCooldownEnds.Clear();
        ClearLocalConfirmedSelection();
        ClearLocalChargeAim();
        pieceSpawner?.SetVoiceSelectionTarget(null);
        pieceSpawner?.SetVoiceCommandTarget(null);
    }

    private static Vector2 GetVoiceMoveDirection(
        PlayerTeam team,
        float heading)
    {
        Vector2 forward = team == PlayerTeam.Black
            ? Vector2.down
            : Vector2.up;
        float radians = heading * Mathf.Deg2Rad;
        float sine = Mathf.Sin(radians);
        float cosine = Mathf.Cos(radians);

        return new Vector2(
            forward.x * cosine + forward.y * sine,
            -forward.x * sine + forward.y * cosine);
    }

    private static float GetVoiceHeadingForDirection(
        PlayerTeam team,
        Vector2 direction)
    {
        direction.Normalize();
        return team == PlayerTeam.Black
            ? Mathf.Atan2(-direction.x, -direction.y) * Mathf.Rad2Deg
            : Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;
    }

    private float GetVoiceSpeedMultiplier(float commandLoudness)
    {
        return Mathf.Lerp(
            quietVoiceSpeedMultiplier,
            loudVoiceSpeedMultiplier,
            Mathf.Clamp01(commandLoudness));
    }

    private static PlayerTeam GetOpponent(PlayerTeam team)
    {
        return team == PlayerTeam.White
            ? PlayerTeam.Black
            : PlayerTeam.White;
    }

    private void OnValidate()
    {
        _fallbackPieceSettings.Clear();
        matchDurationSeconds = Mathf.Max(1f, matchDurationSeconds);
        voiceMoveSpeed = Mathf.Max(0.05f, voiceMoveSpeed);
        voiceTurnSpeed = Mathf.Max(5f, voiceTurnSpeed);
        quietVoiceSpeedMultiplier = Mathf.Clamp(
            quietVoiceSpeedMultiplier,
            0.05f,
            1f);
        loudVoiceSpeedMultiplier = Mathf.Clamp(
            loudVoiceSpeedMultiplier,
            1f,
            3f);
        pieceCollisionRadius = Mathf.Clamp(pieceCollisionRadius, 0.2f, 0.49f);
        collisionRestitution = Mathf.Clamp01(collisionRestitution);
        collisionImpulseMultiplier = Mathf.Clamp(
            collisionImpulseMultiplier,
            0.5f,
            2f);
        knockbackDrag = Mathf.Max(0f, knockbackDrag);
        ringOutDistance = Mathf.Max(0.1f, ringOutDistance);
        playerPieceCollisionHeight = Mathf.Max(
            0.1f,
            playerPieceCollisionHeight);
        minimumPlayerImpactSpeed = Mathf.Max(0f, minimumPlayerImpactSpeed);
        minimumPlayerImpactAlignment = Mathf.Clamp01(
            minimumPlayerImpactAlignment);
        kingDeathCinematicDuration = Mathf.Max(1f, kingDeathCinematicDuration);
        kingDeathCameraDistanceInSquares = Mathf.Max(
            0.5f,
            kingDeathCameraDistanceInSquares);
        kingDeathCameraHeightInSquares = Mathf.Max(
            0.1f,
            kingDeathCameraHeightInSquares);
        kingDeathDropDistanceInSquares = Mathf.Max(
            0.1f,
            kingDeathDropDistanceInSquares);
        kingDeathOutwardDistanceInSquares = Mathf.Max(
            0f,
            kingDeathOutwardDistanceInSquares);
        kingDeathTiltAngle = Mathf.Clamp(kingDeathTiltAngle, 0f, 180f);
        kingDeathCameraFieldOfView = Mathf.Clamp(
            kingDeathCameraFieldOfView,
            25f,
            80f);
    }
}
