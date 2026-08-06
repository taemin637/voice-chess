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
    private bool _open;
    private GUIStyle _panel;
    private GUIStyle _panelSoft;
    private GUIStyle _row;
    private GUIStyle _buttonDark;
    private GUIStyle _buttonLight;
    private GUIStyle _buttonSelected;
    private GUIStyle _title;
    private GUIStyle _section;
    private GUIStyle _body;
    private GUIStyle _muted;
    private GUIStyle _small;
    private Texture2D _whiteTexture;
    private Texture2D _dimTexture;

    public static bool IsOpen => _instance != null && _instance._open;

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

        Keyboard keyboard = Keyboard.current;

        if (_open && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            _open = false;
        }
    }

    private void OnGUI()
    {
        if (!NetworkPlayer.MatchStarted || SessionManager.IsFrontEndVisible)
        {
            _open = false;
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

        DrawVoiceHud();

        if (GUI.Button(new Rect(1380f, 86f, 180f, 48f), "VOICE SETTINGS", _buttonDark))
        {
            _open = true;
        }

        if (_open)
        {
            DrawSettingsModal();
        }

        GUI.matrix = previousMatrix;
    }

    private void DrawVoiceHud()
    {
        Rect rect = new(36f, 726f, 500f, 138f);
        GUI.Box(rect, GUIContent.none, _panel);

        GUI.Label(new Rect(58f, 742f, 250f, 28f), "VOICE COMMAND", _section);

        bool ready = _speechInput != null &&
                     _speechInput.IsMicrophoneRunning &&
                     _speechInput.HasSpeechCredentials;
        DrawStatusDot(new Rect(474f, 747f, 12f, 12f), ready);
        GUI.Label(
            new Rect(324f, 738f, 142f, 28f),
            ready ? "READY" : "CHECK SETUP",
            _small);

        string status = _speechInput == null
            ? "음성 입력을 준비하는 중입니다."
            : _speechInput.Status;
        GUI.Label(new Rect(58f, 776f, 306f, 58f), status, _muted);

        bool previousEnabled = GUI.enabled;
        bool pushToTalk = _speechInput != null &&
                          _speechInput.InputMode == VoiceInputMode.PushToTalk;
        bool canToggleInput = pushToTalk &&
                               (!_speechInput.IsRecognitionInProgress ||
                                _speechInput.IsCapturingSpeech);
        GUI.enabled = canToggleInput;

        if (GUI.Button(
                new Rect(374f, 782f, 136f, 48f),
                GetListenButtonLabel(),
                _buttonLight))
        {
            _speechInput.RequestGameRecognition();
        }

        GUI.enabled = previousEnabled;
    }

    private void DrawSettingsModal()
    {
        GUI.DrawTexture(
            new Rect(0f, 0f, DesignWidth, DesignHeight),
            _dimTexture,
            ScaleMode.StretchToFill);

        Rect panelRect = new(350f, 30f, 900f, 840f);
        GUI.Box(panelRect, GUIContent.none, _panel);

        GUI.Label(new Rect(398f, 58f, 520f, 46f), "VOICE SETTINGS", _title);
        GUI.Label(
            new Rect(400f, 100f, 660f, 28f),
            "자동 음성 감지, 마이크 장치와 Azure 한국어 음성 인식을 설정합니다.",
            _muted);

        if (GUI.Button(new Rect(1162f, 52f, 48f, 42f), "×", _buttonDark))
        {
            _open = false;
        }

        DrawAzureStatus();
        DrawVoiceActivationSettings();
        DrawMicrophoneDevices();
        DrawInputLevel();
        DrawRecognitionTest();

        if (GUI.Button(new Rect(1010f, 800f, 190f, 48f), "CLOSE", _buttonLight))
        {
            _open = false;
        }
    }

    private string GetListenButtonLabel()
    {
        if (_speechInput == null)
        {
            return "VOICE OFF";
        }

        if (_speechInput.IsCapturingSpeech)
        {
            return "STOP";
        }

        if (_speechInput.IsRecognitionInProgress)
        {
            return "ANALYZING...";
        }

        return _speechInput.InputMode == VoiceInputMode.Automatic
            ? _speechInput.IsNoiseCalibrating
                ? "CALIBRATING"
                : "AUTO LISTEN"
            : "HOLD  [V]";
    }

    private void DrawAzureStatus()
    {
        Rect row = new(398f, 142f, 804f, 60f);
        GUI.Box(row, GUIContent.none, _row);
        GUI.Label(new Rect(422f, 157f, 200f, 24f), "AZURE SPEECH", _section);

        bool configured = _speechInput != null && _speechInput.HasSpeechCredentials;
        DrawStatusDot(new Rect(1136f, 166f, 12f, 12f), configured);
        GUI.Label(
            new Rect(920f, 156f, 204f, 28f),
            configured
                ? $"CONNECTED · {_speechInput.SpeechRegion}"
                : "KEY / REGION REQUIRED",
            _small);
    }

    private void DrawVoiceActivationSettings()
    {
        GUI.Label(new Rect(400f, 218f, 300f, 30f), "VOICE ACTIVATION", _section);

        if (_speechInput == null)
        {
            GUI.Label(new Rect(400f, 254f, 802f, 80f), "음성 시스템 준비 중", _muted);
            return;
        }

        bool previousEnabled = GUI.enabled;
        GUI.enabled = !_speechInput.IsRecognitionInProgress;

        if (GUI.Button(
                new Rect(400f, 252f, 190f, 42f),
                "AUTOMATIC",
                _speechInput.InputMode == VoiceInputMode.Automatic
                    ? _buttonSelected
                    : _buttonDark))
        {
            _speechInput.SetInputMode(VoiceInputMode.Automatic);
        }

        if (GUI.Button(
                new Rect(600f, 252f, 190f, 42f),
                "HOLD [V]",
                _speechInput.InputMode == VoiceInputMode.PushToTalk
                    ? _buttonSelected
                    : _buttonDark))
        {
            _speechInput.SetInputMode(VoiceInputMode.PushToTalk);
        }

        GUI.Label(new Rect(814f, 246f, 190f, 22f), "SENSITIVITY", _small);
        float sensitivity = GUI.HorizontalSlider(
            new Rect(814f, 276f, 180f, 18f),
            _speechInput.VoiceSensitivity,
            0f,
            1f);
        _speechInput.SetVoiceSensitivity(sensitivity);
        GUI.Label(
            new Rect(1004f, 258f, 74f, 28f),
            $"{sensitivity * 100f:F0}%",
            _small);

        string noiseButton = _speechInput.AutomaticNoiseCalibration
            ? "AUTO NOISE  ON"
            : "AUTO NOISE  OFF";

        if (GUI.Button(new Rect(400f, 306f, 190f, 42f), noiseButton, _buttonDark))
        {
            _speechInput.SetAutomaticNoiseCalibration(
                !_speechInput.AutomaticNoiseCalibration);
        }

        if (GUI.Button(new Rect(600f, 306f, 190f, 42f), "RECALIBRATE", _buttonDark))
        {
            _speechInput.RecalibrateNoiseFloor();
        }

        GUI.Label(
            new Rect(814f, 304f, 388f, 46f),
            _speechInput.IsNoiseCalibrating
                ? "CALIBRATING · 잠시 말하지 마세요"
                : $"NOISE {_speechInput.NoiseFloorDecibels:F1} dBFS  /  " +
                  $"TRIGGER {_speechInput.VoiceActivationThresholdDecibels:F1} dBFS",
            _muted);

        GUI.enabled = previousEnabled;
    }

    private void DrawMicrophoneDevices()
    {
        GUI.Label(new Rect(400f, 366f, 260f, 30f), "MIC INPUT", _section);

        bool previousEnabled = GUI.enabled;
        GUI.enabled = _speechInput != null && !_speechInput.IsRecognitionInProgress;

        if (GUI.Button(new Rect(1060f, 360f, 142f, 38f), "REFRESH", _buttonDark))
        {
            _speechInput.RefreshMicrophoneDevices();
        }

        if (_speechInput == null || _speechInput.MicrophoneDevices.Count == 0)
        {
            GUI.Label(
                new Rect(400f, 408f, 802f, 54f),
                "사용 가능한 마이크를 찾지 못했습니다.",
                _muted);
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
                404f + row * 52f,
                394f,
                44f);
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
        GUI.Label(new Rect(400f, 512f, 300f, 30f), "INPUT LEVEL", _section);

        Rect meterBackground = new(400f, 548f, 802f, 22f);
        GUI.Box(meterBackground, GUIContent.none, _panelSoft);
        Rect fill = new(
            meterBackground.x,
            meterBackground.y,
            meterBackground.width * (_speechInput?.MicrophoneLevel ?? 0f),
            meterBackground.height);
        GUI.DrawTexture(fill, _whiteTexture, ScaleMode.StretchToFill);

        float currentDb = _speechInput?.MicrophoneDecibels ?? -80f;
        float peakDb = _speechInput?.PeakMicrophoneDecibels ?? -80f;
        GUI.Label(
            new Rect(400f, 578f, 400f, 28f),
            $"CURRENT {currentDb:F1} dBFS   /   PEAK {peakDb:F1} dBFS",
            _muted);

        bool signal = _speechInput != null && _speechInput.HasDetectedMicrophoneSignal;
        DrawStatusDot(new Rect(1138f, 586f, 12f, 12f), signal);
        GUI.Label(
            new Rect(928f, 576f, 194f, 28f),
            signal ? "INPUT DETECTED" : "NO SIGNAL",
            _small);
    }

    private void DrawRecognitionTest()
    {
        GUI.Label(new Rect(400f, 618f, 320f, 30f), "RECOGNITION TEST", _section);
        GUI.Label(
            new Rect(730f, 618f, 472f, 30f),
            "START → SPEAK → STOP & ANALYZE",
            _small);

        Rect transcriptRect = new(400f, 654f, 566f, 120f);
        GUI.Box(transcriptRect, GUIContent.none, _row);
        string transcript = _speechInput == null
            ? "음성 시스템을 준비하는 중입니다."
            : string.IsNullOrWhiteSpace(_speechInput.LastTranscript)
                ? _speechInput.Status
                : $"“{_speechInput.LastTranscript}”\n{_speechInput.Status}";
        GUI.Label(
            new Rect(422f, 666f, 520f, 96f),
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
                new Rect(984f, 654f, 218f, 120f),
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

    private void DrawStatusDot(Rect rect, bool active)
    {
        Color previousColor = GUI.color;
        GUI.color = active ? Color.white : new Color32(92, 92, 92, 255);
        GUI.DrawTexture(rect, _whiteTexture, ScaleMode.StretchToFill);
        GUI.color = previousColor;
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
        _section = MakeLabelStyle(18, Color.white, FontStyle.Bold, TextAnchor.MiddleLeft);
        _body = MakeLabelStyle(16, new Color32(235, 235, 235, 255), FontStyle.Bold, TextAnchor.MiddleLeft);
        _muted = MakeLabelStyle(14, new Color32(160, 160, 160, 255), FontStyle.Normal, TextAnchor.MiddleLeft);
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
