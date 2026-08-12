using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class NetworkChessGame
{
    private readonly struct PendingBoardKingRespawn
    {
        public readonly NetworkChessPieceState State;
        public readonly double EndServerTime;

        public PendingBoardKingRespawn(
            NetworkChessPieceState state,
            double endServerTime)
        {
            State = state;
            EndServerTime = endServerTime;
        }
    }

    private readonly struct CaptureTimerKey : IEquatable<CaptureTimerKey>
    {
        public readonly ushort ZoneIndex;
        public readonly ushort PieceId;

        public CaptureTimerKey(ushort zoneIndex, ushort pieceId)
        {
            ZoneIndex = zoneIndex;
            PieceId = pieceId;
        }

        public bool Equals(CaptureTimerKey other)
        {
            return ZoneIndex == other.ZoneIndex && PieceId == other.PieceId;
        }

        public override bool Equals(object obj)
        {
            return obj is CaptureTimerKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ZoneIndex, PieceId);
        }
    }

    private sealed class CaptureZoneVisual
    {
        public GameObject Root;
        public Mesh FillMesh;
        public Material FillMaterial;
        public LineRenderer Outline;
        public Material OutlineMaterial;
        public LineRenderer ProgressOutline;
        public Material ProgressMaterial;
        public bool IsRandomRoundVisual;
    }

    private readonly Dictionary<CaptureTimerKey, float> _capturePieceTimers = new();
    private readonly HashSet<CaptureTimerKey> _activeCaptureTimers = new();
    private readonly HashSet<ushort> _liveCapturePieces = new();
    private readonly List<CaptureTimerKey> _captureTimersToRemove = new();
    private readonly List<CaptureZoneVisual> _captureZoneVisuals = new();
    private readonly List<NetworkChessPieceState> _randomCapturePieces = new();
    private readonly Dictionary<ushort, PendingBoardKingRespawn>
        _pendingBoardKingRespawns = new();
    private readonly List<ushort> _completedBoardKingRespawns = new();
    private Transform _captureZoneVisualRoot;
    private System.Random _randomCaptureGenerator;
    private Vector2 _previousRandomCaptureCentre;
    private bool _hasPreviousRandomCaptureCentre;

    public bool IsCaptureModeEnabled =>
        gameMode != null && gameMode.CaptureMode.Enabled;
    public bool IsCaptureKingRespawnEnabled => IsCaptureModeEnabled &&
        gameMode.CaptureMode.RespawnEliminatedKings;
    public float CaptureKingRespawnDelaySeconds => gameMode != null
        ? gameMode.CaptureMode.KingRespawnDelaySeconds
        : 10f;
    public CaptureModeVersion ActiveCaptureModeVersion => gameMode != null
        ? gameMode.CaptureMode.Version
        : CaptureModeVersion.LegacyConfiguredZones;
    public CaptureScoringRule ActiveCaptureScoringRule => gameMode != null
        ? gameMode.CaptureMode.ScoringRule
        : CaptureScoringRule.PeriodicPerPiece;
    public int RandomCaptureScoreToWin => gameMode != null
        ? gameMode.CaptureMode.RandomRoundScoreToWin
        : 3;

    public bool TryGetLocalCaptureKingRespawnRemaining(out float remaining)
    {
        remaining = 0f;

        if (!IsCaptureKingRespawnEnabled ||
            NetworkManager == null ||
            gameMode == null)
        {
            return false;
        }

        NetworkPlayer localPlayer = NetworkPlayer.LocalPlayer;

        if (localPlayer == null ||
            (localPlayer.Team != PlayerTeam.White &&
             localPlayer.Team != PlayerTeam.Black))
        {
            return false;
        }

        bool waiting = false;

        if (gameMode.Victory.UsesPlayerCommander &&
            localPlayer.IsEliminated &&
            localPlayer.HasCaptureRespawnScheduled)
        {
            waiting = true;
            remaining = Mathf.Max(
                remaining,
                localPlayer.RemainingCaptureRespawnTime);
        }

        if (gameMode.Victory.UsesBoardKing)
        {
            double deadline = localPlayer.Team == PlayerTeam.White
                ? _whiteBoardKingRespawnEndServerTime.Value
                : _blackBoardKingRespawnEndServerTime.Value;

            if (deadline > 0d)
            {
                waiting = true;
                remaining = Mathf.Max(
                    remaining,
                    Mathf.Max(
                        0f,
                        (float)(deadline - NetworkManager.ServerTime.Time)));
            }
        }

        return waiting;
    }

    private void InitializeCaptureKingRespawns()
    {
        _pendingBoardKingRespawns.Clear();
        _completedBoardKingRespawns.Clear();
        _whiteBoardKingRespawnEndServerTime.Value = 0d;
        _blackBoardKingRespawnEndServerTime.Value = 0d;
    }

    private void ScheduleBoardKingRespawn(NetworkChessPieceState king)
    {
        if (NetworkManager == null)
        {
            return;
        }

        double deadline = NetworkManager.ServerTime.Time +
            CaptureKingRespawnDelaySeconds;
        _pendingBoardKingRespawns[king.Id] = new PendingBoardKingRespawn(
            king,
            deadline);
        SetBoardKingRespawnDeadline(king.OwnerTeam, deadline);
    }

    private void UpdateCaptureKingRespawns()
    {
        if (!IsCaptureKingRespawnEnabled || NetworkManager == null)
        {
            return;
        }

        double now = NetworkManager.ServerTime.Time;
        _completedBoardKingRespawns.Clear();

        foreach (KeyValuePair<ushort, PendingBoardKingRespawn> pair in
                 _pendingBoardKingRespawns)
        {
            if (now < pair.Value.EndServerTime)
            {
                continue;
            }

            NetworkChessPieceState king = pair.Value.State;
            Vector2 position = SelectRandomKingRespawnPosition(
                GetPieceSettings(ChessPieceType.King).CollisionRadius);
            king.BoardFile = position.x;
            king.BoardRank = position.y;
            king.VoiceHeading = NextRandomFloat() * 360f;
            king.VoiceMoveHeadingOffset = 0f;
            king.VoiceMoveAxis = 0;
            king.VoiceTurnAxis = 0;
            king.VoiceChargeDistanceRemaining = 0f;
            king.KnockbackFileVelocity = 0f;
            king.KnockbackRankVelocity = 0f;
            _pieces.Add(king);
            _completedBoardKingRespawns.Add(pair.Key);
        }

        foreach (ushort pieceId in _completedBoardKingRespawns)
        {
            PlayerTeam team = _pendingBoardKingRespawns[pieceId].State.OwnerTeam;
            _pendingBoardKingRespawns.Remove(pieceId);
            RefreshBoardKingRespawnDeadline(team);
        }

        foreach (NetworkPlayer player in NetworkPlayer.Players)
        {
            if (player == null ||
                !player.IsSpawned ||
                !player.IsServer ||
                !player.IsEliminated ||
                !player.HasCaptureRespawnScheduled ||
                player.RemainingCaptureRespawnTime > 0f)
            {
                continue;
            }

            float playerRadius = GetPlayerSettings()?.CollisionRadiusInSquares ??
                0.16f;
            Vector2 position = SelectRandomKingRespawnPosition(playerRadius);
            player.ServerRespawnForCapture(position, NextRandomFloat() * 360f);
        }
    }

    private void SetBoardKingRespawnDeadline(PlayerTeam team, double deadline)
    {
        if (team == PlayerTeam.White)
        {
            _whiteBoardKingRespawnEndServerTime.Value = deadline;
        }
        else if (team == PlayerTeam.Black)
        {
            _blackBoardKingRespawnEndServerTime.Value = deadline;
        }
    }

    private void RefreshBoardKingRespawnDeadline(PlayerTeam team)
    {
        double latest = 0d;

        foreach (PendingBoardKingRespawn pending in
                 _pendingBoardKingRespawns.Values)
        {
            if (pending.State.OwnerTeam == team)
            {
                latest = Math.Max(latest, pending.EndServerTime);
            }
        }

        SetBoardKingRespawnDeadline(team, latest);
    }

    private Vector2 SelectRandomKingRespawnPosition(float respawningRadius)
    {
        CaptureModeSettings settings = gameMode.CaptureMode;
        float padding = settings.KingRespawnEdgePaddingInSquares;
        float minimum = padding;
        float maximum = 7f - padding;
        Vector2 bestCandidate = new(3.5f, 3.5f);
        float bestClearance = float.NegativeInfinity;

        for (int attempt = 0; attempt < 32; attempt++)
        {
            Vector2 candidate = new(
                Mathf.Lerp(minimum, maximum, NextRandomFloat()),
                Mathf.Lerp(minimum, maximum, NextRandomFloat()));
            float clearance = GetKingRespawnClearance(
                candidate,
                respawningRadius,
                settings.KingRespawnClearanceInSquares);

            if (clearance > bestClearance)
            {
                bestCandidate = candidate;
                bestClearance = clearance;
            }

            if (clearance >= 0f)
            {
                return candidate;
            }
        }

        return bestCandidate;
    }

    private float GetKingRespawnClearance(
        Vector2 candidate,
        float respawningRadius,
        float extraClearance)
    {
        float minimumClearance = float.PositiveInfinity;

        for (int index = 0; index < _pieces.Count; index++)
        {
            NetworkChessPieceState piece = _pieces[index];
            float occupiedRadius = GetPieceSettings(piece.PieceType).CollisionRadius;
            float clearance = Vector2.Distance(
                candidate,
                new Vector2(piece.BoardFile, piece.BoardRank)) -
                respawningRadius - occupiedRadius - extraClearance;
            minimumClearance = Mathf.Min(minimumClearance, clearance);
        }

        foreach (NetworkPlayer player in NetworkPlayer.Players)
        {
            if (player == null || !player.IsSpawned || player.IsEliminated)
            {
                continue;
            }

            Vector3 pose = player.AvatarBoardPose;
            float playerRadius = GetPlayerSettings()?.CollisionRadiusInSquares ??
                0.16f;
            float clearance = Vector2.Distance(
                candidate,
                new Vector2(pose.x, pose.z)) -
                respawningRadius - playerRadius - extraClearance;
            minimumClearance = Mathf.Min(minimumClearance, clearance);
        }

        return float.IsPositiveInfinity(minimumClearance)
            ? 0f
            : minimumClearance;
    }

    private void InitializeCaptureZones()
    {
        _capturePieceTimers.Clear();
        _activeCaptureTimers.Clear();
        _liveCapturePieces.Clear();
        _captureZoneStates.Clear();
        _randomCaptureRoundActive.Value = false;
        _randomCaptureRoundStartServerTime.Value = 0d;
        _randomCaptureRoundEndServerTime.Value = 0d;
        _randomCaptureNextRoundServerTime.Value = 0d;
        _randomCaptureRoundNumber.Value = 0u;
        _hasPreviousRandomCaptureCentre = false;

        if (!IsCaptureModeEnabled)
        {
            return;
        }

        if (gameMode.CaptureMode.Version ==
            CaptureModeVersion.RandomRoundControl)
        {
            int configuredSeed = gameMode.CaptureMode.RandomSeed;
            int seed = configuredSeed != 0
                ? configuredSeed
                : unchecked(Environment.TickCount ^ GetInstanceID());
            _randomCaptureGenerator = new System.Random(seed);
            _captureZoneStates.Add(new CaptureZoneNetworkState(0));
            StartRandomCaptureRound();
            return;
        }

        IReadOnlyList<CaptureZoneSettings> zones = gameMode.CaptureMode.Zones;

        for (int index = 0; index < zones.Count; index++)
        {
            CaptureZoneSettings settings = zones[index];

            if (settings != null && settings.Enabled)
            {
                _captureZoneStates.Add(new CaptureZoneNetworkState((ushort)index));
            }
        }
    }

    private void UpdateCaptureZones(float deltaTime)
    {
        if (!IsCaptureModeEnabled || _captureZoneStates.Count == 0)
        {
            return;
        }

        if (gameMode.CaptureMode.Version ==
            CaptureModeVersion.RandomRoundControl)
        {
            UpdateRandomCaptureRound();
            return;
        }

        CaptureModeSettings captureMode = gameMode.CaptureMode;
        bool periodicScoring =
            captureMode.ScoringRule == CaptureScoringRule.PeriodicPerPiece;
        _activeCaptureTimers.Clear();
        _liveCapturePieces.Clear();
        float whitePeriodicAward = 0f;
        float blackPeriodicAward = 0f;
        float whiteFinalValue = 0f;
        float blackFinalValue = 0f;

        for (int pieceIndex = 0; pieceIndex < _pieces.Count; pieceIndex++)
        {
            _liveCapturePieces.Add(_pieces[pieceIndex].Id);
        }

        for (int stateIndex = 0; stateIndex < _captureZoneStates.Count; stateIndex++)
        {
            CaptureZoneNetworkState previousState = _captureZoneStates[stateIndex];

            if (previousState.Index >= captureMode.Zones.Count)
            {
                continue;
            }

            CaptureZoneSettings zone = captureMode.Zones[previousState.Index];

            if (zone == null || !zone.Enabled)
            {
                continue;
            }

            int whiteCount = 0;
            int blackCount = 0;
            float zoneWhiteValue = 0f;
            float zoneBlackValue = 0f;

            for (int pieceIndex = 0; pieceIndex < _pieces.Count; pieceIndex++)
            {
                NetworkChessPieceState piece = _pieces[pieceIndex];

                if (!IsPieceInsideCaptureZone(piece, zone))
                {
                    continue;
                }

                PieceArchetypeSettings pieceSettings = GetPieceSettings(
                    piece.PieceType);

                if (piece.OwnerTeam == PlayerTeam.White)
                {
                    whiteCount++;
                    zoneWhiteValue += pieceSettings.FinalCaptureValue;
                }
                else if (piece.OwnerTeam == PlayerTeam.Black)
                {
                    blackCount++;
                    zoneBlackValue += pieceSettings.FinalCaptureValue;
                }

                if (!periodicScoring || pieceSettings.PeriodicCapturePoints <= 0f)
                {
                    continue;
                }

                CaptureTimerKey timerKey = new(previousState.Index, piece.Id);
                _activeCaptureTimers.Add(timerKey);
                _capturePieceTimers.TryGetValue(timerKey, out float elapsed);
                elapsed += Mathf.Max(0f, deltaTime);
                float interval = pieceSettings.PeriodicCaptureIntervalSeconds;
                int completedIntervals = Mathf.FloorToInt(elapsed / interval);

                if (completedIntervals > 0)
                {
                    float award = completedIntervals *
                        pieceSettings.PeriodicCapturePoints;
                    elapsed -= completedIntervals * interval;

                    if (piece.OwnerTeam == PlayerTeam.White)
                    {
                        whitePeriodicAward += award;
                    }
                    else if (piece.OwnerTeam == PlayerTeam.Black)
                    {
                        blackPeriodicAward += award;
                    }
                }

                _capturePieceTimers[timerKey] = elapsed;
            }

            CaptureZoneNetworkState newState = new(previousState.Index)
            {
                WhitePieceCount = (ushort)Mathf.Min(ushort.MaxValue, whiteCount),
                BlackPieceCount = (ushort)Mathf.Min(ushort.MaxValue, blackCount),
                WhiteOccupancyValue = zoneWhiteValue,
                BlackOccupancyValue = zoneBlackValue
            };

            if (!previousState.Equals(newState))
            {
                _captureZoneStates[stateIndex] = newState;
            }

            whiteFinalValue += zoneWhiteValue;
            blackFinalValue += zoneBlackValue;
        }

        if (periodicScoring)
        {
            if (whitePeriodicAward > 0f)
            {
                _whiteCaptureScore.Value += whitePeriodicAward;
            }

            if (blackPeriodicAward > 0f)
            {
                _blackCaptureScore.Value += blackPeriodicAward;
            }
        }
        else
        {
            SetCaptureScore(PlayerTeam.White, whiteFinalValue);
            SetCaptureScore(PlayerTeam.Black, blackFinalValue);
        }

        CleanupCapturePieceTimers(captureMode.ResetPeriodicTimerWhenLeaving);
    }

    private void UpdateRandomCaptureRound()
    {
        if (NetworkManager == null || _isGameOver.Value)
        {
            return;
        }

        double now = NetworkManager.ServerTime.Time;

        if (!_randomCaptureRoundActive.Value)
        {
            if (now >= _randomCaptureNextRoundServerTime.Value)
            {
                StartRandomCaptureRound();
            }

            return;
        }

        _randomCapturePieces.Clear();

        for (int pieceIndex = 0; pieceIndex < _pieces.Count; pieceIndex++)
        {
            _randomCapturePieces.Add(_pieces[pieceIndex]);
        }

        PlayerTeam leadingTeam = EvaluateRandomCaptureRound(
            _randomCapturePieces,
            RandomCaptureZoneBoardPosition,
            _randomCaptureZoneRadius.Value,
            gameMode.CaptureMode.RandomDistanceTieToleranceInSquares,
            out int whiteCount,
            out int blackCount,
            out float whiteDistance,
            out float blackDistance);
        SetRandomCaptureZoneState(
            whiteCount,
            blackCount,
            whiteDistance,
            blackDistance);

        if (now < _randomCaptureRoundEndServerTime.Value)
        {
            return;
        }

        _randomCaptureRoundActive.Value = false;

        if (leadingTeam == PlayerTeam.White)
        {
            _whiteCaptureScore.Value += 1f;
        }
        else if (leadingTeam == PlayerTeam.Black)
        {
            _blackCaptureScore.Value += 1f;
        }

        if (leadingTeam != PlayerTeam.Unassigned &&
            GetCaptureScore(leadingTeam) >=
            gameMode.CaptureMode.RandomRoundScoreToWin)
        {
            EndMatch(leadingTeam, MatchEndReason.CaptureScoreReached);
            return;
        }

        _randomCaptureNextRoundServerTime.Value = now +
            gameMode.CaptureMode.RandomRoundIntervalSeconds;
    }

    private void StartRandomCaptureRound()
    {
        if (NetworkManager == null || !IsCaptureModeEnabled)
        {
            return;
        }

        CaptureModeSettings settings = gameMode.CaptureMode;
        float radius = SelectRandomCaptureRadius(settings);
        Vector2 centre = SelectRandomCaptureCentre(settings, radius);
        double now = NetworkManager.ServerTime.Time;

        _previousRandomCaptureCentre = centre;
        _hasPreviousRandomCaptureCentre = true;
        _randomCaptureZoneFile.Value = centre.x;
        _randomCaptureZoneRank.Value = centre.y;
        _randomCaptureZoneRadius.Value = radius;
        _randomCaptureRoundStartServerTime.Value = now;
        _randomCaptureRoundEndServerTime.Value =
            now + settings.RandomRoundDurationSeconds;
        _randomCaptureNextRoundServerTime.Value = 0d;
        _randomCaptureRoundNumber.Value++;
        _randomCaptureRoundActive.Value = true;
        SetRandomCaptureZoneState(0, 0, 0f, 0f);
    }

    private float SelectRandomCaptureRadius(CaptureModeSettings settings)
    {
        GetRandomCaptureBoardBounds(out float boardMinimum, out float boardMaximum);
        float maximumRadiusThatFits = Mathf.Max(
            0.05f,
            (boardMaximum - boardMinimum) * 0.5f -
            settings.RandomPositionPaddingInSquares);
        float minimum = Mathf.Min(
            settings.RandomRadiusMinimumInSquares,
            maximumRadiusThatFits);
        float maximum = Mathf.Min(
            settings.RandomRadiusMaximumInSquares,
            maximumRadiusThatFits);
        return Mathf.Lerp(minimum, maximum, NextRandomFloat());
    }

    private Vector2 SelectRandomCaptureCentre(
        CaptureModeSettings settings,
        float radius)
    {
        GetRandomCaptureBoardBounds(out float boardMinimum, out float boardMaximum);
        float edgeClearance = settings.RandomPositionPaddingInSquares +
            (settings.RandomKeepEntireCircleInsideBoard ? radius : 0f);
        float minimum = boardMinimum + edgeClearance;
        float maximum = boardMaximum - edgeClearance;

        if (minimum > maximum)
        {
            float middle = (boardMinimum + boardMaximum) * 0.5f;
            minimum = middle;
            maximum = middle;
        }

        Vector2 candidate = new(
            Mathf.Lerp(minimum, maximum, NextRandomFloat()),
            Mathf.Lerp(minimum, maximum, NextRandomFloat()));
        float minimumDistance = settings.RandomMinimumCentreDistanceInSquares;

        if (!_hasPreviousRandomCaptureCentre || minimumDistance <= 0f)
        {
            return candidate;
        }

        for (int attempt = 0; attempt < 15; attempt++)
        {
            if ((candidate - _previousRandomCaptureCentre).sqrMagnitude >=
                minimumDistance * minimumDistance)
            {
                return candidate;
            }

            candidate = new Vector2(
                Mathf.Lerp(minimum, maximum, NextRandomFloat()),
                Mathf.Lerp(minimum, maximum, NextRandomFloat()));
        }

        return candidate;
    }

    private void GetRandomCaptureBoardBounds(
        out float boardMinimum,
        out float boardMaximum)
    {
        const float standardBoardMinimum = -0.5f;
        const float standardBoardMaximum = 7.5f;
        boardMinimum = pieceSpawner != null
            ? Mathf.Max(standardBoardMinimum, pieceSpawner.GroundMinimumCoordinate)
            : standardBoardMinimum;
        boardMaximum = pieceSpawner != null
            ? Mathf.Min(standardBoardMaximum, pieceSpawner.GroundMaximumCoordinate)
            : standardBoardMaximum;
    }

    private float NextRandomFloat()
    {
        _randomCaptureGenerator ??= new System.Random(
            unchecked(Environment.TickCount ^ GetInstanceID()));
        return (float)_randomCaptureGenerator.NextDouble();
    }

    private void SetRandomCaptureZoneState(
        int whiteCount,
        int blackCount,
        float whiteDistance,
        float blackDistance)
    {
        if (_captureZoneStates.Count == 0)
        {
            return;
        }

        CaptureZoneNetworkState previousState = _captureZoneStates[0];
        CaptureZoneNetworkState newState = new(0)
        {
            WhitePieceCount = (ushort)Mathf.Min(ushort.MaxValue, whiteCount),
            BlackPieceCount = (ushort)Mathf.Min(ushort.MaxValue, blackCount),
            WhiteOccupancyValue = whiteDistance,
            BlackOccupancyValue = blackDistance
        };

        if (!previousState.Equals(newState))
        {
            _captureZoneStates[0] = newState;
        }
    }

    public static PlayerTeam EvaluateRandomCaptureRound(
        IEnumerable<NetworkChessPieceState> pieces,
        Vector2 centre,
        float radius,
        float distanceTieTolerance,
        out int whiteCount,
        out int blackCount,
        out float whiteDistanceSum,
        out float blackDistanceSum)
    {
        whiteCount = 0;
        blackCount = 0;
        whiteDistanceSum = 0f;
        blackDistanceSum = 0f;
        float radiusSquared = Mathf.Max(0f, radius) * Mathf.Max(0f, radius);

        if (pieces != null)
        {
            foreach (NetworkChessPieceState piece in pieces)
            {
                Vector2 offset = new(
                    piece.BoardFile - centre.x,
                    piece.BoardRank - centre.y);

                if (offset.sqrMagnitude > radiusSquared)
                {
                    continue;
                }

                float distance = offset.magnitude;

                if (piece.OwnerTeam == PlayerTeam.White)
                {
                    whiteCount++;
                    whiteDistanceSum += distance;
                }
                else if (piece.OwnerTeam == PlayerTeam.Black)
                {
                    blackCount++;
                    blackDistanceSum += distance;
                }
            }
        }

        if (whiteCount != blackCount)
        {
            return whiteCount > blackCount
                ? PlayerTeam.White
                : PlayerTeam.Black;
        }

        if (whiteCount == 0 || Mathf.Abs(whiteDistanceSum - blackDistanceSum) <=
            Mathf.Max(0f, distanceTieTolerance))
        {
            return PlayerTeam.Unassigned;
        }

        return whiteDistanceSum < blackDistanceSum
            ? PlayerTeam.White
            : PlayerTeam.Black;
    }

    private static bool IsPieceInsideCaptureZone(
        NetworkChessPieceState piece,
        CaptureZoneSettings zone)
    {
        Vector2 position = new(piece.BoardFile, piece.BoardRank);
        return (position - zone.BoardPosition).sqrMagnitude <=
               zone.RadiusInSquares * zone.RadiusInSquares;
    }

    private void CleanupCapturePieceTimers(bool resetWhenLeaving)
    {
        _captureTimersToRemove.Clear();

        foreach (KeyValuePair<CaptureTimerKey, float> pair in _capturePieceTimers)
        {
            bool shouldRemove = !_liveCapturePieces.Contains(pair.Key.PieceId) ||
                (resetWhenLeaving && !_activeCaptureTimers.Contains(pair.Key));

            if (shouldRemove)
            {
                _captureTimersToRemove.Add(pair.Key);
            }
        }

        foreach (CaptureTimerKey key in _captureTimersToRemove)
        {
            _capturePieceTimers.Remove(key);
        }
    }

    private void SetCaptureScore(PlayerTeam team, float value)
    {
        value = Mathf.Max(0f, value);

        if (team == PlayerTeam.White &&
            !Mathf.Approximately(_whiteCaptureScore.Value, value))
        {
            _whiteCaptureScore.Value = value;
        }
        else if (team == PlayerTeam.Black &&
                 !Mathf.Approximately(_blackCaptureScore.Value, value))
        {
            _blackCaptureScore.Value = value;
        }
    }

    private void RefreshFinalOccupancyScores()
    {
        if (IsCaptureModeEnabled &&
            gameMode.CaptureMode.Version ==
            CaptureModeVersion.LegacyConfiguredZones &&
            gameMode.CaptureMode.ScoringRule ==
            CaptureScoringRule.FinalOccupancyValue)
        {
            UpdateCaptureZones(0f);
        }
    }

    private void RebuildCaptureZoneVisuals()
    {
        CleanupCaptureZoneVisuals();

        if (!IsCaptureModeEnabled || pieceSpawner == null)
        {
            return;
        }

        GameObject rootObject = new("Capture Zone Visuals");
        rootObject.transform.SetParent(transform, worldPositionStays: false);
        _captureZoneVisualRoot = rootObject.transform;

        if (gameMode.CaptureMode.Version ==
            CaptureModeVersion.RandomRoundControl)
        {
            _captureZoneVisuals.Add(CreateRandomCaptureZoneVisual());
            UpdateRandomCaptureZoneVisual();
            return;
        }

        IReadOnlyList<CaptureZoneSettings> zones = gameMode.CaptureMode.Zones;

        for (int index = 0; index < zones.Count; index++)
        {
            CaptureZoneSettings zone = zones[index];

            if (zone != null && zone.Enabled)
            {
                _captureZoneVisuals.Add(CreateCaptureZoneVisual(zone, index));
            }
        }
    }

    private CaptureZoneVisual CreateCaptureZoneVisual(
        CaptureZoneSettings zone,
        int index)
    {
        float squareSize = Mathf.Min(pieceSpawner.FileSpacing, pieceSpawner.RankSpacing);
        float radius = zone.RadiusInSquares * squareSize;
        GameObject root = new($"Capture Zone {index + 1:00} - {zone.DisplayName}");
        root.transform.SetParent(_captureZoneVisualRoot, worldPositionStays: true);
        root.transform.SetPositionAndRotation(
            pieceSpawner.GetBoardWorldPosition(
                zone.BoardPosition.x,
                zone.BoardPosition.y) +
            pieceSpawner.BoardUp * (zone.HeightOffsetInSquares * squareSize),
            Quaternion.LookRotation(pieceSpawner.BoardForward, pieceSpawner.BoardUp));

        CaptureZoneVisual visual = new() { Root = root };
        Shader shader = Shader.Find("Sprites/Default");

        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (zone.ShowFilledCircle && shader != null)
        {
            GameObject fill = new("Fill");
            fill.transform.SetParent(root.transform, worldPositionStays: false);
            MeshFilter meshFilter = fill.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = fill.AddComponent<MeshRenderer>();
            visual.FillMesh = CreateCaptureDiscMesh(radius, zone.CircleSegments);
            visual.FillMaterial = new Material(shader)
            {
                name = $"{zone.DisplayName} Capture Fill",
                color = zone.FillColor
            };
            meshFilter.sharedMesh = visual.FillMesh;
            meshRenderer.sharedMaterial = visual.FillMaterial;
            meshRenderer.sortingOrder = -20;
        }

        GameObject outline = new("Outline");
        outline.transform.SetParent(root.transform, worldPositionStays: false);
        LineRenderer line = outline.AddComponent<LineRenderer>();
        visual.Outline = line;
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = zone.CircleSegments;
        line.startWidth = zone.OutlineWidthInSquares * squareSize;
        line.endWidth = line.startWidth;
        line.startColor = zone.OutlineColor;
        line.endColor = zone.OutlineColor;
        line.numCornerVertices = 4;
        line.numCapVertices = 4;

        if (shader != null)
        {
            visual.OutlineMaterial = new Material(shader)
            {
                name = $"{zone.DisplayName} Capture Outline",
                color = zone.OutlineColor
            };
            line.sharedMaterial = visual.OutlineMaterial;
        }

        for (int segment = 0; segment < zone.CircleSegments; segment++)
        {
            float angle = segment * Mathf.PI * 2f / zone.CircleSegments;
            line.SetPosition(
                segment,
                new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }

        return visual;
    }

    private CaptureZoneVisual CreateRandomCaptureZoneVisual()
    {
        CaptureModeSettings settings = gameMode.CaptureMode;
        GameObject root = new("Random Capture Round Preview");
        root.transform.SetParent(_captureZoneVisualRoot, worldPositionStays: true);
        root.transform.rotation = Quaternion.LookRotation(
            pieceSpawner.BoardForward,
            pieceSpawner.BoardUp);
        CaptureZoneVisual visual = new()
        {
            Root = root,
            IsRandomRoundVisual = true
        };
        Shader shader = Shader.Find("Sprites/Default");

        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (settings.RandomShowFilledCircle && shader != null)
        {
            GameObject fill = new("Preview Fill");
            fill.transform.SetParent(root.transform, worldPositionStays: false);
            MeshFilter meshFilter = fill.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = fill.AddComponent<MeshRenderer>();
            visual.FillMesh = CreateCaptureDiscMesh(
                1f,
                settings.RandomCircleSegments);
            visual.FillMaterial = new Material(shader)
            {
                name = "Random Capture Preview Fill",
                color = settings.RandomFillColor
            };
            meshFilter.sharedMesh = visual.FillMesh;
            meshRenderer.sharedMaterial = visual.FillMaterial;
            meshRenderer.sortingOrder = -20;
        }

        GameObject faintOutline = new("Faint Full Outline");
        faintOutline.transform.SetParent(root.transform, worldPositionStays: false);
        visual.Outline = faintOutline.AddComponent<LineRenderer>();
        ConfigureRandomCaptureLine(
            visual.Outline,
            settings.RandomFaintOutlineColor,
            loop: true);

        if (shader != null)
        {
            visual.OutlineMaterial = new Material(shader)
            {
                name = "Random Capture Faint Outline",
                color = Color.white
            };
            visual.Outline.sharedMaterial = visual.OutlineMaterial;
        }

        int segments = settings.RandomCircleSegments;

        for (int segment = 0; segment < segments; segment++)
        {
            float angle = segment * Mathf.PI * 2f / segments;
            visual.Outline.SetPosition(
                segment,
                new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
        }

        GameObject progressOutline = new("Loading Progress Outline");
        progressOutline.transform.SetParent(root.transform, worldPositionStays: false);
        visual.ProgressOutline = progressOutline.AddComponent<LineRenderer>();
        ConfigureRandomCaptureLine(
            visual.ProgressOutline,
            settings.RandomProgressOutlineColor,
            loop: false);
        visual.ProgressOutline.positionCount = segments + 1;

        if (shader != null)
        {
            visual.ProgressMaterial = new Material(shader)
            {
                name = "Random Capture Progress Outline",
                color = Color.white
            };
            visual.ProgressOutline.sharedMaterial = visual.ProgressMaterial;
        }

        return visual;
    }

    private void ConfigureRandomCaptureLine(
        LineRenderer line,
        Color color,
        bool loop)
    {
        CaptureModeSettings settings = gameMode.CaptureMode;
        line.useWorldSpace = false;
        line.loop = loop;
        line.positionCount = settings.RandomCircleSegments;
        line.startColor = color;
        line.endColor = color;
        line.numCornerVertices = 4;
        line.numCapVertices = 4;
    }

    private void UpdateRandomCaptureZoneVisual()
    {
        if (ActiveCaptureModeVersion != CaptureModeVersion.RandomRoundControl ||
            _captureZoneVisuals.Count == 0 ||
            pieceSpawner == null)
        {
            return;
        }

        CaptureZoneVisual visual = _captureZoneVisuals[0];

        if (visual == null || !visual.IsRandomRoundVisual || visual.Root == null)
        {
            return;
        }

        bool visible = IsCaptureModeEnabled &&
            IsRandomCaptureRoundActive &&
            !_isGameOver.Value;
        visual.Root.SetActive(visible);

        if (!visible)
        {
            return;
        }

        CaptureModeSettings settings = gameMode.CaptureMode;
        float squareSize = Mathf.Min(pieceSpawner.FileSpacing, pieceSpawner.RankSpacing);
        float radiusInSquares = Mathf.Max(0.05f, RandomCaptureZoneRadius);
        float worldRadius = radiusInSquares * squareSize;
        visual.Root.transform.SetPositionAndRotation(
            pieceSpawner.GetBoardWorldPosition(
                RandomCaptureZoneBoardPosition.x,
                RandomCaptureZoneBoardPosition.y) +
            pieceSpawner.BoardUp *
            (settings.RandomHeightOffsetInSquares * squareSize),
            Quaternion.LookRotation(pieceSpawner.BoardForward, pieceSpawner.BoardUp));
        visual.Root.transform.localScale = Vector3.one * worldRadius;

        float localWidth = settings.RandomOutlineWidthInSquares /
            radiusInSquares;

        if (visual.Outline != null)
        {
            visual.Outline.startWidth = localWidth;
            visual.Outline.endWidth = localWidth;
        }

        if (visual.ProgressOutline == null)
        {
            return;
        }

        visual.ProgressOutline.startWidth = localWidth;
        visual.ProgressOutline.endWidth = localWidth;
        float progress = RandomCaptureRoundProgress;
        visual.ProgressOutline.enabled = progress > 0.0001f;

        if (!visual.ProgressOutline.enabled)
        {
            return;
        }

        int segments = settings.RandomCircleSegments;
        visual.ProgressOutline.positionCount = segments + 1;
        float startAngle = settings.RandomProgressStartAngleDegrees * Mathf.Deg2Rad;

        for (int segment = 0; segment <= segments; segment++)
        {
            float normalized = segment / (float)segments;
            float angle = startAngle + normalized * progress * Mathf.PI * 2f;
            visual.ProgressOutline.SetPosition(
                segment,
                new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
        }
    }

    private static Mesh CreateCaptureDiscMesh(float radius, int segments)
    {
        Mesh mesh = new() { name = "Capture Zone Disc" };
        Vector3[] vertices = new Vector3[segments + 1];
        int[] triangles = new int[segments * 3];
        vertices[0] = Vector3.zero;

        for (int segment = 0; segment < segments; segment++)
        {
            float angle = segment * Mathf.PI * 2f / segments;
            vertices[segment + 1] = new Vector3(
                Mathf.Cos(angle) * radius,
                0f,
                Mathf.Sin(angle) * radius);
            int triangle = segment * 3;
            triangles[triangle] = 0;
            triangles[triangle + 1] = (segment + 1) % segments + 1;
            triangles[triangle + 2] = segment + 1;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void CleanupCaptureZoneVisuals()
    {
        foreach (CaptureZoneVisual visual in _captureZoneVisuals)
        {
            if (visual.FillMesh != null)
            {
                Destroy(visual.FillMesh);
            }

            if (visual.FillMaterial != null)
            {
                Destroy(visual.FillMaterial);
            }

            if (visual.OutlineMaterial != null)
            {
                Destroy(visual.OutlineMaterial);
            }

            if (visual.ProgressMaterial != null)
            {
                Destroy(visual.ProgressMaterial);
            }
        }

        _captureZoneVisuals.Clear();

        if (_captureZoneVisualRoot != null)
        {
            Destroy(_captureZoneVisualRoot.gameObject);
            _captureZoneVisualRoot = null;
        }
    }

    private void OnDrawGizmos()
    {
        if (!IsCaptureModeEnabled)
        {
            return;
        }

        if (gameMode.CaptureMode.Version ==
            CaptureModeVersion.RandomRoundControl)
        {
            return;
        }

        ChessPieceSpawner spawner = pieceSpawner;

        if (spawner == null)
        {
            spawner = FindFirstObjectByType<ChessPieceSpawner>();
        }

        if (spawner == null)
        {
            return;
        }

        float squareSize = Mathf.Min(spawner.FileSpacing, spawner.RankSpacing);

        foreach (CaptureZoneSettings zone in gameMode.CaptureMode.Zones)
        {
            if (zone == null || !zone.Enabled)
            {
                continue;
            }

            Gizmos.color = zone.OutlineColor;
            Vector3 centre = spawner.GetBoardWorldPosition(
                zone.BoardPosition.x,
                zone.BoardPosition.y) +
                spawner.BoardUp * (zone.HeightOffsetInSquares * squareSize);
            Vector3 previous = centre +
                spawner.BoardRight * (zone.RadiusInSquares * squareSize);

            for (int segment = 1; segment <= zone.CircleSegments; segment++)
            {
                float angle = segment * Mathf.PI * 2f / zone.CircleSegments;
                Vector3 next = centre +
                    spawner.BoardRight *
                    (Mathf.Cos(angle) * zone.RadiusInSquares * squareSize) +
                    spawner.BoardForward *
                    (Mathf.Sin(angle) * zone.RadiusInSquares * squareSize);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
