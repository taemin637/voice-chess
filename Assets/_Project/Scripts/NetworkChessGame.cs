using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

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

public enum ChessMoveResult : byte
{
    Accepted,
    InvalidSquare,
    PlayerNotFound,
    TeamNotSelected,
    NotYourTurn,
    NoPieceOnSource,
    NotYourStack,
    OwnStackOnDestination,
    IllegalMove,
    MustContinueActiveStack,
    StackHasNoLegalMove,
    KingCaptureMustBeFirstMove,
    KingCannotCaptureStack,
    PromotionRequired,
    GameAlreadyFinished
}

public struct NetworkChessPieceState :
    INetworkSerializable,
    IEquatable<NetworkChessPieceState>
{
    public ushort Id;
    public PlayerTeam OwnerTeam;
    public ChessPieceType PieceType;
    public byte File;
    public byte Rank;
    public byte StackDepth;
    public byte NextMoveDepth;
    public bool HasMoved;

    public NetworkChessPieceState(
        ushort id,
        PlayerTeam ownerTeam,
        ChessPieceType pieceType,
        int file,
        int rank)
    {
        Id = id;
        OwnerTeam = ownerTeam;
        PieceType = pieceType;
        File = (byte)file;
        Rank = (byte)rank;
        StackDepth = 0;
        NextMoveDepth = 0;
        HasMoved = false;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref Id);
        serializer.SerializeValue(ref OwnerTeam);
        serializer.SerializeValue(ref PieceType);
        serializer.SerializeValue(ref File);
        serializer.SerializeValue(ref Rank);
        serializer.SerializeValue(ref StackDepth);
        serializer.SerializeValue(ref NextMoveDepth);
        serializer.SerializeValue(ref HasMoved);
    }

    public bool Equals(NetworkChessPieceState other)
    {
        return Id == other.Id &&
               OwnerTeam == other.OwnerTeam &&
               PieceType == other.PieceType &&
               File == other.File &&
               Rank == other.Rank &&
               StackDepth == other.StackDepth &&
               NextMoveDepth == other.NextMoveDepth &&
               HasMoved == other.HasMoved;
    }
}

public sealed class NetworkChessGame : NetworkBehaviour
{
    [SerializeField] private ChessPieceSpawner pieceSpawner;

