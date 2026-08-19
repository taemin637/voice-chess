using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum VoiceInputMode
{
    Automatic,
    PushToTalk
}

[DisallowMultipleComponent]
public sealed class AzureKoreanSpeechInput : MonoBehaviour
{
    private const string SpeechKeyVariable = "AZURE_SPEECH_KEY";
    private const string SpeechRegionVariable = "AZURE_SPEECH_REGION";
    private const string BuildCredentialsFileName = "azure-speech.json";
    private const string EditorSpeechKeyPreference = "VoiceChess.AzureSpeech.Key";
    private const string EditorSpeechRegionPreference = "VoiceChess.AzureSpeech.Region";
    private const string SelectedMicrophonePreference = "VoiceChess.SelectedMicrophone";
    private const string InputModePreference = "VoiceChess.VoiceInputMode";
    private const string VoiceSensitivityPreference = "VoiceChess.VoiceSensitivity";
    private const string AutoNoiseCalibrationPreference = "VoiceChess.AutoNoiseCalibration";
    private const string NoiseFloorPreference = "VoiceChess.NoiseFloorDb";
    private const int MicrophoneSampleRate = 16000;
    private const int PreRollSampleCount = 3200;

    [Header("음성 인식 조정")]
    [SerializeField, HideInInspector, Range(0f, 1f)] private float minimumConfidence = 0.55f;
    [SerializeField, HideInInspector, Range(-80f, 0f)] private float quietCommandDecibels = -45f;
    [SerializeField, HideInInspector, Range(-80f, 0f)] private float loudCommandDecibels = -12f;
    [SerializeField, HideInInspector, Min(0f)] private float minimumCommandReach = 1f;
    [SerializeField, HideInInspector, Min(0.01f)] private float maximumCommandReach = 12f;

    [Header("자동 음성 감지")]
    [SerializeField, HideInInspector, Min(0.01f)] private float voiceStartHoldSeconds = 0.08f;
    [SerializeField, HideInInspector, Min(0.01f)] private float voiceEndSilenceSeconds = 0.3f;
    [SerializeField, HideInInspector, Min(0f)] private float targetSwitchBoundarySilenceSeconds = 0.04f;
    [SerializeField, HideInInspector, Min(0f)] private float minimumTargetSwitchUtteranceSeconds = 0.3f;
    [SerializeField, HideInInspector, Min(0.1f)] private float maximumAutomaticUtteranceSeconds = 3f;
    [SerializeField, HideInInspector, Min(0.1f)] private float noiseCalibrationSeconds = 1.5f;
    [SerializeField, HideInInspector, Min(0.01f)] private float speechBoundaryAverageSeconds = 0.2f;

    private readonly ConcurrentQueue<RecognitionOutcome> _outcomes = new();
    private readonly ConcurrentQueue<CapturedUtterance> _utteranceQueue = new();
    private readonly ConcurrentQueue<string> _partialTranscripts = new();
    private readonly List<float> _speechLoudnessSamples = new();
    private readonly List<LoudnessFrame> _voicedLoudnessFrames = new();
    private readonly List<LoudnessFrame> _preRollLoudnessFrames = new();
    private readonly List<float> _noiseCalibrationSamples = new();
    private readonly List<byte> _capturedSpeechPcm = new();
    private readonly List<string> _whiteVoiceCommandHistory = new();
    private readonly List<string> _blackVoiceCommandHistory = new();
    private readonly float[] _preRollSamples = new float[PreRollSampleCount];

    private AudioClip _microphoneClip;
    private float[] _microphoneSamples;
    private byte[] _speechPcmBuffer;
    private string[] _microphoneDevices = Array.Empty<string>();
    private string _activeMicrophoneDevice;
    private int _selectedMicrophoneIndex;
    private int _lastMicrophonePosition = -1;
    private int _preRollWriteIndex;
    private int _preRollCount;
    private float _preRollLoudnessDuration;
    private NetworkChessGame _game;
    private bool _microphoneRunning;
    private bool _recognitionInProgress;
    private bool _azureWorkerBusy;
    private bool _isCapturingSpeech;
    private bool _isDestroyed;
    private LiveRecognitionSession _activeLiveSession;
    private bool _activeLiveSessionFailed;
    private float _microphoneLevel;
    private float _microphoneDecibels = -80f;
    private float _peakMicrophoneDecibels = -80f;
    private string _status = "자동 음성 감지 준비 중입니다.";
    private string _microphoneStatus = "마이크를 준비하는 중입니다.";
    private string _lastTranscript = string.Empty;
    private bool _hasPendingVoiceTarget;
    private bool _pendingCommandContextCaptured;
    private ushort _pendingVoiceTargetPieceId;
    private float _pendingVoiceTargetDistance;
    private bool _pendingHasChargeAim;
    private Vector2 _pendingChargeAimBoardPosition;
    private bool _utteranceStartTargetCaptured;
    private bool _hasUtteranceStartTarget;
    private ushort _utteranceStartTargetPieceId;
    private float _utteranceStartTargetDistance;
    private bool _utteranceStartHasChargeAim;
    private Vector2 _utteranceStartChargeAimBoardPosition;
    private float _lastCommandLoudnessDecibels = -80f;
    private float _lastCommandReachInSquares;
    private VoiceInputMode _inputMode = VoiceInputMode.Automatic;
    private float _voiceSensitivity = 0.55f;
    private bool _automaticNoiseCalibration = true;
    private float _noiseFloorDecibels = -55f;
    private float _noiseCalibrationRemaining;
    private float _voiceAboveThresholdDuration;
    private float _voiceSilenceDuration;
    private float _automaticUtteranceDuration;
    private float _automaticCandidateStartTime;
    private float _automaticRequestedStartTime;
    private bool _automaticStartRequested;
    private bool _automaticStopRequested;
    private bool _currentRecognitionIsAutomatic;
    private bool _currentRecognitionExecutesCommand;
    private float _liveChargePronunciationScore;
    private bool _debugOverlayVisible;
    private string _liveTranscript = string.Empty;
    private string _lastRecognizedCommand = "아직 인식된 명령이 없습니다.";
    private bool _commandHistoryVisible;
    private CursorLockMode _cursorLockModeBeforeCommandHistory;
    private bool _cursorVisibleBeforeCommandHistory;
    private int _whiteSuccessfulVoiceCommandCount;
    private int _blackSuccessfulVoiceCommandCount;
    private Vector2 _whiteHistoryScrollPosition;
    private Vector2 _blackHistoryScrollPosition;
    private GUIStyle _debugOverlayBoxStyle;
    private GUIStyle _debugOverlayTextStyle;
    private GUIStyle _historyTitleStyle;
    private GUIStyle _historyHintStyle;
    private GUIStyle _historyHeaderStyle;
    private GUIStyle _historyEntryStyle;
    private GUIStyle _historyEmptyStyle;
    private int _debugOverlayFontSize;

    private static bool _buildCredentialsLoaded;
    private static string _buildSpeechKey = string.Empty;
    private static string _buildSpeechRegion = string.Empty;
    private static int _commandHistoryClosedFrame = -1;

    public static bool IsCommandHistoryOpen { get; private set; }
    public static bool DidCloseCommandHistoryThisFrame =>
        _commandHistoryClosedFrame == Time.frameCount;

    [Serializable]
    private sealed class BuildSpeechCredentials
    {
        public string key = string.Empty;
        public string region = string.Empty;
    }

    private readonly struct LoudnessFrame
    {
        public readonly float Decibels;
        public readonly float Duration;

        public LoudnessFrame(float decibels, float duration)
        {
            Decibels = decibels;
            Duration = duration;
        }
    }

    public IReadOnlyList<string> MicrophoneDevices => _microphoneDevices;
    public int SelectedMicrophoneIndex => _selectedMicrophoneIndex;
    public string SelectedMicrophoneName => GetSelectedMicrophoneDevice();
    public bool IsMicrophoneRunning => _microphoneRunning;
    public bool IsRecognitionInProgress => _recognitionInProgress;
    public bool IsCapturingSpeech => _isCapturingSpeech;
    public bool HasDetectedMicrophoneSignal => _peakMicrophoneDecibels > -55f;
    public float MicrophoneLevel => _microphoneLevel;
    public float MicrophoneDecibels => _microphoneDecibels;
    public float PeakMicrophoneDecibels => _peakMicrophoneDecibels;
    public string Status => _status;
    public string MicrophoneStatus => _microphoneStatus;
    public string LastTranscript => _lastTranscript;
    public float LastCommandLoudnessDecibels => _lastCommandLoudnessDecibels;
    public float LastCommandReachInSquares => _lastCommandReachInSquares;
    public VoiceInputMode InputMode => _inputMode;
    public float VoiceSensitivity => _voiceSensitivity;
    public bool AutomaticNoiseCalibration => _automaticNoiseCalibration;
    public bool IsNoiseCalibrating => _noiseCalibrationRemaining > 0f;
    public float NoiseFloorDecibels => _noiseFloorDecibels;
    public Key PushToTalkKey => ResolveVoiceSettings()?.PushToTalkKey ?? Key.V;
    public float VoiceActivationThresholdDecibels =>
        _noiseFloorDecibels + Mathf.Lerp(18f, 6f, _voiceSensitivity);
    public string SpeechRegion
    {
        get
        {
            GetCredentials(out _, out string region);
            return region;
        }
    }
    public bool HasSpeechCredentials
    {
        get
        {
            GetCredentials(out string key, out string region);
            return !string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(region);
        }
    }

    private sealed class CapturedUtterance
    {
        public readonly bool ExecuteCommand;
        public readonly byte[] PcmData;
        public readonly bool HasVoiceTarget;
        public readonly ushort VoiceTargetPieceId;
        public readonly float VoiceTargetDistance;
        public readonly bool HasChargeAim;
        public readonly Vector2 ChargeAimBoardPosition;
        public readonly float CommandLoudnessDecibels;
        public readonly float CommandReachInSquares;
        public readonly float SpeechStartAverageDecibels;
        public readonly float SpeechEndAverageDecibels;
        public readonly float SpeechAverageDecibels;
        public readonly float SpeechDurationSeconds;

