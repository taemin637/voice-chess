using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class FirstPersonCommanderController : MonoBehaviour
{
    [SerializeField, HideInInspector, Min(0.1f)] private float moveSpeedInSquares = 5.0f;
    [SerializeField, HideInInspector, Min(0.01f)] private float mouseSensitivity = 0.08f;
    [SerializeField, HideInInspector, Range(-89f, 0f)] private float minimumPitch = -75f;
    [SerializeField, HideInInspector, Range(0f, 89f)] private float maximumPitch = 75f;
    [SerializeField, HideInInspector, Range(0.05f, 1f)] private float eyeHeightAsPieceFraction = 0.5f;

    [Header("플레이어 캡슐 물리")]
    [SerializeField, HideInInspector, Range(0.08f, 0.35f)]
    private float collisionRadiusInSquares = 0.16f;
    [SerializeField, HideInInspector, Min(0.1f)] private float jumpSpeedInSquares = 4.2f;
    [SerializeField, HideInInspector, Min(0.1f)] private float gravityInSquares = 15.0f;
    [SerializeField, HideInInspector, Min(0f)] private float playerKnockbackDrag = 2.2f;

    [Header("턴 조작")]
    [Tooltip("Used only when the Game Mode command mode is Alternating Turns.")]
    [SerializeField, HideInInspector] private Key endTurnKey = Key.Enter;

    private Camera _viewCamera;
    private ChessPieceSpawner _pieceSpawner;
    private NetworkChessGame _game;
    private float _file = 3.5f;
    private float _rank = 3.5f;
    private float _yaw;
    private float _pitch;
    private float _eyeHeightWorld;
    private float _heightInSquares;
    private float _verticalVelocity;
    private Vector2 _knockbackVelocity;
    private Vector2 _fallHorizontalVelocity;
    private NetworkPlayer _localNetworkPlayer;
    private bool _cameraConfigured;
    private bool _isGrounded = true;
    private bool _matchStartPoseApplied;
    private bool _wasMatchStarted;
    private bool _wasGameOver;
    private bool _isFallingOffBoard;
    private bool _isMouseChargeHeld;
    private float _mouseChargeStartedAt;
    private PlayerTeam _matchStartPoseTeam = PlayerTeam.Unassigned;
    private GUIStyle _respawnCountdownStyle;
    private GUIStyle _respawnCountdownShadowStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (SceneManager.GetActiveScene().name != "SampleScene" ||
            FindFirstObjectByType<FirstPersonCommanderController>() != null)
        {
            return;
        }

        GameObject commander = new("First Person Commander");
        commander.AddComponent<FirstPersonCommanderController>();
    }

    private void Update()
    {
        ResolveReferences();

        if (!_cameraConfigured && _viewCamera != null && _pieceSpawner != null)
        {
            ConfigureCamera();
        }

        if (!_cameraConfigured || _game == null || !_game.IsSpawned)
        {
            CancelMouseCharge();
            return;
        }

        bool matchStarted = NetworkPlayer.MatchStarted;
        bool isGameOver = _game.IsGameOver;
        NetworkPlayer localPlayer = ResolveLocalNetworkPlayer();
        PlayerTeam localTeam = localPlayer != null
            ? localPlayer.Team
            : PlayerTeam.Unassigned;
        bool roundRestarted = _wasGameOver && !isGameOver;

        if (matchStarted &&
            (!_matchStartPoseApplied ||
             !_wasMatchStarted ||
             roundRestarted ||
             _matchStartPoseTeam != localTeam))
        {
            ApplyMatchStartPose(localPlayer);
        }
        else if (!matchStarted)
        {
            _matchStartPoseApplied = false;
            _matchStartPoseTeam = PlayerTeam.Unassigned;
        }

        bool captureKingRespawning =
            _game.TryGetLocalCaptureKingRespawnRemaining(out _);
        bool gameplayInputActive =
            matchStarted &&
            !captureKingRespawning &&
            (localPlayer == null || !localPlayer.IsEliminated) &&
            !SessionManager.IsFrontEndVisible &&
            !InGameVoiceSettingsUI.IsBlockingGameplay;

        UpdateCursor(gameplayInputActive);

        if (captureKingRespawning)
        {
            CancelMouseCharge();
            UpdateCaptureRespawnCamera();
            _game.UpdateLocalChargeAim(default, PlayerTeam.Unassigned);
            _game.UpdateLocalVoiceGazeTarget(null, _file, _rank);
            _wasMatchStarted = matchStarted;
            _wasGameOver = isGameOver;
            return;
        }

        if (gameplayInputActive)
        {
            UpdateLook();
            UpdateTurnInput();
        }

        UpdateMovement(gameplayInputActive);
        UpdateCameraTransform();
        UpdateGazeTarget(gameplayInputActive);
        UpdateMouseChargeInput(gameplayInputActive);
        _wasMatchStarted = matchStarted;
        _wasGameOver = isGameOver;
    }

    private void ResolveReferences()
    {
        if (_viewCamera == null)
        {
            _viewCamera = Camera.main;
        }

        if (_pieceSpawner == null)
        {
            _pieceSpawner = FindFirstObjectByType<ChessPieceSpawner>();
        }

        if (_game == null || !_game.IsSpawned)
        {
            _game = FindFirstObjectByType<NetworkChessGame>();
        }
    }

    private void ConfigureCamera()
    {
        OrbitCamera orbitCamera = _viewCamera.GetComponent<OrbitCamera>();

        if (orbitCamera != null)
        {
            orbitCamera.enabled = false;
        }

        _viewCamera.nearClipPlane = Mathf.Min(_viewCamera.nearClipPlane, 0.01f);
        _file = 3.5f;
        _rank = 3.5f;
        transform.position = _pieceSpawner.GetBoardWorldPosition(_file, _rank);
        _cameraConfigured = true;
        UpdateCameraTransform();
    }

    private void ApplyMatchStartPose(NetworkPlayer localPlayer)
    {
        bool usePlayerKing =
            _game.GameMode?.Victory.UsesPlayerCommander == true;
        PlayerTeam team = localPlayer != null
            ? localPlayer.Team
            : PlayerTeam.Unassigned;

        if (usePlayerKing && team == PlayerTeam.Unassigned)
        {
            return;
        }

        Vector2 start = new(3.5f, 3.5f);
        float startYaw = 0f;
        PlayerCommanderSettings settings = _game.GetPlayerSettings();

        if (usePlayerKing)
        {
            start = settings?.GetPlayerKingFallbackStart(team) ??
                (team == PlayerTeam.Black
                    ? new Vector2(4f, 7f)
                    : new Vector2(4f, 0f));

            if (settings?.UseBoardKingPlacementAsPlayerStart != false &&
                _game.GameMode.TryGetConfiguredKingPosition(
                    team,
                    out Vector2 configuredKingPosition))
            {
                start = configuredKingPosition;
            }

            startYaw = settings?.GetPlayerKingStartYaw(team) ??
                (team == PlayerTeam.Black ? 180f : 0f);
        }

        _file = Mathf.Clamp(
            start.x,
            _pieceSpawner.GroundMinimumCoordinate,
            _pieceSpawner.GroundMaximumCoordinate);
        _rank = Mathf.Clamp(
            start.y,
            _pieceSpawner.GroundMinimumCoordinate,
            _pieceSpawner.GroundMaximumCoordinate);
        _yaw = startYaw;
        _pitch = 0f;
        _heightInSquares = 0f;
        _verticalVelocity = 0f;
        _knockbackVelocity = Vector2.zero;
        _fallHorizontalVelocity = Vector2.zero;
        _isGrounded = true;
        _isFallingOffBoard = false;
        _eyeHeightWorld = 0f;
        transform.position = _pieceSpawner.GetBoardWorldPosition(_file, _rank);
        localPlayer?.SetLocalAvatarPose(_file, _rank, 0f, _yaw);
        _matchStartPoseApplied = true;
        _matchStartPoseTeam = team;
        UpdateCameraTransform();
    }

    public void ApplyCaptureRespawnPose(Vector2 boardPosition, float yaw)
    {
        if (_pieceSpawner == null)
        {
            _pieceSpawner = FindFirstObjectByType<ChessPieceSpawner>();
        }

        if (_pieceSpawner == null)
        {
            return;
        }

        _file = Mathf.Clamp(boardPosition.x, 0f, 7f);
        _rank = Mathf.Clamp(boardPosition.y, 0f, 7f);
        _yaw = Mathf.Repeat(yaw, 360f);
        _pitch = 0f;
        _heightInSquares = 0f;
        _verticalVelocity = 0f;
        _knockbackVelocity = Vector2.zero;
        _fallHorizontalVelocity = Vector2.zero;
        _isGrounded = true;
        _isFallingOffBoard = false;
        transform.position = _pieceSpawner.GetBoardWorldPosition(_file, _rank);
        UpdateCameraTransform();
    }

    private void UpdateCursor(bool gameplayInputActive)
    {
        SetCursorLocked(gameplayInputActive);
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void UpdateLook()
    {
        if (Mouse.current == null)
        {
            return;
        }

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        PlayerCommanderSettings settings = _game?.GetPlayerSettings();
        float sensitivity = settings?.MouseSensitivity ?? mouseSensitivity;
        float minPitch = settings?.MinimumPitch ?? minimumPitch;
        float maxPitch = settings?.MaximumPitch ?? maximumPitch;
        _yaw += mouseDelta.x * sensitivity;
        _pitch = Mathf.Clamp(
            _pitch - mouseDelta.y * sensitivity,
            minPitch,
            maxPitch);
    }

    private void UpdateTurnInput()
    {
        Keyboard keyboard = Keyboard.current;
        PlayerCommanderSettings settings = _game?.GetPlayerSettings();
        Key resolvedEndTurnKey = settings?.EndTurnKey ?? endTurnKey;

        if (_game == null ||
            keyboard == null ||
            resolvedEndTurnKey == Key.None ||
            !keyboard[resolvedEndTurnKey].wasPressedThisFrame)
        {
            return;
        }

        _game.TryEndLocalTurn(out _);
    }

    private void UpdateMovement(bool gameplayInputActive)
    {
        Keyboard keyboard = Keyboard.current;
        Vector2 input = Vector2.zero;

        if (gameplayInputActive && keyboard != null)
        {
            input.x = (keyboard.dKey.isPressed ? 1f : 0f) -
                      (keyboard.aKey.isPressed ? 1f : 0f);
            input.y = (keyboard.wKey.isPressed ? 1f : 0f) -
                      (keyboard.sKey.isPressed ? 1f : 0f);
        }

        if (input.sqrMagnitude > 1f)
        {
            input.Normalize();
        }

        Vector3 horizontalForward = Quaternion.AngleAxis(
            _yaw,
            _pieceSpawner.BoardUp) * _pieceSpawner.BoardForward;
        Vector3 horizontalRight = Vector3.Cross(
            _pieceSpawner.BoardUp,
            horizontalForward).normalized;
        Vector3 desiredDirection =
            horizontalRight * input.x + horizontalForward * input.y;
        Vector2 desiredVelocity = new(
            Vector3.Dot(desiredDirection, _pieceSpawner.BoardRight),
            Vector3.Dot(desiredDirection, _pieceSpawner.BoardForward));
        PlayerCommanderSettings settings = _game?.GetPlayerSettings();
        desiredVelocity *= settings?.MoveSpeedInSquares ?? moveSpeedInSquares;

        float deltaTime = Time.deltaTime;
        float knockbackDrag = settings?.KnockbackDrag ?? playerKnockbackDrag;
        _knockbackVelocity *= Mathf.Exp(-knockbackDrag * deltaTime);
        Vector2 playerVelocity = _isFallingOffBoard
            ? _fallHorizontalVelocity
            : desiredVelocity + _knockbackVelocity;
        Vector2 playerPosition = new(_file, _rank);
        playerPosition += playerVelocity * deltaTime;

        NetworkPlayer localPlayer = ResolveLocalNetworkPlayer();
        PlayerTeam team = localPlayer != null
            ? localPlayer.Team
            : PlayerTeam.Unassigned;
        bool usePlayerKing =
            _game?.GameMode?.Victory.UsesPlayerCommander == true;
        bool canFallOffBoard = usePlayerKing &&
            (settings?.PlayerKingCanFallOffBoard ?? true);

        if (!_isFallingOffBoard)
        {
            _game.ResolvePlayerPieceCollisions(
                team,
                _heightInSquares,
                settings?.CollisionRadiusInSquares ?? collisionRadiusInSquares,
                usePlayerKing &&
                    (settings?.PlayerKingCollidesWithFriendlyPieces ?? true),
                ref playerPosition,
                ref playerVelocity);
            _knockbackVelocity = playerVelocity - desiredVelocity;

            if (canFallOffBoard && IsOutsideBoardGround(playerPosition))
            {
                BeginBoardFall(playerVelocity);
            }
        }

        if (_isFallingOffBoard)
        {
            UpdateBoardFall(deltaTime, settings);
        }
        else
        {
            UpdateJump(gameplayInputActive, keyboard, deltaTime);
        }

        if (canFallOffBoard)
        {
            float horizontalLimit = settings?
                .PlayerKingMaximumOutOfBoundsDistanceInSquares ?? 4f;
            _file = Mathf.Clamp(
                playerPosition.x,
                _pieceSpawner.GroundMinimumCoordinate - horizontalLimit,
                _pieceSpawner.GroundMaximumCoordinate + horizontalLimit);
            _rank = Mathf.Clamp(
                playerPosition.y,
                _pieceSpawner.GroundMinimumCoordinate - horizontalLimit,
                _pieceSpawner.GroundMaximumCoordinate + horizontalLimit);
        }
        else
        {
            _file = Mathf.Clamp(
                playerPosition.x,
                _pieceSpawner.GroundMinimumCoordinate,
                _pieceSpawner.GroundMaximumCoordinate);
            _rank = Mathf.Clamp(
                playerPosition.y,
                _pieceSpawner.GroundMinimumCoordinate,
                _pieceSpawner.GroundMaximumCoordinate);
        }
        float squareSize = Mathf.Min(
            _pieceSpawner.FileSpacing,
            _pieceSpawner.RankSpacing);
        transform.position = _pieceSpawner.GetBoardWorldPosition(_file, _rank) +
            _pieceSpawner.BoardUp * (_heightInSquares * squareSize);

        localPlayer?.SetLocalAvatarPose(
            _file,
            _rank,
            _heightInSquares,
            _yaw);
    }

    private bool IsOutsideBoardGround(Vector2 position)
    {
        return position.x < _pieceSpawner.GroundMinimumCoordinate ||
            position.x > _pieceSpawner.GroundMaximumCoordinate ||
            position.y < _pieceSpawner.GroundMinimumCoordinate ||
            position.y > _pieceSpawner.GroundMaximumCoordinate;
    }

    private void BeginBoardFall(Vector2 horizontalVelocity)
    {
        _isFallingOffBoard = true;
        _isGrounded = false;
        _verticalVelocity = Mathf.Min(0f, _verticalVelocity);
        _fallHorizontalVelocity = horizontalVelocity;
        _knockbackVelocity = Vector2.zero;
    }

    private void UpdateBoardFall(
        float deltaTime,
        PlayerCommanderSettings settings)
    {
        float gravity = settings?.PlayerKingFallGravityInSquares ??
            gravityInSquares;
        float eliminationDepth = settings?
            .PlayerKingEliminationDepthInSquares ?? 2.5f;
        _verticalVelocity -= gravity * deltaTime;
        _heightInSquares = Mathf.Max(
            -eliminationDepth,
            _heightInSquares + _verticalVelocity * deltaTime);
    }

    private void UpdateJump(
        bool gameplayInputActive,
        Keyboard keyboard,
        float deltaTime)
    {
        if (gameplayInputActive &&
            keyboard != null &&
            keyboard.spaceKey.wasPressedThisFrame &&
            _isGrounded)
        {
            PlayerCommanderSettings settings = _game?.GetPlayerSettings();
            _verticalVelocity = settings?.JumpSpeedInSquares ?? jumpSpeedInSquares;
            _isGrounded = false;
        }

        if (_isGrounded)
        {
            _heightInSquares = 0f;
            _verticalVelocity = 0f;
            return;
        }

        PlayerCommanderSettings gravitySettings = _game?.GetPlayerSettings();
        _verticalVelocity -= (gravitySettings?.GravityInSquares ?? gravityInSquares) *
            deltaTime;
        _heightInSquares += _verticalVelocity * deltaTime;

        if (_heightInSquares <= 0f)
        {
            _heightInSquares = 0f;
            _verticalVelocity = 0f;
            _isGrounded = true;
        }
    }

    private NetworkPlayer ResolveLocalNetworkPlayer()
    {
        if (_localNetworkPlayer == null || !_localNetworkPlayer.IsSpawned)
        {
            _localNetworkPlayer = NetworkPlayer.LocalPlayer;
        }

        return _localNetworkPlayer;
    }

    private void UpdateCameraTransform()
    {
        Vector3 up = _pieceSpawner.BoardUp;
        Vector3 horizontalForward = Quaternion.AngleAxis(
            _yaw,
            up) * _pieceSpawner.BoardForward;
        Vector3 horizontalRight = Vector3.Cross(up, horizontalForward).normalized;
        Vector3 lookDirection = Quaternion.AngleAxis(
            _pitch,
            horizontalRight) * horizontalForward;
        PlayerCommanderSettings settings = _game?.GetPlayerSettings();
        NetworkPlayer localPlayer = ResolveLocalNetworkPlayer();
        bool usePlayerKing =
            _game?.GameMode?.Victory.UsesPlayerCommander == true;
        float eyeHeight = 0f;

        if (usePlayerKing &&
            localPlayer != null &&
            localPlayer.TryGetKingAvatarWorldHeight(up, out float kingHeight))
        {
            eyeHeight = kingHeight *
                (settings?.PlayerKingEyeHeightAsModelFraction ?? 0.82f);
        }
        else if (!usePlayerKing &&
                 _eyeHeightWorld <= 0f &&
                 _pieceSpawner.TryGetRepresentativePieceHeight(out float pieceHeight))
        {
            _eyeHeightWorld = pieceHeight *
                (settings?.EyeHeightAsPieceFraction ?? eyeHeightAsPieceFraction);
        }

        if (eyeHeight <= 0f)
        {
            eyeHeight = _eyeHeightWorld > 0f
                ? _eyeHeightWorld
                : Mathf.Min(
                    _pieceSpawner.FileSpacing,
                    _pieceSpawner.RankSpacing) *
                  (settings?.EyeHeightAsPieceFraction ??
                   eyeHeightAsPieceFraction);
        }

        _viewCamera.transform.SetPositionAndRotation(
            transform.position + up * eyeHeight,
            Quaternion.LookRotation(lookDirection, up));
    }

    private void UpdateCaptureRespawnCamera()
    {
        if (_viewCamera == null || _pieceSpawner == null)
        {
            return;
        }

        float squareSize = Mathf.Min(
            _pieceSpawner.FileSpacing,
            _pieceSpawner.RankSpacing);
        float heightInSquares = _game?.GameMode?.CaptureMode
            .KingRespawnCameraHeightInSquares ?? 10f;
        Vector3 up = _pieceSpawner.BoardUp;
        Vector3 centre = _pieceSpawner.GetBoardWorldPosition(3.5f, 3.5f);
        _viewCamera.transform.SetPositionAndRotation(
            centre + up * (heightInSquares * squareSize),
            Quaternion.LookRotation(-up, _pieceSpawner.BoardForward));
    }

    private void UpdateGazeTarget(bool gameplayInputActive)
    {
        NetworkPlayer localPlayer = NetworkPlayer.LocalPlayer;

        if (gameplayInputActive && localPlayer != null)
        {
            _game.UpdateLocalChargeAim(
                new Ray(_viewCamera.transform.position, _viewCamera.transform.forward),
                localPlayer.Team);
        }
        else
        {
            _game.UpdateLocalChargeAim(default, PlayerTeam.Unassigned);
        }

        if (!gameplayInputActive || localPlayer == null ||
            !_pieceSpawner.TryGetGazeTarget(
                _viewCamera,
                localPlayer.Team,
                out ushort pieceId))
        {
            _game.UpdateLocalVoiceGazeTarget(null, _file, _rank);
            return;
        }

        _game.UpdateLocalVoiceGazeTarget(pieceId, _file, _rank);

        if (_game.UsesManualConfirmedSelection &&
            Mouse.current != null &&
            WasConfirmSelectionPressed(
                Mouse.current,
                _game.GetPlayerSettings()?.ConfirmSelectionButton ??
                PieceSelectionMouseButton.Left))
        {
            _game.ConfirmLocalVoiceSelection(pieceId, out _);
        }
    }

    private static bool WasConfirmSelectionPressed(
        Mouse mouse,
        PieceSelectionMouseButton button)
    {
        return button switch
        {
            PieceSelectionMouseButton.Right => mouse.rightButton.wasPressedThisFrame,
            PieceSelectionMouseButton.Middle => mouse.middleButton.wasPressedThisFrame,
            _ => mouse.leftButton.wasPressedThisFrame
        };
    }

    private void UpdateMouseChargeInput(bool gameplayInputActive)
    {
        Mouse mouse = Mouse.current;

        if (!gameplayInputActive ||
            mouse == null ||
            _game == null ||
            !_game.UsesChargeSelectionCommand)
        {
            CancelMouseCharge();
            return;
        }

        if (mouse.rightButton.wasPressedThisFrame)
        {
            _isMouseChargeHeld = true;
            _mouseChargeStartedAt = Time.unscaledTime;
        }

        if (!_isMouseChargeHeld)
        {
            return;
        }

        float heldDuration = Mathf.Max(
            0f,
            Time.unscaledTime - _mouseChargeStartedAt);

        if (mouse.rightButton.wasReleasedThisFrame ||
            !mouse.rightButton.isPressed)
        {
            ExecuteMouseCharge(heldDuration);
            return;
        }

        float normalizedLoudness = GetMouseChargeNormalizedLoudness(
            heldDuration);
        _game.UpdateLocalVoiceChargePreview(
            heldDuration,
            normalizedLoudness,
            pronunciationScore: 1f);
    }

    private void ExecuteMouseCharge(float heldDuration)
    {
        _isMouseChargeHeld = false;
        float normalizedLoudness = GetMouseChargeNormalizedLoudness(
            heldDuration);

        if (!_game.TryGetLocalVoiceCommandSnapshot(
                out ushort pieceId,
                out float targetDistanceInSquares,
                out bool hasChargeAim,
                out Vector2 chargeAimBoardPosition))
        {
            _game.ClearLocalVoiceChargePreview();
            return;
        }

        bool accepted = _game.TryExecuteLocalVoiceCommand(
            pieceId,
            targetDistanceInSquares,
            0f,
            normalizedLoudness,
            PieceVoiceCommand.Charge,
            hasChargeAim,
            chargeAimBoardPosition,
            heldDuration,
            1f,
            out string rejection);

        _game.ClearLocalVoiceChargePreview();

        if (!accepted)
        {
            _game.ShowLocalVoiceFailure(pieceId);
            Debug.LogWarning($"[Mouse Charge] {rejection}", this);
        }
    }

    private float GetMouseChargeNormalizedLoudness(float heldDuration)
    {
        return _game?.GameMode?.Commands?
            .GetMouseChargeNormalizedLoudness(heldDuration) ?? 0f;
    }

    private void CancelMouseCharge()
    {
        if (!_isMouseChargeHeld)
        {
            return;
        }

        _isMouseChargeHeld = false;
        _game?.ClearLocalVoiceChargePreview();
    }

    private void OnGUI()
    {
        if (!NetworkPlayer.MatchStarted || SessionManager.IsFrontEndVisible ||
            InGameVoiceSettingsUI.IsBlockingGameplay)
        {
            return;
        }

        if (_game != null &&
            _game.TryGetLocalCaptureKingRespawnRemaining(out float remaining))
        {
            DrawCaptureRespawnCountdown(remaining);
            return;
        }

        DrawCommandCooldownReticle();
        Rect shadow = new(Screen.width * 0.5f - 2f, Screen.height * 0.5f - 2f, 5f, 5f);
        GUI.color = new Color(0f, 0f, 0f, 0.8f);
        GUI.DrawTexture(shadow, Texture2D.whiteTexture);
        Rect dot = new(Screen.width * 0.5f - 1f, Screen.height * 0.5f - 1f, 3f, 3f);
        GUI.color = Color.white;
        GUI.DrawTexture(dot, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private void DrawCommandCooldownReticle()
    {
        NetworkPlayer localPlayer = NetworkPlayer.LocalPlayer;
        CommandEconomySettings settings = _game?.GameMode?.Commands;

        if (localPlayer == null ||
            settings == null ||
            !settings.CooldownSystemEnabled)
        {
            return;
        }

        float remaining = localPlayer.RemainingCommandCooldown;

        if (remaining <= 0.001f)
        {
            return;
        }

        float duration = settings.CommandCooldownSeconds;
        float readyProgress = 1f - Mathf.Clamp01(remaining / duration);
        float diameter = settings.CommandCooldownReticleDiameterPixels;
        float radius = diameter * 0.5f;
        float thickness = Mathf.Clamp(diameter * 0.075f, 3f, 10f);
        int segments = Mathf.Clamp(
            Mathf.CeilToInt(Mathf.PI * diameter / Mathf.Max(2f, thickness * 0.7f)),
            48,
            128);
        int readySegments = Mathf.FloorToInt(readyProgress * segments);
        float segmentArcLength = Mathf.PI * diameter / segments * 1.18f;
        Vector2 centre = new(Screen.width * 0.5f, Screen.height * 0.5f);
        Rect segmentRect = new(
            centre.x - segmentArcLength * 0.5f,
            centre.y - radius - thickness * 0.5f,
            segmentArcLength,
            thickness);
        Matrix4x4 originalMatrix = GUI.matrix;
        Color originalColor = GUI.color;
        Color backgroundColor = new(0f, 0f, 0f, 0.38f);
        Color progressColor = new(1f, 0.38f, 0.05f, 0.95f);

        for (int index = 0; index < segments; index++)
        {
            GUI.matrix = originalMatrix;
            GUIUtility.RotateAroundPivot(
                index * 360f / segments,
                centre);
            GUI.color = index < readySegments
                ? progressColor
                : backgroundColor;
            GUI.DrawTexture(segmentRect, Texture2D.whiteTexture);
        }

        GUI.matrix = originalMatrix;
        GUI.color = originalColor;
    }

    private void DrawCaptureRespawnCountdown(float remaining)
    {
        CaptureModeSettings settings = _game.GameMode.CaptureMode;
        int fontSize = settings.KingRespawnCountdownFontSize;
        Color color = settings.KingRespawnCountdownColor;

        _respawnCountdownStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        _respawnCountdownShadowStyle ??= new GUIStyle(_respawnCountdownStyle);
        _respawnCountdownStyle.fontSize = fontSize;
        _respawnCountdownStyle.normal.textColor = color;
        _respawnCountdownShadowStyle.fontSize = fontSize;
        _respawnCountdownShadowStyle.normal.textColor =
            new Color(0f, 0f, 0f, color.a * 0.75f);
        string number = Mathf.Max(1, Mathf.CeilToInt(remaining)).ToString();
        Rect rect = new(
            Screen.width * 0.5f - 160f,
            Screen.height * 0.5f - 100f,
            320f,
            200f);
        Rect shadow = new(rect.x + 4f, rect.y + 4f, rect.width, rect.height);
        GUI.Label(shadow, number, _respawnCountdownShadowStyle);
        GUI.Label(rect, number, _respawnCountdownStyle);
    }

    private void OnDisable()
    {
        CancelMouseCharge();

        if (_game != null)
        {
            _game.UpdateLocalVoiceGazeTarget(null, _file, _rank);
        }

        SetCursorLocked(false);
    }

    private void OnValidate()
    {
        moveSpeedInSquares = Mathf.Max(0.1f, moveSpeedInSquares);
        collisionRadiusInSquares = Mathf.Clamp(
            collisionRadiusInSquares,
            0.08f,
            0.35f);
        jumpSpeedInSquares = Mathf.Max(0.1f, jumpSpeedInSquares);
        gravityInSquares = Mathf.Max(0.1f, gravityInSquares);
        playerKnockbackDrag = Mathf.Max(0f, playerKnockbackDrag);
    }
}
