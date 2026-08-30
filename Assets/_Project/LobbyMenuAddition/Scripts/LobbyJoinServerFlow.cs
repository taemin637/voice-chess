using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class LobbyJoinServerFlow : MonoBehaviour
{
    [SerializeField] private LobbyCreateServerCameraTransition cameraTransition;
    [SerializeField] private LobbyCreateServerDepthOfField depthOfFieldController;
    [SerializeField] private LobbyCreateServerKingDrop kingDropController;
    [SerializeField] private LobbyHangingSideSigns sideSignsController;

    private readonly List<RectTransform> joinButtonRects = new();
    private GameObject interfaceRoot;
    private RectTransform mainMenuButtonRect;
    private bool serverListIsVisible;

    private void Awake()
    {
        CreateServerListInterface();

        if (cameraTransition != null)
        {
            cameraTransition.JoinServerTransitionStarted += OnJoinTransitionStarted;
            cameraTransition.JoinServerViewReached += ShowServerList;
        }
    }

    private void Update()
    {
        if (!serverListIsVisible || !TryGetPointerDown(out Vector2 pointerPosition))
        {
            return;
        }

        if (mainMenuButtonRect != null &&
            RectTransformUtility.RectangleContainsScreenPoint(
                mainMenuButtonRect,
                pointerPosition))
        {
            ReturnToMainMenu();
            return;
        }

        foreach (RectTransform joinButtonRect in joinButtonRects)
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(
                    joinButtonRect,
                    pointerPosition))
            {
                JoinTemporaryServer();
                return;
            }
        }
    }

    private void OnJoinTransitionStarted()
    {
        serverListIsVisible = false;
        interfaceRoot.SetActive(false);
        depthOfFieldController?.SuppressAutomaticActivation();
    }

    private void ShowServerList()
    {
        depthOfFieldController?.ActivateFullBackgroundBlur();
        interfaceRoot.SetActive(true);
        serverListIsVisible = true;
    }

    private void JoinTemporaryServer()
    {
        serverListIsVisible = false;
        interfaceRoot.SetActive(false);
        depthOfFieldController?.ActivateBackgroundBlur();
        kingDropController?.DropKings();
        sideSignsController?.ConfigureJoinedServerWithWhiteHost();
    }

    private void ReturnToMainMenu()
    {
        if (cameraTransition == null || !cameraTransition.ReturnToMainMenu())
        {
            return;
        }

        serverListIsVisible = false;
        interfaceRoot.SetActive(false);
        depthOfFieldController?.DeactivateBackgroundBlur();
        kingDropController?.ResetKingsForMainMenu();
    }

    private void CreateServerListInterface()
    {
        interfaceRoot = new GameObject("Lobby Temporary Server List (Scene Only)");
        interfaceRoot.transform.SetParent(transform, false);

        Canvas canvas = interfaceRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 190;

        CanvasScaler scaler = interfaceRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject panelObject = CreateImage(
            "Temporary Server List Panel",
            interfaceRoot.transform,
            new Vector2(0.16f, 0.14f),
            new Vector2(0.84f, 0.86f),
            new Color(0.075f, 0.08f, 0.095f, 0.34f));

        Outline panelOutline = panelObject.AddComponent<Outline>();
        panelOutline.effectColor = new Color(0.58f, 0.6f, 0.62f, 0.62f);
        panelOutline.effectDistance = new Vector2(2f, -2f);

        CreateText(
            "Server List Title",
            panelObject.transform,
            new Vector2(0.08f, 0.82f),
            new Vector2(0.92f, 0.96f),
            "SERVER LIST",
            54,
            FontStyle.Bold,
            new Color(0.9f, 0.91f, 0.9f, 1f),
            TextAnchor.MiddleLeft);

        CreateText(
            "Temporary List Notice",
            panelObject.transform,
            new Vector2(0.08f, 0.74f),
            new Vector2(0.92f, 0.82f),
            "Temporary rooms for lobby preview",
            26,
            FontStyle.Normal,
            new Color(0.66f, 0.68f, 0.7f, 1f),
            TextAnchor.MiddleLeft);

        CreateServerRow(panelObject.transform, 0.58f,
            "Voice Chess Room 01", "1 / 2");
        CreateServerRow(panelObject.transform, 0.39f,
            "Casual Match 02", "1 / 2");
        CreateServerRow(panelObject.transform, 0.2f,
            "Practice Lobby", "0 / 2");

        GameObject mainMenuButton = CreateImage(
            "Server List Main Menu Button",
            interfaceRoot.transform,
            new Vector2(0.025f, 0.9f),
            new Vector2(0.19f, 0.97f),
            new Color(0.12f, 0.13f, 0.15f, 0.92f));
        mainMenuButtonRect = mainMenuButton.GetComponent<RectTransform>();

        Outline mainMenuOutline = mainMenuButton.AddComponent<Outline>();
        mainMenuOutline.effectColor = new Color(0.7f, 0.71f, 0.7f, 0.85f);
        mainMenuOutline.effectDistance = new Vector2(2f, -2f);

        CreateText(
            "Server List Main Menu Label",
            mainMenuButton.transform,
            new Vector2(0.05f, 0.08f),
            new Vector2(0.95f, 0.92f),
            "MAIN MENU",
            34,
            FontStyle.Bold,
            new Color(0.84f, 0.85f, 0.84f, 1f),
            TextAnchor.MiddleCenter);

        interfaceRoot.SetActive(false);
    }

    private void CreateServerRow(
        Transform panel,
        float bottom,
        string serverName,
        string playerCount)
    {
        GameObject row = CreateImage(
            serverName + " Row",
            panel,
            new Vector2(0.07f, bottom),
            new Vector2(0.93f, bottom + 0.145f),
            new Color(0.18f, 0.19f, 0.21f, 1f));

        CreateText(
            serverName + " Name",
            row.transform,
            new Vector2(0.04f, 0.12f),
            new Vector2(0.58f, 0.88f),
            serverName,
            31,
            FontStyle.Bold,
            new Color(0.87f, 0.88f, 0.87f, 1f),
            TextAnchor.MiddleLeft);

        CreateText(
            serverName + " Players",
            row.transform,
            new Vector2(0.6f, 0.12f),
            new Vector2(0.76f, 0.88f),
            playerCount,
            28,
            FontStyle.Normal,
            new Color(0.7f, 0.72f, 0.74f, 1f),
            TextAnchor.MiddleCenter);

        GameObject joinButton = CreateImage(
            serverName + " Join Button",
            row.transform,
            new Vector2(0.79f, 0.18f),
            new Vector2(0.96f, 0.82f),
            new Color(0.32f, 0.34f, 0.37f, 1f));
        joinButtonRects.Add(joinButton.GetComponent<RectTransform>());

        CreateText(
            serverName + " Join Label",
            joinButton.transform,
            new Vector2(0.04f, 0.08f),
            new Vector2(0.96f, 0.92f),
            "JOIN",
            28,
            FontStyle.Bold,
            new Color(0.94f, 0.94f, 0.92f, 1f),
            TextAnchor.MiddleCenter);
    }

    private static GameObject CreateImage(
        string objectName,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Color color)
    {
        GameObject imageObject = new(objectName);
        imageObject.transform.SetParent(parent, false);
        RectTransform rect = imageObject.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return imageObject;
    }

    private static void CreateText(
        string objectName,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        string value,
        int fontSize,
        FontStyle fontStyle,
        Color color,
        TextAnchor alignment)
    {
        GameObject textObject = new(objectName);
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Text text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
    }

    private static bool TryGetPointerDown(out Vector2 pointerPosition)
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            pointerPosition = Mouse.current.position.ReadValue();
            return true;
        }

        if (Touchscreen.current != null &&
            Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            pointerPosition = Touchscreen.current.primaryTouch.position.ReadValue();
            return true;
        }

        pointerPosition = default;
        return false;
    }

    private void OnDestroy()
    {
        if (cameraTransition != null)
        {
            cameraTransition.JoinServerTransitionStarted -= OnJoinTransitionStarted;
            cameraTransition.JoinServerViewReached -= ShowServerList;
        }

        if (interfaceRoot != null)
        {
            Destroy(interfaceRoot);
        }
    }
}