        public CapturedUtterance(
            bool executeCommand,
            byte[] pcmData,
            bool hasVoiceTarget,
            ushort voiceTargetPieceId,
            float voiceTargetDistance,
            bool hasChargeAim,
            Vector2 chargeAimBoardPosition,
            float commandLoudnessDecibels,
            float commandReachInSquares,
            float speechStartAverageDecibels,
            float speechEndAverageDecibels,
            float speechAverageDecibels,
            float speechDurationSeconds)
        {
            ExecuteCommand = executeCommand;
            PcmData = pcmData;
            HasVoiceTarget = hasVoiceTarget;
            VoiceTargetPieceId = voiceTargetPieceId;
            VoiceTargetDistance = voiceTargetDistance;
            HasChargeAim = hasChargeAim;
            ChargeAimBoardPosition = chargeAimBoardPosition;
            CommandLoudnessDecibels = commandLoudnessDecibels;
            CommandReachInSquares = commandReachInSquares;
            SpeechStartAverageDecibels = speechStartAverageDecibels;
            SpeechEndAverageDecibels = speechEndAverageDecibels;
            SpeechAverageDecibels = speechAverageDecibels;
            SpeechDurationSeconds = speechDurationSeconds;
        }
    }

    private sealed class LiveRecognitionSession : IDisposable
    {
        private readonly object _streamLock = new();
        private readonly SpeechConfig _speechConfig;
        private readonly AudioStreamFormat _streamFormat;
        private readonly PushAudioInputStream _pushStream;
        private readonly AudioConfig _audioConfig;
        private readonly SpeechRecognizer _recognizer;
        private bool _inputClosed;

        public Task<SpeechRecognitionResult> Recognition { get; }

        public LiveRecognitionSession(
            SpeechConfig speechConfig,
            AudioStreamFormat streamFormat,
            PushAudioInputStream pushStream,
            AudioConfig audioConfig,
            SpeechRecognizer recognizer,
            Action<string> recognizingCallback)
        {
            _speechConfig = speechConfig;
            _streamFormat = streamFormat;
            _pushStream = pushStream;
            _audioConfig = audioConfig;
            _recognizer = recognizer;

            if (recognizingCallback != null)
            {
                _recognizer.Recognizing += (_, eventArgs) =>
                {
                    string partialText = eventArgs.Result?.Text;

                    if (!string.IsNullOrWhiteSpace(partialText))
                    {
                        recognizingCallback(partialText);
                    }
                };
            }

            Recognition = _recognizer.RecognizeOnceAsync();
        }

        public void Write(byte[] pcmData, int byteCount)
        {
            lock (_streamLock)
            {
                if (_inputClosed)
                {
                    return;
                }

                _pushStream.Write(pcmData, byteCount);
            }
        }

        public void CloseInput()
        {
            lock (_streamLock)
            {
                if (_inputClosed)
                {
                    return;
                }

                _pushStream.Close();
                _inputClosed = true;
            }
        }

        public void Dispose()
        {
            CloseInput();
            _recognizer.Dispose();
            _audioConfig.Dispose();
            _pushStream.Dispose();
            _streamFormat.Dispose();
            GC.KeepAlive(_speechConfig);
        }
    }

    private readonly struct RecognitionOutcome
    {
        public readonly bool Accepted;
        public readonly bool ExecuteCommand;
        public readonly PieceVoiceCommand[] Commands;
        public readonly string Text;
        public readonly double Confidence;
        public readonly string Error;
        public readonly bool HasVoiceTarget;
        public readonly ushort VoiceTargetPieceId;
        public readonly float VoiceTargetDistance;
        public readonly bool HasChargeAim;
        public readonly Vector2 ChargeAimBoardPosition;
        public readonly float CommandLoudnessDecibels;
        public readonly float CommandReachInSquares;
        public readonly float SpeechStartAverageDecibels;
        public readonly float SpeechEndAverageDecibels;
        public readonly float SpeechAverageDecibels;
        public readonly float SpeechDurationSeconds;
        public readonly float TextSimilarityScore;

        private RecognitionOutcome(
            CapturedUtterance utterance,
            bool accepted,
            PieceVoiceCommand[] commands,
            string text,
            double confidence,
            string error,
            float textSimilarityScore)
        {
            Accepted = accepted;
            ExecuteCommand = utterance.ExecuteCommand;
            Commands = commands;
            Text = text;
            Confidence = confidence;
            Error = error;
            HasVoiceTarget = utterance.HasVoiceTarget;
            VoiceTargetPieceId = utterance.VoiceTargetPieceId;
            VoiceTargetDistance = utterance.VoiceTargetDistance;
            HasChargeAim = utterance.HasChargeAim;
            ChargeAimBoardPosition = utterance.ChargeAimBoardPosition;
            CommandLoudnessDecibels = utterance.CommandLoudnessDecibels;
            CommandReachInSquares = utterance.CommandReachInSquares;
            SpeechStartAverageDecibels = utterance.SpeechStartAverageDecibels;
            SpeechEndAverageDecibels = utterance.SpeechEndAverageDecibels;
            SpeechAverageDecibels = utterance.SpeechAverageDecibels;
            SpeechDurationSeconds = utterance.SpeechDurationSeconds;
            TextSimilarityScore = Mathf.Clamp01(textSimilarityScore);
        }

        public static RecognitionOutcome Success(
            CapturedUtterance utterance,
            PieceVoiceCommand[] commands,
            string text,
            double confidence,
            float textSimilarityScore)
        {
            return new RecognitionOutcome(
                utterance,
                true,
                commands,
                text,
                confidence,
                null,
                textSimilarityScore);
        }

        public static RecognitionOutcome Rejected(
            CapturedUtterance utterance,
            string text,
            double confidence,
            string error,
            float textSimilarityScore = 0f)
        {
            return new RecognitionOutcome(
                utterance,
                false,
                Array.Empty<PieceVoiceCommand>(),
                text,
                confidence,
                error,
                textSimilarityScore);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (SceneManager.GetActiveScene().name != "SampleScene" ||
            FindFirstObjectByType<AzureKoreanSpeechInput>() != null)
        {
            return;
        }

        GameObject speechInput = new("Azure Korean Speech Input");
        speechInput.AddComponent<AzureKoreanSpeechInput>();
    }

    private void Awake()
    {
        VoiceRecognitionSettings settings = ResolveVoiceSettings();
        VoiceInputMode defaultInputMode = settings?.DefaultInputMode ??
            VoiceInputMode.Automatic;
        float defaultSensitivity = settings?.DefaultVoiceSensitivity ?? 0.55f;
        bool defaultAutomaticCalibration =
            settings?.DefaultAutomaticNoiseCalibration ?? true;
        float defaultNoiseFloor = settings?.DefaultNoiseFloorDecibels ?? -55f;
        _inputMode = (VoiceInputMode)Mathf.Clamp(
            PlayerPrefs.GetInt(InputModePreference, (int)defaultInputMode),
            (int)VoiceInputMode.Automatic,
            (int)VoiceInputMode.PushToTalk);
        _voiceSensitivity = Mathf.Clamp01(
            PlayerPrefs.GetFloat(VoiceSensitivityPreference, defaultSensitivity));
        _automaticNoiseCalibration =
            PlayerPrefs.GetInt(
                AutoNoiseCalibrationPreference,
                defaultAutomaticCalibration ? 1 : 0) != 0;
        _noiseFloorDecibels = Mathf.Clamp(
            PlayerPrefs.GetFloat(NoiseFloorPreference, defaultNoiseFloor),
            -80f,
            -10f);
        RefreshMicrophoneDevices(restartCapture: false);
        UpdateIdleStatus();
    }

    private IEnumerator Start()
    {
        yield return StartMicrophoneCapture();
    }

    private void Update()
    {
        if (_game == null || !_game.IsSpawned)
        {
            _game = FindFirstObjectByType<NetworkChessGame>();
        }

        UpdateMicrophoneCapture();
        ProcessAutomaticVoiceRequests();
        ProcessPartialTranscripts();

        while (_outcomes.TryDequeue(out RecognitionOutcome outcome))
        {
            HandleOutcome(outcome);
        }

        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && keyboard.backquoteKey.wasPressedThisFrame)
        {
            _debugOverlayVisible = !_debugOverlayVisible;
        }

        bool enterPressed = keyboard != null &&
            (keyboard.enterKey.wasPressedThisFrame ||
             keyboard.numpadEnterKey.wasPressedThisFrame);
        bool escapePressed = keyboard != null &&
            keyboard.escapeKey.wasPressedThisFrame;

        if (_commandHistoryVisible && (enterPressed || escapePressed))
        {
            CloseCommandHistory();
        }
        else if (enterPressed && !InGameVoiceSettingsUI.IsBlockingGameplay)
        {
            OpenCommandHistory();
        }

        if (_inputMode == VoiceInputMode.PushToTalk &&
            keyboard != null &&
            NetworkPlayer.MatchStarted &&
            !InGameVoiceSettingsUI.IsBlockingGameplay)
        {
            Key pushToTalkKey = PushToTalkKey;

            if (pushToTalkKey != Key.None &&
                keyboard[pushToTalkKey].wasPressedThisFrame)
            {
                BeginRecognition(executeCommand: true, includePreRoll: false);
            }

            if (pushToTalkKey != Key.None &&
                keyboard[pushToTalkKey].wasReleasedThisFrame)
            {
                FinishSpeechInput();
            }
        }
    }

