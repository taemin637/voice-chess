using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class JumpMapTestPlayer : MonoBehaviour
{
    // Keep these defaults in sync with FirstPersonCommanderController.
    [SerializeField, Min(0.1f)] private float moveSpeed = 5.0f;
    [SerializeField, Min(0.1f)] private float jumpSpeed = 4.2f;
    [SerializeField, Min(0.1f)] private float gravity = 15.0f;
    [SerializeField, Min(0.01f)] private float mouseSensitivity = 0.08f;
    [SerializeField, Range(-89f, 0f)] private float minimumPitch = -75f;
    [SerializeField, Range(0f, 89f)] private float maximumPitch = 75f;

    [Header("플레이어 캡슐")]
    [SerializeField, Min(0.01f)] private float capsuleHeight = 0.68f;
    [SerializeField, Min(0.01f)] private float capsuleRadius = 0.16f;
    [SerializeField, Min(0f)] private float eyeHeight = 0.5f;

    [Header("시야")]
    [SerializeField] private Camera viewCamera;

    private CharacterController _characterController;
    private float _verticalVelocity;
    private float _pitch;

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();

        if (_characterController == null)
        {
            _characterController = gameObject.AddComponent<CharacterController>();
        }

        ConfigureCharacterController();
        ConfigureCamera();
        SetCursorLocked(true);
    }

    private void Update()
    {
        UpdateCursor();
        UpdateLook();
        UpdateMovement();
    }

    private void UpdateCursor()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetCursorLocked(false);
        }
        else if (Mouse.current != null &&
                 Mouse.current.leftButton.wasPressedThisFrame)
        {
            SetCursorLocked(true);
        }
    }

    private void UpdateLook()
    {
        if (viewCamera == null || Mouse.current == null ||
            Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        transform.Rotate(
            Vector3.up,
            mouseDelta.x * mouseSensitivity,
            Space.World);
        _pitch = Mathf.Clamp(
            _pitch - mouseDelta.y * mouseSensitivity,
            minimumPitch,
            maximumPitch);
        viewCamera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    private void UpdateMovement()
    {
        Keyboard keyboard = Keyboard.current;
        Vector2 input = Vector2.zero;

        if (keyboard != null)
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

        Vector3 horizontalVelocity =
            (transform.right * input.x + transform.forward * input.y) *
            moveSpeed;

        if (_characterController.isGrounded)
        {
            _verticalVelocity = 0f;

            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            {
                _verticalVelocity = jumpSpeed;
            }
        }

        _verticalVelocity -= gravity * Time.deltaTime;

        Vector3 velocity = horizontalVelocity +
            Vector3.up * _verticalVelocity;
        CollisionFlags collisionFlags = _characterController.Move(
            velocity * Time.deltaTime);

        if ((collisionFlags & CollisionFlags.Below) != 0 &&
            _verticalVelocity < 0f)
        {
            _verticalVelocity = 0f;
        }
        else if ((collisionFlags & CollisionFlags.Above) != 0 &&
                 _verticalVelocity > 0f)
        {
            _verticalVelocity = 0f;
        }
    }

    private void ConfigureCharacterController()
    {
        float radius = Mathf.Min(capsuleRadius, capsuleHeight * 0.5f);
        _characterController.height = capsuleHeight;
        _characterController.radius = radius;
        _characterController.center = Vector3.up * (capsuleHeight * 0.5f);
    }

    private void ConfigureCamera()
    {
        if (viewCamera == null)
        {
            viewCamera = GetComponentInChildren<Camera>();
        }

        if (viewCamera == null)
        {
            viewCamera = Camera.main;
        }

        if (viewCamera == null)
        {
            return;
        }

        OrbitCamera orbitCamera = viewCamera.GetComponent<OrbitCamera>();

        if (orbitCamera != null)
        {
            orbitCamera.enabled = false;
        }

        viewCamera.transform.SetParent(transform, worldPositionStays: false);
        viewCamera.transform.localPosition = Vector3.up * eyeHeight;
        viewCamera.transform.localRotation = Quaternion.identity;
        viewCamera.nearClipPlane = Mathf.Min(viewCamera.nearClipPlane, 0.01f);
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void OnGUI()
    {
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        Rect shadow = new(
            Screen.width * 0.5f - 2f,
            Screen.height * 0.5f - 2f,
            5f,
            5f);
        GUI.color = new Color(0f, 0f, 0f, 0.8f);
        GUI.DrawTexture(shadow, Texture2D.whiteTexture);

        Rect dot = new(
            Screen.width * 0.5f - 1f,
            Screen.height * 0.5f - 1f,
            3f,
            3f);
        GUI.color = Color.white;
        GUI.DrawTexture(dot, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private void OnDisable()
    {
        SetCursorLocked(false);
    }

    private void OnValidate()
    {
        moveSpeed = Mathf.Max(0.1f, moveSpeed);
        jumpSpeed = Mathf.Max(0.1f, jumpSpeed);
        gravity = Mathf.Max(0.1f, gravity);
        mouseSensitivity = Mathf.Max(0.01f, mouseSensitivity);
        capsuleHeight = Mathf.Max(0.01f, capsuleHeight);
        capsuleRadius = Mathf.Clamp(
            capsuleRadius,
            0.01f,
            capsuleHeight * 0.5f);
        eyeHeight = Mathf.Max(0f, eyeHeight);

        if (_characterController != null)
        {
            ConfigureCharacterController();
        }
    }
}
