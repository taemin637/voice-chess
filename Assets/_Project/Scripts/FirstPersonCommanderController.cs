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
    private NetworkPlayer _localNetworkPlayer;
    private bool _cameraConfigured;
    private bool _isGrounded = true;

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
            return;
        }

        bool gameplayInputActive =
            NetworkPlayer.MatchStarted &&
            (ResolveLocalNetworkPlayer() == null ||
             !ResolveLocalNetworkPlayer().IsEliminated) &&
            !SessionManager.IsFrontEndVisible &&
            !InGameVoiceSettingsUI.IsBlockingGameplay;

        UpdateCursor(gameplayInputActive);

        if (gameplayInputActive)
        {
            UpdateLook();
            UpdateTurnInput();
        }

        UpdateMovement(gameplayInputActive);
        UpdateCameraTransform();
        UpdateGazeTarget(gameplayInputActive);
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
        Vector2 playerVelocity = desiredVelocity + _knockbackVelocity;
        Vector2 playerPosition = new(_file, _rank);
        playerPosition += playerVelocity * deltaTime;

        UpdateJump(gameplayInputActive, keyboard, deltaTime);

        NetworkPlayer localPlayer = ResolveLocalNetworkPlayer();
        PlayerTeam team = localPlayer != null
            ? localPlayer.Team
            : PlayerTeam.Unassigned;
        _game.ResolvePlayerPieceCollisions(
            team,
            _heightInSquares,
            settings?.CollisionRadiusInSquares ?? collisionRadiusInSquares,
            ref playerPosition,
            ref playerVelocity);
        _knockbackVelocity = playerVelocity - desiredVelocity;

        _file = Mathf.Clamp(
            playerPosition.x,
            _pieceSpawner.GroundMinimumCoordinate,
            _pieceSpawner.GroundMaximumCoordinate);
        _rank = Mathf.Clamp(
            playerPosition.y,
            _pieceSpawner.GroundMinimumCoordinate,
            _pieceSpawner.GroundMaximumCoordinate);
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
        if (_eyeHeightWorld <= 0f &&
            _pieceSpawner.TryGetRepresentativePieceHeight(out float pieceHeight))
        {
            PlayerCommanderSettings settings = _game?.GetPlayerSettings();
            _eyeHeightWorld = pieceHeight *
                (settings?.EyeHeightAsPieceFraction ?? eyeHeightAsPieceFraction);
        }

        float eyeHeight = _eyeHeightWorld > 0f
            ? _eyeHeightWorld
            : Mathf.Min(
                _pieceSpawner.FileSpacing,
                _pieceSpawner.RankSpacing) *
              (_game?.GetPlayerSettings()?.EyeHeightAsPieceFraction ??
               eyeHeightAsPieceFraction);

        _viewCamera.transform.SetPositionAndRotation(
            transform.position + up * eyeHeight,
            Quaternion.LookRotation(lookDirection, up));
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

        if (_game.ActiveVoiceCommandVersion ==
                VoiceCommandVersion.ConfirmedSelectionCharge &&
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

    private void OnGUI()
    {
        if (!NetworkPlayer.MatchStarted || SessionManager.IsFrontEndVisible ||
            InGameVoiceSettingsUI.IsBlockingGameplay)
        {
            return;
        }

        Rect shadow = new(Screen.width * 0.5f - 2f, Screen.height * 0.5f - 2f, 5f, 5f);
        GUI.color = new Color(0f, 0f, 0f, 0.8f);
        GUI.DrawTexture(shadow, Texture2D.whiteTexture);
        Rect dot = new(Screen.width * 0.5f - 1f, Screen.height * 0.5f - 1f, 3f, 3f);
        GUI.color = Color.white;
        GUI.DrawTexture(dot, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private void OnDisable()
    {
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