    private void OpenCommandHistory()
    {
        if (_commandHistoryVisible)
        {
            return;
        }

        _cursorLockModeBeforeCommandHistory = Cursor.lockState;
        _cursorVisibleBeforeCommandHistory = Cursor.visible;
        _commandHistoryVisible = true;
        IsCommandHistoryOpen = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void CloseCommandHistory()
    {
        if (!_commandHistoryVisible)
        {
            return;
        }

        _commandHistoryVisible = false;
        IsCommandHistoryOpen = false;
        _commandHistoryClosedFrame = Time.frameCount;
        Cursor.lockState = _cursorLockModeBeforeCommandHistory;
        Cursor.visible = _cursorVisibleBeforeCommandHistory;
    }

    private void OnGUI()
    {
        if (!_debugOverlayVisible && !_commandHistoryVisible)
        {
            return;
        }

        float scale = Mathf.Clamp(Screen.height / 900f, 0.8f, 2f);
        int fontSize = Mathf.RoundToInt(17f * scale);
        EnsureDebugOverlayStyles(fontSize);

        int previousDepth = GUI.depth;
        GUI.depth = -2000;

        if (_commandHistoryVisible)
        {
            DrawCommandHistoryWindow(scale);
        }

        if (_debugOverlayVisible)
        {
            DrawRecognitionDebugOverlay(scale);
        }

        GUI.depth = previousDepth;
    }

    private void DrawRecognitionDebugOverlay(float scale)
    {

        const float baseMargin = 16f;
        const float basePanelWidth = 720f;
        const float baseLineHeight = 26f;
        const float baseHorizontalPadding = 12f;
        const float baseVerticalPadding = 8f;
        float margin = baseMargin * scale;
        float lineHeight = baseLineHeight * scale;
        float horizontalPadding = baseHorizontalPadding * scale;
        float verticalPadding = baseVerticalPadding * scale;
        float availableWidth = Mathf.Max(1f, Screen.width - margin * 2f);
        float panelWidth = Mathf.Min(basePanelWidth * scale, availableWidth);
        float panelHeight = verticalPadding * 2f + lineHeight * 2f;
        Rect panelRect = new(
            Screen.width - margin - panelWidth,
            Screen.height - margin - panelHeight,
            panelWidth,
            panelHeight);

        GUI.Box(panelRect, GUIContent.none, _debugOverlayBoxStyle);

        string liveText = _isCapturingSpeech &&
                          !string.IsNullOrWhiteSpace(_liveTranscript)
            ? $"“{_liveTranscript}”"
            : _status;
        Rect liveRect = new(
            panelRect.x + horizontalPadding,
            panelRect.y + verticalPadding,
            panelRect.width - horizontalPadding * 2f,
            lineHeight);
        Rect commandRect = new(
            liveRect.x,
            liveRect.y + lineHeight,
            liveRect.width,
            lineHeight);
        GUI.Label(liveRect, $"실시간 인식: {liveText}", _debugOverlayTextStyle);
        GUI.Label(
            commandRect,
            $"최근 명령 인식: {_lastRecognizedCommand}",
            _debugOverlayTextStyle);
    }

    private void DrawCommandHistoryWindow(float scale)
    {
        float margin = 28f * scale;
        float availableWidth = Mathf.Max(1f, Screen.width - margin * 2f);
        float availableHeight = Mathf.Max(1f, Screen.height - margin * 2f);
        float panelWidth = Mathf.Min(1180f * scale, availableWidth);
        float panelHeight = Mathf.Min(720f * scale, availableHeight);
        Rect panelRect = new(
            (Screen.width - panelWidth) * 0.5f,
            (Screen.height - panelHeight) * 0.5f,
            panelWidth,
            panelHeight);
        GUI.Box(panelRect, GUIContent.none, _debugOverlayBoxStyle);

        float padding = 18f * scale;
        float titleHeight = 42f * scale;
        float hintHeight = 26f * scale;
        GUI.Label(
            new Rect(
                panelRect.x + padding,
                panelRect.y + padding,
                panelRect.width - padding * 2f,
                titleHeight),
            "음성 명령 기록",
            _historyTitleStyle);
        GUI.Label(
            new Rect(
                panelRect.x + padding,
                panelRect.y + padding + titleHeight,
                panelRect.width - padding * 2f,
                hintHeight),
            "기물이 선택된 상태에서 시도한 명령만 표시됩니다. Enter / Esc: 닫기",
            _historyHintStyle);

        float columnsY = panelRect.y + padding + titleHeight + hintHeight + 10f * scale;
        float columnsHeight = panelRect.yMax - padding - columnsY;
        float columnGap = 12f * scale;
        float columnWidth = (panelRect.width - padding * 2f - columnGap) * 0.5f;
        Rect whiteColumn = new(
            panelRect.x + padding,
            columnsY,
            columnWidth,
            columnsHeight);
        Rect blackColumn = new(
            whiteColumn.xMax + columnGap,
            columnsY,
            columnWidth,
            columnsHeight);

        DrawCommandHistoryColumn(
            whiteColumn,
            "백 (WHITE)",
            _whiteVoiceCommandHistory,
            _whiteSuccessfulVoiceCommandCount,
            ref _whiteHistoryScrollPosition,
            scale);
        DrawCommandHistoryColumn(
            blackColumn,
            "흑 (BLACK)",
            _blackVoiceCommandHistory,
            _blackSuccessfulVoiceCommandCount,
            ref _blackHistoryScrollPosition,
            scale);
    }

    private void DrawCommandHistoryColumn(
        Rect columnRect,
        string title,
        IReadOnlyList<string> history,
        int successfulCount,
        ref Vector2 scrollPosition,
        float scale)
    {
        GUI.Box(columnRect, GUIContent.none, _debugOverlayBoxStyle);
        float padding = 12f * scale;
        float headerHeight = 34f * scale;
        int recognitionRate = history.Count == 0
            ? 0
            : Mathf.RoundToInt(successfulCount * 100f / history.Count);
        string header =
            $"{title} · {history.Count}개 · 음성 인식률: {recognitionRate}% " +
            $"({successfulCount}/{history.Count})";
        GUI.Label(
            new Rect(
                columnRect.x + padding,
                columnRect.y + padding,
                columnRect.width - padding * 2f,
                headerHeight),
            header,
            _historyHeaderStyle);

        Rect contentRect = new(
            columnRect.x + padding,
            columnRect.y + padding + headerHeight,
            columnRect.width - padding * 2f,
            columnRect.height - padding * 2f - headerHeight);
        GUILayout.BeginArea(contentRect);
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        if (history.Count == 0)
        {
            GUILayout.Label("아직 시도된 음성 명령이 없습니다.", _historyEmptyStyle);
        }
        else
        {
            for (int index = 0; index < history.Count; index++)
            {
                int sequence = history.Count - index;
                GUILayout.Label(
                    $"{sequence}. {history[index]}",
                    _historyEntryStyle);
                GUILayout.Space(5f * scale);
            }
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void EnsureDebugOverlayStyles(int fontSize)
    {
        if (_debugOverlayTextStyle != null && _debugOverlayFontSize == fontSize)
        {
            return;
        }

        _debugOverlayFontSize = fontSize;
        _debugOverlayBoxStyle = new GUIStyle(GUI.skin.box);
        _debugOverlayTextStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            wordWrap = false
        };
        _debugOverlayTextStyle.normal.textColor = Color.white;
        _historyTitleStyle = new GUIStyle(_debugOverlayTextStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(fontSize * 1.55f)
        };
        _historyHintStyle = new GUIStyle(_debugOverlayTextStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.Max(12, Mathf.RoundToInt(fontSize * 0.82f)),
            fontStyle = FontStyle.Normal
        };
        _historyHintStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
        _historyHeaderStyle = new GUIStyle(_debugOverlayTextStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(fontSize * 1.1f)
        };
        _historyEntryStyle = new GUIStyle(_debugOverlayTextStyle)
        {
            alignment = TextAnchor.UpperLeft,
            fontStyle = FontStyle.Normal,
            wordWrap = true,
            clipping = TextClipping.Overflow
        };
        _historyEmptyStyle = new GUIStyle(_historyEntryStyle)
        {
            alignment = TextAnchor.MiddleCenter
        };
        _historyEmptyStyle.normal.textColor = new Color(0.65f, 0.65f, 0.65f);
    }

    public void RequestRecognitionTest()
    {
        if (!_isCapturingSpeech)
        {
            _lastTranscript = string.Empty;
        }

        ToggleRecognition(executeCommand: false);
    }

    public void RequestGameRecognition()
    {
        ToggleRecognition(executeCommand: true);
    }

    public void SetInputMode(VoiceInputMode mode)
    {
        if (_recognitionInProgress || _inputMode == mode)
        {
            return;
        }

        _inputMode = mode;
        PlayerPrefs.SetInt(InputModePreference, (int)_inputMode);
        PlayerPrefs.Save();
        ResetVoiceActivationState();
        UpdateIdleStatus();
    }

    public void SetVoiceSensitivity(float sensitivity)
    {
        sensitivity = Mathf.Clamp01(sensitivity);

        if (Mathf.Approximately(_voiceSensitivity, sensitivity))
        {
            return;
        }

        _voiceSensitivity = sensitivity;
        PlayerPrefs.SetFloat(VoiceSensitivityPreference, _voiceSensitivity);
        PlayerPrefs.Save();
    }

    public void SetAutomaticNoiseCalibration(bool enabled)
    {
        _automaticNoiseCalibration = enabled;
        PlayerPrefs.SetInt(AutoNoiseCalibrationPreference, enabled ? 1 : 0);
        PlayerPrefs.Save();

        if (enabled)
        {
            RecalibrateNoiseFloor();
        }
        else
        {
            _noiseCalibrationRemaining = 0f;
            _noiseCalibrationSamples.Clear();
            UpdateIdleStatus();
        }
    }

    public void RecalibrateNoiseFloor()
    {
        _noiseCalibrationSamples.Clear();
        _noiseCalibrationRemaining = NoiseCalibrationSeconds;
        ResetVoiceActivationState();
        _status = "주변 소음을 보정하는 중입니다. 잠시 말하지 마세요.";
    }

    public void FinishSpeechInput()
    {
        if (!_isCapturingSpeech)
        {
            return;
        }

        CapturePendingCommandContext();
        FinalizeCommandLoudness();
        CalculateSpeechLoudnessAverages(
            out float speechStartAverageDecibels,
            out float speechEndAverageDecibels,
            out float speechAverageDecibels);
        float speechDurationSeconds = GetVoicedSpeechDuration();
        UpdateLocalVoiceChargePreview();
        _isCapturingSpeech = false;
        _status = "입력 종료 · 결과 분석 중...";

        CapturedUtterance utterance = new(
            _currentRecognitionExecutesCommand,
            _capturedSpeechPcm.ToArray(),
            _hasPendingVoiceTarget,
            _pendingVoiceTargetPieceId,
            _pendingVoiceTargetDistance,
            _pendingHasChargeAim,
            _pendingChargeAimBoardPosition,
            _lastCommandLoudnessDecibels,
            _lastCommandReachInSquares,
            speechStartAverageDecibels,
            speechEndAverageDecibels,
            speechAverageDecibels,
            speechDurationSeconds);
        LiveRecognitionSession liveSession = _activeLiveSession;
        bool useLiveResult = liveSession != null && !_activeLiveSessionFailed;
        _activeLiveSession = null;
        _activeLiveSessionFailed = false;
        _capturedSpeechPcm.Clear();
        _currentRecognitionIsAutomatic = false;
        _currentRecognitionExecutesCommand = false;
        ResetVoiceActivationState();

        if (useLiveResult)
        {
            liveSession.CloseInput();
            CompleteLiveRecognition(liveSession, utterance);
        }
        else
        {
            if (liveSession != null)
            {
                liveSession.Dispose();
                _azureWorkerBusy = false;
            }

            _utteranceQueue.Enqueue(utterance);
            ProcessQueuedUtterances();
        }

        UpdateRecognitionActivity();
    }

    private void ToggleRecognition(bool executeCommand)
    {
        if (_isCapturingSpeech)
        {
            FinishSpeechInput();
            return;
        }

        BeginRecognition(executeCommand, includePreRoll: false);
    }

    public void SelectMicrophone(int index)
    {
        if (_recognitionInProgress ||
            index < 0 ||
            index >= _microphoneDevices.Length ||
            index == _selectedMicrophoneIndex)
        {
            return;
        }

        StopMicrophoneCapture();
        _selectedMicrophoneIndex = index;
        PlayerPrefs.SetString(SelectedMicrophonePreference, SelectedMicrophoneName);
        PlayerPrefs.Save();
        StartCoroutine(StartMicrophoneCapture());
    }

    public void RefreshMicrophoneDevices()
    {
        RefreshMicrophoneDevices(restartCapture: true);
    }

    private void BeginRecognition(
        bool executeCommand,
        bool includePreRoll)
    {
        if (_isCapturingSpeech)
        {
            return;
        }

        if (Application.platform != RuntimePlatform.WindowsEditor &&
            Application.platform != RuntimePlatform.WindowsPlayer)
        {
            _status = "현재 포함된 Azure Speech SDK는 Windows x64용입니다.";
            return;
        }

        if (!_microphoneRunning || _microphoneClip == null)
        {
            _status = "선택한 마이크가 아직 준비되지 않았습니다.";
            RestartMicrophoneCapture();
            return;
        }

        if (IsNoiseCalibrating)
        {
            _status = "주변 소음 보정이 끝날 때까지 잠시 기다려 주세요.";
            return;
        }

        if (executeCommand)
        {
            if (_game == null || !_game.IsSpawned)
            {
                _status = "게임 네트워크가 아직 준비되지 않았습니다.";
                return;
            }

        }

        _currentRecognitionIsAutomatic = includePreRoll;
        _currentRecognitionExecutesCommand = executeCommand;
        _liveChargePronunciationScore = 0f;
        _liveTranscript = string.Empty;

        while (_partialTranscripts.TryDequeue(out _))
        {
        }

        _game?.ClearLocalVoiceChargePreview();
        _speechLoudnessSamples.Clear();
        _voicedLoudnessFrames.Clear();

        if (includePreRoll)
        {
            foreach (LoudnessFrame frame in _preRollLoudnessFrames)
            {
                if (IsVoicedCommandFrame(frame.Decibels))
                {
                    _voicedLoudnessFrames.Add(frame);
                }
            }
        }

        _capturedSpeechPcm.Clear();
        _hasPendingVoiceTarget = false;
        _pendingCommandContextCaptured = false;
        _pendingVoiceTargetPieceId = 0;
        _pendingVoiceTargetDistance = 0f;
        _pendingHasChargeAim = false;
        _pendingChargeAimBoardPosition = default;
        _utteranceStartTargetCaptured = false;
        _hasUtteranceStartTarget = false;
        _utteranceStartTargetPieceId = 0;
        _utteranceStartTargetDistance = 0f;
        _utteranceStartHasChargeAim = false;
        _utteranceStartChargeAimBoardPosition = default;
        _game?.ShowLocalVoiceCommandTarget(null);
        _lastCommandLoudnessDecibels = -80f;
        _lastCommandReachInSquares = 0f;

        if (includePreRoll && _microphoneDecibels > -60f)
        {
            _speechLoudnessSamples.Add(_microphoneDecibels);
        }
        _status = executeCommand
            ? "듣는 중... 명령을 말하세요."
            : "마이크 테스트 중... 문장을 말하세요.";

        _isCapturingSpeech = true;
        _activeLiveSessionFailed = false;

        if (!_azureWorkerBusy && _utteranceQueue.IsEmpty)
        {
            try
            {
                _activeLiveSession = CreateLiveRecognitionSession(
                    partialText => _partialTranscripts.Enqueue(partialText));
                _azureWorkerBusy = true;
            }
            catch (Exception)
            {
                _activeLiveSession = null;
            }
        }

        if (includePreRoll)
        {
            FlushPreRollToCaptureBuffer();
        }

        UpdateRecognitionActivity();
    }

    private async void CompleteLiveRecognition(
        LiveRecognitionSession liveSession,
        CapturedUtterance utterance)
    {
        try
        {
            SpeechRecognitionResult result = await liveSession.Recognition;
            _outcomes.Enqueue(CreateOutcome(result, utterance));
        }
        catch (Exception exception)
        {
            _outcomes.Enqueue(RecognitionOutcome.Rejected(
                utterance,
                string.Empty,
                0d,
                $"음성 인식 오류: {exception.Message}"));
        }
        finally
        {
            liveSession.Dispose();
            _azureWorkerBusy = false;
            UpdateRecognitionActivity();
            ProcessQueuedUtterances();
        }
    }

    private async void ProcessQueuedUtterances()
    {
        if (_azureWorkerBusy || _isDestroyed)
        {
            return;
        }

        _azureWorkerBusy = true;
        UpdateRecognitionActivity();

        while (!_isDestroyed && _utteranceQueue.TryDequeue(out CapturedUtterance utterance))
        {
            try
            {
                SpeechRecognitionResult result = await RecognizeUtteranceAsync(utterance);
                _outcomes.Enqueue(CreateOutcome(result, utterance));
            }
            catch (Exception exception)
            {
                _outcomes.Enqueue(RecognitionOutcome.Rejected(
                    utterance,
                    string.Empty,
                    0d,
                    $"음성 인식 오류: {exception.Message}"));
            }
        }

        _azureWorkerBusy = false;
        UpdateRecognitionActivity();

        if (!_isDestroyed && !_utteranceQueue.IsEmpty)
        {
            ProcessQueuedUtterances();
        }
    }

    private static LiveRecognitionSession CreateLiveRecognitionSession(
        Action<string> recognizingCallback)
    {
        GetCredentials(out string subscriptionKey, out string region);

        if (string.IsNullOrWhiteSpace(subscriptionKey) ||
            string.IsNullOrWhiteSpace(region))
        {
            throw new InvalidOperationException(
                "Azure Speech Key/Region이 없습니다. 에디터의 " +
                "Voice Chess > Azure Speech Settings에서 저장하세요.");
        }

        SpeechConfig speechConfig = null;
        AudioStreamFormat streamFormat = null;
        PushAudioInputStream pushStream = null;
        AudioConfig audioConfig = null;
        SpeechRecognizer recognizer = null;

        try
        {
            speechConfig = SpeechConfig.FromSubscription(subscriptionKey, region);
            speechConfig.SpeechRecognitionLanguage = "ko-KR";
            speechConfig.OutputFormat = OutputFormat.Detailed;
            speechConfig.SetProfanity(ProfanityOption.Raw);
            speechConfig.SetProperty(
                PropertyId.SpeechServiceConnection_InitialSilenceTimeoutMs,
                "5000");
            speechConfig.SetProperty(
                PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs,
                "10000");

            streamFormat = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
            pushStream = AudioInputStream.CreatePushStream(streamFormat);
            audioConfig = AudioConfig.FromStreamInput(pushStream);
            recognizer = new SpeechRecognizer(speechConfig, audioConfig);

            PhraseListGrammar phraseList = PhraseListGrammar.FromRecognizer(recognizer);

            foreach (string phrase in KoreanVoiceCommandParser.ChargePhraseHints)
            {
                phraseList.AddPhrase(phrase);
            }

            phraseList.SetWeight(2.0);
            return new LiveRecognitionSession(
                speechConfig,
                streamFormat,
                pushStream,
                audioConfig,
                recognizer,
                recognizingCallback);
        }
        catch
        {
            recognizer?.Dispose();
            audioConfig?.Dispose();
            pushStream?.Dispose();
            streamFormat?.Dispose();
            throw;
        }
    }

    private static async Task<SpeechRecognitionResult> RecognizeUtteranceAsync(
        CapturedUtterance utterance)
    {
        GetCredentials(out string subscriptionKey, out string region);

        if (string.IsNullOrWhiteSpace(subscriptionKey) ||
            string.IsNullOrWhiteSpace(region))
        {
            throw new InvalidOperationException(
                "Azure Speech Key/Region이 없습니다. 에디터의 " +
                "Voice Chess > Azure Speech Settings에서 저장하세요.");
        }

        SpeechConfig speechConfig = null;
        AudioStreamFormat streamFormat = null;
        PushAudioInputStream pushStream = null;
        AudioConfig audioConfig = null;
        SpeechRecognizer recognizer = null;

        try
        {
            speechConfig = SpeechConfig.FromSubscription(subscriptionKey, region);
            speechConfig.SpeechRecognitionLanguage = "ko-KR";
            speechConfig.OutputFormat = OutputFormat.Detailed;
            speechConfig.SetProfanity(ProfanityOption.Raw);
            speechConfig.SetProperty(
                PropertyId.SpeechServiceConnection_InitialSilenceTimeoutMs,
                "5000");
            speechConfig.SetProperty(
                PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs,
                "10000");

            streamFormat = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
            pushStream = AudioInputStream.CreatePushStream(streamFormat);
            audioConfig = AudioConfig.FromStreamInput(pushStream);
            recognizer = new SpeechRecognizer(speechConfig, audioConfig);

            PhraseListGrammar phraseList = PhraseListGrammar.FromRecognizer(recognizer);

            foreach (string phrase in KoreanVoiceCommandParser.ChargePhraseHints)
            {
                phraseList.AddPhrase(phrase);
            }

            phraseList.SetWeight(2.0);
            Task<SpeechRecognitionResult> recognition = recognizer.RecognizeOnceAsync();

            if (utterance.PcmData.Length > 0)
            {
                pushStream.Write(utterance.PcmData, utterance.PcmData.Length);
            }

            pushStream.Close();
            return await recognition;
        }
        finally
        {
            recognizer?.Dispose();
            audioConfig?.Dispose();
            pushStream?.Dispose();
            streamFormat?.Dispose();
        }
    }

    private void UpdateRecognitionActivity()
    {
        _recognitionInProgress = _isCapturingSpeech ||
            _azureWorkerBusy ||
            !_utteranceQueue.IsEmpty;
    }

    private void ProcessPartialTranscripts()
    {
        while (_partialTranscripts.TryDequeue(out string partialText))
        {
            if (!_isCapturingSpeech)
            {
                continue;
            }

            _liveChargePronunciationScore =
                KoreanVoiceCommandParser.GetChargePronunciationScore(partialText);
            _liveTranscript = partialText.Trim();
        }

        if (_isCapturingSpeech)
        {
            UpdateLocalVoiceChargePreview();
        }
    }

    private void UpdateLocalVoiceChargePreview()
    {
        if (!_currentRecognitionExecutesCommand || _game == null)
        {
            return;
        }

        float commandLoudness = Mathf.InverseLerp(
            QuietCommandDecibels,
            LoudCommandDecibels,
            GetCurrentCommandLoudnessDecibels());
        bool hasChargeAim = _game.TryGetCurrentLocalChargeAim(
            out Vector2 chargeAimBoardPosition);
        _game.UpdateLocalVoiceChargePreview(
            GetVoicedSpeechDuration(),
            commandLoudness,
            _liveChargePronunciationScore,
            hasChargeAim,
            chargeAimBoardPosition);
    }

    private RecognitionOutcome CreateOutcome(
        SpeechRecognitionResult result,
        CapturedUtterance utterance)
    {
        if (result.Reason == ResultReason.Canceled)
        {
            CancellationDetails cancellation = CancellationDetails.FromResult(result);
            return RecognitionOutcome.Rejected(
                utterance,
                result.Text,
                0d,
                $"음성 서비스 취소: {cancellation.Reason} {cancellation.ErrorDetails}");
        }

        if (result.Reason != ResultReason.RecognizedSpeech)
        {
            return RecognitionOutcome.Rejected(
                utterance,
                result.Text,
                0d,
                "말을 인식하지 못했습니다. 다시 말해 주세요.");
        }

        List<DetailedSpeechRecognitionResult> candidates = result.Best()
            .OrderByDescending(candidate => candidate.Confidence)
            .ToList();

        foreach (DetailedSpeechRecognitionResult candidate in candidates)
        {
            IReadOnlyList<KoreanVoiceParseResult> parses =
                KoreanVoiceCommandParser.ParseSequence(candidate.Text);

            if (candidate.Confidence >= MinimumConfidence && parses.Count > 0)
            {
                return RecognitionOutcome.Success(
                    utterance,
                    parses.Select(parse => parse.Command).ToArray(),
                    candidate.Text,
                    candidate.Confidence,
                    parses.Average(parse => parse.Score));
            }
        }

        DetailedSpeechRecognitionResult best = candidates.FirstOrDefault();
        string text = best?.Text ?? result.Text;
        double confidence = best?.Confidence ?? 0d;
        KoreanVoiceParseResult bestParse = KoreanVoiceCommandParser.Parse(text);
        string error = confidence < MinimumConfidence
            ? $"음성 신뢰도가 낮습니다 ({confidence:P0})."
            : bestParse.Reason;

        return RecognitionOutcome.Rejected(
            utterance,
            text,
            confidence,
            error,
            bestParse.Score);
    }

    private void HandleOutcome(RecognitionOutcome outcome)
    {
        _lastTranscript = outcome.Text;
        _lastCommandLoudnessDecibels = outcome.CommandLoudnessDecibels;
        _lastCommandReachInSquares = outcome.CommandReachInSquares;

        if (outcome.ExecuteCommand)
        {
            _lastRecognizedCommand = BuildDebugCommandSummary(outcome);
        }

        if (outcome.ExecuteCommand && !_isCapturingSpeech)
        {
            _game?.ShowLocalVoiceCommandTarget(
                outcome.HasVoiceTarget ? outcome.VoiceTargetPieceId : null,
                1f);
        }

        if (!outcome.Accepted)
        {
            _game?.ClearLocalVoiceChargePreview();
            _status = string.IsNullOrWhiteSpace(_lastTranscript)
                ? outcome.Error
                : $"“{_lastTranscript}” — {outcome.Error}";

            if (outcome.ExecuteCommand)
            {
                _game?.ShowLocalVoiceFailure(
                    outcome.HasVoiceTarget ? outcome.VoiceTargetPieceId : null);
                RecordVoiceCommandAttempt(outcome, false, outcome.Error);
            }

            return;
        }

        string commandName = string.Join(
            " + ",
            outcome.Commands.Select(KoreanVoiceCommand.GetDisplayName));

        // Debug.Log(
        //     $"[음성 명령 dB] “{outcome.Text}” → {commandName} | " +
        //     $"시작 {SpeechBoundaryAverageSeconds:F2}초 평균 " +
        //     $"{outcome.SpeechStartAverageDecibels:F1} dBFS | " +
        //     $"끝 {SpeechBoundaryAverageSeconds:F2}초 평균 " +
        //     $"{outcome.SpeechEndAverageDecibels:F1} dBFS | " +
        //     $"전체 발화 평균 {outcome.SpeechAverageDecibels:F1} dBFS | " +
        //     $"기존 이동 기준(P80) {outcome.CommandLoudnessDecibels:F1} dBFS",
        //     this);

        if (!outcome.ExecuteCommand)
        {
            _game?.ClearLocalVoiceChargePreview();
            _status =
                $"테스트: “{outcome.Text}” → {commandName} ({outcome.Confidence:P0})";
            return;
        }

        if (!outcome.HasVoiceTarget)
        {
            _game?.ClearLocalVoiceChargePreview();
            _status = _game != null && _game.UsesProximityAutoSelection
                ? "플레이어 주변 선택 범위 안에 아군 기물이 없습니다."
                : _game != null && _game.UsesChargeSelectionCommand
                ? $"{GetConfirmSelectionButtonName()}으로 확정 선택한 아군 말이 없습니다."
                : "명령을 말하기 시작할 때 바라본 아군 말이 없었습니다.";
            RecordVoiceCommandAttempt(outcome, false, _status);
            return;
        }

        float finalChargeCost = 0f;
        float finalChargeDistance = 0f;
        float finalPronunciationScore = outcome.TextSimilarityScore;
        CommandEconomySettings economy = _game?.GameMode?.Commands;
        Vector2 currentChargeAimBoardPosition = default;
        bool hasCurrentChargeAim = _game != null &&
            _game.TryGetCurrentLocalChargeAim(
                out currentChargeAimBoardPosition);

        if (economy != null)
        {
            finalPronunciationScore = economy.GetVoiceChargePronunciationScore(
                (float)outcome.Confidence,
                outcome.TextSimilarityScore);
        }

        foreach (PieceVoiceCommand command in outcome.Commands)
        {
            string rejection = string.Empty;
            float commandLoudness = Mathf.InverseLerp(
                QuietCommandDecibels,
                LoudCommandDecibels,
                outcome.CommandLoudnessDecibels);

            if (command == PieceVoiceCommand.Charge && _game != null)
            {
                _game.UpdateLocalVoiceChargePreview(
                    outcome.SpeechDurationSeconds,
                    commandLoudness,
                    finalPronunciationScore,
                    hasCurrentChargeAim,
                    currentChargeAimBoardPosition);
                finalChargeCost = _game.LocalVoiceChargePreviewCost;
                finalChargeDistance = _game.LocalVoiceChargePreviewDistance;
            }

            if (_game == null ||
                !_game.TryExecuteLocalVoiceCommand(
                    outcome.VoiceTargetPieceId,
                    outcome.VoiceTargetDistance,
                    outcome.CommandReachInSquares,
                    commandLoudness,
                    command,
                    hasCurrentChargeAim,
                    currentChargeAimBoardPosition,
                    outcome.SpeechDurationSeconds,
                    finalPronunciationScore,
                    out rejection))
            {
                _game?.ClearLocalVoiceChargePreview();
                _status = string.IsNullOrWhiteSpace(rejection)
                    ? "명령을 실행할 수 없습니다."
                    : rejection;
                RecordVoiceCommandAttempt(outcome, false, _status);
                return;
            }
        }

        _game?.ClearLocalVoiceChargePreview();
        bool usedTargetedCharge = outcome.Commands.Contains(PieceVoiceCommand.Charge);
        string chargeRestriction = economy == null
            ? string.Empty
            : economy.CooldownSystemEnabled
                ? $"쿨타임 {economy.CommandCooldownSeconds:0.##}초"
                : economy.CostSystemEnabled
                    ? $"코스트 {finalChargeCost:0.##}"
                    : "명령 제한 없음";
        _status = usedTargetedCharge
            ? $"“{outcome.Text}” → {commandName} · " +
              $"목표 {finalChargeDistance:F1}칸 / {chargeRestriction}"
            : $"“{outcome.Text}” → {commandName} ({outcome.Confidence:P0}) · " +
              $"음량 {outcome.CommandLoudnessDecibels:F1} dBFS / " +
              $"전달 {outcome.CommandReachInSquares:F1}칸";
        RecordVoiceCommandAttempt(outcome, true, string.Empty);
    }

    private static string BuildDebugCommandSummary(RecognitionOutcome outcome)
    {
        string transcript = string.IsNullOrWhiteSpace(outcome.Text)
            ? "(인식 텍스트 없음)"
            : $"“{outcome.Text.Trim()}”";

        if (!outcome.Accepted)
        {
            return $"{transcript} → 미인식 ({outcome.Error})";
        }

        string commandName = string.Join(
            " + ",
            outcome.Commands.Select(KoreanVoiceCommand.GetDisplayName));
        string target = outcome.HasVoiceTarget
            ? $"기물 #{outcome.VoiceTargetPieceId}"
            : "기물 미선택";
        return $"{target} · {transcript} → {commandName} ({outcome.Confidence:P0})";
    }

    private void RecordVoiceCommandAttempt(
        RecognitionOutcome outcome,
        bool successful,
        string failureReason)
    {
        if (!outcome.HasVoiceTarget)
        {
            return;
        }

        PlayerTeam team = NetworkPlayer.LocalPlayer?.Team ?? PlayerTeam.Unassigned;

        if (team != PlayerTeam.White && team != PlayerTeam.Black)
        {
            return;
        }

        string transcript = string.IsNullOrWhiteSpace(outcome.Text)
            ? "(인식 텍스트 없음)"
            : $"“{outcome.Text.Trim()}”";
        string commandName = outcome.Accepted
            ? string.Join(
                " + ",
                outcome.Commands.Select(KoreanVoiceCommand.GetDisplayName))
            : "미인식";
        string target = $"기물 #{outcome.VoiceTargetPieceId}";
        string result = successful
            ? "성공"
            : string.IsNullOrWhiteSpace(failureReason)
                ? "실패"
                : $"실패: {failureReason}";
        string entry =
            $"{DateTime.Now:HH:mm:ss} · {result} · {target} · " +
            $"{transcript} → {commandName}";
        List<string> history = team == PlayerTeam.White
            ? _whiteVoiceCommandHistory
            : _blackVoiceCommandHistory;
        history.Insert(0, entry);

        if (!successful)
        {
            return;
        }

        if (team == PlayerTeam.White)
        {
            _whiteSuccessfulVoiceCommandCount++;
        }
        else
        {
            _blackSuccessfulVoiceCommandCount++;
        }
    }

    private void RefreshMicrophoneDevices(bool restartCapture)
    {
        bool wasRunning = _microphoneRunning;
        string previousSelection = GetSelectedMicrophoneDevice();

        if (wasRunning)
        {
            StopMicrophoneCapture();
        }

        _microphoneDevices = Microphone.devices;
        string savedSelection = PlayerPrefs.GetString(
            SelectedMicrophonePreference,
            previousSelection);
        int savedIndex = Array.IndexOf(_microphoneDevices, savedSelection);
        _selectedMicrophoneIndex = _microphoneDevices.Length == 0
            ? 0
            : savedIndex >= 0
                ? savedIndex
                : Mathf.Clamp(_selectedMicrophoneIndex, 0, _microphoneDevices.Length - 1);

        if (_microphoneDevices.Length == 0)
        {
            _microphoneStatus = "사용 가능한 마이크를 찾지 못했습니다.";
        }

        if (wasRunning && restartCapture && _microphoneDevices.Length > 0)
        {
            StartCoroutine(StartMicrophoneCapture());
        }
    }

    private void RestartMicrophoneCapture()
    {
        StopMicrophoneCapture();
        StartCoroutine(StartMicrophoneCapture());
    }

    private IEnumerator StartMicrophoneCapture()
    {
        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            _microphoneStatus = "마이크 권한을 요청하는 중입니다.";
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
        }

        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            _microphoneStatus = "Windows 설정에서 데스크톱 앱의 마이크 접근을 허용하세요.";
            yield break;
        }

        string selectedDevice = GetSelectedMicrophoneDevice();

        if (string.IsNullOrWhiteSpace(selectedDevice))
        {
            _microphoneStatus = "선택 가능한 마이크가 없습니다.";
            yield break;
        }

        _activeMicrophoneDevice = selectedDevice;
        _microphoneClip = Microphone.Start(
            _activeMicrophoneDevice,
            true,
            10,
            MicrophoneSampleRate);

        if (_microphoneClip == null)
        {
            _microphoneStatus = "선택한 마이크를 열지 못했습니다.";
            yield break;
        }

        _lastMicrophonePosition = -1;
        _microphoneLevel = 0f;
        _microphoneDecibels = -80f;
        _peakMicrophoneDecibels = -80f;
        _preRollWriteIndex = 0;
        _preRollCount = 0;
        _preRollLoudnessFrames.Clear();
        _preRollLoudnessDuration = 0f;
        _microphoneRunning = true;
        _microphoneStatus = $"{_activeMicrophoneDevice} 입력을 감시 중입니다.";

        if (_automaticNoiseCalibration)
        {
            RecalibrateNoiseFloor();
        }
        else
        {
            UpdateIdleStatus();
        }
    }

