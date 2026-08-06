using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class FirstPersonCommanderController : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float moveSpeedInSquares = 3.2f;
    [SerializeField, Min(0.01f)] private float mouseSensitivity = 0.08f;
    [SerializeField, Range(-89f, 0f)] private float minimumPitch = -75f;
    [SerializeField, Range(0f, 89f)] private float maximumPitch = 75f;
    [SerializeField, Min(0.1f)] private float eyeHeightInSquares = 1.7f;

    private Camera _viewCamera;
    private ChessPieceSpawner _pieceSpawner;
    private NetworkChessGame _game;
    private float _file = 3.5f;
    private float _rank = 3.5f;
    private float _yaw;
    private float _pitch;
    private bool _cameraConfigured;
    private bool _cursorReleased;

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
            !SessionManager.IsFrontEndVisible &&
            !InGameVoiceSettingsUI.IsOpen;

        UpdateCursor(gameplayInputActive);

        if (gameplayInputActive)
        {
            UpdateLook();
            UpdateMovement();
        }

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
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (!gameplayInputActive)
        {
            SetCursorLocked(false);
            return;
        }

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            _cursorReleased = true;
        }
        else if (_cursorReleased && mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            _cursorReleased = false;
        }

        SetCursorLocked(!_cursorReleased);
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void UpdateLook()
    {
        if (_cursorReleased || Mouse.current == null)
        {
            return;
        }

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        _yaw += mouseDelta.x * mouseSensitivity;
        _pitch = Mathf.Clamp(
            _pitch - mouseDelta.y * mouseSensitivity,
            minimumPitch,
            maximumPitch);
    }

    private void UpdateMovement()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
        {
            return;
        }

        Vector2 input = Vector2.zero;
        input.x = (keyboard.dKey.isPressed ? 1f : 0f) -
                  (keyboard.aKey.isPressed ? 1f : 0f);
        input.y = (keyboard.wKey.isPressed ? 1f : 0f) -
                  (keyboard.sKey.isPressed ? 1f : 0f);

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
        float distance = moveSpeedInSquares * Time.deltaTime;
        float fileDelta = Vector3.Dot(desiredDirection, _pieceSpawner.BoardRight) * distance;
        float rankDelta = Vector3.Dot(desiredDirection, _pieceSpawner.BoardForward) * distance;
        _file = Mathf.Clamp(_file + fileDelta, 0f, 7f);
        _rank = Mathf.Clamp(_rank + rankDelta, 0f, 7f);
        transform.position = _pieceSpawner.GetBoardWorldPosition(_file, _rank);
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
        float eyeHeight = Mathf.Min(
            _pieceSpawner.FileSpacing,
            _pieceSpawner.RankSpacing) * eyeHeightInSquares;

        _viewCamera.transform.SetPositionAndRotation(
            transform.position + up * eyeHeight,
            Quaternion.LookRotation(lookDirection, up));
    }

    private void UpdateGazeTarget(bool gameplayInputActive)
    {
        NetworkPlayer localPlayer = NetworkPlayer.LocalPlayer;

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
    }

    private void OnGUI()
    {
        if (!NetworkPlayer.MatchStarted || SessionManager.IsFrontEndVisible ||
            InGameVoiceSettingsUI.IsOpen)
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
}
