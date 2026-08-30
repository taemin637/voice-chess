using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class LobbySettingsPanel : MonoBehaviour
{
    private const string MasterVolumeKey = "VoiceChess.MasterVolume";
    private const string MusicVolumeKey = "VoiceChess.MusicVolume";
    private const string MicrophoneVolumeKey = "VoiceChess.MicrophoneVolume";
    private const string EffectsVolumeKey = "VoiceChess.EffectsVolume";
    private const string VoiceChatVolumeKey = "VoiceChess.VoiceChatVolume";
    private const string DisplayModeKey = "VoiceChess.DisplayMode";

    [SerializeField] private Camera targetCamera;
    [SerializeField] private Transform settingsButton;
    [SerializeField] private GameObject[] menuObjectsToHide;
    [SerializeField] private LobbyCreateServerDepthOfField depthOfFieldController;

    private readonly Dictionary<AudioSource, float> musicBaseVolumes = new();
    private readonly RectTransform[] sliderRects = new RectTransform[5];
    private readonly Image[] sliderFills = new Image[5];
    private readonly RectTransform[] sliderKnobs = new RectTransform[5];
    private readonly Text[] sliderValues = new Text[5];
    private readonly RectTransform[] displayModeRects = new RectTransform[3];
    private readonly Image[] displayModeImages = new Image[3];

    private GameObject interfaceRoot;
    private GameObject audioPage;
    private GameObject videoPage;
    private GameObject accessibilityPage;
    private RectTransform mainMenuRect;
    private RectTransform audioTabRect;
    private RectTransform videoTabRect;
    private RectTransform accessibilityTabRect;
    private Image audioTabImage;
    private Image videoTabImage;
    private Image accessibilityTabImage;
    private bool panelIsVisible;
    private int activeSlider = -1;
    private float masterVolume;
    private float musicVolume;
    private float microphoneVolume;
    private float effectsVolume;
    private float voiceChatVolume;
    private int displayMode;

    public static float SavedMicrophoneVolume =>
        PlayerPrefs.GetFloat(MicrophoneVolumeKey, 1f);

    private void Awake()
    {
        masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumeKey, 1f));
        musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumeKey, 0.8f));
        microphoneVolume = Mathf.Clamp01(
            PlayerPrefs.GetFloat(MicrophoneVolumeKey, 1f));
        effectsVolume = Mathf.Clamp01(
            PlayerPrefs.GetFloat(EffectsVolumeKey, 0.8f));
        voiceChatVolume = Mathf.Clamp01(
            PlayerPrefs.GetFloat(VoiceChatVolumeKey, 0.8f));
        displayMode = Mathf.Clamp(PlayerPrefs.GetInt(DisplayModeKey, 1), 0, 2);

        AudioListener.volume = masterVolume;
        RefreshMusicSources();
        ApplyMusicVolume();
        CreateInterface();
        ShowAudioPage();
        RefreshAllVisuals();
    }

    private void Update()
    {
        if (!panelIsVisible)
        {
            if (TryGetPointerDown(out Vector2 pointerPosition) &&
                IsSettingsButtonHit(pointerPosition))
            {
                OpenSettings();
            }

            return;
        }

        if (activeSlider >= 0)
        {
            if (TryGetPointerPosition(out Vector2 pointerPosition) &&
                PointerIsHeld())
            {
                SetSliderFromPointer(activeSlider, pointerPosition);
            }
            else
            {
                activeSlider = -1;
                PlayerPrefs.Save();
            }
        }

        if (!TryGetPointerDown(out Vector2 pressedPosition))
        {
            return;
        }

        if (Contains(mainMenuRect, pressedPosition))
        {
            CloseSettings();
            return;
        }

        if (Contains(audioTabRect, pressedPosition))
        {
            ShowAudioPage();
            return;
        }

        if (Contains(videoTabRect, pressedPosition))
        {
            ShowVideoPage();
            return;
        }

        if (Contains(accessibilityTabRect, pressedPosition))
        {
            ShowAccessibilityPage();
            return;
        }

        if (audioPage.activeSelf)
        {
            for (int index = 0; index < sliderRects.Length; index++)
            {
                if (!Contains(sliderRects[index], pressedPosition))
                {
                    continue;
                }

                activeSlider = index;
                SetSliderFromPointer(index, pressedPosition);
                return;
            }
        }
        else if (videoPage.activeSelf)
        {
            for (int index = 0; index < displayModeRects.Length; index++)
            {
                if (!Contains(displayModeRects[index], pressedPosition))
                {
                    continue;
                }

                ApplyDisplayMode(index);
                return;
            }
        }
    }

    private bool IsSettingsButtonHit(Vector2 pointerPosition)
    {
        if (targetCamera == null || settingsButton == null)
        {
            return false;
        }

        Ray ray = targetCamera.ScreenPointToRay(pointerPosition);
        return Physics.Raycast(ray, out RaycastHit hit) &&
            (hit.transform == settingsButton || hit.transform.IsChildOf(settingsButton));
    }

    private void OpenSettings()
    {
        panelIsVisible = true;
        activeSlider = -1;
        RefreshMusicSources();
        ApplyMusicVolume();
        SetMenuObjectsActive(false);
        depthOfFieldController?.ActivateFullBackgroundBlur();
        interfaceRoot.SetActive(true);
        ShowAudioPage();
        RefreshAllVisuals();
    }

    private void CloseSettings()
    {
        panelIsVisible = false;
        activeSlider = -1;
        interfaceRoot.SetActive(false);
        SetMenuObjectsActive(true);
        depthOfFieldController?.DeactivateBackgroundBlur();
        PlayerPrefs.Save();
    }

    private void ShowAudioPage()
    {
        audioPage.SetActive(true);
        videoPage.SetActive(false);
        accessibilityPage.SetActive(false);
        audioTabImage.color = new Color(0.36f, 0.38f, 0.42f, 1f);
        videoTabImage.color = new Color(0.19f, 0.2f, 0.23f, 1f);
        accessibilityTabImage.color = new Color(0.19f, 0.2f, 0.23f, 1f);
    }

    private void SetMenuObjectsActive(bool isActive)
    {
        if (menuObjectsToHide == null)
        {
            return;
        }

        foreach (GameObject menuObject in menuObjectsToHide)
        {
            if (menuObject != null)
            {
                menuObject.SetActive(isActive);
            }
        }
    }

    private void ShowVideoPage()
    {
        audioPage.SetActive(false);
        videoPage.SetActive(true);
        accessibilityPage.SetActive(false);
        audioTabImage.color = new Color(0.19f, 0.2f, 0.23f, 1f);
        videoTabImage.color = new Color(0.36f, 0.38f, 0.42f, 1f);
        accessibilityTabImage.color = new Color(0.19f, 0.2f, 0.23f, 1f);
        RefreshDisplayModeVisuals();
    }

    private void ShowAccessibilityPage()
    {
        audioPage.SetActive(false);
        videoPage.SetActive(false);
        accessibilityPage.SetActive(true);
        audioTabImage.color = new Color(0.19f, 0.2f, 0.23f, 1f);
        videoTabImage.color = new Color(0.19f, 0.2f, 0.23f, 1f);
        accessibilityTabImage.color = new Color(0.36f, 0.38f, 0.42f, 1f);
    }

    private void SetSliderFromPointer(int index, Vector2 pointerPosition)
    {
        RectTransform rect = sliderRects[index];
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rect,
            pointerPosition,
            null,
            out Vector2 localPoint);
        float value = Mathf.InverseLerp(rect.rect.xMin, rect.rect.xMax, localPoint.x);

        switch (index)
        {
            case 0:
                masterVolume = value;
                AudioListener.volume = value;
                PlayerPrefs.SetFloat(MasterVolumeKey, value);
                break;
            case 1:
                musicVolume = value;
                PlayerPrefs.SetFloat(MusicVolumeKey, value);
                ApplyMusicVolume();
                break;
            case 2:
                microphoneVolume = value;
                PlayerPrefs.SetFloat(MicrophoneVolumeKey, value);
                break;
            case 3:
                effectsVolume = value;
                PlayerPrefs.SetFloat(EffectsVolumeKey, value);
                break;
            case 4:
                voiceChatVolume = value;
                PlayerPrefs.SetFloat(VoiceChatVolumeKey, value);
                break;
        }

        RefreshSliderVisual(index);
    }

    private void ApplyDisplayMode(int mode)
    {
        displayMode = mode;
        PlayerPrefs.SetInt(DisplayModeKey, mode);
        PlayerPrefs.Save();

        int displayWidth = Display.main.systemWidth;
        int displayHeight = Display.main.systemHeight;
        switch (mode)
        {
            case 0:
                Screen.SetResolution(
                    displayWidth,
                    displayHeight,
                    FullScreenMode.ExclusiveFullScreen);
                break;
            case 1:
                int windowWidth = Mathf.Min(1600, Mathf.Max(960, displayWidth - 320));
                int windowHeight = Mathf.RoundToInt(windowWidth * 9f / 16f);
                Screen.SetResolution(windowWidth, windowHeight, FullScreenMode.Windowed);
                break;
            case 2:
                Screen.SetResolution(
                    displayWidth,
                    displayHeight,
                    FullScreenMode.FullScreenWindow);
                break;
        }

        RefreshDisplayModeVisuals();
    }

    private void RefreshMusicSources()
    {
        foreach (AudioSource source in FindObjectsByType<AudioSource>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (source == null || !source.loop || musicBaseVolumes.ContainsKey(source))
            {
                continue;
            }

            musicBaseVolumes[source] = source.volume;
        }
    }

    private void ApplyMusicVolume()
    {
        List<AudioSource> missingSources = null;
        foreach (KeyValuePair<AudioSource, float> entry in musicBaseVolumes)
        {
            if (entry.Key == null)
            {
                missingSources ??= new List<AudioSource>();
                missingSources.Add(entry.Key);
                continue;
            }

            entry.Key.volume = entry.Value * musicVolume;
        }

        if (missingSources == null)
        {
            return;
        }

        foreach (AudioSource source in missingSources)
        {
            musicBaseVolumes.Remove(source);
        }
    }

    private void CreateInterface()
    {
        interfaceRoot = new GameObject("Lobby Settings Interface (Scene Only)");
        interfaceRoot.transform.SetParent(transform, false);

        Canvas canvas = interfaceRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 210;

        CanvasScaler scaler = interfaceRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject panel = CreateImage(
            "Settings Panel",
            interfaceRoot.transform,
            new Vector2(0.16f, 0.11f),
            new Vector2(0.84f, 0.89f),
            new Color(0.075f, 0.08f, 0.095f, 0.42f));
        AddOutline(panel, new Color(0.58f, 0.6f, 0.62f, 0.68f));

        CreateText(
            "Settings Title",
            panel.transform,
            new Vector2(0.075f, 0.84f),
            new Vector2(0.93f, 0.96f),
            "SETTINGS",
            54,
            FontStyle.Bold,
            new Color(0.92f, 0.93f, 0.92f, 1f),
            TextAnchor.MiddleLeft);

        GameObject audioTab = CreateImage(
            "Audio Tab",
            panel.transform,
            new Vector2(0.075f, 0.73f),
            new Vector2(0.29f, 0.82f),
            Color.white);
        audioTabRect = audioTab.GetComponent<RectTransform>();
        audioTabImage = audioTab.GetComponent<Image>();
        CreateCenteredLabel(audioTab.transform, "AUDIO", 29);

        GameObject videoTab = CreateImage(
            "Video Tab",
            panel.transform,
            new Vector2(0.305f, 0.73f),
            new Vector2(0.52f, 0.82f),
            Color.white);
        videoTabRect = videoTab.GetComponent<RectTransform>();
        videoTabImage = videoTab.GetComponent<Image>();
        CreateCenteredLabel(videoTab.transform, "VIDEO", 29);

        GameObject accessibilityTab = CreateImage(
            "Accessibility Tab",
            panel.transform,
            new Vector2(0.535f, 0.73f),
            new Vector2(0.925f, 0.82f),
            Color.white);
        accessibilityTabRect = accessibilityTab.GetComponent<RectTransform>();
        accessibilityTabImage = accessibilityTab.GetComponent<Image>();
        CreateCenteredLabel(accessibilityTab.transform, "ACCESSIBILITY", 27);

        audioPage = CreatePage("Audio Settings Page", panel.transform);
        videoPage = CreatePage("Video Settings Page", panel.transform);
        accessibilityPage = CreatePage(
            "Accessibility Settings Page",
            panel.transform);
        CreateAudioPage();
        CreateVideoPage();
        CreateAccessibilityPage();

        GameObject mainMenu = CreateImage(
            "Settings Main Menu Button",
            interfaceRoot.transform,
            new Vector2(0.025f, 0.9f),
            new Vector2(0.19f, 0.97f),
            new Color(0.12f, 0.13f, 0.15f, 0.94f));
        mainMenuRect = mainMenu.GetComponent<RectTransform>();
        AddOutline(mainMenu, new Color(0.7f, 0.71f, 0.7f, 0.85f));
        CreateCenteredLabel(mainMenu.transform, "MAIN MENU", 34);

        interfaceRoot.SetActive(false);
    }

    private void CreateAudioPage()
    {
        CreateText(
            "Audio Description",
            audioPage.transform,
            new Vector2(0.075f, 0.78f),
            new Vector2(0.93f, 0.94f),
            "Adjust lobby sound and voice input levels.",
            25,
            FontStyle.Normal,
            new Color(0.69f, 0.71f, 0.73f, 1f),
            TextAnchor.MiddleLeft);

        CreateSlider(0, "MASTER VOLUME", 0.58f, 0.075f, 0.47f);
        CreateSlider(1, "MUSIC VOLUME", 0.34f, 0.075f, 0.47f);
        CreateSlider(3, "EFFECTS VOLUME", 0.1f, 0.075f, 0.47f);
        CreateSlider(2, "MICROPHONE", 0.58f, 0.53f, 0.925f);
        CreateSlider(4, "VOICE CHAT", 0.38f, 0.53f, 0.925f);
        CreateMicrophonePlaceholders();
    }

    private void CreateSlider(
        int index,
        string label,
        float bottom,
        float left,
        float right)
    {
        CreateText(
            label + " Label",
            audioPage.transform,
            new Vector2(left, bottom + 0.11f),
            new Vector2(right - 0.12f, bottom + 0.21f),
            label,
            30,
            FontStyle.Bold,
            new Color(0.89f, 0.9f, 0.89f, 1f),
            TextAnchor.MiddleLeft);

        sliderValues[index] = CreateText(
            label + " Value",
            audioPage.transform,
            new Vector2(right - 0.12f, bottom + 0.11f),
            new Vector2(right, bottom + 0.21f),
            "100%",
            27,
            FontStyle.Bold,
            new Color(0.8f, 0.82f, 0.84f, 1f),
            TextAnchor.MiddleRight);

        GameObject track = CreateImage(
            label + " Slider",
            audioPage.transform,
            new Vector2(left, bottom + 0.035f),
            new Vector2(right, bottom + 0.095f),
            new Color(0.15f, 0.16f, 0.18f, 1f));
        sliderRects[index] = track.GetComponent<RectTransform>();

        GameObject fill = CreateImage(
            label + " Fill",
            track.transform,
            Vector2.zero,
            Vector2.one,
            new Color(0.67f, 0.69f, 0.72f, 1f));
        sliderFills[index] = fill.GetComponent<Image>();
        sliderFills[index].type = Image.Type.Filled;
        sliderFills[index].fillMethod = Image.FillMethod.Horizontal;
        sliderFills[index].fillOrigin = 0;

        GameObject knob = CreateImage(
            label + " Position Marker",
            track.transform,
            new Vector2(0f, -0.22f),
            new Vector2(0f, 1.22f),
            new Color(0.25f, 0.27f, 0.3f, 1f));
        sliderKnobs[index] = knob.GetComponent<RectTransform>();
        sliderKnobs[index].sizeDelta = new Vector2(18f, 0f);
        AddOutline(knob, new Color(0.84f, 0.85f, 0.84f, 0.9f));
    }

    private void CreateMicrophonePlaceholders()
    {
        CreateText(
            "Input Device Label",
            audioPage.transform,
            new Vector2(0.53f, 0.29f),
            new Vector2(0.925f, 0.36f),
            "INPUT DEVICE",
            27,
            FontStyle.Bold,
            new Color(0.89f, 0.9f, 0.89f, 1f),
            TextAnchor.MiddleLeft);

        GameObject deviceField = CreateImage(
            "Microphone Device Placeholder",
            audioPage.transform,
            new Vector2(0.53f, 0.21f),
            new Vector2(0.925f, 0.29f),
            new Color(0.18f, 0.19f, 0.21f, 1f));
        AddOutline(deviceField, new Color(0.45f, 0.47f, 0.5f, 0.8f));
        CreateText(
            "Microphone Device Placeholder Label",
            deviceField.transform,
            new Vector2(0.05f, 0.08f),
            new Vector2(0.95f, 0.92f),
            "DEFAULT MICROPHONE     >",
            22,
            FontStyle.Bold,
            new Color(0.72f, 0.74f, 0.76f, 1f),
            TextAnchor.MiddleLeft);

        CreateText(
            "Mic Test Label",
            audioPage.transform,
            new Vector2(0.53f, 0.135f),
            new Vector2(0.925f, 0.2f),
            "MIC TEST",
            27,
            FontStyle.Bold,
            new Color(0.89f, 0.9f, 0.89f, 1f),
            TextAnchor.MiddleLeft);

        GameObject meter = CreateImage(
            "Microphone Test Meter Placeholder",
            audioPage.transform,
            new Vector2(0.53f, 0.075f),
            new Vector2(0.78f, 0.125f),
            new Color(0.15f, 0.16f, 0.18f, 1f));
        CreateImage(
            "Microphone Test Meter Preview Level",
            meter.transform,
            Vector2.zero,
            new Vector2(0.18f, 1f),
            new Color(0.42f, 0.44f, 0.47f, 1f));

        GameObject testButton = CreateImage(
            "Microphone Test Placeholder Button",
            audioPage.transform,
            new Vector2(0.8f, 0.06f),
            new Vector2(0.925f, 0.14f),
            new Color(0.24f, 0.25f, 0.28f, 1f));
        CreateText(
            "Microphone Test Placeholder Button Label",
            testButton.transform,
            new Vector2(0.04f, 0.08f),
            new Vector2(0.96f, 0.92f),
            "TEST",
            22,
            FontStyle.Bold,
            new Color(0.69f, 0.71f, 0.73f, 1f),
            TextAnchor.MiddleCenter);

    }

    private void CreateVideoPage()
    {
        CreateText(
            "Video Description",
            videoPage.transform,
            new Vector2(0.075f, 0.78f),
            new Vector2(0.93f, 0.94f),
            "Choose how Voice Chess is displayed.",
            25,
            FontStyle.Normal,
            new Color(0.69f, 0.71f, 0.73f, 1f),
            TextAnchor.MiddleLeft);

        CreateDisplayModeButton(0, "FULLSCREEN", 0.55f,
            "Exclusive full-screen display");
        CreateDisplayModeButton(1, "WINDOWED", 0.34f,
            "Resizable framed window");
        CreateDisplayModeButton(2, "BORDERLESS", 0.13f,
            "Borderless full-screen window");

        CreateChoicePlaceholder(videoPage.transform, "RESOLUTION", "1920 x 1080", 0.61f);
        CreateChoicePlaceholder(videoPage.transform, "QUALITY", "HIGH", 0.45f);
        CreateChoicePlaceholder(videoPage.transform, "FPS LIMIT", "60 FPS", 0.29f);
        CreateCompactToggle(videoPage.transform, "V-SYNC", "ON", 0.13f, 0.53f, 0.72f);
        CreateCompactToggle(
            videoPage.transform,
            "BACKGROUND BLUR",
            "ON",
            0.13f,
            0.735f,
            0.925f);
    }

    private void CreateDisplayModeButton(
        int index,
        string label,
        float bottom,
        string description)
    {
        GameObject button = CreateImage(
            label + " Display Mode",
            videoPage.transform,
            new Vector2(0.075f, bottom),
            new Vector2(0.47f, bottom + 0.16f),
            Color.white);
        displayModeRects[index] = button.GetComponent<RectTransform>();
        displayModeImages[index] = button.GetComponent<Image>();

        CreateText(
            label + " Display Label",
            button.transform,
            new Vector2(0.055f, 0.45f),
            new Vector2(0.76f, 0.9f),
            label,
            30,
            FontStyle.Bold,
            new Color(0.94f, 0.94f, 0.93f, 1f),
            TextAnchor.MiddleLeft);
        CreateText(
            label + " Display Description",
            button.transform,
            new Vector2(0.055f, 0.08f),
            new Vector2(0.95f, 0.49f),
            description,
            22,
            FontStyle.Normal,
            new Color(0.71f, 0.73f, 0.75f, 1f),
            TextAnchor.MiddleLeft);
        CreateText(
            label + " Selected Mark",
            button.transform,
            new Vector2(0.77f, 0.5f),
            new Vector2(0.96f, 0.9f),
            "SELECT",
            18,
            FontStyle.Bold,
            new Color(0.88f, 0.89f, 0.88f, 1f),
            TextAnchor.MiddleCenter);
    }

    private void CreateAccessibilityPage()
    {
        CreateText(
            "Accessibility Description",
            accessibilityPage.transform,
            new Vector2(0.075f, 0.78f),
            new Vector2(0.93f, 0.94f),
            "Preview of game and accessibility options.",
            25,
            FontStyle.Normal,
            new Color(0.69f, 0.71f, 0.73f, 1f),
            TextAnchor.MiddleLeft);

        CreateChoicePlaceholder(
            accessibilityPage.transform,
            "LANGUAGE",
            "ENGLISH",
            0.61f);
        CreateChoicePlaceholder(
            accessibilityPage.transform,
            "SUBTITLES",
            "ON",
            0.45f);
        CreateChoicePlaceholder(
            accessibilityPage.transform,
            "CAMERA SHAKE",
            "ON",
            0.29f);
        CreateCompactToggle(
            accessibilityPage.transform,
            "TOON OUTLINE",
            "MEDIUM",
            0.13f,
            0.075f,
            0.47f);
        CreateCompactToggle(
            accessibilityPage.transform,
            "COLORBLIND MODE",
            "OFF",
            0.13f,
            0.53f,
            0.925f);
    }

    private static void CreateChoicePlaceholder(
        Transform page,
        string label,
        string value,
        float bottom)
    {
        CreateText(
            label + " Placeholder Label",
            page,
            new Vector2(0.53f, bottom + 0.085f),
            new Vector2(0.925f, bottom + 0.16f),
            label,
            25,
            FontStyle.Bold,
            new Color(0.89f, 0.9f, 0.89f, 1f),
            TextAnchor.MiddleLeft);

        GameObject field = CreateImage(
            label + " Placeholder Field",
            page,
            new Vector2(0.53f, bottom),
            new Vector2(0.925f, bottom + 0.085f),
            new Color(0.18f, 0.19f, 0.21f, 1f));
        AddOutline(field, new Color(0.45f, 0.47f, 0.5f, 0.75f));
        CreateText(
            label + " Placeholder Value",
            field.transform,
            new Vector2(0.06f, 0.08f),
            new Vector2(0.94f, 0.92f),
            value + "     >",
            22,
            FontStyle.Bold,
            new Color(0.76f, 0.78f, 0.8f, 1f),
            TextAnchor.MiddleLeft);
    }

    private static void CreateCompactToggle(
        Transform page,
        string label,
        string value,
        float bottom,
        float left,
        float right)
    {
        CreateText(
            label + " Placeholder Label",
            page,
            new Vector2(left, bottom + 0.085f),
            new Vector2(right, bottom + 0.16f),
            label,
            22,
            FontStyle.Bold,
            new Color(0.89f, 0.9f, 0.89f, 1f),
            TextAnchor.MiddleLeft);

        GameObject field = CreateImage(
            label + " Placeholder Toggle",
            page,
            new Vector2(left, bottom),
            new Vector2(right, bottom + 0.085f),
            new Color(0.18f, 0.19f, 0.21f, 1f));
        CreateText(
            label + " Placeholder Value",
            field.transform,
            new Vector2(0.05f, 0.08f),
            new Vector2(0.95f, 0.92f),
            value,
            21,
            FontStyle.Bold,
            new Color(0.76f, 0.78f, 0.8f, 1f),
            TextAnchor.MiddleCenter);
    }

    private void RefreshAllVisuals()
    {
        for (int index = 0; index < sliderRects.Length; index++)
        {
            RefreshSliderVisual(index);
        }

        RefreshDisplayModeVisuals();
    }

    private void RefreshSliderVisual(int index)
    {
        float value = index switch
        {
            0 => masterVolume,
            1 => musicVolume,
            2 => microphoneVolume,
            3 => effectsVolume,
            _ => voiceChatVolume
        };
        sliderFills[index].fillAmount = value;
        sliderValues[index].text = $"{Mathf.RoundToInt(value * 100f)}%";
        sliderKnobs[index].anchorMin = new Vector2(value, -0.22f);
        sliderKnobs[index].anchorMax = new Vector2(value, 1.22f);
        sliderKnobs[index].anchoredPosition = Vector2.zero;
        sliderKnobs[index].sizeDelta = new Vector2(18f, 0f);
    }

    private void RefreshDisplayModeVisuals()
    {
        for (int index = 0; index < displayModeImages.Length; index++)
        {
            displayModeImages[index].color = index == displayMode
                ? new Color(0.38f, 0.4f, 0.44f, 1f)
                : new Color(0.18f, 0.19f, 0.21f, 1f);
        }
    }

    private static GameObject CreatePage(string name, Transform parent)
    {
        GameObject page = new(name);
        page.transform.SetParent(parent, false);
        RectTransform rect = page.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0.73f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return page;
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

    private static Text CreateText(
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
        return text;
    }

    private static void CreateCenteredLabel(Transform parent, string value, int size)
    {
        CreateText(
            value + " Label",
            parent,
            new Vector2(0.04f, 0.08f),
            new Vector2(0.96f, 0.92f),
            value,
            size,
            FontStyle.Bold,
            new Color(0.94f, 0.94f, 0.92f, 1f),
            TextAnchor.MiddleCenter);
    }

    private static void AddOutline(GameObject target, Color color)
    {
        Outline outline = target.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(2f, -2f);
    }

    private static bool Contains(RectTransform rect, Vector2 pointerPosition)
    {
        return rect != null && RectTransformUtility.RectangleContainsScreenPoint(
            rect,
            pointerPosition);
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

    private static bool TryGetPointerPosition(out Vector2 pointerPosition)
    {
        if (Mouse.current != null)
        {
            pointerPosition = Mouse.current.position.ReadValue();
            return true;
        }

        if (Touchscreen.current != null)
        {
            pointerPosition = Touchscreen.current.primaryTouch.position.ReadValue();
            return true;
        }

        pointerPosition = default;
        return false;
    }

    private static bool PointerIsHeld()
    {
        return (Mouse.current != null && Mouse.current.leftButton.isPressed) ||
            (Touchscreen.current != null &&
             Touchscreen.current.primaryTouch.press.isPressed);
    }

    private void OnDestroy()
    {
        if (interfaceRoot != null)
        {
            Destroy(interfaceRoot);
        }
    }
}
