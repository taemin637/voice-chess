using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public struct CaptureZoneNetworkState :
    INetworkSerializable,
    IEquatable<CaptureZoneNetworkState>
{
    public ushort Index;
    public ushort WhitePieceCount;
    public ushort BlackPieceCount;
    public float WhiteOccupancyValue;
    public float BlackOccupancyValue;

    public CaptureZoneNetworkState(ushort index)
    {
        Index = index;
        WhitePieceCount = 0;
        BlackPieceCount = 0;
        WhiteOccupancyValue = 0f;
        BlackOccupancyValue = 0f;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref Index);
        serializer.SerializeValue(ref WhitePieceCount);
        serializer.SerializeValue(ref BlackPieceCount);
        serializer.SerializeValue(ref WhiteOccupancyValue);
        serializer.SerializeValue(ref BlackOccupancyValue);
    }

    public bool Equals(CaptureZoneNetworkState other)
    {
        return Index == other.Index &&
               WhitePieceCount == other.WhitePieceCount &&
               BlackPieceCount == other.BlackPieceCount &&
               WhiteOccupancyValue.Equals(other.WhiteOccupancyValue) &&
               BlackOccupancyValue.Equals(other.BlackOccupancyValue);
    }
}

public sealed partial class NetworkChessGame
{
    private readonly struct AbilityCooldownKey : IEquatable<AbilityCooldownKey>
    {
        public readonly ushort PieceId;
        public readonly int AbilityId;

        public AbilityCooldownKey(ushort pieceId, int abilityId)
        {
            PieceId = pieceId;
            AbilityId = abilityId;
        }

        public bool Equals(AbilityCooldownKey other)
        {
            return PieceId == other.PieceId && AbilityId == other.AbilityId;
        }

        public override bool Equals(object obj)
        {
            return obj is AbilityCooldownKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(PieceId, AbilityId);
        }
    }

    [Header("확장형 게임 모드")]
    [Tooltip("Central inspector asset for clock, turns/cost, victory rules, roster, capture zones, player settings and presentation.")]
    [SerializeField] private GameModeConfiguration gameMode;

