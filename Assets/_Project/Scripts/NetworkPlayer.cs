using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum PlayerTeam
{
    Unassigned,
    White,
    Black
}

public sealed class NetworkPlayer : NetworkBehaviour
{
    private const int MaxPlayersPerTeam = 2;
    private const float AvatarPoseSendInterval = 1f / 20f;
    private const float AvatarHeightInSquares = 0.68f;
    private const float AvatarRadiusInSquares = 0.16f;

    private static readonly List<NetworkPlayer> SpawnedPlayers = new();

    private readonly NetworkVariable<PlayerTeam> _team = new(
        PlayerTeam.Unassigned,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _matchStarted = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<Vector3> _avatarBoardPose = new(
        new Vector3(3.5f, 0f, 3.5f),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<float> _avatarYaw = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private string _selectionStatus = "Choose a team.";
    private ChessPieceSpawner _pieceSpawner;
    private GameObject _avatarCapsule;
    private Renderer _avatarRenderer;
    private Material _avatarMaterial;
    private Vector3 _localAvatarBoardPose = new(3.5f, 0f, 3.5f);
    private float _localAvatarYaw;
    private float _nextAvatarPoseSendTime;
    private bool _hasLocalAvatarPose;
    private bool _avatarTransformInitialized;

    public PlayerTeam Team => _team.Value;
    public string SelectionStatus => _selectionStatus;
    public bool IsOwnedByMe => IsOwner;
    public string DisplayName => $"Player {OwnerClientId + 1:00}";
    public static IReadOnlyList<NetworkPlayer> Players => SpawnedPlayers;

    public static bool MatchStarted
    {
        get
        {
            foreach (var player in SpawnedPlayers)
            {
                if (player != null && player.IsSpawned && player._matchStarted.Value)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public static NetworkPlayer LocalPlayer
    {
        get
        {
            foreach (var player in SpawnedPlayers)
            {
                if (player != null && player.IsSpawned && player.IsOwner)
                {
                    return player;
                }
            }

            return null;
        }
    }

    public static bool TryGetByClientId(
        ulong clientId,
        out NetworkPlayer networkPlayer)
    {
        foreach (var player in SpawnedPlayers)
        {
            if (player != null &&
                player.IsSpawned &&
                player.OwnerClientId == clientId)
            {
                networkPlayer = player;
                return true;
            }
        }

        networkPlayer = null;
        return false;
    }

    public override void OnNetworkSpawn()
    {
        SpawnedPlayers.Add(this);
        SpawnedPlayers.Sort(
            (left, right) => left.OwnerClientId.CompareTo(right.OwnerClientId));

        if (IsServer && _team.Value == PlayerTeam.Unassigned)
        {
            AssignTeamAutomatically();
        }

        _team.OnValueChanged += HandleTeamChanged;
        EnsureAvatarCapsule();
        UpdateAvatarAppearance();

        Debug.Log(
            $"NetworkPlayer spawned. " +
            $"Owner Client ID: {OwnerClientId}, " +
            $"Team: {_team.Value}, " +
            $"Is Owner: {IsOwner}, " +
            $"Is Server: {IsServer}");
    }

    public override void OnNetworkDespawn()
    {
        _team.OnValueChanged -= HandleTeamChanged;
        SpawnedPlayers.Remove(this);

        if (_avatarCapsule != null)
        {
            Destroy(_avatarCapsule);
        }

        if (_avatarMaterial != null)
        {
            Destroy(_avatarMaterial);
        }
    }

    private void LateUpdate()
    {
        if (!IsSpawned)
        {
            return;
        }

        EnsureAvatarCapsule();
        UpdateAvatarTransform();
    }

    /// <summary>
    /// Publishes the owner-controlled capsule pose. Height is measured from the
    /// board in square units so later falling-item logic can use the same space.
    /// </summary>
    public void SetLocalAvatarPose(
        float file,
        float rank,
        float heightInSquares,
        float yaw)
    {
        if (!IsSpawned || !IsOwner)
        {
            return;
        }

        _localAvatarBoardPose = new Vector3(
            file,
            Mathf.Max(0f, heightInSquares),
            rank);
        _localAvatarYaw = Mathf.Repeat(yaw, 360f);
        _hasLocalAvatarPose = true;
        UpdateAvatarTransform();

        if (Time.unscaledTime < _nextAvatarPoseSendTime)
        {
            return;
        }

        _nextAvatarPoseSendTime = Time.unscaledTime + AvatarPoseSendInterval;
        SubmitAvatarPoseRpc(_localAvatarBoardPose, _localAvatarYaw);
    }

    [Rpc(
        SendTo.Server,
        InvokePermission = RpcInvokePermission.Owner)]
    private void SubmitAvatarPoseRpc(Vector3 boardPose, float yaw)
    {
        ChessPieceSpawner spawner = ResolvePieceSpawner();
        float minimum = spawner != null
            ? spawner.GroundMinimumCoordinate
            : -0.5f - ChessPieceSpawner.DefaultBoardBorderWidthInSquares;
        float maximum = spawner != null
            ? spawner.GroundMaximumCoordinate
            : 7.5f + ChessPieceSpawner.DefaultBoardBorderWidthInSquares;

        boardPose.x = Mathf.Clamp(boardPose.x, minimum, maximum);
        boardPose.y = Mathf.Clamp(boardPose.y, 0f, 4f);
        boardPose.z = Mathf.Clamp(boardPose.z, minimum, maximum);
        _avatarBoardPose.Value = boardPose;
        _avatarYaw.Value = Mathf.Repeat(yaw, 360f);
    }

    private void EnsureAvatarCapsule()
    {
        if (_avatarCapsule != null || ResolvePieceSpawner() == null)
        {
            return;
        }

        _avatarCapsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        _avatarCapsule.name = $"{DisplayName} Capsule";
        _avatarCapsule.transform.SetParent(transform, worldPositionStays: true);
        _avatarRenderer = _avatarCapsule.GetComponent<Renderer>();

        Rigidbody rigidbody = _avatarCapsule.AddComponent<Rigidbody>();
        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;
        rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        // The owner's first-person camera sits inside the local capsule. Keep its
        // collider active for future falling pickups, but only render teammates
        // and opponents on that client.
        if (_avatarRenderer != null)
        {
            _avatarRenderer.enabled = !IsOwner;
        }

        UpdateAvatarAppearance();
        UpdateAvatarTransform();
    }

    private void UpdateAvatarTransform()
    {
        ChessPieceSpawner spawner = ResolvePieceSpawner();

        if (_avatarCapsule == null || spawner == null)
        {
            return;
        }

        Vector3 boardPose = IsOwner && _hasLocalAvatarPose
            ? _localAvatarBoardPose
            : _avatarBoardPose.Value;
        float yaw = IsOwner && _hasLocalAvatarPose
            ? _localAvatarYaw
            : _avatarYaw.Value;
        float squareSize = Mathf.Min(spawner.FileSpacing, spawner.RankSpacing);
        float capsuleHeight = AvatarHeightInSquares * squareSize;

        if (spawner.TryGetRepresentativePieceHeight(out float pieceHeight))
        {
            // Keep the doubled first-person eye height inside the upper part
            // of the correspondingly taller visible capsule.
            capsuleHeight = Mathf.Max(capsuleHeight, pieceHeight * 0.6f);
        }

        float capsuleDiameter = AvatarRadiusInSquares * 2f * squareSize;
        Vector3 groundPosition = spawner.GetBoardWorldPosition(
            boardPose.x,
            boardPose.z);
        Vector3 capsuleCentre = groundPosition + spawner.BoardUp *
            ((boardPose.y * squareSize) + capsuleHeight * 0.5f);
        Quaternion rotation = Quaternion.LookRotation(
            Quaternion.AngleAxis(yaw, spawner.BoardUp) * spawner.BoardForward,
            spawner.BoardUp);

        bool snapToPose = IsOwner || IsServer || !_avatarTransformInitialized;

        if (snapToPose)
        {
            _avatarCapsule.transform.SetPositionAndRotation(capsuleCentre, rotation);
        }
        else
        {
            float blend = 1f - Mathf.Exp(-18f * Time.deltaTime);
            _avatarCapsule.transform.SetPositionAndRotation(
                Vector3.Lerp(
                    _avatarCapsule.transform.position,
                    capsuleCentre,
                    blend),
                Quaternion.Slerp(
                    _avatarCapsule.transform.rotation,
                    rotation,
                    blend));
        }

        _avatarTransformInitialized = true;
        _avatarCapsule.transform.localScale = new Vector3(
            capsuleDiameter,
            capsuleHeight * 0.5f,
            capsuleDiameter);
    }

    private ChessPieceSpawner ResolvePieceSpawner()
    {
        if (_pieceSpawner == null)
        {
            _pieceSpawner = FindFirstObjectByType<ChessPieceSpawner>();
        }

        return _pieceSpawner;
    }

    private void HandleTeamChanged(PlayerTeam previousTeam, PlayerTeam newTeam)
    {
        UpdateAvatarAppearance();
    }

    private void UpdateAvatarAppearance()
    {
        if (_avatarRenderer == null)
        {
            return;
        }

        if (_avatarMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            _avatarMaterial = new Material(shader)
            {
                name = $"{DisplayName} Capsule Material"
            };
            _avatarRenderer.sharedMaterial = _avatarMaterial;
        }

        Color teamColor = Team switch
        {
            PlayerTeam.White => new Color(0.92f, 0.95f, 1f, 1f),
            PlayerTeam.Black => new Color(0.08f, 0.12f, 0.2f, 1f),
            _ => new Color(0.45f, 0.5f, 0.55f, 1f)
        };
        _avatarMaterial.color = teamColor;
    }

    public void SelectTeam(PlayerTeam requestedTeam)
    {
        if (!IsSpawned || !IsOwner)
        {
            return;
        }

        RequestTeamRpc(requestedTeam);
    }

    public void StartMatch()
    {
        if (!IsSpawned || !IsOwner || !IsServer)
        {
            return;
        }

        RequestStartMatchRpc();
    }

    public void ReturnToLobby()
    {
        if (!IsSpawned || !IsOwner || !IsServer)
        {
            return;
        }

        RequestReturnToLobbyRpc();
    }

    [Rpc(
        SendTo.Server,
        InvokePermission = RpcInvokePermission.Owner)]
    private void RequestTeamRpc(PlayerTeam requestedTeam)
    {
        bool isValidTeam =
            requestedTeam == PlayerTeam.Unassigned ||
            requestedTeam == PlayerTeam.White ||
            requestedTeam == PlayerTeam.Black;

        if (!isValidTeam)
        {
            TeamSelectionResultRpc(false, requestedTeam);
            return;
        }

        if (requestedTeam != PlayerTeam.Unassigned &&
            CountOtherPlayersOnTeam(requestedTeam) >= MaxPlayersPerTeam)
        {
            TeamSelectionResultRpc(false, requestedTeam);
            return;
        }

        _team.Value = requestedTeam;
        TeamSelectionResultRpc(true, requestedTeam);
    }

    [Rpc(
        SendTo.Owner,
        InvokePermission = RpcInvokePermission.Server)]
    private void TeamSelectionResultRpc(
        bool accepted,
        PlayerTeam requestedTeam)
    {
        _selectionStatus = accepted
            ? $"Selected {requestedTeam}."
            : $"{requestedTeam} team is full.";
    }

    [Rpc(
        SendTo.Server,
        InvokePermission = RpcInvokePermission.Owner)]
    private void RequestStartMatchRpc()
    {
        if (!IsServer)
        {
            return;
        }

        NetworkChessGame chessGame = FindFirstObjectByType<NetworkChessGame>();

        if (chessGame == null || !chessGame.ResetGame())
        {
            Debug.LogError(
                "Cannot start the match because NetworkChessGame is not ready.");
            return;
        }

        SetMatchStartedForAll(true);
    }

    [Rpc(
        SendTo.Server,
        InvokePermission = RpcInvokePermission.Owner)]
    private void RequestReturnToLobbyRpc()
    {
        if (IsServer)
        {
            SetMatchStartedForAll(false);
        }
    }

    private static void SetMatchStartedForAll(bool started)
    {
        foreach (NetworkPlayer player in SpawnedPlayers)
        {
            if (player != null && player.IsSpawned && player.IsServer)
            {
                player._matchStarted.Value = started;
            }
        }
    }

    private int CountOtherPlayersOnTeam(PlayerTeam team)
    {
        int count = 0;

        foreach (var player in SpawnedPlayers)
        {
            if (player != null &&
                player != this &&
                player.IsSpawned &&
                player.Team == team)
            {
                count++;
            }
        }

        return count;
    }

    private void AssignTeamAutomatically()
    {
        int whiteCount = CountOtherPlayersOnTeam(PlayerTeam.White);
        int blackCount = CountOtherPlayersOnTeam(PlayerTeam.Black);

        PlayerTeam assignedTeam;

        if (whiteCount >= MaxPlayersPerTeam && blackCount >= MaxPlayersPerTeam)
        {
            Debug.LogWarning(
                $"Could not automatically assign Player {OwnerClientId + 1:00}: " +
                "both teams are full.");
            return;
        }

        if (whiteCount >= MaxPlayersPerTeam)
        {
            assignedTeam = PlayerTeam.Black;
        }
        else if (blackCount >= MaxPlayersPerTeam)
        {
            assignedTeam = PlayerTeam.White;
        }
        else
        {
            // White wins an empty or tied lobby. Otherwise choose the smaller team.
            assignedTeam = whiteCount <= blackCount
                ? PlayerTeam.White
                : PlayerTeam.Black;
        }

        _team.Value = assignedTeam;

        Debug.Log(
            $"Automatically assigned Player {OwnerClientId + 1:00} to " +
            $"{assignedTeam} (White: {whiteCount}, Black: {blackCount}).");
    }
}