    private void UpdateMicrophoneCapture()
    {
        if (!_microphoneRunning || _microphoneClip == null)
        {
            return;
        }

        int position = Microphone.GetPosition(_activeMicrophoneDevice);

        if (position < 0)
        {
            return;
        }

        if (_lastMicrophonePosition < 0)
        {
            _lastMicrophonePosition = position;
            return;
        }

        int clipFrames = _microphoneClip.samples;
        int availableFrames = position >= _lastMicrophonePosition
            ? position - _lastMicrophonePosition
            : clipFrames - _lastMicrophonePosition + position;

        if (availableFrames <= 0)
        {
            return;
        }

        int readPosition = _lastMicrophonePosition;
        int remainingFrames = Mathf.Min(availableFrames, clipFrames);

        while (remainingFrames > 0)
        {
            int chunkFrames = Mathf.Min(
                remainingFrames,
                Mathf.Min(clipFrames - readPosition, 1024));
            int framesAfterChunk = remainingFrames - chunkFrames;
            float capturedAtTime = Time.unscaledTime -
                (framesAfterChunk + chunkFrames * 0.5f) / MicrophoneSampleRate;
            ProcessMicrophoneChunk(readPosition, chunkFrames, capturedAtTime);
            ProcessAutomaticVoiceRequests();
            readPosition = (readPosition + chunkFrames) % clipFrames;
            remainingFrames -= chunkFrames;
        }

        _lastMicrophonePosition = position;
    }