    private readonly NetworkVariable<PlayerTeam> _activeCommandTeam = new(
        PlayerTeam.White,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<uint> _turnNumber = new(
        1u,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<double> _turnEndServerTime = new(
        0d,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> _whiteCommandPoints = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> _blackCommandPoints = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> _whiteCaptureScore = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> _blackCaptureScore = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> _randomCaptureRoundActive = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> _randomCaptureZoneFile = new(
        3.5f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> _randomCaptureZoneRank = new(
        3.5f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> _randomCaptureZoneRadius = new(
        1f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<double> _randomCaptureRoundStartServerTime = new(
        0d,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<double> _randomCaptureRoundEndServerTime = new(
        0d,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<double> _randomCaptureNextRoundServerTime = new(
        0d,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<uint> _randomCaptureRoundNumber = new(
        0u,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<double> _whiteBoardKingRespawnEndServerTime = new(
        0d,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<double> _blackBoardKingRespawnEndServerTime = new(
        0d,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<MatchEndReason> _matchEndReason = new(
        MatchEndReason.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkList<CaptureZoneNetworkState> _captureZoneStates = new(
        readPerm: NetworkVariableReadPermission.Everyone,
        writePerm: NetworkVariableWritePermission.Server);
    private readonly Dictionary<AbilityCooldownKey, double> _abilityReadyTimes = new();
    private readonly Dictionary<ChessPieceType, PieceArchetypeSettings>
        _fallbackPieceSettings = new();
    private float _commandCostRechargeElapsed;

    public GameModeConfiguration GameMode => gameMode;
    public bool IsMatchClockEnabled => gameMode != null
        ? gameMode.Clock.IsEnabled
        : true;
    public CommandIssuingMode CommandMode => gameMode != null
        ? gameMode.Commands.Mode
        : CommandIssuingMode.RealTime;
    public VoiceCommandVersion ActiveVoiceCommandVersion => gameMode != null
        ? gameMode.Commands.VoiceCommandVersion
        : VoiceCommandVersion.LegacyLookSelection;
    public bool UsesChargeSelectionCommand =>
        ActiveVoiceCommandVersion == VoiceCommandVersion.ConfirmedSelectionCharge ||
        ActiveVoiceCommandVersion == VoiceCommandVersion.ProximityAutoSelectionCharge;
    public bool UsesManualConfirmedSelection =>
        ActiveVoiceCommandVersion == VoiceCommandVersion.ConfirmedSelectionCharge;
    public bool UsesProximityAutoSelection =>
        ActiveVoiceCommandVersion == VoiceCommandVersion.ProximityAutoSelectionCharge;
    public CostConsumptionVersion ActiveCostConsumptionVersion => gameMode != null
        ? gameMode.Commands.CostConsumptionVersion
        : CostConsumptionVersion.FixedPerCommand;
    public bool IsCostSystemEnabled => gameMode != null &&
        gameMode.Commands.CostSystemEnabled;
    public bool IsCommandCooldownEnabled => gameMode != null &&
        gameMode.Commands.CooldownSystemEnabled;
    public bool IsPieceMovementCooldownEnabled => gameMode != null &&
        gameMode.Commands.PieceMovementCooldownEnabled;
    public float PieceMovementCooldownDuration => gameMode != null
        ? gameMode.Commands.PieceMovementCooldownSeconds
        : 0f;
    public bool HasCommandRestriction =>
        IsCostSystemEnabled || IsCommandCooldownEnabled;
    public float CommandCooldownDuration => gameMode != null
        ? gameMode.Commands.CommandCooldownSeconds
        : 0f;
    public float MaximumCommandCost => gameMode != null
        ? gameMode.Commands.MaximumCost
        : 0f;
    public PlayerTeam ActiveCommandTeam => _activeCommandTeam.Value;
    public uint TurnNumber => _turnNumber.Value;
    public MatchEndReason EndReason => _matchEndReason.Value;
    public float WhiteCaptureScore => _whiteCaptureScore.Value;
    public float BlackCaptureScore => _blackCaptureScore.Value;
    public bool IsRandomCaptureRoundActive =>
        _randomCaptureRoundActive.Value;
    public Vector2 RandomCaptureZoneBoardPosition => new(
        _randomCaptureZoneFile.Value,
        _randomCaptureZoneRank.Value);
    public float RandomCaptureZoneRadius => _randomCaptureZoneRadius.Value;
    public uint RandomCaptureRoundNumber => _randomCaptureRoundNumber.Value;
    public float RandomCaptureRoundProgress
    {
        get
        {
            if (!_randomCaptureRoundActive.Value || NetworkManager == null)
            {
                return 0f;
            }

            double duration = _randomCaptureRoundEndServerTime.Value -
                _randomCaptureRoundStartServerTime.Value;

            if (duration <= 0d)
            {
                return 1f;
            }

            return Mathf.Clamp01((float)(
                (NetworkManager.ServerTime.Time -
                 _randomCaptureRoundStartServerTime.Value) / duration));
        }
    }
    public float RemainingRandomCaptureRoundTime =>
        !_randomCaptureRoundActive.Value || NetworkManager == null
            ? 0f
            : Mathf.Max(
                0f,
                (float)(_randomCaptureRoundEndServerTime.Value -
                    NetworkManager.ServerTime.Time));
    public NetworkList<CaptureZoneNetworkState> CaptureZoneStates => _captureZoneStates;
    public float RemainingTurnTime
    {
        get
        {
            if (CommandMode != CommandIssuingMode.AlternatingTurns ||
                gameMode == null ||
                gameMode.Commands.TurnDurationSeconds <= 0f ||
                NetworkManager == null)
            {
                return 0f;
            }

            return Mathf.Max(
                0f,
                (float)(_turnEndServerTime.Value - NetworkManager.ServerTime.Time));
        }
    }

    public float GetCommandPoints(PlayerTeam team)
    {
        return team switch
        {
            PlayerTeam.White => _whiteCommandPoints.Value,
            PlayerTeam.Black => _blackCommandPoints.Value,
            _ => 0f
        };
    }

    public float GetDisplayedCommandPoints(PlayerTeam team)
    {
        float points = GetCommandPoints(team);

        if (_localVoiceChargePreviewCost <= 0f ||
            !TryGetLocalPlayer(out NetworkPlayer localPlayer) ||
            localPlayer.Team != team)
        {
            return points;
        }

        return Mathf.Max(0f, points - _localVoiceChargePreviewCost);
    }

    public float GetCaptureScore(PlayerTeam team)
    {
        return team switch
        {
            PlayerTeam.White => _whiteCaptureScore.Value,
            PlayerTeam.Black => _blackCaptureScore.Value,
            _ => 0f
        };
    }

    public PieceArchetypeSettings GetPieceSettings(ChessPieceType pieceType)
    {
        if (gameMode != null)
        {
            return gameMode.GetPiece(pieceType);
        }

        if (!_fallbackPieceSettings.TryGetValue(
                pieceType,
                out PieceArchetypeSettings settings))
        {
            settings = PieceArchetypeSettings.CreateLegacyFallback(
                pieceType,
                voiceMoveSpeed,
                voiceTurnSpeed,
                pieceType switch
                {
                    ChessPieceType.Pawn => 1f,
                    ChessPieceType.Knight => 1.5f,
                    ChessPieceType.Bishop => 1.25f,
                    ChessPieceType.Rook => 2f,
                    ChessPieceType.Queen => 1.6f,
                    ChessPieceType.King => 1.4f,
                    _ => 1f
                },
                pieceCollisionRadius,
                knockbackDrag,
                ringOutDistance);
            _fallbackPieceSettings[pieceType] = settings;
        }

        return settings;
    }

    public PlayerCommanderSettings GetPlayerSettings()
    {
        return gameMode != null ? gameMode.Players : null;
    }

    private void InitializeConfiguredPieces()
    {
        _pieces.Clear();
        ushort nextId = 0;

        if (gameMode == null)
        {
            AddClassicStartingPieces(ref nextId);
            return;
        }

        foreach (InitialPiecePlacement placement in gameMode.InitialPlacements)
        {
            if (placement == null ||
                !placement.Enabled ||
                !gameMode.ShouldSpawnBoardPiece(placement) ||
                placement.PieceType == ChessPieceType.None ||
                (placement.Team != PlayerTeam.White &&
                 placement.Team != PlayerTeam.Black))
            {
                continue;
            }

            Vector2 position = placement.BoardPosition;
            NetworkChessPieceState state = new(
                nextId++,
                placement.Team,
                placement.PieceType,
                position.x,
                position.y)
            {
                VoiceHeading = placement.Heading
            };
            _pieces.Add(state);
        }
    }

    private void AddClassicStartingPieces(ref ushort nextId)
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
    }

    private void InitializeRuleState(bool startMatchClock)
    {
        _abilityReadyTimes.Clear();
        _matchEndReason.Value = MatchEndReason.None;
        _whiteCaptureScore.Value = 0f;
        _blackCaptureScore.Value = 0f;
        InitializeCaptureZones();
        InitializeCaptureKingRespawns();
        InitializeCommandEconomy();

        foreach (NetworkPlayer player in NetworkPlayer.Players)
        {
            if (player != null && player.IsSpawned)
            {
                player.ServerResetForMatch();
            }
        }

        _matchTimerRunning.Value = false;

        if (startMatchClock && IsMatchClockEnabled && NetworkManager != null)
        {
            float duration = gameMode != null
                ? gameMode.Clock.DurationSeconds
                : matchDurationSeconds;
            _matchEndServerTime.Value = NetworkManager.ServerTime.Time + duration;
            _matchTimerRunning.Value = true;
        }
    }

    private void InitializeCommandEconomy()
    {
        CommandEconomySettings settings = gameMode?.Commands;
        _activeCommandTeam.Value = settings?.FirstTeam ?? PlayerTeam.White;
        _turnNumber.Value = 1u;
        float startingCost = settings?.StartingCost ?? 0f;
        _whiteCommandPoints.Value = startingCost;
        _blackCommandPoints.Value = startingCost;
        _commandCostRechargeElapsed = 0f;
        ResetTurnDeadline();
    }

    private void ServerUpdateRuleState(float deltaTime)
    {
        if (_isGameOver.Value || !NetworkPlayer.MatchStarted)
        {
            return;
        }

        UpdateCommandEconomy(deltaTime);
        UpdateCaptureZones(deltaTime);
        UpdateCaptureKingRespawns();
        EvaluateConfiguredVictory();

        if (!_isGameOver.Value &&
            _matchTimerRunning.Value &&
            NetworkManager != null &&
            NetworkManager.ServerTime.Time >= _matchEndServerTime.Value)
        {
            FinishMatchAtTimeLimit();
        }
    }

    private void UpdateCommandEconomy(float deltaTime)
    {
        if (gameMode == null)
        {
            return;
        }

        CommandEconomySettings settings = gameMode.Commands;

        if (settings.CostSystemEnabled)
        {
            _commandCostRechargeElapsed += Mathf.Max(0f, deltaTime);
            int completedTicks = Mathf.FloorToInt(
                _commandCostRechargeElapsed / settings.RechargeIntervalSeconds);

            if (completedTicks > 0)
            {
                _commandCostRechargeElapsed -=
                    completedTicks * settings.RechargeIntervalSeconds;
                float restoredCost = completedTicks * settings.RechargeAmount;
                _whiteCommandPoints.Value = Mathf.Min(
                    settings.MaximumCost,
                    _whiteCommandPoints.Value + restoredCost);
                _blackCommandPoints.Value = Mathf.Min(
                    settings.MaximumCost,
                    _blackCommandPoints.Value + restoredCost);
            }
        }

        if (settings.Mode == CommandIssuingMode.AlternatingTurns &&
            settings.TurnDurationSeconds > 0f &&
            NetworkManager != null &&
            NetworkManager.ServerTime.Time >= _turnEndServerTime.Value)
        {
            AdvanceTurn();
        }
    }

    private void EvaluateConfiguredVictory()
    {
        if (gameMode == null || _isGameOver.Value)
        {
            return;
        }

        VictorySettings victory = gameMode.Victory;

        if (IsCaptureModeEnabled &&
            gameMode.CaptureMode.Version ==
            CaptureModeVersion.RandomRoundControl)
        {
            float target = gameMode.CaptureMode.RandomRoundScoreToWin;
            bool whiteReached = _whiteCaptureScore.Value >= target;
            bool blackReached = _blackCaptureScore.Value >= target;

            if (whiteReached || blackReached)
            {
                EndMatch(
                    whiteReached == blackReached
                        ? PlayerTeam.Unassigned
                        : whiteReached ? PlayerTeam.White : PlayerTeam.Black,
                    MatchEndReason.CaptureScoreReached);
                return;
            }
        }

        if (victory.EndAtCaptureScore &&
            IsCaptureModeEnabled &&
            gameMode.CaptureMode.Version ==
            CaptureModeVersion.LegacyConfiguredZones &&
            gameMode.CaptureMode.ScoringRule == CaptureScoringRule.PeriodicPerPiece)
        {
            bool whiteReached = _whiteCaptureScore.Value >= victory.CaptureScoreToWin;
            bool blackReached = _blackCaptureScore.Value >= victory.CaptureScoreToWin;

            if (whiteReached || blackReached)
            {
                EndMatch(
                    whiteReached == blackReached
                        ? PlayerTeam.Unassigned
                        : whiteReached ? PlayerTeam.White : PlayerTeam.Black,
                    MatchEndReason.CaptureScoreReached);
                return;
            }
        }

        if (victory.EndWhenAllPiecesEliminated)
        {
            bool whiteEliminated = GetRemainingPieceCount(PlayerTeam.White) == 0;
            bool blackEliminated = GetRemainingPieceCount(PlayerTeam.Black) == 0;

            if (whiteEliminated || blackEliminated)
            {
                EndMatch(
                    whiteEliminated == blackEliminated
                        ? PlayerTeam.Unassigned
                        : whiteEliminated ? PlayerTeam.Black : PlayerTeam.White,
                    MatchEndReason.AllPiecesEliminated);
                return;
            }
        }

        if (!victory.EndWhenRoyalEliminated)
        {
            return;
        }

        if (IsCaptureKingRespawnEnabled)
        {
            return;
        }

        bool whiteRoyalLost = IsRoyalTeamEliminated(PlayerTeam.White, victory);
        bool blackRoyalLost = IsRoyalTeamEliminated(PlayerTeam.Black, victory);

        if (whiteRoyalLost || blackRoyalLost)
        {
            EndMatch(
                whiteRoyalLost == blackRoyalLost
                    ? PlayerTeam.Unassigned
                    : whiteRoyalLost ? PlayerTeam.Black : PlayerTeam.White,
                MatchEndReason.RoyalEliminated);
        }
    }

    private bool IsRoyalTeamEliminated(PlayerTeam team, VictorySettings victory)
    {
        bool boardKingLost = victory.UsesBoardKing && !HasLivingBoardKing(team);
        GetPlayerCommanderRoyalState(
            team,
            out bool anyPlayerCommanderLost,
            out bool allPlayerCommandersLost);

        if (victory.RoyalRequirement == RoyalEliminationRequirement.AnyRoyalLost)
        {
            return boardKingLost ||
                (victory.UsesPlayerCommander && anyPlayerCommanderLost);
        }

        bool allBoardRoyalsLost = !victory.UsesBoardKing || boardKingLost;
        bool allPlayerRoyalsLost =
            !victory.UsesPlayerCommander || allPlayerCommandersLost;
        return allBoardRoyalsLost && allPlayerRoyalsLost;
    }

    private bool HasLivingBoardKing(PlayerTeam team)
    {
        for (int index = 0; index < _pieces.Count; index++)
        {
            if (_pieces[index].OwnerTeam == team &&
                _pieces[index].PieceType == ChessPieceType.King)
            {
                return true;
            }
        }

        return false;
    }

    private static void GetPlayerCommanderRoyalState(
        PlayerTeam team,
        out bool anyEliminated,
        out bool allEliminated)
    {
        int total = 0;
        int eliminated = 0;

        foreach (NetworkPlayer player in NetworkPlayer.Players)
        {
            if (player != null &&
                player.IsSpawned &&
                player.Team == team)
            {
                total++;

                if (player.IsEliminated)
                {
                    eliminated++;
                }
            }
        }

#if UNITY_EDITOR
        if (EditorSoloPlayTester.HasLivingDummyPlayer(team))
        {
            total++;
        }
#endif

        anyEliminated = eliminated > 0;
        allEliminated = total == 0 || eliminated == total;
    }

    private void FinishMatchAtTimeLimit()
    {
        RefreshFinalOccupancyScores();
        bool captureResolvesTimeLimit = IsCaptureModeEnabled &&
            gameMode.CaptureMode.ResolveWinnerAtTimeLimit;
        TimeLimitResolution resolution = captureResolvesTimeLimit
            ? TimeLimitResolution.CaptureScore
            : gameMode != null
                ? gameMode.Clock.TimeLimitResolution
                : TimeLimitResolution.RemainingPieces;
        float whiteValue;
        float blackValue;

        switch (resolution)
        {
            case TimeLimitResolution.RemainingPieces:
                whiteValue = GetRemainingPieceCount(PlayerTeam.White);
                blackValue = GetRemainingPieceCount(PlayerTeam.Black);
                break;
            case TimeLimitResolution.CaptureScore:
                whiteValue = _whiteCaptureScore.Value;
                blackValue = _blackCaptureScore.Value;
                break;
            case TimeLimitResolution.CombinedPieceAndCaptureScore:
                whiteValue = GetRemainingPieceCount(PlayerTeam.White) +
                    _whiteCaptureScore.Value;
                blackValue = GetRemainingPieceCount(PlayerTeam.Black) +
                    _blackCaptureScore.Value;
                break;
            default:
                whiteValue = 0f;
                blackValue = 0f;
                break;
        }

        PlayerTeam winner = Mathf.Approximately(whiteValue, blackValue)
            ? PlayerTeam.Unassigned
            : whiteValue > blackValue ? PlayerTeam.White : PlayerTeam.Black;
        EndMatch(winner, MatchEndReason.TimeExpired);
    }

    private void EndMatch(
        PlayerTeam winner,
        MatchEndReason reason,
        NetworkChessPieceState? cinematicKing = null)
    {
        if (_isGameOver.Value)
        {
            return;
        }

        RefreshFinalOccupancyScores();

        bool playCinematic = cinematicKing.HasValue &&
            (gameMode == null || gameMode.Presentation.PlayRoyalDeathCinematic);
        _matchTimerRunning.Value = false;
        _winner.Value = winner;
        _matchEndReason.Value = reason;
        _isGameOver.Value = true;
        _gameOverPresentationReady.Value = !playCinematic;

        if (playCinematic)
        {
            BeginKingDeathCinematicRpc(cinematicKing.Value);
        }
    }

    private void HandleConfiguredPieceRingOut(NetworkChessPieceState piece)
    {
        if (piece.PieceType == ChessPieceType.King &&
            IsCaptureKingRespawnEnabled)
        {
            ScheduleBoardKingRespawn(piece);
            return;
        }

        if (gameMode == null)
        {
            if (piece.PieceType == ChessPieceType.King)
            {
                EndMatch(
                    GetOpponent(piece.OwnerTeam),
                    MatchEndReason.RoyalEliminated,
                    piece);
            }

            return;
        }

        VictorySettings victory = gameMode.Victory;

        if (piece.PieceType == ChessPieceType.King &&
            victory.EndWhenRoyalEliminated &&
            victory.UsesBoardKing &&
            (victory.RoyalUnitMode != RoyalUnitMode.BoardKingAndPlayerCommander ||
             victory.RoyalRequirement == RoyalEliminationRequirement.AnyRoyalLost ||
             IsEveryPlayerCommanderEliminated(piece.OwnerTeam)))
        {
            EndMatch(
                GetOpponent(piece.OwnerTeam),
                MatchEndReason.RoyalEliminated,
                piece);
        }
    }

    private static bool IsEveryPlayerCommanderEliminated(PlayerTeam team)
    {
        GetPlayerCommanderRoyalState(team, out _, out bool allEliminated);
        return allEliminated;
    }

    public bool CanIssueCommand(
        PlayerTeam team,
        ChessPieceType pieceType,
        PieceVoiceCommand command,
        out string rejection,
        float voicedDurationSeconds = -1f,
        NetworkPlayer issuingPlayer = null)
    {
        rejection = string.Empty;
        PieceArchetypeSettings pieceSettings = GetPieceSettings(pieceType);

        if (UsesChargeSelectionCommand &&
            command != PieceVoiceCommand.Charge)
        {
            rejection = "신규 명령 방식에서는 현재 ‘돌진’ 명령만 사용할 수 있습니다.";
            return false;
        }

        if (ActiveVoiceCommandVersion == VoiceCommandVersion.LegacyLookSelection &&
            command == PieceVoiceCommand.Charge)
        {
            rejection = "‘돌진’은 신규 명령 방식에서만 사용할 수 있습니다.";
            return false;
        }

        if (!pieceSettings.AcceptsCommands)
        {
            rejection = "이 기물은 현재 명령을 받지 않도록 설정되어 있습니다.";
            return false;
        }

        if (pieceSettings.MovementMode == PieceMovementMode.Stationary &&
            command != PieceVoiceCommand.Stop &&
            command != PieceVoiceCommand.SkillPrimary &&
            command != PieceVoiceCommand.SkillSecondary)
        {
            rejection = "이 기물은 고정형이라 이동/회전 명령을 받을 수 없습니다.";
            return false;
        }

        if (!IsMovementCommandAllowed(pieceSettings.MovementMode, command))
        {
            rejection = $"{pieceSettings.MovementMode} 행마 설정에서 사용할 수 없는 이동 명령입니다.";
            return false;
        }

        if ((command == PieceVoiceCommand.SkillPrimary ||
             command == PieceVoiceCommand.SkillSecondary) &&
            FindAbility(pieceSettings, command) == null)
        {
            rejection = "이 기물에는 해당 스킬이 지정되어 있지 않습니다.";
            return false;
        }

        if (gameMode == null)
        {
            return true;
        }

        CommandEconomySettings economy = gameMode.Commands;

        if (economy.Mode == CommandIssuingMode.AlternatingTurns &&
            _activeCommandTeam.Value != team)
        {
            rejection = $"현재는 {_activeCommandTeam.Value} 팀의 명령 차례입니다.";
            return false;
        }

        if (economy.CooldownSystemEnabled)
        {
            if (issuingPlayer == null)
            {
                rejection = "명령 쿨타임을 확인할 플레이어가 없습니다.";
                return false;
            }

            float remainingCooldown = issuingPlayer.RemainingCommandCooldown;

            if (remainingCooldown > 0.0001f)
            {
                rejection = $"명령 쿨타임 중입니다. {remainingCooldown:F1}초 남았습니다.";
                return false;
            }
        }

        if (economy.CostSystemEnabled)
        {
            float cost = GetCommandCost(
                pieceSettings,
                command,
                voicedDurationSeconds);

            if (GetCommandPoints(team) + 0.0001f < cost)
            {
                rejection = $"명령 코스트가 부족합니다. 필요 {cost:F1}, 보유 {GetCommandPoints(team):F1}";
                return false;
            }
        }

        return true;
    }

    private static bool IsMovementCommandAllowed(
        PieceMovementMode movementMode,
        PieceVoiceCommand command)
    {
        bool isMovementCommand = IsPieceMovementCommand(command);

        if (!isMovementCommand || movementMode == PieceMovementMode.Free)
        {
            return true;
        }

        return movementMode switch
        {
            PieceMovementMode.ForwardOnly =>
                command == PieceVoiceCommand.MoveForward ||
                command == PieceVoiceCommand.Charge,
            PieceMovementMode.ForwardAndBackward =>
                command == PieceVoiceCommand.MoveForward ||
                command == PieceVoiceCommand.MoveBackward ||
                command == PieceVoiceCommand.Charge,
            PieceMovementMode.StrafeOnly =>
                command == PieceVoiceCommand.MoveLeft ||
                command == PieceVoiceCommand.MoveRight,
            PieceMovementMode.Stationary => false,
            _ => true
        };
    }

    private static bool IsPieceMovementCommand(PieceVoiceCommand command)
    {
        return command == PieceVoiceCommand.MoveForward ||
               command == PieceVoiceCommand.MoveBackward ||
               command == PieceVoiceCommand.MoveLeft ||
               command == PieceVoiceCommand.MoveRight ||
               command == PieceVoiceCommand.MoveUpperRight ||
               command == PieceVoiceCommand.MoveUpperLeft ||
               command == PieceVoiceCommand.MoveLowerRight ||
               command == PieceVoiceCommand.MoveLowerLeft ||
               command == PieceVoiceCommand.Charge;
    }

    private bool CanIssuePieceMovementCommand(
        NetworkChessPieceState piece,
        PieceVoiceCommand command,
        out string rejection)
    {
        rejection = string.Empty;

        if (!IsPieceMovementCooldownEnabled ||
            !IsPieceMovementCommand(command))
        {
            return true;
        }

        float remaining = GetRemainingPieceMovementCooldown(piece);

        if (remaining <= 0.0001f)
        {
            return true;
        }

        rejection = $"이 기물은 이동 쿨타임 중입니다. {remaining:F1}초 남았습니다.";
        return false;
    }

    private float GetRemainingPieceMovementCooldown(
        NetworkChessPieceState piece)
    {
        if (!IsPieceMovementCooldownEnabled ||
            NetworkManager == null ||
            !NetworkManager.IsListening)
        {
            return 0f;
        }

        return Mathf.Max(
            0f,
            (float)(piece.MovementCooldownEndServerTime -
                NetworkManager.ServerTime.Time));
    }

    private void StartPieceMovementCooldown(
        ref NetworkChessPieceState piece)
    {
        if (!IsPieceMovementCooldownEnabled ||
            NetworkManager == null ||
            !NetworkManager.IsListening)
        {
            piece.MovementCooldownEndServerTime = 0d;
            return;
        }

        piece.MovementCooldownEndServerTime =
            NetworkManager.ServerTime.Time + PieceMovementCooldownDuration;
    }

    private static bool TryGetMovementHeadingOffset(
        PieceVoiceCommand command,
        out float headingOffset)
    {
        headingOffset = command switch
        {
            PieceVoiceCommand.MoveForward => 0f,
            PieceVoiceCommand.MoveBackward => 180f,
            PieceVoiceCommand.MoveLeft => -90f,
            PieceVoiceCommand.MoveRight => 90f,
            PieceVoiceCommand.MoveUpperRight => 45f,
            PieceVoiceCommand.MoveUpperLeft => -45f,
            PieceVoiceCommand.MoveLowerRight => 135f,
            PieceVoiceCommand.MoveLowerLeft => -135f,
            _ => 0f
        };

        return command == PieceVoiceCommand.MoveForward ||
               command == PieceVoiceCommand.MoveBackward ||
               command == PieceVoiceCommand.MoveLeft ||
               command == PieceVoiceCommand.MoveRight ||
               command == PieceVoiceCommand.MoveUpperRight ||
               command == PieceVoiceCommand.MoveUpperLeft ||
               command == PieceVoiceCommand.MoveLowerRight ||
               command == PieceVoiceCommand.MoveLowerLeft;
    }

    private void ApplyMovementCommand(
        ref NetworkChessPieceState piece,
        PieceArchetypeSettings settings,
        float headingOffset,
        float commandLoudness)
    {
        piece.VoiceMoveHeadingOffset = headingOffset;
        piece.VoiceMoveLoudness = commandLoudness;
        piece.VoiceChargeDistanceRemaining = 0f;
        ArmFirstAttackingCollision(ref piece);

        if (settings.MovementControl == PieceMovementControl.Continuous)
        {
            piece.VoiceMoveAxis = 1;
            return;
        }

        piece.VoiceMoveAxis = 0;
        Vector2 direction = GetVoiceMoveDirection(
            piece.OwnerTeam,
            piece.VoiceHeading + headingOffset);
        Vector2 flickVelocity = direction * settings.GetFlickSpeed(commandLoudness);
        Vector2 currentVelocity = new(
            piece.KnockbackFileVelocity,
            piece.KnockbackRankVelocity);
        Vector2 newVelocity = settings.AccumulateFlickImpulses
            ? currentVelocity + flickVelocity
            : flickVelocity;
        newVelocity = Vector2.ClampMagnitude(
            newVelocity,
            settings.MaximumFlickSpeed);
        piece.KnockbackFileVelocity = newVelocity.x;
        piece.KnockbackRankVelocity = newVelocity.y;
    }

    private void ApplyVoiceChargedMovement(
        ref NetworkChessPieceState piece,
        PieceArchetypeSettings settings,
        float chargePower,
        float chargeDistance)
    {
        piece.VoiceMoveHeadingOffset = 0f;
        piece.VoiceMoveLoudness = Mathf.Clamp01(chargePower);
        piece.VoiceChargeDistanceRemaining = Mathf.Max(0f, chargeDistance);
        ArmFirstAttackingCollision(ref piece);

        if (settings.MovementControl == PieceMovementControl.Continuous)
        {
            piece.VoiceMoveAxis = chargeDistance > 0f ? (sbyte)1 : (sbyte)0;
            return;
        }

        piece.VoiceMoveAxis = 0;
        Vector2 direction = GetVoiceMoveDirection(
            piece.OwnerTeam,
            piece.VoiceHeading);
        float launchSpeed = settings.FlickFriction > 0.0001f
            ? Mathf.Sqrt(2f * settings.FlickFriction * Mathf.Max(0f, chargeDistance))
            : settings.GetFlickSpeed(chargePower);
        launchSpeed = Mathf.Min(launchSpeed, settings.MaximumFlickSpeed);
        Vector2 velocity = direction * launchSpeed;
        piece.KnockbackFileVelocity = velocity.x;
        piece.KnockbackRankVelocity = velocity.y;
    }

    public float GetVoiceChargeCost(
        ChessPieceType pieceType,
        float voicedDurationSeconds,
        int selectedPieceCount = 1)
    {
        PieceArchetypeSettings pieceSettings = GetPieceSettings(pieceType);
        float baseCost = gameMode == null
            ? 0f
            : gameMode.Commands.UsesVoiceDurationCost
                ? gameMode.Commands.GetVoiceChargeCost(
                    voicedDurationSeconds,
                    selectedPieceCount)
                : Mathf.Min(
                    gameMode.Commands.MaximumCost,
                    gameMode.Commands.GetBaseCost(PieceVoiceCommand.Charge) *
                    Mathf.Max(1, selectedPieceCount));
        return Mathf.Max(1f, baseCost * pieceSettings.CommandCostMultiplier);
    }

    public float GetVoiceChargeDistance(
        ChessPieceType pieceType,
        float chargePower)
    {
        PieceArchetypeSettings settings = GetPieceSettings(pieceType);
        ResolvedPieceTraits traits = new(
            settings.Traits,
            default,
            GetCurrentServerTime());
        return GetVoiceChargeDistance(settings, traits, chargePower);
    }

    public float GetVoiceChargeDistance(
        in NetworkChessPieceState piece,
        float chargePower)
    {
        PieceArchetypeSettings settings = GetPieceSettings(piece.PieceType);
        return GetVoiceChargeDistance(
            settings,
            ResolvePieceTraits(piece),
            chargePower);
    }

    private float GetVoiceChargeDistance(
        PieceArchetypeSettings settings,
        in ResolvedPieceTraits traits,
        float chargePower)
    {
        if (gameMode == null)
        {
            return 0f;
        }

        float shapedChargePower = traits.ShapeChargePower(chargePower);
        float distance = gameMode.Commands.GetVoiceChargeDistance(
            shapedChargePower) * traits.ChargeDistanceMultiplier;

        if (settings.MovementControl == PieceMovementControl.FlickImpulse &&
            settings.FlickFriction > 0.0001f)
        {
            float maximumPhysicalDistance =
                settings.MaximumFlickSpeed * settings.MaximumFlickSpeed /
                (2f * settings.FlickFriction);
            distance = Mathf.Min(distance, maximumPhysicalDistance);
        }

        return Mathf.Max(0f, distance);
    }

    private void ArmFirstAttackingCollision(
        ref NetworkChessPieceState piece)
    {
        ResolvedPieceTraits traits = ResolvePieceTraits(piece);
        piece.FirstAttackingCollisionAvailable =
            traits.FirstAttackingCollisionOnly &&
            !Mathf.Approximately(traits.AttackingImpactMultiplier, 1f);
    }

    private static Vector2 DeceleratePhysicalVelocity(
        PieceArchetypeSettings settings,
        Vector2 velocity,
        float deltaTime)
    {
        if (settings.MovementControl == PieceMovementControl.FlickImpulse)
        {
            return Vector2.MoveTowards(
                velocity,
                Vector2.zero,
                settings.FlickFriction * deltaTime);
        }

        return velocity * Mathf.Exp(-settings.KnockbackDrag * deltaTime);
    }

    private float GetCommandCost(
        PieceArchetypeSettings pieceSettings,
        PieceVoiceCommand command,
        float voicedDurationSeconds = -1f)
    {
        if (gameMode == null)
        {
            return 0f;
        }

        CommandEconomySettings economy = gameMode.Commands;
        float baseCost = command == PieceVoiceCommand.Charge &&
            economy.UsesVoiceDurationCost
            ? economy.GetVoiceChargeCost(
                voicedDurationSeconds >= 0f
                    ? Mathf.Max(0.0001f, voicedDurationSeconds)
                    : economy.VoiceChargeSecondsPerCostStep)
            : economy.GetBaseCost(command);
        float cost = baseCost * pieceSettings.CommandCostMultiplier;
        ChessPieceAbility ability = FindAbility(pieceSettings, command);

        if (ability != null)
        {
            cost += ability.AdditionalCommandCost;
        }

        return command == PieceVoiceCommand.Charge
            ? Mathf.Max(1f, cost)
            : Mathf.Max(0f, cost);
    }

    private void AcceptCommand(
        PlayerTeam team,
        PieceArchetypeSettings pieceSettings,
        PieceVoiceCommand command,
        float acceptedCost = -1f,
        NetworkPlayer issuingPlayer = null)
    {
        if (gameMode == null)
        {
            return;
        }

        CommandEconomySettings economy = gameMode.Commands;

        if (economy.CostSystemEnabled)
        {
            float cost = acceptedCost >= 0f
                ? acceptedCost
                : GetCommandCost(pieceSettings, command);

            if (team == PlayerTeam.White)
            {
                _whiteCommandPoints.Value = Mathf.Max(
                    0f,
                    _whiteCommandPoints.Value - cost);
            }
            else if (team == PlayerTeam.Black)
            {
                _blackCommandPoints.Value = Mathf.Max(
                    0f,
                    _blackCommandPoints.Value - cost);
            }
        }

        if (economy.CooldownSystemEnabled)
        {
            issuingPlayer?.ServerStartCommandCooldown(
                economy.CommandCooldownSeconds);
        }

        if (economy.Mode == CommandIssuingMode.AlternatingTurns &&
            economy.AdvanceAfterAcceptedCommand)
        {
            AdvanceTurn();
        }
    }

    public bool TryEndLocalTurn(out string rejection)
    {
        rejection = string.Empty;

        if (!TryGetLocalPlayer(out NetworkPlayer localPlayer))
        {
            rejection = "로컬 플레이어를 찾지 못했습니다.";
            return false;
        }

        if (CommandMode != CommandIssuingMode.AlternatingTurns ||
            localPlayer.Team != _activeCommandTeam.Value)
        {
            rejection = "현재 종료할 수 있는 아군 턴이 없습니다.";
            return false;
        }

        RequestEndTurnRpc();
        return true;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestEndTurnRpc(RpcParams rpcParams = default)
    {
        if (CommandMode != CommandIssuingMode.AlternatingTurns ||
            !NetworkPlayer.TryGetByClientId(
                rpcParams.Receive.SenderClientId,
                out NetworkPlayer player) ||
            player.Team != _activeCommandTeam.Value)
        {
            return;
        }

        AdvanceTurn();
    }

    private void AdvanceTurn()
    {
        _activeCommandTeam.Value = GetOpponent(_activeCommandTeam.Value);
        _turnNumber.Value++;
        ResetTurnDeadline();
    }

#if UNITY_EDITOR
    public bool EditorForceAdvanceTurn()
    {
        if (!IsSpawned ||
            !IsServer ||
            _isGameOver.Value ||
            !NetworkPlayer.MatchStarted ||
            CommandMode != CommandIssuingMode.AlternatingTurns)
        {
            return false;
        }

        AdvanceTurn();
        return true;
    }

    public bool EditorForceFinishMatchAtTimeLimit()
    {
        if (!IsSpawned ||
            !IsServer ||
            _isGameOver.Value ||
            !NetworkPlayer.MatchStarted)
        {
            return false;
        }

        FinishMatchAtTimeLimit();
        return true;
    }
#endif

    private void ResetTurnDeadline()
    {
        float duration = gameMode?.Commands.TurnDurationSeconds ?? 0f;
        _turnEndServerTime.Value = duration > 0f && NetworkManager != null
            ? NetworkManager.ServerTime.Time + duration
            : 0d;
    }

    private bool ShouldFreezePieceMovement(PlayerTeam team)
    {
        return gameMode != null &&
               gameMode.Commands.Mode == CommandIssuingMode.AlternatingTurns &&
               gameMode.Commands.FreezeInactiveTeamMovement &&
               team != _activeCommandTeam.Value;
    }

    private ChessPieceAbility FindAbility(
        PieceArchetypeSettings pieceSettings,
        PieceVoiceCommand command)
    {
        foreach (ChessPieceAbility ability in pieceSettings.Abilities)
        {
            if (ability != null && ability.Trigger == command)
            {
                return ability;
            }
        }

        return null;
    }

    private bool TryExecuteAbility(
        ref NetworkChessPieceState piece,
        PieceArchetypeSettings pieceSettings,
        PieceVoiceCommand command,
        float commandLoudness)
    {
        ChessPieceAbility ability = FindAbility(pieceSettings, command);

        if (ability == null)
        {
            return false;
        }

        AbilityCooldownKey cooldownKey = new(piece.Id, ability.GetInstanceID());
        double now = NetworkManager != null ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

        if (_abilityReadyTimes.TryGetValue(cooldownKey, out double readyAt) && now < readyAt)
        {
            return false;
        }

        ChessPieceAbilityContext context = new(
            piece.OwnerTeam,
            commandLoudness,
            piece.OwnerTeam == PlayerTeam.Black ? Vector2.down : Vector2.up,
            now);

        if (!ability.TryExecute(ref piece, context, out _))
        {
            return false;
        }

        _abilityReadyTimes[cooldownKey] = now + ability.CooldownSeconds;
        return true;
    }

    private float ResolveRestitution()
    {
        return gameMode != null
            ? gameMode.Collisions.Restitution
            : collisionRestitution;
    }

    private float ResolveSeparationEpsilon()
    {
        return gameMode != null
            ? gameMode.Collisions.SeparationEpsilon
            : 0.0001f;
    }

    private float ResolveCollisionImpulseMultiplier()
    {
        return gameMode != null
            ? gameMode.Collisions.ImpulseMultiplier
            : collisionImpulseMultiplier;
    }

    private float ResolvePlayerCollisionHeight()
    {
        return gameMode != null
            ? gameMode.Collisions.PlayerCollisionHeight
            : playerPieceCollisionHeight;
    }

    private float ResolveMinimumPlayerImpactSpeed()
    {
        return gameMode != null
            ? gameMode.Collisions.MinimumPlayerImpactSpeed
            : minimumPlayerImpactSpeed;
    }

    private float ResolveMinimumPlayerImpactAlignment()
    {
        return gameMode != null
            ? gameMode.Collisions.MinimumPlayerImpactAlignment
            : minimumPlayerImpactAlignment;
    }

    private bool AreFriendlyPiecesIntangible()
    {
        return gameMode == null || gameMode.Collisions.FriendlyPiecesAreIntangible;
    }

    private float ResolvePresentationDuration()
    {
        return gameMode != null
            ? gameMode.Presentation.RoyalDeathDuration
            : kingDeathCinematicDuration;
    }

    private float ResolvePresentationCameraDistance()
    {
        return gameMode != null
            ? gameMode.Presentation.CameraDistanceInSquares
            : kingDeathCameraDistanceInSquares;
    }

    private float ResolvePresentationCameraHeight()
    {
        return gameMode != null
            ? gameMode.Presentation.CameraHeightInSquares
            : kingDeathCameraHeightInSquares;
    }

    private float ResolvePresentationDropDistance()
    {
        return gameMode != null
            ? gameMode.Presentation.DropDistanceInSquares
            : kingDeathDropDistanceInSquares;
    }

    private float ResolvePresentationOutwardDistance()
    {
        return gameMode != null
            ? gameMode.Presentation.OutwardDistanceInSquares
            : kingDeathOutwardDistanceInSquares;
    }

    private float ResolvePresentationTiltAngle()
    {
        return gameMode != null
            ? gameMode.Presentation.TiltAngle
            : kingDeathTiltAngle;
    }

    private float ResolvePresentationFieldOfView()
    {
        return gameMode != null
            ? gameMode.Presentation.CameraFieldOfView
            : kingDeathCameraFieldOfView;
    }

    private int GetConfiguredInitialPieceCount(PlayerTeam team)
    {
        return gameMode != null ? gameMode.GetInitialPieceCount(team) : StartingPiecesPerTeam;
    }

}
