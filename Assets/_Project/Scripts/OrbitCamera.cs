using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Keeps the camera at a fixed distance from a target and moves it over the
/// surface of an imaginary sphere. Attach this component to the camera.
/// </summary>
[DisallowMultipleComponent]
public sealed class OrbitCamera : MonoBehaviour
{
    private enum OrbitMouseButton
    {
        Left,
        Middle,
        Right
    }

    [Header("Orbit Target")]
    [SerializeField] private Transform target;
    [SerializeField, Min(0.01f)] private float distance = 10f;
    [SerializeField] private bool useStartingDistance = true;

    [Header("Mouse Control")]
    [SerializeField] private bool onlyWhileDragging = true;
    [SerializeField] private OrbitMouseButton dragButton = OrbitMouseButton.Right;
    [SerializeField, Min(0f)] private float sensitivity = 0.15f;

    [Header("Zoom")]
    [SerializeField, Min(0f)] private float zoomSensitivity = 0.01f;
    [SerializeField, Min(0.01f)] private float minimumDistance = 3f;
    [SerializeField, Min(0.01f)] private float maximumDistance = 30f;

    [Header("Vertical Limit")]
    [SerializeField, Range(-89f, 89f)] private float minimumPitch = -20f;
    [SerializeField, Range(-89f, 89f)] private float maximumPitch = 80f;

    private float yaw;
    private float pitch;

    private void Start()
    {
        if (target == null)
        {
            Debug.LogError($"{nameof(OrbitCamera)} on '{name}' needs a target.", this);
            enabled = false;
            return;
        }

        Vector3 targetToCamera = transform.position - target.position;

        if (targetToCamera.sqrMagnitude < 0.0001f)
        {
            targetToCamera = Vector3.back;
        }

        if (useStartingDistance)
        {
            distance = targetToCamera.magnitude;
        }

        distance = Mathf.Clamp(distance, minimumDistance, maximumDistance);

        // Convert the camera's current position into spherical orbit angles so
        // pressing Play does not make the camera jump to a different viewpoint.
        Vector3 directionToTarget = -targetToCamera.normalized;
        Vector3 angles = Quaternion.LookRotation(directionToTarget, Vector3.up).eulerAngles;
        yaw = angles.y;
        pitch = NormalizeAngle(angles.x);
        pitch = Mathf.Clamp(pitch, minimumPitch, maximumPitch);

        ApplyOrbit();
    }

    private void LateUpdate()
    {
        Mouse mouse = Mouse.current;

        if (mouse != null)
        {
            // Positive wheel input moves the camera closer to the target.
            float scrollDelta = mouse.scroll.ReadValue().y;
            distance = Mathf.Clamp(
                distance - scrollDelta * zoomSensitivity,
                minimumDistance,
                maximumDistance);

            if (!onlyWhileDragging || IsDragButtonPressed(mouse))
            {
                Vector2 mouseDelta = mouse.delta.ReadValue();

                // The signs intentionally make the camera move opposite the mouse:
                // mouse right -> camera left, mouse up -> camera down.
                yaw += mouseDelta.x * sensitivity;
                pitch -= mouseDelta.y * sensitivity;
                pitch = Mathf.Clamp(pitch, minimumPitch, maximumPitch);
            }
        }

        ApplyOrbit();
    }

    private bool IsDragButtonPressed(Mouse mouse)
    {
        return dragButton switch
        {
            OrbitMouseButton.Left => mouse.leftButton.isPressed,
            OrbitMouseButton.Middle => mouse.middleButton.isPressed,
            OrbitMouseButton.Right => mouse.rightButton.isPressed,
            _ => false
        };
    }

    private void ApplyOrbit()
    {
        Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
        transform.SetPositionAndRotation(
            target.position - orbitRotation * Vector3.forward * distance,
            orbitRotation);
    }

    private static float NormalizeAngle(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }

    private void OnValidate()
    {
        minimumDistance = Mathf.Max(0.01f, minimumDistance);
        maximumDistance = Mathf.Max(minimumDistance, maximumDistance);
        distance = Mathf.Clamp(distance, minimumDistance, maximumDistance);

        if (minimumPitch > maximumPitch)
        {
            minimumPitch = maximumPitch;
        }
    }
}
