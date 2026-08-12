using System;
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
    [Header("게임 모드 미지정 시 구버전 대체 설정")]
    [SerializeField, HideInInspector, Min(1)] private int maximumPlayersPerTeam = 2;
    [SerializeField, HideInInspector, Range(1f, 60f)] private float avatarPoseUpdatesPerSecond = 20f;
    [SerializeField, HideInInspector, Min(0.1f)] private float avatarHeightInSquares = 0.68f;
    [SerializeField, HideInInspector, Min(0.01f)] private float avatarRadiusInSquares = 0.16f;
    [SerializeField, HideInInspector, Min(0f)] private float maximumAvatarHeightInSquares = 4f;
    [SerializeField, HideInInspector] private Color whiteAvatarColor = new(0.92f, 0.95f, 1f, 1f);
    [SerializeField, HideInInspector] private Color blackAvatarColor = new(0.08f, 0.12f, 0.2f, 1f);
    [SerializeField, HideInInspector] private Color unassignedAvatarColor = new(0.45f, 0.5f, 0.55f, 1f);

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

    private readonly NetworkVariable<bool> _isEliminated = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<double> _captureRespawnEndServerTime = new(
        0d,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private string _selectionStatus = "Choose a team.";
    private ChessPieceSpawner _pieceSpawner;
    private NetworkChessGame _chessGame;
    private GameObject _avatarCapsule;
    private Renderer _avatarRenderer;
    private Material _avatarMaterial;
    private GameObject _avatarKingVisual;
    private GameObject _avatarKingPrefab;
    private Renderer[] _avatarKingRenderers = Array.Empty<Renderer>();
    private PlayerTeam _avatarKingTeam = PlayerTeam.Unassigned;
    private Vector3 _localAvatarBoardPose = new(3.5f, 0f, 3.5f);
    private float _localAvatarYaw;
    private float _nextAvatarPoseSendTime;
    private bool _hasLocalAvatarPose;
    private bool _avatarTransformInitialized;
    private bool _serverAvatarRingOutStarted;

    public PlayerTeam Team => _team.Value;
    public bool IsEliminated => _isEliminated.Value;
    public bool HasCaptureRespawnScheduled =>
        _isEliminated.Value && _captureRespawnEndServerTime.Value > 0d;
    public float RemainingCaptureRespawnTime =>
        !HasCaptureRespawnScheduled || NetworkManager == null
            ? 0f
            : Mathf.Max(
                0f,
                (float)(_captureRespawnEndServerTime.Value -
                    NetworkManager.ServerTime.Time));
    public Vector3 AvatarBoardPose => IsOwner && _hasLocalAvatarPose
        ? _localAvatarBoardPose
        : _avatarBoardPose.Value;
    public string SelectionStatus => _selectionStatus;
    public bool IsOwnedByMe => IsOwner;
    public bool IsUsingKingAvatarModel => _avatarKingVisual != null;
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

    public bool TryGetAvatarWorldBounds(out Bounds bounds)
    {
        bounds = default;

        if (_avatarKingVisual != null)
        {
            bool foundRenderer = false;

            foreach (Renderer renderer in _avatarKingRenderers)
            {
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!foundRenderer)
                {
                    bounds = renderer.bounds;
                    foundRenderer = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return foundRenderer && bounds.size.sqrMagnitude > 0.0001f;
        }

        if (_avatarRenderer == null || !_avatarRenderer.enabled)
        {
            return false;
        }

        bounds = _avatarRenderer.bounds;
        return bounds.size.sqrMagnitude > 0.0001f;
    }

    public bool TryGetKingAvatarWorldHeight(Vector3 worldUp, out float height)
    {
        height = 0f;

        if (_avatarKingVisual == null || worldUp.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        Vector3 up = worldUp.normalized;
        float minimum = float.PositiveInfinity;
        float maximum = float.NegativeInfinity;

        foreach (Renderer renderer in _avatarKingRenderers)
        {
            if (renderer == null)
            {
                continue;
            }

            Bounds rendererBounds = renderer.bounds;
            Vector3 extents = rendererBounds.extents;
            float projectedCentre = Vector3.Dot(rendererBounds.center, up);
            float projectedExtent =
                Mathf.Abs(up.x) * extents.x +
                Mathf.Abs(up.y) * extents.y +
                Mathf.Abs(up.z) * extents.z;
            minimum = Mathf.Min(minimum, projectedCentre - projectedExtent);
            maximum = Mathf.Max(maximum, projectedCentre + projectedExtent);
        }

        height = maximum - minimum;
        return !float.IsNaN(height) &&
            !float.IsInfinity(height) &&
            height > 0.001f;
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
        _isEliminated.OnValueChanged += HandleEliminatedChanged;
        EnsureAvatarCapsule();
        UpdateAvatarAppearance();

        // Debug.Log(
        //     $"NetworkPlayer spawned. " +
        //     $"Owner Client ID: {OwnerClientId}, " +
        //     $"Team: {_team.Value}, " +
        //     $"Is Owner: {IsOwner}, " +
        //     $"Is Server: {IsServer}");
    }

    public override void OnNetworkDespawn()
    {
        _team.OnValueChanged -= HandleTeamChanged;
        _isEliminated.OnValueChanged -= HandleEliminatedChanged;
        SpawnedPlayers.Remove(this);

        if (_avatarCapsule != null)
        {
            Destroy(_avatarCapsule);
        }

        DestroyKingAvatarVisual();

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

        PlayerCommanderSettings settings = ResolvePlayerSettings();
        bool allowPlayerKingFall = IsPlayerCommanderKing() &&
            (settings?.PlayerKingCanFallOffBoard ?? true);
        float minimumHeight = allowPlayerKingFall
            ? -(settings?.PlayerKingEliminationDepthInSquares ?? 2.5f)
            : 0f;
        _localAvatarBoardPose = new Vector3(
            file,
            Mathf.Clamp(
                heightInSquares,
                minimumHeight,
                ResolveMaximumAvatarHeight()),
            rank);
        _localAvatarYaw = Mathf.Repeat(yaw, 360f);
        _hasLocalAvatarPose = true;
        UpdateAvatarTransform();

        if (Time.unscaledTime < _nextAvatarPoseSendTime)
        {
            return;
        }

        _nextAvatarPoseSendTime = Time.unscaledTime + ResolvePoseSendInterval();
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
        PlayerCommanderSettings settings = ResolvePlayerSettings();
        bool allowPlayerKingFall = IsPlayerCommanderKing() &&
            (settings?.PlayerKingCanFallOffBoard ?? true);
        float horizontalMargin = allowPlayerKingFall
            ? settings?.PlayerKingMaximumOutOfBoundsDistanceInSquares ?? 4f
            : 0f;
        float eliminationDepth = settings?
            .PlayerKingEliminationDepthInSquares ?? 2.5f;

        boardPose.x = Mathf.Clamp(
            boardPose.x,
            minimum - horizontalMargin,
            maximum + horizontalMargin);
        boardPose.y = Mathf.Clamp(
            boardPose.y,
            allowPlayerKingFall ? -eliminationDepth : 0f,
            ResolveMaximumAvatarHeight());
        boardPose.z = Mathf.Clamp(
            boardPose.z,
            minimum - horizontalMargin,
            maximum + horizontalMargin);

        if (allowPlayerKingFall &&
            (boardPose.x < minimum ||
             boardPose.x > maximum ||
             boardPose.z < minimum ||
             boardPose.z > maximum))
        {
            _serverAvatarRingOutStarted = true;
        }

        _avatarBoardPose.Value = boardPose;
        _avatarYaw.Value = Mathf.Repeat(yaw, 360f);

        if (_serverAvatarRingOutStarted &&
            boardPose.y <= -eliminationDepth + 0.001f)
        {
            SetEliminatedOnServer(true);
        }
    }

    private void EnsureAvatarCapsule()
    {
        ChessPieceSpawner spawner = ResolvePieceSpawner();

        if (spawner == null)
        {
            return;
        }

        if (_avatarCapsule == null)
        {
            _avatarCapsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _avatarCapsule.name = $"{DisplayName} Collision Capsule";
            _avatarCapsule.transform.SetParent(transform, worldPositionStays: true);
            _avatarRenderer = _avatarCapsule.GetComponent<Renderer>();

            Rigidbody rigidbody = _avatarCapsule.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.collisionDetectionMode =
                CollisionDetectionMode.ContinuousSpeculative;
        }

        EnsureAvatarVisual(spawner);
    }

    private void EnsureAvatarVisual(ChessPieceSpawner spawner)
    {
        bool useKingModel = ResolveChessGame()?.GameMode?.Victory.UsesPlayerCommander ==
            true;
        GameObject kingPrefab = useKingModel && Team != PlayerTeam.Unassigned
            ? spawner.GetKingPrefab(Team)
            : null;

        if (kingPrefab == null)
        {
            DestroyKingAvatarVisual();

            if (_avatarRenderer != null)
            {
                _avatarRenderer.enabled = !IsOwner && !_isEliminated.Value;
            }

            return;
        }

        if (_avatarRenderer != null)
        {
            _avatarRenderer.enabled = false;
        }

        if (_avatarKingVisual == null ||
            _avatarKingPrefab != kingPrefab ||
            _avatarKingTeam != Team)
        {
            DestroyKingAvatarVisual();
            _avatarKingVisual = Instantiate(kingPrefab);
            _avatarKingVisual.name = $"{DisplayName} {Team} Player King";
            _avatarKingVisual.transform.SetParent(transform, worldPositionStays: true);
            _avatarKingPrefab = kingPrefab;
            _avatarKingTeam = Team;
            _avatarKingRenderers = _avatarKingVisual.GetComponentsInChildren<Renderer>(
                includeInactive: true);

            foreach (Collider visualCollider in
                     _avatarKingVisual.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                visualCollider.enabled = false;
            }

            foreach (Rigidbody visualRigidbody in
                     _avatarKingVisual.GetComponentsInChildren<Rigidbody>(includeInactive: true))
            {
                visualRigidbody.isKinematic = true;
                visualRigidbody.useGravity = false;
            }

            _avatarTransformInitialized = false;
        }

        bool showKing = !IsOwner && !_isEliminated.Value;

        foreach (Renderer renderer in _avatarKingRenderers)
        {
            if (renderer != null)
            {
                renderer.enabled = showKing;
            }
        }
    }

    private void DestroyKingAvatarVisual()
    {
        if (_avatarKingVisual != null)
        {
            Destroy(_avatarKingVisual);
        }

        _avatarKingVisual = null;
        _avatarKingPrefab = null;
        _avatarKingRenderers = Array.Empty<Renderer>();
        _avatarKingTeam = PlayerTeam.Unassigned;
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
        float capsuleHeight = ResolveAvatarHeight() * squareSize;

        if (spawner.TryGetRepresentativePieceHeight(out float pieceHeight))
        {
            // Keep the doubled first-person eye height inside the upper part
            // of the correspondingly taller visible capsule.
            capsuleHeight = Mathf.Max(capsuleHeight, pieceHeight * 0.6f);
        }

        float capsuleDiameter = ResolveAvatarRadius() * 2f * squareSize;
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

        if (_avatarKingVisual != null && _avatarKingPrefab != null)
        {
            Vector3 kingPosition = groundPosition +
                spawner.BoardUp * (boardPose.y * squareSize);
            Quaternion kingRotation = spawner.GetPieceWorldRotation(
                Team,
                _avatarKingPrefab,
                yaw);

            if (snapToPose)
            {
                _avatarKingVisual.transform.SetPositionAndRotation(
                    kingPosition,
                    kingRotation);
            }
            else
            {
                float blend = 1f - Mathf.Exp(-18f * Time.deltaTime);
                _avatarKingVisual.transform.SetPositionAndRotation(
                    Vector3.Lerp(
                        _avatarKingVisual.transform.position,
                        kingPosition,
                        blend),
                    Quaternion.Slerp(
                        _avatarKingVisual.transform.rotation,
                        kingRotation,
                        blend));
            }
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

    private NetworkChessGame ResolveChessGame()
    {
        if (_chessGame == null || !_chessGame.IsSpawned)
        {
            _chessGame = FindFirstObjectByType<NetworkChessGame>();
        }

        return _chessGame;
    }

    private PlayerCommanderSettings ResolvePlayerSettings()
    {
        return ResolveChessGame()?.GetPlayerSettings();
    }

    private bool IsPlayerCommanderKing()
    {
        return ResolveChessGame()?.GameMode?.Victory.UsesPlayerCommander == true;
    }

    private int ResolveMaximumPlayersPerTeam()
    {
        return ResolvePlayerSettings()?.MaximumPlayersPerTeam ??
            Mathf.Max(1, maximumPlayersPerTeam);
    }

    private float ResolvePoseSendInterval()
    {
        return ResolvePlayerSettings()?.PoseSendInterval ??
            1f / Mathf.Clamp(avatarPoseUpdatesPerSecond, 1f, 60f);
    }

    private float ResolveAvatarHeight()
    {
        return ResolvePlayerSettings()?.AvatarHeightInSquares ??
            Mathf.Max(0.1f, avatarHeightInSquares);
    }

    private float ResolveAvatarRadius()
    {
        return ResolvePlayerSettings()?.AvatarRadiusInSquares ??
            Mathf.Max(0.01f, avatarRadiusInSquares);
    }

    private float ResolveMaximumAvatarHeight()
    {
        return ResolvePlayerSettings()?.MaximumPoseHeightInSquares ??
            Mathf.Max(0f, maximumAvatarHeightInSquares);
    }

    private void HandleTeamChanged(PlayerTeam previousTeam, PlayerTeam newTeam)
    {
        DestroyKingAvatarVisual();
        EnsureAvatarCapsule();
        UpdateAvatarAppearance();
    }

    private void HandleEliminatedChanged(bool wasEliminated, bool isEliminated)
    {
        UpdateAvatarAppearance();
    }

    /// <summary>
    /// Server-side extension point for damage, ring-out or objective systems that
    /// eliminate the player when the configured royal unit is the commander.
    /// </summary>
    public void ServerSetEliminated(bool eliminated)
    {
        if (IsSpawned && IsServer)
        {
            SetEliminatedOnServer(eliminated);
        }
    }

    private void SetEliminatedOnServer(bool eliminated)
    {
        if (!IsServer)
        {
            return;
        }

        if (eliminated &&
            _isEliminated.Value &&
            _captureRespawnEndServerTime.Value > 0d)
        {
            return;
        }

        _isEliminated.Value = eliminated;

        if (!eliminated)
        {
            _captureRespawnEndServerTime.Value = 0d;
            return;
        }

        NetworkChessGame game = ResolveChessGame();

        if (game != null &&
            game.IsCaptureKingRespawnEnabled &&
            IsPlayerCommanderKing() &&
            NetworkManager != null)
        {
            _captureRespawnEndServerTime.Value =
                NetworkManager.ServerTime.Time +
                game.CaptureKingRespawnDelaySeconds;
        }
        else
        {
            _captureRespawnEndServerTime.Value = 0d;
        }
    }

    public void ServerRespawnForCapture(Vector2 boardPosition, float yaw)
    {
        if (!IsSpawned || !IsServer)
        {
            return;
        }

        Vector3 boardPose = new(boardPosition.x, 0f, boardPosition.y);
        _avatarBoardPose.Value = boardPose;
        _avatarYaw.Value = Mathf.Repeat(yaw, 360f);
        _serverAvatarRingOutStarted = false;
        _captureRespawnEndServerTime.Value = 0d;
        _isEliminated.Value = false;
        ApplyCaptureRespawnRpc(boardPose, _avatarYaw.Value);
    }

    [Rpc(
        SendTo.Owner,
        InvokePermission = RpcInvokePermission.Server)]
    private void ApplyCaptureRespawnRpc(Vector3 boardPose, float yaw)
    {
        _localAvatarBoardPose = boardPose;
        _localAvatarYaw = yaw;
        _hasLocalAvatarPose = true;
        _serverAvatarRingOutStarted = false;
        FirstPersonCommanderController controller =
            FindFirstObjectByType<FirstPersonCommanderController>();
        controller?.ApplyCaptureRespawnPose(
            new Vector2(boardPose.x, boardPose.z),
            yaw);
    }

    public void ServerResetForMatch()
    {
        if (IsSpawned && IsServer)
        {
            _isEliminated.Value = false;
            _captureRespawnEndServerTime.Value = 0d;
            _serverAvatarRingOutStarted = false;
        }
    }

    private void UpdateAvatarAppearance()
    {
        if (_avatarKingVisual != null)
        {
            bool showKing = !IsOwner && !_isEliminated.Value;

            foreach (Renderer renderer in _avatarKingRenderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = showKing;
                }
            }

            return;
        }

        if (_avatarRenderer == null)
        {
            return;
        }

        _avatarRenderer.enabled = !IsOwner && !_isEliminated.Value;

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

        PlayerCommanderSettings playerSettings = ResolvePlayerSettings();
        Color teamColor = Team switch
        {
            PlayerTeam.White => playerSettings?.WhiteAvatarColor ?? whiteAvatarColor,
            PlayerTeam.Black => playerSettings?.BlackAvatarColor ?? blackAvatarColor,
            _ => playerSettings?.UnassignedAvatarColor ?? unassignedAvatarColor
        };

        if (_isEliminated.Value)
        {
            teamColor = Color.Lerp(teamColor, Color.gray, 0.75f);
        }

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
            CountOtherPlayersOnTeam(requestedTeam) >= ResolveMaximumPlayersPerTeam())
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
            // Debug.LogError(
            //     "Cannot start the match because NetworkChessGame is not ready.");
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

        int teamLimit = ResolveMaximumPlayersPerTeam();

        if (whiteCount >= teamLimit && blackCount >= teamLimit)
        {
            // Debug.LogWarning(
            //     $"Could not automatically assign Player {OwnerClientId + 1:00}: " +
            //     "both teams are full.");
            return;
        }

        if (whiteCount >= teamLimit)
        {
            assignedTeam = PlayerTeam.Black;
        }
        else if (blackCount >= teamLimit)
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

        // Debug.Log(
        //     $"Automatically assigned Player {OwnerClientId + 1:00} to " +
        //     $"{assignedTeam} (White: {whiteCount}, Black: {blackCount}).");
    }

    private void OnValidate()
    {
        maximumPlayersPerTeam = Mathf.Max(1, maximumPlayersPerTeam);
        avatarPoseUpdatesPerSecond = Mathf.Clamp(
            avatarPoseUpdatesPerSecond,
            1f,
            60f);
        avatarHeightInSquares = Mathf.Max(0.1f, avatarHeightInSquares);
        avatarRadiusInSquares = Mathf.Max(0.01f, avatarRadiusInSquares);
        maximumAvatarHeightInSquares = Mathf.Max(0f, maximumAvatarHeightInSquares);
    }
}
