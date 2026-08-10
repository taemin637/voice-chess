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
    public float KnockbackFileVelocity;
    public float KnockbackRankVelocity;

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
        KnockbackFileVelocity = 0f;
        KnockbackRankVelocity = 0f;
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
        serializer.SerializeValue(ref KnockbackFileVelocity);
        serializer.SerializeValue(ref KnockbackRankVelocity);
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
               KnockbackFileVelocity.Equals(other.KnockbackFileVelocity) &&
               KnockbackRankVelocity.Equals(other.KnockbackRankVelocity);
    }
}

[DisallowMultipleComponent]
public sealed class NetworkChessGame : NetworkBehaviour
{
    private const int StartingPiecesPerTeam = 16;

    private readonly struct LocalVoiceGazeSample
    {
        public readonly float Time;
        public readonly int PieceId;
        public readonly float DistanceInSquares;

        public LocalVoiceGazeSample(
            float time,
            int pieceId,
            float distanceInSquares)
        {
            Time = time;
            PieceId = pieceId;
            DistanceInSquares = distanceInSquares;
        }
    }

    [SerializeField] private ChessPieceSpawner pieceSpawner;

    [Header("Match Rules")]
    [SerializeField, Min(1f)] private float matchDurationSeconds = 60f;

    [Header("Voice Free Movement")]
    [SerializeField, Min(0.05f)] private float voiceMoveSpeed = 0.85f;
    [SerializeField, Min(5f)] private float voiceTurnSpeed = 90f;
    [SerializeField, Range(0.05f, 1f)] private float quietVoiceSpeedMultiplier = 0.25f;
    [SerializeField, Range(1f, 3f)] private float loudVoiceSpeedMultiplier = 1.75f;

    [Header("Piece Collision and Ring Out")]
    [Tooltip("Collision radius measured in chess-square units.")]
    [SerializeField, Range(0.2f, 0.49f)] private float pieceCollisionRadius = 0.36f;
    [Tooltip("How strongly pieces bounce apart. 0 is inelastic, 1 is fully elastic.")]
    [SerializeField, Range(0f, 1f)] private float collisionRestitution = 0.72f;
    [Tooltip("Extra multiplier for the impulse produced by a collision.")]
    [SerializeField, Range(0.5f, 2f)] private float collisionImpulseMultiplier = 1.15f;
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