    private readonly NetworkList<NetworkChessPieceState> _pieces = new(
        readPerm: NetworkVariableReadPermission.Everyone,
        writePerm: NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<PlayerTeam> _currentTurn = new(
        PlayerTeam.White,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<PlayerTeam> _winner = new(
        PlayerTeam.Unassigned,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<FixedString64Bytes> _lastMove = new(
        new FixedString64Bytes("No moves yet."),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _sequenceActive = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<byte> _activeFile = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<byte> _activeRank = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<byte> _activeMoveDepth = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _promotionPending = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<ushort> _promotionPieceId = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _promotionEndsTurn = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private string _moveStatus = "Select a stack or enter a move.";
    private int _selectedFile = -1;
    private int _selectedRank = -1;
    private bool _visualRefreshPending;

    public PlayerTeam CurrentTurn => _currentTurn.Value;

    public override void OnNetworkSpawn()
    {
        if (IsServer && _pieces.Count == 0)
        {
            InitializePieces();
        }

        if (pieceSpawner == null)
        {
            pieceSpawner = FindFirstObjectByType<ChessPieceSpawner>();
        }

        _pieces.OnListChanged += HandlePiecesChanged;
        _visualRefreshPending = true;
    }

    public override void OnNetworkDespawn()
    {
        _pieces.OnListChanged -= HandlePiecesChanged;
        ClearSelection();
    }

    private void Update()
    {
        if (!IsSpawned || pieceSpawner == null)
        {
            return;
        }

        RefreshLocalSequenceSelection();

        Mouse mouse = Mouse.current;

        if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
        {
            return;
        }

        if (pieceSpawner.TryGetSquareFromScreenPoint(
                Camera.main,
                mouse.position.ReadValue(),
                out int file,
                out int rank))
        {
            HandleSquareClicked(file, rank);
        }
    }

    private void LateUpdate()
    {
        if (!_visualRefreshPending || pieceSpawner == null)
        {
            return;
        }

        _visualRefreshPending = false;

        List<NetworkChessPieceState> visualStates = new(_pieces.Count);

        for (int index = 0; index < _pieces.Count; index++)
        {
            visualStates.Add(_pieces[index]);
        }

        pieceSpawner.RebuildFromNetworkState(visualStates);
    }

    private void InitializePieces()
    {
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
                nextId++, PlayerTeam.White, backRank[file], file, 0));
            _pieces.Add(new NetworkChessPieceState(
                nextId++, PlayerTeam.White, ChessPieceType.Pawn, file, 1));
            _pieces.Add(new NetworkChessPieceState(
                nextId++, PlayerTeam.Black, ChessPieceType.Pawn, file, 6));
            _pieces.Add(new NetworkChessPieceState(
                nextId++, PlayerTeam.Black, backRank[file], file, 7));
        }

        _currentTurn.Value = PlayerTeam.White;
        _winner.Value = PlayerTeam.Unassigned;
        _lastMove.Value = new FixedString64Bytes("No moves yet.");
        ClearServerSequence();
        _promotionPending.Value = false;
        _promotionPieceId.Value = 0;
        _promotionEndsTurn.Value = false;
    }

    private void HandlePiecesChanged(
        NetworkListEvent<NetworkChessPieceState> changeEvent)
    {
        _visualRefreshPending = true;
    }

    private void RefreshLocalSequenceSelection()
    {
        if (!TryGetLocalPlayer(out NetworkPlayer localPlayer))
        {
            return;
        }

        if (localPlayer.Team != _currentTurn.Value)
        {
            if (HasSelectedSquare)
            {
                ClearSelection();
            }

            return;
        }

        if (!_sequenceActive.Value)
        {
            return;
        }

        int activeFile = _activeFile.Value;
        int activeRank = _activeRank.Value;

        if (_selectedFile == activeFile && _selectedRank == activeRank)
        {
            return;
        }

        _selectedFile = activeFile;
        _selectedRank = activeRank;
        pieceSpawner.ShowSelection(activeFile, activeRank);
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

    private void HandleSquareClicked(int file, int rank)
    {
        if (!NetworkPlayer.TryGetByClientId(
                NetworkManager.LocalClientId,
                out NetworkPlayer localPlayer))
        {
            _moveStatus = "Local network player was not found.";
            return;
        }

        if (localPlayer.Team != PlayerTeam.White &&
            localPlayer.Team != PlayerTeam.Black)
        {
            _moveStatus = "Choose White or Black first.";
            return;
        }

        if (!HasSelectedSquare)
        {
            TrySelectStack(file, rank, localPlayer.Team);
            return;
        }

        if (file == _selectedFile && rank == _selectedRank)
        {
            ClearSelection();
            _moveStatus = "Selection cleared.";
            return;
        }

        int clickedTopIndex = FindTopPieceIndexAt(file, rank);

        if (clickedTopIndex >= 0 &&
            _pieces[clickedTopIndex].OwnerTeam == localPlayer.Team)
        {
            TrySelectStack(file, rank, localPlayer.Team);
            return;
        }

        SubmitMove(_selectedFile, _selectedRank, file, rank);
    }

    private void TrySelectStack(int file, int rank, PlayerTeam localTeam)
    {
        if (_winner.Value != PlayerTeam.Unassigned)
        {
            _moveStatus = "The game has already finished.";
            return;
        }

        if (localTeam != _currentTurn.Value)
        {
            _moveStatus = "It is not your team's turn.";
            return;
        }

        if (_promotionPending.Value)
        {
            _moveStatus = "Choose a promotion piece before moving again.";
            return;
        }

        if (_sequenceActive.Value &&
            (file != _activeFile.Value || rank != _activeRank.Value))
        {
            _moveStatus = "Your team must continue with the active stack.";
            return;
        }

        List<int> stack = GetStackIndicesAt(file, rank);

        if (stack.Count == 0)
        {
            _moveStatus = "There is no stack on that square.";
            return;
        }

        if (_pieces[stack[0]].OwnerTeam != localTeam)
        {
            _moveStatus = "That stack belongs to the other team.";
            return;
        }

        int moveDepth = _sequenceActive.Value
            ? _activeMoveDepth.Value
            : GetStackCursor(stack);
        int movementPieceIndex = FindPieceIndexAtDepth(stack, moveDepth);

        if (movementPieceIndex < 0)
        {
            _moveStatus = "The stack movement order is invalid.";
            return;
        }

        NetworkChessPieceState movementPiece = _pieces[movementPieceIndex];

        if (!HasAnyLegalMove(
                movementPiece,
                file,
                rank,
                firstMoveOfTurn: !_sequenceActive.Value))
        {
            _moveStatus =
                $"{movementPiece.PieceType} on layer {moveDepth + 1} has no legal move.";
            return;
        }

        _selectedFile = file;
        _selectedRank = rank;
        pieceSpawner?.ShowSelection(file, rank);
        _moveStatus =
            $"Selected {GetSquareName(file, rank)}. " +
            $"Next: layer {moveDepth + 1} {movementPiece.PieceType}.";
    }

    private void SubmitMove(
        int fromFile,
        int fromRank,
        int toFile,
        int toRank)
    {
        _moveStatus = "Waiting for server...";
        RequestMoveRpc(
            (byte)fromFile,
            (byte)fromRank,
            (byte)toFile,
            (byte)toRank);
    }

    [Rpc(
        SendTo.Server,
        InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestPromotionRpc(
        ChessPieceType requestedType,
        RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        bool accepted = false;

        if (_promotionPending.Value &&
            IsValidPromotionType(requestedType) &&
            NetworkPlayer.TryGetByClientId(
                senderClientId,
                out NetworkPlayer player) &&
            player.Team == _currentTurn.Value)
        {
            int pieceIndex = FindPieceIndexById(_promotionPieceId.Value);

            if (pieceIndex >= 0 &&
                _pieces[pieceIndex].PieceType == ChessPieceType.Pawn)
            {
                NetworkChessPieceState promotedPiece = _pieces[pieceIndex];
                promotedPiece.PieceType = requestedType;
                _pieces[pieceIndex] = promotedPiece;

                bool endsTurn = _promotionEndsTurn.Value;
                _promotionPending.Value = false;
                _promotionPieceId.Value = 0;
                _promotionEndsTurn.Value = false;
                accepted = true;

                if (endsTurn)
                {
                    EndTurn(player.Team);
                }
            }
        }

        PromotionResultRpc(
            accepted,
            requestedType,
            RpcTarget.Single(senderClientId, RpcTargetUse.Temp));
    }

    [Rpc(
        SendTo.SpecifiedInParams,
        InvokePermission = RpcInvokePermission.Server)]
    private void PromotionResultRpc(
        bool accepted,
        ChessPieceType requestedType,
        RpcParams rpcParams = default)
    {
        _moveStatus = accepted
            ? $"Pawn promoted to {requestedType}."
            : "Promotion request was rejected.";
    }

    private void ClearSelection()
    {
        _selectedFile = -1;
        _selectedRank = -1;
        pieceSpawner?.HideSelection();
    }

    [Rpc(
        SendTo.Server,
        InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestMoveRpc(
        byte fromFile,
        byte fromRank,
        byte toFile,
        byte toRank,
        RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        ChessMoveResult result = ValidateAndApplyMove(
            senderClientId,
            fromFile,
            fromRank,
            toFile,
            toRank);

        MoveResultRpc(
            result,
            RpcTarget.Single(senderClientId, RpcTargetUse.Temp));
    }

    [Rpc(
        SendTo.SpecifiedInParams,
        InvokePermission = RpcInvokePermission.Server)]
    private void MoveResultRpc(
        ChessMoveResult result,
        RpcParams rpcParams = default)
    {
        _moveStatus = GetMoveResultMessage(result);

        if (result == ChessMoveResult.Accepted)
        {
            ClearSelection();
        }
    }

    private ChessMoveResult ValidateAndApplyMove(
        ulong senderClientId,
        int fromFile,
        int fromRank,
        int toFile,
        int toRank)
    {
        if (_winner.Value != PlayerTeam.Unassigned)
        {
            return ChessMoveResult.GameAlreadyFinished;
        }

        if (_promotionPending.Value)
        {
            return ChessMoveResult.PromotionRequired;
        }

        if (!IsValidSquare(fromFile, fromRank) ||
            !IsValidSquare(toFile, toRank))
        {
            return ChessMoveResult.InvalidSquare;
        }

        if (!NetworkPlayer.TryGetByClientId(
                senderClientId,
                out NetworkPlayer player))
        {
            return ChessMoveResult.PlayerNotFound;
        }

        if (player.Team != PlayerTeam.White &&
            player.Team != PlayerTeam.Black)
        {
            return ChessMoveResult.TeamNotSelected;
        }

        if (player.Team != _currentTurn.Value)
        {
            return ChessMoveResult.NotYourTurn;
        }

        bool firstMoveOfTurn = !_sequenceActive.Value;

        if (_sequenceActive.Value &&
            (fromFile != _activeFile.Value || fromRank != _activeRank.Value))
        {
            return ChessMoveResult.MustContinueActiveStack;
        }

        List<int> movingStack = GetStackIndicesAt(fromFile, fromRank);

        if (movingStack.Count == 0)
        {
            return ChessMoveResult.NoPieceOnSource;
        }

        int movementDepth = _sequenceActive.Value
            ? _activeMoveDepth.Value
            : GetStackCursor(movingStack);
        int movementPieceIndex = FindPieceIndexAtDepth(
            movingStack,
            movementDepth);

        if (movementPieceIndex < 0)
        {
            return ChessMoveResult.IllegalMove;
        }

        NetworkChessPieceState movementPiece = _pieces[movementPieceIndex];

        if (movementPiece.OwnerTeam != player.Team)
        {
            return ChessMoveResult.NotYourStack;
        }

        List<int> targetStack = GetStackIndicesAt(toFile, toRank);

        if (targetStack.Count > 0 &&
            _pieces[targetStack[0]].OwnerTeam == player.Team)
        {
            return ChessMoveResult.OwnStackOnDestination;
        }


        bool targetContainsKing = StackContainsPieceType(
            targetStack,
            ChessPieceType.King);

        if (targetContainsKing && !firstMoveOfTurn)
        {
            return ChessMoveResult.KingCaptureMustBeFirstMove;
        }

        if (movementPiece.PieceType == ChessPieceType.King &&
            targetStack.Count > 1)
        {
            return ChessMoveResult.KingCannotCaptureStack;
        }

        if (!IsLegalBasicMove(
                movementPiece,
                targetStack.Count > 0,
                fromFile,
                fromRank,
                toFile,
                toRank))
        {
            return ChessMoveResult.IllegalMove;
        }

        if (!HasAnyLegalMove(
                movementPiece,
                fromFile,
                fromRank,
                firstMoveOfTurn))
        {
            return ChessMoveResult.StackHasNoLegalMove;
        }

        bool captured = targetStack.Count > 0;
        bool pawnReachedPromotionRank =
            movementPiece.PieceType == ChessPieceType.Pawn &&
            IsPromotionRank(player.Team, toRank);

        MoveStack(
            movingStack,
            targetStack,
            movementPieceIndex,
            player.Team,
            toFile,
            toRank);

        _lastMove.Value = new FixedString64Bytes(
            $"{player.Team}: " +
            $"{GetSquareName(fromFile, fromRank)}" +
            (captured ? "x" : "-") +
            GetSquareName(toFile, toRank));

        bool turnShouldEnd = captured;

        if (targetContainsKing)
        {
            _winner.Value = player.Team;
            ClearServerSequence();
            turnShouldEnd = false;
        }
        else if (captured)
        {
            List<int> mergedStack = GetStackIndicesAt(toFile, toRank);
            SetStackCursor(mergedStack, 0);
            ClearServerSequence();
        }
        else
        {
            int nextMoveDepth = movementDepth + 1;

            if (nextMoveDepth >= movingStack.Count)
            {
                SetStackCursor(movingStack, 0);
                ClearServerSequence();
                turnShouldEnd = true;
            }
            else
            {
                SetStackCursor(movingStack, nextMoveDepth);
                _sequenceActive.Value = true;
                _activeFile.Value = (byte)toFile;
                _activeRank.Value = (byte)toRank;
                _activeMoveDepth.Value = (byte)nextMoveDepth;

                int nextPieceIndex = FindPieceIndexAtDepth(
                    movingStack,
                    nextMoveDepth);
                NetworkChessPieceState nextPiece = _pieces[nextPieceIndex];

                if (!HasAnyLegalMove(
                        nextPiece,
                        toFile,
                        toRank,
                        firstMoveOfTurn: false))
                {
                    ClearServerSequence();
                    turnShouldEnd = true;
                }
            }
        }

        if (pawnReachedPromotionRank && !targetContainsKing)
        {
            _promotionPieceId.Value = movementPiece.Id;
            _promotionEndsTurn.Value = turnShouldEnd;
            _promotionPending.Value = true;
        }
        else if (turnShouldEnd)
        {
            EndTurn(player.Team);
        }

        return ChessMoveResult.Accepted;
    }

    private void MoveStack(
        List<int> movingStack,
        List<int> targetStack,
        int movementPieceIndex,
        PlayerTeam newOwner,
        int toFile,
        int toRank)
    {
        int targetDepthOffset = targetStack.Count;

        foreach (int index in targetStack)
        {
            NetworkChessPieceState piece = _pieces[index];
            piece.OwnerTeam = newOwner;
            _pieces[index] = piece;
        }

        foreach (int index in movingStack)
        {
            NetworkChessPieceState piece = _pieces[index];
            piece.OwnerTeam = newOwner;
            piece.File = (byte)toFile;
            piece.Rank = (byte)toRank;
            piece.StackDepth = (byte)(targetDepthOffset + piece.StackDepth);

            if (index == movementPieceIndex)
            {
                piece.HasMoved = true;
            }

            _pieces[index] = piece;
        }
    }

    private void SetStackCursor(List<int> stack, int nextMoveDepth)
    {
        byte cursor = (byte)Mathf.Clamp(nextMoveDepth, 0, byte.MaxValue);

        foreach (int index in stack)
        {
            NetworkChessPieceState piece = _pieces[index];
            piece.NextMoveDepth = cursor;
            _pieces[index] = piece;
        }
    }

    private int GetStackCursor(List<int> stack)
    {
        if (stack.Count == 0)
        {
            return 0;
        }

        int cursor = _pieces[stack[0]].NextMoveDepth;
        return cursor < stack.Count ? cursor : 0;
    }

    private int FindPieceIndexAtDepth(List<int> stack, int depth)
    {
        foreach (int index in stack)
        {
            if (_pieces[index].StackDepth == depth)
            {
                return index;
            }
        }

        return -1;
    }

    private bool HasAnyLegalMove(
        NetworkChessPieceState movementPiece,
        int fromFile,
        int fromRank,
        bool firstMoveOfTurn)
    {
        for (int toRank = 0; toRank < 8; toRank++)
        {
            for (int toFile = 0; toFile < 8; toFile++)
            {
                List<int> targetStack = GetStackIndicesAt(toFile, toRank);

                if (targetStack.Count > 0 &&
                    _pieces[targetStack[0]].OwnerTeam == movementPiece.OwnerTeam)
                {
                    continue;
                }

                bool targetContainsKing = StackContainsPieceType(
                    targetStack,
                    ChessPieceType.King);

                if (targetContainsKing && !firstMoveOfTurn)
                {
                    continue;
                }

                if (movementPiece.PieceType == ChessPieceType.King &&
                    targetStack.Count > 1)
                {
                    continue;
                }

                if (IsLegalBasicMove(
                        movementPiece,
                        targetStack.Count > 0,
                        fromFile,
                        fromRank,
                        toFile,
                        toRank))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void ClearServerSequence()
    {
        _sequenceActive.Value = false;
        _activeFile.Value = 0;
        _activeRank.Value = 0;
        _activeMoveDepth.Value = 0;
    }

    private void EndTurn(PlayerTeam teamThatMoved)
    {
        ClearServerSequence();
        _currentTurn.Value = GetOpponent(teamThatMoved);
    }

    private static bool IsPromotionRank(PlayerTeam team, int rank)
    {
        return team == PlayerTeam.White ? rank == 7 : rank == 0;
    }

    private bool IsLegalBasicMove(
        NetworkChessPieceState movementPiece,
        bool destinationOccupied,
        int fromFile,
        int fromRank,
        int toFile,
        int toRank)
    {
        int fileDelta = toFile - fromFile;
        int rankDelta = toRank - fromRank;
        int absoluteFileDelta = Mathf.Abs(fileDelta);
        int absoluteRankDelta = Mathf.Abs(rankDelta);

        return movementPiece.PieceType switch
        {
            ChessPieceType.Pawn => IsLegalPawnMove(
                movementPiece,
                destinationOccupied,
                fromFile,
                fromRank,
                toFile,
                toRank),

            ChessPieceType.Knight =>
                absoluteFileDelta == 1 && absoluteRankDelta == 2 ||
                absoluteFileDelta == 2 && absoluteRankDelta == 1,

            ChessPieceType.Bishop =>
                absoluteFileDelta == absoluteRankDelta &&
                absoluteFileDelta > 0 &&
                IsPathClear(fromFile, fromRank, toFile, toRank),

            ChessPieceType.Rook =>
                (fileDelta == 0) != (rankDelta == 0) &&
                IsPathClear(fromFile, fromRank, toFile, toRank),

            ChessPieceType.Queen =>
                (((fileDelta == 0) != (rankDelta == 0)) ||
                 absoluteFileDelta == absoluteRankDelta &&
                 absoluteFileDelta > 0) &&
                IsPathClear(fromFile, fromRank, toFile, toRank),

            ChessPieceType.King =>
                Mathf.Max(absoluteFileDelta, absoluteRankDelta) == 1,

            _ => false
        };
    }

    private bool IsLegalPawnMove(
        NetworkChessPieceState movementPiece,
        bool destinationOccupied,
        int fromFile,
        int fromRank,
        int toFile,
        int toRank)
    {
        int direction = movementPiece.OwnerTeam == PlayerTeam.White ? 1 : -1;
        int startingRank = movementPiece.OwnerTeam == PlayerTeam.White ? 1 : 6;
        int fileDelta = toFile - fromFile;
        int rankDelta = toRank - fromRank;

        if (fileDelta == 0)
        {
            if (destinationOccupied)
            {
                return false;
            }

            if (rankDelta == direction)
            {
                return true;
            }

            if (fromRank == startingRank &&
                !movementPiece.HasMoved &&
                rankDelta == direction * 2)
            {
                int middleRank = fromRank + direction;
                return !IsSquareOccupied(fromFile, middleRank);
            }

            return false;
        }

        return Mathf.Abs(fileDelta) == 1 &&
               rankDelta == direction &&
               destinationOccupied;
    }

    private bool IsPathClear(
        int fromFile,
        int fromRank,
        int toFile,
        int toRank)
    {
        int fileStep = Math.Sign(toFile - fromFile);
        int rankStep = Math.Sign(toRank - fromRank);
        int currentFile = fromFile + fileStep;
        int currentRank = fromRank + rankStep;

        while (currentFile != toFile || currentRank != toRank)
        {
            if (IsSquareOccupied(currentFile, currentRank))
            {
                return false;
            }

            currentFile += fileStep;
            currentRank += rankStep;
        }

        return true;
    }

    private bool IsSquareOccupied(int file, int rank)
    {
        for (int index = 0; index < _pieces.Count; index++)
        {
            NetworkChessPieceState piece = _pieces[index];

            if (piece.File == file && piece.Rank == rank)
            {
                return true;
            }
        }

        return false;
    }

    private List<int> GetStackIndicesAt(int file, int rank)
    {
        List<int> indices = new();

        for (int index = 0; index < _pieces.Count; index++)
        {
            NetworkChessPieceState piece = _pieces[index];

            if (piece.File == file && piece.Rank == rank)
            {
                indices.Add(index);
            }
        }

        indices.Sort(
            (left, right) =>
                _pieces[left].StackDepth.CompareTo(_pieces[right].StackDepth));
        return indices;
    }

    private int FindTopPieceIndexAt(int file, int rank)
    {
        List<int> stack = GetStackIndicesAt(file, rank);
        return stack.Count == 0 ? -1 : stack[^1];
    }

    private int FindBottomPieceIndexAt(int file, int rank)
    {
        List<int> stack = GetStackIndicesAt(file, rank);
        return stack.Count == 0 ? -1 : stack[0];
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

    private int FindIndexWithLowestDepth(List<int> stack)
    {
        int result = stack[0];

        foreach (int index in stack)
        {
            if (_pieces[index].StackDepth < _pieces[result].StackDepth)
            {
                result = index;
            }
        }

        return result;
    }

    private bool StackContainsPieceType(
        List<int> stack,
        ChessPieceType pieceType)
    {
        foreach (int index in stack)
        {
            if (_pieces[index].PieceType == pieceType)
            {
                return true;
            }
        }

        return false;
    }

    private int CountStacks()
    {
        bool[] occupiedSquares = new bool[64];
        int count = 0;

        foreach (NetworkChessPieceState piece in _pieces)
        {
            int squareIndex = GetSquareIndex(piece.File, piece.Rank);

            if (!occupiedSquares[squareIndex])
            {
                occupiedSquares[squareIndex] = true;
                count++;
            }
        }

        return count;
    }

    private int GetLargestStackSize()
    {
        int[] stackSizes = new int[64];
        int largest = 0;

        foreach (NetworkChessPieceState piece in _pieces)
        {
            int squareIndex = GetSquareIndex(piece.File, piece.Rank);
            stackSizes[squareIndex]++;
            largest = Mathf.Max(largest, stackSizes[squareIndex]);
        }

        return largest;
    }

    private int CountOwnedPieces(PlayerTeam team)
    {
        int count = 0;

        foreach (NetworkChessPieceState piece in _pieces)
        {
            if (piece.OwnerTeam == team)
            {
                count++;
            }
        }

        return count;
    }

    private bool HasSelectedSquare =>
        IsValidSquare(_selectedFile, _selectedRank);

    private static int GetSquareIndex(int file, int rank)
    {
        return rank * 8 + file;
    }

    private static bool TryParseSquare(
        string text,
        out int file,
        out int rank)
    {
        string normalized = text.Trim().ToLowerInvariant();

        if (normalized.Length == 2)
        {
            file = normalized[0] - 'a';
            rank = normalized[1] - '1';
            return IsValidSquare(file, rank);
        }

        file = -1;
        rank = -1;
        return false;
    }

    private static bool IsValidSquare(int file, int rank)
    {
        return (uint)file < 8 && (uint)rank < 8;
    }

    private static string GetSquareName(int file, int rank)
    {
        return $"{(char)('a' + file)}{rank + 1}";
    }

    private static PlayerTeam GetOpponent(PlayerTeam team)
    {
        return team == PlayerTeam.White
            ? PlayerTeam.Black
            : PlayerTeam.White;
    }

    private static bool IsValidPromotionType(ChessPieceType pieceType)
    {
        return pieceType == ChessPieceType.Queen ||
               pieceType == ChessPieceType.Rook ||
               pieceType == ChessPieceType.Bishop ||
               pieceType == ChessPieceType.Knight;
    }

    private static string GetMoveResultMessage(ChessMoveResult result)
    {
        return result switch
        {
            ChessMoveResult.Accepted => "Move accepted.",
            ChessMoveResult.InvalidSquare => "Use squares from a1 to h8.",
            ChessMoveResult.PlayerNotFound => "Network player was not found.",
            ChessMoveResult.TeamNotSelected => "Choose White or Black first.",
            ChessMoveResult.NotYourTurn => "It is not your team's turn.",
            ChessMoveResult.NoPieceOnSource => "There is no stack on that square.",
            ChessMoveResult.NotYourStack => "That stack belongs to the other team.",
            ChessMoveResult.OwnStackOnDestination => "Your stack occupies the destination.",
            ChessMoveResult.IllegalMove => "That piece cannot legally move there.",
            ChessMoveResult.MustContinueActiveStack => "Continue with your team's active stack.",
            ChessMoveResult.StackHasNoLegalMove => "The next piece in this stack has no legal move.",
            ChessMoveResult.KingCaptureMustBeFirstMove => "A king can only be captured on the first move of the turn.",
            ChessMoveResult.KingCannotCaptureStack => "A king cannot capture a stack of multiple pieces.",
            ChessMoveResult.PromotionRequired => "Choose a promotion piece before moving again.",
            ChessMoveResult.GameAlreadyFinished => "The game has already finished.",
            _ => "Move rejected."
        };
    }
}