    private void ProcessMicrophoneChunk(
        int offsetFrames,
        int frameCount,
        float capturedAtTime)
    {
        int channels = _microphoneClip.channels;
        int sampleCount = frameCount * channels;

        if (_microphoneSamples == null || _microphoneSamples.Length != sampleCount)
        {
            _microphoneSamples = new float[sampleCount];
        }

        if (!_microphoneClip.GetData(_microphoneSamples, offsetFrames))
        {
            return;
        }

        double sumOfSquares = 0d;

        foreach (float sample in _microphoneSamples)
        {
            sumOfSquares += sample * sample;
        }

        float rms = Mathf.Sqrt((float)(sumOfSquares / Math.Max(1, sampleCount)));
        _microphoneDecibels = 20f * Mathf.Log10(Mathf.Max(rms, 0.0001f));
        float chunkDuration = frameCount / (float)MicrophoneSampleRate;

        if (!_isCapturingSpeech)
        {
            QueuePreRoll(_microphoneSamples, frameCount, channels);
            QueuePreRollLoudnessFrame(_microphoneDecibels, chunkDuration);
        }

        UpdateNoiseFloor(_microphoneDecibels, chunkDuration);
        UpdateAutomaticVoiceDetection(
            _microphoneDecibels,
            chunkDuration,
            capturedAtTime);

        if (_isCapturingSpeech &&
            _currentRecognitionExecutesCommand &&
            IsVoicedCommandFrame(_microphoneDecibels))
        {
            CaptureUtteranceStartCommandTarget(capturedAtTime);
        }

        if (_isCapturingSpeech && _microphoneDecibels > -60f)
        {
            if (_speechLoudnessSamples.Count >= 512)
            {
                _speechLoudnessSamples.RemoveAt(0);
            }

            _speechLoudnessSamples.Add(_microphoneDecibels);
        }

        if (_isCapturingSpeech && IsVoicedCommandFrame(_microphoneDecibels))
        {
            _voicedLoudnessFrames.Add(new LoudnessFrame(
                _microphoneDecibels,
                chunkDuration));
        }

        _peakMicrophoneDecibels = Mathf.Max(_peakMicrophoneDecibels, _microphoneDecibels);
        float targetLevel = Mathf.InverseLerp(-55f, -8f, _microphoneDecibels);
        _microphoneLevel = Mathf.Lerp(_microphoneLevel, targetLevel, 0.35f);
        _microphoneStatus = _microphoneDecibels < -55f
            ? "입력 대기 중"
            : _isCapturingSpeech
                ? "입력을 Azure로 전송 중"
                : _recognitionInProgress
                    ? "음성 결과 분석 중"
                : "마이크 입력 정상";

        if (_isCapturingSpeech)
        {
            AppendToCaptureBuffer(_microphoneSamples, frameCount, channels);
        }
    }

