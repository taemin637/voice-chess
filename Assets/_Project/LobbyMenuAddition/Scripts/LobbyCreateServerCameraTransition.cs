using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class LobbyCreateServerCameraTransition : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform createServerButton;
    [SerializeField] private Transform joinServerButton;
    [SerializeField] private Transform destination;
    [SerializeField] private GameObject[] menuObjectsToHide;
    [SerializeField, Min(0.01f)] private float duration = 1.2f;
    [SerializeField] private float targetFieldOfView = 60f;
    [SerializeField] private AnimationCurve easing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private UnityEvent onCreateServerClicked = new UnityEvent();

    private Vector3 startPosition;
    private Quaternion startRotation;
    private float startFieldOfView;
    private Vector3 transitionStartPosition;
    private Quaternion transitionStartRotation;
    private float transitionStartFieldOfView;
    private float elapsed;
    private bool isMoving;
    private bool hasFinished;
    private bool isReturning;
    private bool isJoinServerTransition;

    public event Action JoinServerTransitionStarted;
    public event Action JoinServerViewReached;

    private void Update()
    {
        if (isMoving)
        {
            MoveCamera();
            return;
        }

        if (!hasFinished && TryGetPointerDown(out Vector2 screenPosition))
        {
            TryBeginTransition(screenPosition);
        }
    }

    public void Configure(
        Camera cameraToMove,
        Transform button,
        Transform cameraDestination,
        GameObject[] objectsToHide,
        float destinationFieldOfView,
        float transitionDuration)
    {
        targetCamera = cameraToMove;
        createServerButton = button;
        destination = cameraDestination;
        menuObjectsToHide = objectsToHide;
        targetFieldOfView = destinationFieldOfView;
        duration = Mathf.Max(0.01f, transitionDuration);

        if (easing == null || easing.length == 0)
        {
            easing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        }

        if (onCreateServerClicked == null)
        {
            onCreateServerClicked = new UnityEvent();
        }
    }

    public bool ReturnToMainMenu()
    {
        if (targetCamera == null || isMoving || !hasFinished)
        {
            return false;
        }

        transitionStartPosition = targetCamera.transform.position;
        transitionStartRotation = targetCamera.transform.rotation;
        transitionStartFieldOfView = targetCamera.fieldOfView;
        elapsed = 0f;
        isReturning = true;
        isMoving = true;
        return true;
    }

    private static bool TryGetPointerDown(out Vector2 screenPosition)
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            screenPosition = Mouse.current.position.ReadValue();
            return true;
        }

        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            screenPosition = Touchscreen.current.primaryTouch.position.ReadValue();
            return true;
        }

        screenPosition = default;
        return false;
    }

    private void TryBeginTransition(Vector2 screenPosition)
    {
        if (targetCamera == null || destination == null)
        {
            return;
        }

        Ray ray = targetCamera.ScreenPointToRay(screenPosition);
        if (!Physics.Raycast(ray, out RaycastHit hit))
        {
            return;
        }

        bool hitCreateButton = IsButtonHit(hit.transform, createServerButton);
        bool hitJoinButton = IsButtonHit(hit.transform, joinServerButton);
        if (!hitCreateButton && !hitJoinButton)
        {
            return;
        }

        startPosition = targetCamera.transform.position;
        startRotation = targetCamera.transform.rotation;
        startFieldOfView = targetCamera.fieldOfView;
        transitionStartPosition = startPosition;
        transitionStartRotation = startRotation;
        transitionStartFieldOfView = startFieldOfView;
        elapsed = 0f;
        isReturning = false;
        isJoinServerTransition = hitJoinButton;
        isMoving = true;

        if (menuObjectsToHide != null)
        {
            foreach (GameObject menuObject in menuObjectsToHide)
            {
                if (menuObject != null)
                {
                    menuObject.SetActive(false);
                }
            }
        }

        if (isJoinServerTransition)
        {
            JoinServerTransitionStarted?.Invoke();
        }
        else
        {
            onCreateServerClicked.Invoke();
        }
    }

    private static bool IsButtonHit(Transform hitTransform, Transform button)
    {
        return button != null &&
            (hitTransform == button || hitTransform.IsChildOf(button));
    }

    private void MoveCamera()
    {
        if (targetCamera == null || destination == null)
        {
            isMoving = false;
            return;
        }

        elapsed += Time.unscaledDeltaTime;
        float normalizedTime = Mathf.Clamp01(elapsed / duration);
        float easedTime = easing.Evaluate(normalizedTime);

        Vector3 targetPosition = isReturning ? startPosition : destination.position;
        Quaternion targetRotation = isReturning ? startRotation : destination.rotation;
        float destinationFieldOfView = isReturning ? startFieldOfView : targetFieldOfView;

        targetCamera.transform.position = Vector3.LerpUnclamped(
            transitionStartPosition,
            targetPosition,
            easedTime);
        targetCamera.transform.rotation = Quaternion.SlerpUnclamped(
            transitionStartRotation,
            targetRotation,
            easedTime);
        targetCamera.fieldOfView = Mathf.LerpUnclamped(
            transitionStartFieldOfView,
            destinationFieldOfView,
            easedTime);

        if (normalizedTime < 1f)
        {
            return;
        }

        targetCamera.transform.SetPositionAndRotation(targetPosition, targetRotation);
        targetCamera.fieldOfView = destinationFieldOfView;
        isMoving = false;

        if (isReturning)
        {
            if (menuObjectsToHide != null)
            {
                foreach (GameObject menuObject in menuObjectsToHide)
                {
                    if (menuObject != null)
                    {
                        menuObject.SetActive(true);
                    }
                }
            }

            hasFinished = false;
            isReturning = false;
            return;
        }

        hasFinished = true;
        if (isJoinServerTransition)
        {
            JoinServerViewReached?.Invoke();
        }
    }
}
