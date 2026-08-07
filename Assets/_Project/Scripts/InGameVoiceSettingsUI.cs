using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class InGameVoiceSettingsUI : MonoBehaviour
{
    private const float DesignWidth = 1600f;
    private const float DesignHeight = 900f;

    private static InGameVoiceSettingsUI _instance;
    private readonly List<Texture2D> _generatedTextures = new();

    private AzureKoreanSpeechInput _speechInput;
    private bool _pauseMenuOpen;
    private bool _open;
    private GUIStyle _panel;
    private GUIStyle _panelSoft;
    private GUIStyle _row;
    private GUIStyle _buttonDark;
    private GUIStyle _buttonLight;
    private GUIStyle _buttonSelected;
    private GUIStyle _title;
    private GUIStyle _menuTitle;
    private GUIStyle _section;
    private GUIStyle _body;
    private GUIStyle _small;
    private Texture2D _whiteTexture;
    private Texture2D _dimTexture;

    public static bool IsOpen => _instance != null && _instance._open;
    public static bool IsBlockingGameplay =>
        _instance != null &&
        (_instance._pauseMenuOpen || _instance._open);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (SceneManager.GetActiveScene().name != "SampleScene" ||
            FindFirstObjectByType<InGameVoiceSettingsUI>() != null)
        {
            return;
        }

        GameObject settings = new("In-game Voice Settings UI");
        settings.AddComponent<InGameVoiceSettingsUI>();
    }

    private void Awake()
    {
        _instance = this;
    }

    private void Update()
    {
        if (_speechInput == null)
        {
            _speechInput = FindFirstObjectByType<AzureKoreanSpeechInput>();
        }

        if (!NetworkPlayer.MatchStarted || SessionManager.IsFrontEndVisible)
        {
            _pauseMenuOpen = false;
            _open = false;
            return;
        }

        Keyboard keyboard = Keyboard.current;

        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
        {
            return;
        }

        if (_open)
        {
            _open = false;
            _pauseMenuOpen = true;
        }
        else
        {
            _pauseMenuOpen = !_pauseMenuOpen;
        }
    }

    private void OnGUI()
    {
        if (!NetworkPlayer.MatchStarted || SessionManager.IsFrontEndVisible)
        {
            _pauseMenuOpen = false;
            _open = false;
            return;
        }

        if (!_pauseMenuOpen && !_open)
        {
            return;
        }

        EnsureStyles();
        GUI.depth = -1500;

        Matrix4x4 previousMatrix = GUI.matrix;
        float scale = Mathf.Min(Screen.width / DesignWidth, Screen.height / DesignHeight);
        float offsetX = (Screen.width - DesignWidth * scale) * 0.5f;
        float offsetY = (Screen.height - DesignHeight * scale) * 0.5f;
        GUI.matrix = Matrix4x4.TRS(
            new Vector3(offsetX, offsetY, 0f),
            Quaternion.identity,
            new Vector3(scale, scale, 1f));

        if (_open)
        {
            DrawSettingsModal();
        }
        else
        {
            DrawPauseMenu();
        }

        GUI.matrix = previousMatrix;
    }

    private void DrawPauseMenu()
    {
        GUI.DrawTexture(
            new Rect(0f, 0f, DesignWidth, DesignHeight),
            _dimTexture,
            ScaleMode.StretchToFill);

        Rect panelRect = new(500f, 260f, 600f, 380f);
        GUI.Box(panelRect, GUIContent.none, _panel);
        GUI.Label(new Rect(550f, 320f, 500f, 60f), "SETTINGS", _menuTitle);

        if (GUI.Button(
                new Rect(590f, 430f, 420f, 82f),
                "VOICE SETTINGS",
                _buttonLight))
        {
            _open = true;
        }

        GUI.Label(
            new Rect(550f, 555f, 500f, 30f),
            "PRESS ESC TO RETURN TO THE GAME",
            _small);
    }

    private void DrawSettingsModal()
    {
        GUI.DrawTexture(
            new Rect(0f, 0f, DesignWidth, DesignHeight),
            _dimTexture,
            ScaleMode.StretchToFill);

        Rect panelRect = new(350f, 70f, 900f, 760f);
        GUI.Box(panelRect, GUIContent.none, _panel);

        GUI.Label(new Rect(398f, 102f, 520f, 46f), "VOICE SETTINGS", _title);

        if (GUI.Button(new Rect(1162f, 94f, 48f, 42f), "×", _buttonDark))
        {
            _open = false;
        }

        DrawVoiceActivationSettings();
        DrawMicrophoneDevices();
        DrawInputLevel();
        DrawRecognitionTest();
    }

    private void DrawVoiceActivationSettings()
    {
        GUI.Label(new Rect(400f, 184f, 300f, 30f), "VOICE ACTIVATION", _section);

        if (_speechInput == null)
        {
            return;
        }

        bool previousEnabled = GUI.enabled;
        GUI.enabled = !_speechInput.IsRecognitionInProgress;

        if (GUI.Button(
                new Rect(400f, 222f, 190f, 48f),
                "AUTOMATIC",
                _speechInput.InputMode == VoiceInputMode.Automatic
                    ? _buttonSelected
                    : _buttonDark))
        {
            _speechInput.SetInputMode(VoiceInputMode.Automatic);
        }

        if (GUI.Button(
                new Rect(600f, 222f, 190f, 48f),
                "HOLD [V]",
                _speechInput.InputMode == VoiceInputMode.PushToTalk
                    ? _buttonSelected
                    : _buttonDark))
        {
            _speechInput.SetInputMode(VoiceInputMode.PushToTalk);
        }

        GUI.Label(new Rect(814f, 184f, 220f, 30f), "SENSITIVITY", _section);
        float sensitivity = GUI.HorizontalSlider(
            new Rect(814f, 240f, 388f, 18f),
            _speechInput.VoiceSensitivity,
            0f,
            1f);
        _speechInput.SetVoiceSensitivity(sensitivity);

        GUI.enabled = previousEnabled;
    }

    private void DrawMicrophoneDevices()
    {
        GUI.Label(new Rect(400f, 308f, 260f, 30f), "MIC INPUT", _section);

        bool previousEnabled = GUI.enabled;
        GUI.enabled = _speechInput != null && !_speechInput.IsRecognitionInProgress;

        if (GUI.Button(new Rect(1060f, 300f, 142f, 42f), "REFRESH", _buttonDark))
        {
            _speechInput.RefreshMicrophoneDevices();
        }

        if (_speechInput == null || _speechInput.MicrophoneDevices.Count == 0)
        {
            GUI.enabled = previousEnabled;
            return;
        }

        int visibleCount = Mathf.Min(_speechInput.MicrophoneDevices.Count, 4);

        for (int index = 0; index < visibleCount; index++)
        {
            int column = index % 2;
            int row = index / 2;
            Rect buttonRect = new(
                398f + column * 410f,
                352f + row * 54f,
                394f,
                46f);
            bool selected = index == _speechInput.SelectedMicrophoneIndex;

            if (GUI.Button(
                    buttonRect,
                    _speechInput.MicrophoneDevices[index],
                    selected ? _buttonSelected : _buttonDark))
            {
                _speechInput.SelectMicrophone(index);
            }
        }

        GUI.enabled = previousEnabled;
    }

    private void DrawInputLevel()
    {
        GUI.Label(new Rect(400f, 486f, 300f, 30f), "INPUT LEVEL", _section);

        Rect meterBackground = new(400f, 528f, 802f, 24f);
        GUI.Box(meterBackground, GUIContent.none, _panelSoft);
        Rect fill = new(
            meterBackground.x,
            meterBackground.y,
            meterBackground.width * (_speechInput?.MicrophoneLevel ?? 0f),
            meterBackground.height);
        GUI.DrawTexture(fill, _whiteTexture, ScaleMode.StretchToFill);
    }

    private void DrawRecognitionTest()
    {
        GUI.Label(new Rect(400f, 600f, 320f, 30f), "RECOGNITION TEST", _section);

        Rect transcriptRect = new(400f, 642f, 566f, 120f);
        GUI.Box(transcriptRect, GUIContent.none, _row);
        string transcript = _speechInput == null
            ? "음성 시스템을 준비하는 중입니다."
            : string.IsNullOrWhiteSpace(_speechInput.LastTranscript)
                ? _speechInput.Status
                : $"“{_speechInput.LastTranscript}”";
        GUI.Label(
            new Rect(422f, 654f, 520f, 96f),
            transcript,
            _body);

        bool previousEnabled = GUI.enabled;
        bool canStartTest = _speechInput != null &&
                            _speechInput.IsMicrophoneRunning &&
                            _speechInput.HasSpeechCredentials &&
                            !_speechInput.IsNoiseCalibrating &&
                            !_speechInput.IsRecognitionInProgress;
        bool canStopTest = _speechInput != null && _speechInput.IsCapturingSpeech;
        GUI.enabled = canStartTest || canStopTest;

        if (GUI.Button(
                new Rect(984f, 642f, 218f, 120f),
                _speechInput != null && _speechInput.IsCapturingSpeech
                    ? "STOP & ANALYZE"
                    : _speechInput != null && _speechInput.IsRecognitionInProgress
                        ? "ANALYZING..."
                        : "START TEST",
                _buttonLight))
        {
            _speechInput.RequestRecognitionTest();
        }

        GUI.enabled = previousEnabled;
    }

    private void EnsureStyles()
    {
        if (_panel != null)
        {
            return;
        }

        _whiteTexture = MakeTexture(Color.white);
        _dimTexture = MakeTexture(new Color(0f, 0f, 0f, 0.78f));
        _panel = MakeBoxStyle(new Color32(20, 20, 20, 255), 18);
        _panelSoft = MakeBoxStyle(new Color32(8, 8, 8, 255), 10);
        _row = MakeBoxStyle(new Color32(31, 31, 31, 255), 12);
        _buttonDark = MakeButtonStyle(
            new Color32(42, 42, 42, 255),
            new Color32(60, 60, 60, 255),
            Color.white,
            16,
            10);
        _buttonLight = MakeButtonStyle(
            new Color32(238, 238, 238, 255),
            Color.white,
            new Color32(14, 14, 14, 255),
            17,
            10);
        _buttonSelected = MakeButtonStyle(
            Color.white,
            new Color32(232, 232, 232, 255),
            new Color32(12, 12, 12, 255),
            15,
            10);
        _title = MakeLabelStyle(34, Color.white, FontStyle.Bold, TextAnchor.MiddleLeft);
        _menuTitle = MakeLabelStyle(38, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
        _section = MakeLabelStyle(18, Color.white, FontStyle.Bold, TextAnchor.MiddleLeft);
        _body = MakeLabelStyle(16, new Color32(235, 235, 235, 255), FontStyle.Bold, TextAnchor.MiddleLeft);
        _small = MakeLabelStyle(12, new Color32(205, 205, 205, 255), FontStyle.Bold, TextAnchor.MiddleRight);
    }

    private GUIStyle MakeBoxStyle(Color color, int radius)
    {
        Texture2D background = MakeRoundedTexture(color, radius);
        GUIStyle style = new(GUI.skin.box)
        {
            border = new RectOffset(18, 18, 18, 18)
        };
        style.normal.background = background;
        style.hover.background = background;
        style.active.background = background;
        return style;
    }

    private GUIStyle MakeButtonStyle(
        Color normalColor,
        Color hoverColor,
        Color textColor,
        int fontSize,
        int radius)
    {
        GUIStyle style = new(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            border = new RectOffset(16, 16, 16, 16),
            normal =
            {
                background = MakeRoundedTexture(normalColor, radius),
                textColor = textColor
            },
            hover =
            {
                background = MakeRoundedTexture(hoverColor, radius),
                textColor = textColor
            },
            active =
            {
                background = MakeRoundedTexture(normalColor, radius),
                textColor = textColor
            }
        };
        return style;
    }

    private static GUIStyle MakeLabelStyle(
        int fontSize,
        Color textColor,
        FontStyle fontStyle,
        TextAnchor alignment)
    {
        GUIStyle style = new(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = fontStyle,
            alignment = alignment,
            wordWrap = true
        };
        style.normal.textColor = textColor;
        style.hover.textColor = textColor;
        style.active.textColor = textColor;
        return style;
    }

    private Texture2D MakeTexture(Color color)
    {
        Texture2D texture = new(1, 1, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixel(0, 0, color);
        texture.Apply();
        _generatedTextures.Add(texture);
        return texture;
    }

    private Texture2D MakeRoundedTexture(Color color, int radius)
    {
        const int size = 64;
        Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        Color clear = new(color.r, color.g, color.b, 0f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nearestX = Mathf.Clamp(x, radius, size - 1f - radius);
                float nearestY = Mathf.Clamp(y, radius, size - 1f - radius);
                float distance = Vector2.Distance(
                    new Vector2(x, y),
                    new Vector2(nearestX, nearestY));
                texture.SetPixel(x, y, distance <= radius ? color : clear);
            }
        }

        texture.Apply();
        _generatedTextures.Add(texture);
        return texture;
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }

        foreach (Texture2D texture in _generatedTextures)
        {
            if (texture != null)
            {
                Destroy(texture);
            }
        }
    }
}