    private void ProcessAutomaticVoiceRequests()
    {
        if (_automaticStopRequested)
        {
            _automaticStopRequested = false;
            FinishSpeechInput();
        }

        if (_automaticStartRequested)
        {
            _automaticStartRequested = false;

            if (!_isCapturingSpeech)
            {
                BeginRecognition(executeCommand: true, includePreRoll: true);

                if (_isCapturingSpeech)
                {
                    CaptureUtteranceStartCommandTarget(_automaticRequestedStartTime);
                }
            }
        }
    }

    private void UpdateAutomaticVoiceDetection(
        float decibels,
        float duration,
        float capturedAtTime)
    {
        bool automaticGameplayActive =
            _inputMode == VoiceInputMode.Automatic &&
            NetworkPlayer.MatchStarted &&
            !SessionManager.IsFrontEndVisible &&
            !InGameVoiceSettingsUI.IsBlockingGameplay &&
            HasSpeechCredentials;

        if (!automaticGameplayActive || IsNoiseCalibrating)
        {
            if (_currentRecognitionIsAutomatic &&
                _isCapturingSpeech)
            {
                _automaticStopRequested = true;
            }
            else if (!_currentRecognitionIsAutomatic)
            {
                ResetVoiceActivationState();
            }

            return;
        }

        if (_automaticStartRequested)
        {
            return;
        }

        if (_isCapturingSpeech)
        {
            if (!_currentRecognitionIsAutomatic)
            {
                return;
            }

            _automaticUtteranceDuration += duration;
            float releaseThreshold = VoiceActivationThresholdDecibels - 3f;

            if (decibels < releaseThreshold)
            {
                _voiceSilenceDuration += duration;
            }
            else
            {
                _voiceSilenceDuration = 0f;
            }

            bool targetSwitchBoundary =
                _automaticUtteranceDuration >= MinimumTargetSwitchUtteranceSeconds &&
                _voiceSilenceDuration >= TargetSwitchBoundarySilenceSeconds &&
                HasVoiceTargetChangedSinceUtteranceStart();

            if (_voiceSilenceDuration >= VoiceEndSilenceSeconds ||
                targetSwitchBoundary ||
                _automaticUtteranceDuration >= MaximumAutomaticUtteranceSeconds)
            {
                _automaticStopRequested = true;
            }

            return;
        }

        if (decibels >= VoiceActivationThresholdDecibels)
        {
            if (_voiceAboveThresholdDuration <= 0f)
            {
                _automaticCandidateStartTime = capturedAtTime;
            }

            _voiceAboveThresholdDuration += duration;
        }
        else
        {
            _voiceAboveThresholdDuration = Mathf.Max(
                0f,
                _voiceAboveThresholdDuration - duration * 2f);

            if (_voiceAboveThresholdDuration <= 0f)
            {
                _automaticCandidateStartTime = 0f;
            }
        }

        if (_voiceAboveThresholdDuration >= VoiceStartHoldSeconds)
        {
            _voiceAboveThresholdDuration = 0f;
            _automaticRequestedStartTime = _automaticCandidateStartTime > 0f
                ? _automaticCandidateStartTime
                : capturedAtTime;
            _automaticStartRequested = true;
        }
    }