    [Header("King Death Cinematic")]
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
    public float MatchDurationSeconds => matchDurationSeconds;
    public float RemainingTime
    {
        get
        {
            if (!_matchTimerRunning.Value || NetworkManager == null)
            {
                return _isGameOver.Value ? 0f : matchDurationSeconds;
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
    }

    public override void OnNetworkDespawn()
    {
        _pieces.OnListChanged -= HandlePiecesChanged;
        _isGameOver.OnValueChanged -= HandleGameOverChanged;
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

        if (!_isGameOver.Value &&
            _matchTimerRunning.Value &&
            NetworkPlayer.MatchStarted &&
            NetworkManager.ServerTime.Time >= _matchEndServerTime.Value)
        {
            FinishMatchByPieceCount();
        }

        if (_isGameOver.Value)
        {
            return;
        }

        float deltaTime = Time.fixedDeltaTime;
        List<NetworkChessPieceState> simulatedPieces = new(_pieces.Count);
        List<Vector2> commandedVelocities = new(_pieces.Count);

        for (int index = 0; index < _pieces.Count; index++)
        {
            NetworkChessPieceState piece = _pieces[index];

            if (piece.VoiceTurnAxis != 0)
            {
                float turnSpeed = voiceTurnSpeed * GetVoiceSpeedMultiplier(
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
            knockbackVelocity *= Mathf.Exp(-knockbackDrag * deltaTime);

            if (knockbackVelocity.sqrMagnitude < 0.0001f)
            {
                knockbackVelocity = Vector2.zero;
            }

            Vector2 position = new(piece.BoardFile, piece.BoardRank);
            position += (commandedVelocity + knockbackVelocity) * deltaTime;

            piece.BoardFile = position.x;
            piece.BoardRank = position.y;
            piece.KnockbackFileVelocity = knockbackVelocity.x;
            piece.KnockbackRankVelocity = knockbackVelocity.y;
            simulatedPieces.Add(piece);
            commandedVelocities.Add(commandedVelocity);
        }

        ResolvePieceCollisions(simulatedPieces, commandedVelocities);
        RemoveRingedOutPieces(simulatedPieces);
        ApplySimulatedState(simulatedPieces);
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

        UpdateKingDeathCinematic();
    }

    private void InitializePieces(bool startMatchTimer)
    {
        _matchTimerRunning.Value = false;
        _pieces.Clear();
        ushort nextId = 0;
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

        for (int file = 0; file < 8; file++)
        {
            _pieces.Add(new NetworkChessPieceState(
                nextId++, PlayerTeam.White, backRank[file], file, 0f));
            _pieces.Add(new NetworkChessPieceState(
                nextId++, PlayerTeam.White, ChessPieceType.Pawn, file, 1f));
            _pieces.Add(new NetworkChessPieceState(
                nextId++, PlayerTeam.Black, ChessPieceType.Pawn, file, 6f));
            _pieces.Add(new NetworkChessPieceState(
                nextId++, PlayerTeam.Black, backRank[file], file, 7f));
        }

        _gameOverPresentationReady.Value = true;
        _winner.Value = PlayerTeam.Unassigned;
        _isGameOver.Value = false;

        if (startMatchTimer)
        {
            _matchEndServerTime.Value =
                NetworkManager.ServerTime.Time + matchDurationSeconds;
            _matchTimerRunning.Value = true;
        }
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
            StartingPiecesPerTeam - GetRemainingPieceCount(opponent));
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

        _matchTimerRunning.Value = false;
        _winner.Value = winner;
        _isGameOver.Value = true;
        _gameOverPresentationReady.Value = true;
    }

    private Vector2 GetCommandedVelocity(NetworkChessPieceState piece)
    {
        if (piece.VoiceMoveAxis == 0)
        {
            return Vector2.zero;
        }

        float moveSpeed = voiceMoveSpeed * GetVoiceSpeedMultiplier(
            piece.VoiceMoveLoudness);
        return GetVoiceMoveDirection(
                   piece.OwnerTeam,
                   piece.VoiceHeading + piece.VoiceMoveHeadingOffset) *
               (piece.VoiceMoveAxis * moveSpeed);
    }

    private void ResolvePieceCollisions(
        List<NetworkChessPieceState> pieces,
        List<Vector2> commandedVelocities)
    {
        float minimumDistance = pieceCollisionRadius * 2f;
        float minimumDistanceSquared = minimumDistance * minimumDistance;

        for (int leftIndex = 0; leftIndex < pieces.Count - 1; leftIndex++)
        {
            for (int rightIndex = leftIndex + 1; rightIndex < pieces.Count; rightIndex++)
            {
                NetworkChessPieceState left = pieces[leftIndex];
                NetworkChessPieceState right = pieces[rightIndex];
                Vector2 leftPosition = new(left.BoardFile, left.BoardRank);
                Vector2 rightPosition = new(right.BoardFile, right.BoardRank);
                Vector2 separation = rightPosition - leftPosition;
                float distanceSquared = separation.sqrMagnitude;

                if (distanceSquared >= minimumDistanceSquared)
                {
                    continue;
                }

                float distance = Mathf.Sqrt(distanceSquared);
                Vector2 normal = distance > 0.0001f
                    ? separation / distance
                    : GetStableCollisionNormal(left.Id, right.Id);
                float leftInverseMass = 1f / GetPieceMass(left.PieceType);
                float rightInverseMass = 1f / GetPieceMass(right.PieceType);
                float inverseMassSum = leftInverseMass + rightInverseMass;
                float penetration = minimumDistance - distance;
                Vector2 correction = normal * (penetration / inverseMassSum);

                leftPosition -= correction * leftInverseMass;
                rightPosition += correction * rightInverseMass;

                Vector2 leftVelocity = commandedVelocities[leftIndex] +
                    new Vector2(left.KnockbackFileVelocity, left.KnockbackRankVelocity);
                Vector2 rightVelocity = commandedVelocities[rightIndex] +
                    new Vector2(right.KnockbackFileVelocity, right.KnockbackRankVelocity);
                float closingSpeed = Vector2.Dot(rightVelocity - leftVelocity, normal);

                if (closingSpeed < 0f)
                {
                    float impulseMagnitude =
                        -(1f + collisionRestitution) * closingSpeed /
                        inverseMassSum * collisionImpulseMultiplier;
                    Vector2 impulse = normal * impulseMagnitude;
                    leftVelocity -= impulse * leftInverseMass;
                    rightVelocity += impulse * rightInverseMass;

                    Vector2 leftKnockback = leftVelocity - commandedVelocities[leftIndex];
                    Vector2 rightKnockback = rightVelocity - commandedVelocities[rightIndex];
                    left.KnockbackFileVelocity = leftKnockback.x;
                    left.KnockbackRankVelocity = leftKnockback.y;
                    right.KnockbackFileVelocity = rightKnockback.x;
                    right.KnockbackRankVelocity = rightKnockback.y;
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
        float removalDistance = piece.PieceType == ChessPieceType.King
            ? 0f
            : ringOutDistance;
        return piece.BoardFile < boardMinimumEdge - removalDistance ||
               piece.BoardFile > boardMaximumEdge + removalDistance ||
               piece.BoardRank < boardMinimumEdge - removalDistance ||
               piece.BoardRank > boardMaximumEdge + removalDistance;
    }

    private void HandleRingOut(NetworkChessPieceState piece)
    {
        if (piece.PieceType == ChessPieceType.King &&
            !_isGameOver.Value)
        {
            _gameOverPresentationReady.Value = false;
            BeginKingDeathCinematicRpc(piece);
            _matchTimerRunning.Value = false;
            _winner.Value = GetOpponent(piece.OwnerTeam);
            _isGameOver.Value = true;
        }
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

        float duration = Mathf.Max(0.1f, kingDeathCinematicDuration);
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
            (kingDeathOutwardDistanceInSquares * squareSize * easedProgress) -
            boardUp *
            (kingDeathDropDistanceInSquares * squareSize * progress * progress);

        if (_kingDeathVisual != null)
        {
            Vector3 tiltAxis = Vector3.Cross(_kingDeathOutward, boardUp).normalized;
            Quaternion tilt = Quaternion.AngleAxis(
                kingDeathTiltAngle * easedProgress,
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
                (kingDeathCameraDistanceInSquares * squareSize) +
                boardUp *
                (kingDeathCameraHeightInSquares * squareSize);
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
                kingDeathCameraFieldOfView,
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

    private static float GetPieceMass(ChessPieceType pieceType)
    {
        return pieceType switch
        {
            ChessPieceType.Pawn => 0.8f,
            ChessPieceType.Knight => 1.05f,
            ChessPieceType.Bishop => 0.95f,
            ChessPieceType.Rook => 1.35f,
            ChessPieceType.Queen => 1.15f,
            ChessPieceType.King => 1.4f,
            _ => 1f
        };
    }

    /// <summary>
    /// Resolves a local player capsule against enemy pieces without applying the
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
        if (!IsSpawned ||
            playerTeam == PlayerTeam.Unassigned ||
            playerBottomHeight >= playerPieceCollisionHeight)
        {
            return false;
        }

        bool collided = false;
        float minimumDistance = pieceCollisionRadius + Mathf.Max(0.01f, playerRadius);
        float minimumDistanceSquared = minimumDistance * minimumDistance;

        for (int index = 0; index < _pieces.Count; index++)
        {
            NetworkChessPieceState piece = _pieces[index];

            // Friendly pieces are intentionally intangible to their own players.
            if (piece.OwnerTeam == playerTeam)
            {
                continue;
            }

            Vector2 piecePosition = new(piece.BoardFile, piece.BoardRank);
            Vector2 separation = playerPosition - piecePosition;
            float distanceSquared = separation.sqrMagnitude;

            if (distanceSquared >= minimumDistanceSquared)
            {
                continue;
            }

            float distance = Mathf.Sqrt(distanceSquared);
            Vector2 normal;

            if (distance > 0.0001f)
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
            if (pieceSpeed >= minimumPlayerImpactSpeed &&
                impactAlignment >= minimumPlayerImpactAlignment)
            {
                float pieceImpactSpeed = pieceSpeed * impactAlignment;
                float targetOutwardSpeed = pieceImpactSpeed *
                    (1f + collisionRestitution) *
                    collisionImpulseMultiplier;
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
        _localCommanderFile = commanderFile;
        _localCommanderRank = commanderRank;
        _localVoiceTargetPieceId = pieceId.HasValue ? pieceId.Value : -1;
        pieceSpawner?.SetVoiceSelectionTarget(pieceId);
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
        pieceId = 0;
        distanceInSquares = 0f;

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
            distance));

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

        if (command != PieceVoiceCommand.Stop &&
            _isGameOver.Value)
        {
            rejection = "게임이 이미 끝났습니다.";
            return false;
        }

        if (commandReachInSquares + 0.05f < targetDistanceInSquares)
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
            Mathf.Clamp01(commandLoudness));
        return true;
    }

    [Rpc(
        SendTo.Server,
        InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestVoiceCommandRpc(
        ushort pieceId,
        PieceVoiceCommand command,
        float commandLoudness,
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

        if (piece.OwnerTeam != player.Team ||
            (command != PieceVoiceCommand.Stop &&
             _isGameOver.Value))
        {
            return;
        }

        commandLoudness = Mathf.Clamp01(commandLoudness);

        switch (command)
        {
            case PieceVoiceCommand.MoveForward:
                piece.VoiceMoveAxis = 1;
                piece.VoiceMoveHeadingOffset = 0f;
                piece.VoiceMoveLoudness = commandLoudness;
                break;
            case PieceVoiceCommand.MoveBackward:
                piece.VoiceMoveAxis = 1;
                piece.VoiceMoveHeadingOffset = 180f;
                piece.VoiceMoveLoudness = commandLoudness;
                break;
            case PieceVoiceCommand.Stop:
                piece.VoiceMoveAxis = 0;
                piece.VoiceTurnAxis = 0;
                break;
            case PieceVoiceCommand.TurnLeft:
                piece.VoiceTurnAxis = -1;
                piece.VoiceTurnLoudness = commandLoudness;
                break;
            case PieceVoiceCommand.TurnRight:
                piece.VoiceTurnAxis = 1;
                piece.VoiceTurnLoudness = commandLoudness;
                break;
            case PieceVoiceCommand.MoveLeft:
                piece.VoiceMoveAxis = 1;
                piece.VoiceMoveHeadingOffset = -90f;
                piece.VoiceMoveLoudness = commandLoudness;
                break;
            case PieceVoiceCommand.MoveRight:
                piece.VoiceMoveAxis = 1;
                piece.VoiceMoveHeadingOffset = 90f;
                piece.VoiceMoveLoudness = commandLoudness;
                break;
            case PieceVoiceCommand.MoveUpperRight:
                piece.VoiceMoveAxis = 1;
                piece.VoiceMoveHeadingOffset = 45f;
                piece.VoiceMoveLoudness = commandLoudness;
                break;
            case PieceVoiceCommand.MoveUpperLeft:
                piece.VoiceMoveAxis = 1;
                piece.VoiceMoveHeadingOffset = -45f;
                piece.VoiceMoveLoudness = commandLoudness;
                break;
            case PieceVoiceCommand.MoveLowerRight:
                piece.VoiceMoveAxis = 1;
                piece.VoiceMoveHeadingOffset = 135f;
                piece.VoiceMoveLoudness = commandLoudness;
                break;
            case PieceVoiceCommand.MoveLowerLeft:
                piece.VoiceMoveAxis = 1;
                piece.VoiceMoveHeadingOffset = -135f;
                piece.VoiceMoveLoudness = commandLoudness;
                break;
            default:
                return;
        }

        _pieces[pieceIndex] = piece;
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
        _localVoiceGazeHistory.Clear();
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