    private void UpdateNoiseFloor(float decibels, float duration)
    {
        if (_noiseCalibrationRemaining > 0f)
        {
            _noiseCalibrationRemaining = Mathf.Max(
                0f,
                _noiseCalibrationRemaining - duration);
            _noiseCalibrationSamples.Add(decibels);

            if (_noiseCalibrationRemaining <= 0f)
            {
                CompleteNoiseCalibration();
            }

            return;
        }

        if (!_automaticNoiseCalibration || _isCapturingSpeech ||
            decibels >= VoiceActivationThresholdDecibels)
        {
            return;
        }

        float blend = 1f - Mathf.Exp(-0.6f * duration);
        _noiseFloorDecibels = Mathf.Lerp(_noiseFloorDecibels, decibels, blend);
    }

    private void CompleteNoiseCalibration()
    {
        if (_noiseCalibrationSamples.Count > 0)
        {
            _noiseCalibrationSamples.Sort();
            int index = Mathf.Clamp(
                Mathf.CeilToInt((_noiseCalibrationSamples.Count - 1) * 0.8f),
                0,
                _noiseCalibrationSamples.Count - 1);
            _noiseFloorDecibels = Mathf.Clamp(
                _noiseCalibrationSamples[index],
                -80f,
                -10f);
            PlayerPrefs.SetFloat(NoiseFloorPreference, _noiseFloorDecibels);
            PlayerPrefs.Save();
        }

        _noiseCalibrationSamples.Clear();
        UpdateIdleStatus();
    }

    private void QueuePreRoll(float[] samples, int frameCount, int channels)
    {
        for (int frame = 0; frame < frameCount; frame++)
        {
            float mono = 0f;
            int offset = frame * channels;

            for (int channel = 0; channel < channels; channel++)
            {
                mono += samples[offset + channel];
            }

            _preRollSamples[_preRollWriteIndex] = mono / channels;
            _preRollWriteIndex = (_preRollWriteIndex + 1) % _preRollSamples.Length;
            _preRollCount = Mathf.Min(_preRollCount + 1, _preRollSamples.Length);
        }
    }

    private void FlushPreRollToCaptureBuffer()
    {
        if (_preRollCount <= 0)
        {
            return;
        }

        float[] orderedSamples = new float[_preRollCount];
        int start = (_preRollWriteIndex - _preRollCount + _preRollSamples.Length) %
                    _preRollSamples.Length;

        for (int index = 0; index < _preRollCount; index++)
        {
            orderedSamples[index] = _preRollSamples[(start + index) % _preRollSamples.Length];
        }

        AppendToCaptureBuffer(orderedSamples, orderedSamples.Length, 1);
        _preRollCount = 0;
    }

    private void ResetVoiceActivationState()
    {
        _voiceAboveThresholdDuration = 0f;
        _voiceSilenceDuration = 0f;
        _automaticUtteranceDuration = 0f;
        _automaticCandidateStartTime = 0f;
        _automaticRequestedStartTime = 0f;
        _automaticStartRequested = false;
        _automaticStopRequested = false;
    }

    private void UpdateIdleStatus()
    {
        if (_recognitionInProgress || IsNoiseCalibrating)
        {
            return;
        }

        _status = _inputMode == VoiceInputMode.Automatic
            ? "자동 감지 중 · 명령을 말하세요."
            : "V 키를 누른 채 말하고, 다 말하면 키를 떼세요.";
    }

    private void CapturePendingCommandContext()
    {
        if (_pendingCommandContextCaptured)
        {
            return;
        }

        _pendingCommandContextCaptured = true;

        if (_utteranceStartTargetCaptured)
        {
            _hasPendingVoiceTarget = _hasUtteranceStartTarget;

            if (_hasUtteranceStartTarget)
            {
                _pendingVoiceTargetPieceId = _utteranceStartTargetPieceId;
                _pendingVoiceTargetDistance = _utteranceStartTargetDistance;
                _pendingHasChargeAim = _utteranceStartHasChargeAim;
                _pendingChargeAimBoardPosition =
                    _utteranceStartChargeAimBoardPosition;
            }

            return;
        }

        _hasPendingVoiceTarget = _game != null &&
            _game.TryGetLocalVoiceCommandSnapshot(
                out _pendingVoiceTargetPieceId,
                out _pendingVoiceTargetDistance,
                out _pendingHasChargeAim,
                out _pendingChargeAimBoardPosition);
    }

    private bool IsVoicedCommandFrame(float decibels)
    {
        return decibels >= VoiceActivationThresholdDecibels - 3f;
    }

    private bool HasVoiceTargetChangedSinceUtteranceStart()
    {
        if (!_utteranceStartTargetCaptured)
        {
            return false;
        }

        ushort currentPieceId = 0;
        bool hasCurrentTarget = _game != null &&
            _game.TryGetLocalVoiceTargetSnapshot(
                out currentPieceId,
                out _);

        return hasCurrentTarget != _hasUtteranceStartTarget ||
            (hasCurrentTarget &&
             currentPieceId != _utteranceStartTargetPieceId);
    }

    private void CaptureUtteranceStartCommandTarget(float capturedAtTime)
    {
        if (_utteranceStartTargetCaptured)
        {
            return;
        }

        _utteranceStartTargetCaptured = true;

        if (_game == null ||
            !_game.TryGetLocalVoiceCommandSnapshotAt(
                capturedAtTime,
                out ushort pieceId,
                out float distanceInSquares,
                out bool hasChargeAim,
                out Vector2 chargeAimBoardPosition))
        {
            return;
        }

        _hasUtteranceStartTarget = true;
        _utteranceStartTargetPieceId = pieceId;
        _utteranceStartTargetDistance = distanceInSquares;
        _utteranceStartHasChargeAim = hasChargeAim;
        _utteranceStartChargeAimBoardPosition = chargeAimBoardPosition;
        _game.ShowLocalVoiceCommandTarget(pieceId);
    }

    private void FinalizeCommandLoudness()
    {
        if (_speechLoudnessSamples.Count == 0)
        {
            _lastCommandLoudnessDecibels = -80f;
            _lastCommandReachInSquares = 0f;
            return;
        }

        _lastCommandLoudnessDecibels = GetCurrentCommandLoudnessDecibels();
        float loudness = Mathf.InverseLerp(
            QuietCommandDecibels,
            LoudCommandDecibels,
            _lastCommandLoudnessDecibels);
        _lastCommandReachInSquares = Mathf.Lerp(
            MinimumCommandReach,
            MaximumCommandReach,
            loudness * loudness);
    }

    private float GetCurrentCommandLoudnessDecibels()
    {
        if (_speechLoudnessSamples.Count == 0)
        {
            return -80f;
        }

        List<float> orderedSamples = new(_speechLoudnessSamples);
        orderedSamples.Sort();
        int percentileIndex = Mathf.Clamp(
            Mathf.CeilToInt((orderedSamples.Count - 1) * 0.8f),
            0,
            orderedSamples.Count - 1);
        return orderedSamples[percentileIndex];
    }

    private float GetVoicedSpeechDuration()
    {
        float duration = 0f;

        foreach (LoudnessFrame frame in _voicedLoudnessFrames)
        {
            duration += frame.Duration;
        }

        return duration;
    }

    private void CalculateSpeechLoudnessAverages(
        out float startAverageDecibels,
        out float endAverageDecibels,
        out float speechAverageDecibels)
    {
        if (_voicedLoudnessFrames.Count == 0)
        {
            startAverageDecibels = -80f;
            endAverageDecibels = -80f;
            speechAverageDecibels = -80f;
            return;
        }

        startAverageDecibels = CalculateAverageDecibels(
            fromEnd: false,
            maximumDuration: SpeechBoundaryAverageSeconds);
        endAverageDecibels = CalculateAverageDecibels(
            fromEnd: true,
            maximumDuration: SpeechBoundaryAverageSeconds);
        speechAverageDecibels = CalculateAverageDecibels(
            fromEnd: false,
            maximumDuration: float.PositiveInfinity);
    }

    private float CalculateAverageDecibels(bool fromEnd, float maximumDuration)
    {
        double weightedPower = 0d;
        float measuredDuration = 0f;
        int index = fromEnd ? _voicedLoudnessFrames.Count - 1 : 0;
        int step = fromEnd ? -1 : 1;

        while (index >= 0 && index < _voicedLoudnessFrames.Count &&
               measuredDuration < maximumDuration)
        {
            LoudnessFrame frame = _voicedLoudnessFrames[index];
            float includedDuration = Mathf.Min(
                frame.Duration,
                maximumDuration - measuredDuration);
            weightedPower += Math.Pow(10d, frame.Decibels / 10d) * includedDuration;
            measuredDuration += includedDuration;
            index += step;
        }

        if (measuredDuration <= 0f)
        {
            return -80f;
        }

        return Mathf.Clamp(
            10f * Mathf.Log10((float)(weightedPower / measuredDuration)),
            -80f,
            0f);
    }

    private void QueuePreRollLoudnessFrame(float decibels, float duration)
    {
        _preRollLoudnessFrames.Add(new LoudnessFrame(decibels, duration));
        _preRollLoudnessDuration += duration;

        while (_preRollLoudnessFrames.Count > 0 &&
               _preRollLoudnessDuration > PreRollSampleCount / (float)MicrophoneSampleRate)
        {
            float excessDuration = _preRollLoudnessDuration -
                PreRollSampleCount / (float)MicrophoneSampleRate;
            LoudnessFrame oldestFrame = _preRollLoudnessFrames[0];

            if (oldestFrame.Duration <= excessDuration)
            {
                _preRollLoudnessFrames.RemoveAt(0);
                _preRollLoudnessDuration -= oldestFrame.Duration;
                continue;
            }

            _preRollLoudnessFrames[0] = new LoudnessFrame(
                oldestFrame.Decibels,
                oldestFrame.Duration - excessDuration);
            _preRollLoudnessDuration -= excessDuration;
        }
    }

    private void AppendToCaptureBuffer(float[] samples, int frameCount, int channels)
    {
        int byteCount = frameCount * 2;

        if (_speechPcmBuffer == null || _speechPcmBuffer.Length < byteCount)
        {
            _speechPcmBuffer = new byte[byteCount];
        }

        for (int frame = 0; frame < frameCount; frame++)
        {
            float mono = 0f;
            int offset = frame * channels;

            for (int channel = 0; channel < channels; channel++)
            {
                mono += samples[offset + channel];
            }

            mono /= channels;
            short pcm = (short)Mathf.RoundToInt(
                Mathf.Clamp(mono, -1f, 1f) * short.MaxValue);
            int byteOffset = frame * 2;
            _speechPcmBuffer[byteOffset] = (byte)(pcm & 0xff);
            _speechPcmBuffer[byteOffset + 1] = (byte)((pcm >> 8) & 0xff);
        }

        for (int index = 0; index < byteCount; index++)
        {
            _capturedSpeechPcm.Add(_speechPcmBuffer[index]);
        }

        if (_activeLiveSession != null && !_activeLiveSessionFailed)
        {
            try
            {
                _activeLiveSession.Write(_speechPcmBuffer, byteCount);
            }
            catch (Exception)
            {
                _activeLiveSessionFailed = true;
            }
        }
    }

    private void StopMicrophoneCapture()
    {
        if (!string.IsNullOrWhiteSpace(_activeMicrophoneDevice) &&
            Microphone.IsRecording(_activeMicrophoneDevice))
        {
            Microphone.End(_activeMicrophoneDevice);
        }

        _microphoneRunning = false;
        _microphoneClip = null;
        _microphoneSamples = null;
        _activeMicrophoneDevice = null;
        _lastMicrophonePosition = -1;
        _microphoneLevel = 0f;
        _preRollLoudnessFrames.Clear();
        _preRollLoudnessDuration = 0f;
    }

    private string GetSelectedMicrophoneDevice()
    {
        return _microphoneDevices.Length == 0
            ? string.Empty
            : _microphoneDevices[
                Mathf.Clamp(_selectedMicrophoneIndex, 0, _microphoneDevices.Length - 1)];
    }

    private static void GetCredentials(out string key, out string region)
    {
        key = Environment.GetEnvironmentVariable(SpeechKeyVariable);
        region = Environment.GetEnvironmentVariable(SpeechRegionVariable);

#if UNITY_EDITOR
        if (string.IsNullOrWhiteSpace(key))
        {
            key = EditorPrefs.GetString(EditorSpeechKeyPreference, string.Empty);
        }

        if (string.IsNullOrWhiteSpace(region))
        {
            region = EditorPrefs.GetString(EditorSpeechRegionPreference, string.Empty);
        }
#elif UNITY_STANDALONE_WIN
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region))
        {
            LoadBuildCredentials();

            if (string.IsNullOrWhiteSpace(key))
            {
                key = _buildSpeechKey;
            }

            if (string.IsNullOrWhiteSpace(region))
            {
                region = _buildSpeechRegion;
            }
        }
#endif
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    private static void LoadBuildCredentials()
    {
        if (_buildCredentialsLoaded)
        {
            return;
        }

        _buildCredentialsLoaded = true;

        try
        {
            string credentialsPath = Path.Combine(
                Application.streamingAssetsPath,
                BuildCredentialsFileName);

            if (!File.Exists(credentialsPath))
            {
                // Debug.LogError(
                //     $"Azure Speech build credentials were not found: {credentialsPath}");
                return;
            }

            string json = File.ReadAllText(credentialsPath);
            BuildSpeechCredentials credentials =
                JsonUtility.FromJson<BuildSpeechCredentials>(json);

            if (credentials == null)
            {
                // Debug.LogError("Azure Speech build credentials JSON is invalid.");
                return;
            }

            _buildSpeechKey = credentials.key?.Trim() ?? string.Empty;
            _buildSpeechRegion = credentials.region?.Trim() ?? string.Empty;
        }
        catch (Exception)
        {
            // Debug.LogError(
            //     $"Failed to load Azure Speech build credentials: {exception.Message}");
        }
    }
#endif

    private VoiceRecognitionSettings ResolveVoiceSettings()
    {
        if (_game == null)
        {
            _game = FindFirstObjectByType<NetworkChessGame>();
        }

        return _game?.GameMode?.VoiceRecognition;
    }

    private string GetConfirmSelectionButtonName()
    {
        return (_game?.GetPlayerSettings()?.ConfirmSelectionButton ??
                PieceSelectionMouseButton.Left) switch
        {
            PieceSelectionMouseButton.Right => "우클릭",
            PieceSelectionMouseButton.Middle => "휠 클릭",
            _ => "좌클릭"
        };
    }

    private float MinimumConfidence =>
        ResolveVoiceSettings()?.MinimumConfidence ?? minimumConfidence;
    private float QuietCommandDecibels =>
        ResolveVoiceSettings()?.QuietCommandDecibels ?? quietCommandDecibels;
    private float LoudCommandDecibels =>
        ResolveVoiceSettings()?.LoudCommandDecibels ?? loudCommandDecibels;
    private float MinimumCommandReach =>
        ResolveVoiceSettings()?.MinimumCommandReach ?? minimumCommandReach;
    private float MaximumCommandReach =>
        ResolveVoiceSettings()?.MaximumCommandReach ?? maximumCommandReach;
    private float VoiceStartHoldSeconds =>
        ResolveVoiceSettings()?.VoiceStartHoldSeconds ?? voiceStartHoldSeconds;
    private float VoiceEndSilenceSeconds =>
        ResolveVoiceSettings()?.VoiceEndSilenceSeconds ?? voiceEndSilenceSeconds;
    private float TargetSwitchBoundarySilenceSeconds =>
        ResolveVoiceSettings()?.TargetSwitchBoundarySilenceSeconds ??
        targetSwitchBoundarySilenceSeconds;
    private float MinimumTargetSwitchUtteranceSeconds =>
        ResolveVoiceSettings()?.MinimumTargetSwitchUtteranceSeconds ??
        minimumTargetSwitchUtteranceSeconds;
    private float MaximumAutomaticUtteranceSeconds =>
        ResolveVoiceSettings()?.MaximumAutomaticUtteranceSeconds ??
        maximumAutomaticUtteranceSeconds;
    private float NoiseCalibrationSeconds =>
        ResolveVoiceSettings()?.NoiseCalibrationSeconds ?? noiseCalibrationSeconds;
    private float SpeechBoundaryAverageSeconds =>
        ResolveVoiceSettings()?.SpeechBoundaryAverageSeconds ??
        speechBoundaryAverageSeconds;

    private void OnValidate()
    {
        minimumConfidence = Mathf.Clamp01(minimumConfidence);
        quietCommandDecibels = Mathf.Clamp(quietCommandDecibels, -80f, 0f);
        loudCommandDecibels = Mathf.Clamp(
            loudCommandDecibels,
            quietCommandDecibels + 0.01f,
            0f);
        minimumCommandReach = Mathf.Max(0f, minimumCommandReach);
        maximumCommandReach = Mathf.Max(
            minimumCommandReach + 0.01f,
            maximumCommandReach);
        voiceStartHoldSeconds = Mathf.Max(0.01f, voiceStartHoldSeconds);
        voiceEndSilenceSeconds = Mathf.Max(0.01f, voiceEndSilenceSeconds);
        targetSwitchBoundarySilenceSeconds = Mathf.Max(
            0f,
            targetSwitchBoundarySilenceSeconds);
        minimumTargetSwitchUtteranceSeconds = Mathf.Max(
            0f,
            minimumTargetSwitchUtteranceSeconds);
        maximumAutomaticUtteranceSeconds = Mathf.Max(
            0.1f,
            maximumAutomaticUtteranceSeconds);
        noiseCalibrationSeconds = Mathf.Max(0.1f, noiseCalibrationSeconds);
        speechBoundaryAverageSeconds = Mathf.Max(0.01f, speechBoundaryAverageSeconds);
    }

    private void OnDestroy()
    {
        CloseCommandHistory();
        _isDestroyed = true;
        _game?.ClearLocalVoiceChargePreview();
        StopMicrophoneCapture();
        _capturedSpeechPcm.Clear();
        _activeLiveSession?.Dispose();
        _activeLiveSession = null;

        while (_utteranceQueue.TryDequeue(out _))
        {
        }
    }
}
