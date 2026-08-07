using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    private const string EditorSpeechKeyPreference = "VoiceChess.AzureSpeech.Key";
    private const string EditorSpeechRegionPreference = "VoiceChess.AzureSpeech.Region";
    private const string SelectedMicrophonePreference = "VoiceChess.SelectedMicrophone";
    private const string InputModePreference = "VoiceChess.VoiceInputMode";
    private const string VoiceSensitivityPreference = "VoiceChess.VoiceSensitivity";
    private const string AutoNoiseCalibrationPreference = "VoiceChess.AutoNoiseCalibration";
    private const string NoiseFloorPreference = "VoiceChess.NoiseFloorDb";
    private const double MinimumConfidence = 0.55d;
    private const float QuietCommandDecibels = -45f;
    private const float LoudCommandDecibels = -12f;
    private const float MinimumCommandReach = 1f;
    private const float MaximumCommandReach = 12f;
    private const int MicrophoneSampleRate = 16000;
    private const int PreRollSampleCount = 3200;
    private const float VoiceStartHoldSeconds = 0.08f;
    private const float VoiceEndSilenceSeconds = 0.12f;
    private const float TargetSwitchBoundarySilenceSeconds = 0.04f;
    private const float MinimumTargetSwitchUtteranceSeconds = 0.3f;
    private const float MaximumAutomaticUtteranceSeconds = 3f;
    private const float NoiseCalibrationSeconds = 1.5f;
    private const float SpeechBoundaryAverageSeconds = 0.2f;

    private readonly ConcurrentQueue<RecognitionOutcome> _outcomes = new();
    private readonly ConcurrentQueue<CapturedUtterance> _utteranceQueue = new();
    private readonly List<float> _speechLoudnessSamples = new();
    private readonly List<LoudnessFrame> _voicedLoudnessFrames = new();
    private readonly List<LoudnessFrame> _preRollLoudnessFrames = new();
    private readonly List<float> _noiseCalibrationSamples = new();
    private readonly List<byte> _capturedSpeechPcm = new();
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
    private bool _utteranceStartTargetCaptured;
    private bool _hasUtteranceStartTarget;
    private ushort _utteranceStartTargetPieceId;
    private float _utteranceStartTargetDistance;
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
        public readonly float CommandLoudnessDecibels;
        public readonly float CommandReachInSquares;
        public readonly float SpeechStartAverageDecibels;
        public readonly float SpeechEndAverageDecibels;
        public readonly float SpeechAverageDecibels;

        public CapturedUtterance(
            bool executeCommand,
            byte[] pcmData,
            bool hasVoiceTarget,
            ushort voiceTargetPieceId,
            float voiceTargetDistance,
            float commandLoudnessDecibels,
            float commandReachInSquares,
            float speechStartAverageDecibels,
            float speechEndAverageDecibels,
            float speechAverageDecibels)
        {
            ExecuteCommand = executeCommand;
            PcmData = pcmData;
            HasVoiceTarget = hasVoiceTarget;
            VoiceTargetPieceId = voiceTargetPieceId;
            VoiceTargetDistance = voiceTargetDistance;
            CommandLoudnessDecibels = commandLoudnessDecibels;
            CommandReachInSquares = commandReachInSquares;
            SpeechStartAverageDecibels = speechStartAverageDecibels;
            SpeechEndAverageDecibels = speechEndAverageDecibels;
            SpeechAverageDecibels = speechAverageDecibels;
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
            SpeechRecognizer recognizer)
        {
            _speechConfig = speechConfig;
            _streamFormat = streamFormat;
            _pushStream = pushStream;
            _audioConfig = audioConfig;
            _recognizer = recognizer;
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
        public readonly float CommandLoudnessDecibels;
        public readonly float CommandReachInSquares;
        public readonly float SpeechStartAverageDecibels;
        public readonly float SpeechEndAverageDecibels;
        public readonly float SpeechAverageDecibels;

        private RecognitionOutcome(
            CapturedUtterance utterance,
            bool accepted,
            PieceVoiceCommand[] commands,
            string text,
            double confidence,
            string error)
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
            CommandLoudnessDecibels = utterance.CommandLoudnessDecibels;
            CommandReachInSquares = utterance.CommandReachInSquares;
            SpeechStartAverageDecibels = utterance.SpeechStartAverageDecibels;
            SpeechEndAverageDecibels = utterance.SpeechEndAverageDecibels;
            SpeechAverageDecibels = utterance.SpeechAverageDecibels;
        }

        public static RecognitionOutcome Success(
            CapturedUtterance utterance,
            PieceVoiceCommand[] commands,
            string text,
            double confidence)
        {
            return new RecognitionOutcome(
                utterance,
                true,
                commands,
                text,
                confidence,
                null);
        }

        public static RecognitionOutcome Rejected(
            CapturedUtterance utterance,
            string text,
            double confidence,
            string error)
        {
            return new RecognitionOutcome(
                utterance,
                false,
                Array.Empty<PieceVoiceCommand>(),
                text,
                confidence,
                error);
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
        _inputMode = (VoiceInputMode)Mathf.Clamp(
            PlayerPrefs.GetInt(InputModePreference, (int)VoiceInputMode.Automatic),
            (int)VoiceInputMode.Automatic,
            (int)VoiceInputMode.PushToTalk);
        _voiceSensitivity = Mathf.Clamp01(
            PlayerPrefs.GetFloat(VoiceSensitivityPreference, 0.55f));
        _automaticNoiseCalibration =
            PlayerPrefs.GetInt(AutoNoiseCalibrationPreference, 1) != 0;
        _noiseFloorDecibels = Mathf.Clamp(
            PlayerPrefs.GetFloat(NoiseFloorPreference, -55f),
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

        while (_outcomes.TryDequeue(out RecognitionOutcome outcome))
        {
            HandleOutcome(outcome);
        }

        Keyboard keyboard = Keyboard.current;

        if (_inputMode == VoiceInputMode.PushToTalk &&
            keyboard != null &&
            NetworkPlayer.MatchStarted &&
            !InGameVoiceSettingsUI.IsBlockingGameplay)
        {
            if (keyboard.vKey.wasPressedThisFrame)
            {
                BeginRecognition(executeCommand: true, includePreRoll: false);
            }

            if (keyboard.vKey.wasReleasedThisFrame)
            {
                FinishSpeechInput();
            }
        }
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
        _isCapturingSpeech = false;
        _status = "입력 종료 · 결과 분석 중...";

        CapturedUtterance utterance = new(
            _currentRecognitionExecutesCommand,
            _capturedSpeechPcm.ToArray(),
            _hasPendingVoiceTarget,
            _pendingVoiceTargetPieceId,
            _pendingVoiceTargetDistance,
            _lastCommandLoudnessDecibels,
            _lastCommandReachInSquares,
            speechStartAverageDecibels,
            speechEndAverageDecibels,
            speechAverageDecibels);
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
        _utteranceStartTargetCaptured = false;
        _hasUtteranceStartTarget = false;
        _utteranceStartTargetPieceId = 0;
        _utteranceStartTargetDistance = 0f;
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
                _activeLiveSession = CreateLiveRecognitionSession();
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

    private static LiveRecognitionSession CreateLiveRecognitionSession()
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

            foreach (string phrase in KoreanVoiceCommandParser.PhraseHints)
            {
                phraseList.AddPhrase(phrase);
            }

            phraseList.SetWeight(2.0);
            return new LiveRecognitionSession(
                speechConfig,
                streamFormat,
                pushStream,
                audioConfig,
                recognizer);
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

            foreach (string phrase in KoreanVoiceCommandParser.PhraseHints)
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

    private static RecognitionOutcome CreateOutcome(
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
                    candidate.Confidence);
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
            error);
    }

    private void HandleOutcome(RecognitionOutcome outcome)
    {
        _lastTranscript = outcome.Text;
        _lastCommandLoudnessDecibels = outcome.CommandLoudnessDecibels;
        _lastCommandReachInSquares = outcome.CommandReachInSquares;

        if (outcome.ExecuteCommand && !_isCapturingSpeech)
        {
            _game?.ShowLocalVoiceCommandTarget(
                outcome.HasVoiceTarget ? outcome.VoiceTargetPieceId : null,
                1f);
        }

        if (!outcome.Accepted)
        {
            _status = string.IsNullOrWhiteSpace(_lastTranscript)
                ? outcome.Error
                : $"“{_lastTranscript}” — {outcome.Error}";

            if (outcome.ExecuteCommand)
            {
                _game?.ShowLocalVoiceFailure(
                    outcome.HasVoiceTarget ? outcome.VoiceTargetPieceId : null);
            }

            return;
        }

        string commandName = string.Join(
            " + ",
            outcome.Commands.Select(KoreanVoiceCommand.GetDisplayName));

        Debug.Log(
            $"[음성 명령 dB] “{outcome.Text}” → {commandName} | " +
            $"시작 {SpeechBoundaryAverageSeconds:F2}초 평균 " +
            $"{outcome.SpeechStartAverageDecibels:F1} dBFS | " +
            $"끝 {SpeechBoundaryAverageSeconds:F2}초 평균 " +
            $"{outcome.SpeechEndAverageDecibels:F1} dBFS | " +
            $"전체 발화 평균 {outcome.SpeechAverageDecibels:F1} dBFS | " +
            $"기존 이동 기준(P80) {outcome.CommandLoudnessDecibels:F1} dBFS",
            this);

        if (!outcome.ExecuteCommand)
        {
            _status =
                $"테스트: “{outcome.Text}” → {commandName} ({outcome.Confidence:P0})";
            return;
        }

        if (!outcome.HasVoiceTarget)
        {
            _status = "명령을 말하기 시작할 때 바라본 아군 말이 없었습니다.";
            return;
        }

        foreach (PieceVoiceCommand command in outcome.Commands)
        {
            string rejection = string.Empty;
            float commandLoudness = Mathf.InverseLerp(
                QuietCommandDecibels,
                LoudCommandDecibels,
                outcome.CommandLoudnessDecibels);

            if (_game == null ||
                !_game.TryExecuteLocalVoiceCommand(
                    outcome.VoiceTargetPieceId,
                    outcome.VoiceTargetDistance,
                    outcome.CommandReachInSquares,
                    commandLoudness,
                    command,
                    out rejection))
            {
                _status = string.IsNullOrWhiteSpace(rejection)
                    ? "명령을 실행할 수 없습니다."
                    : rejection;
                return;
            }
        }

        _status =
            $"“{outcome.Text}” → {commandName} ({outcome.Confidence:P0}) · " +
            $"음량 {outcome.CommandLoudnessDecibels:F1} dBFS / " +
            $"전달 {outcome.CommandReachInSquares:F1}칸";
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
            }

            return;
        }

        _hasPendingVoiceTarget = _game != null &&
            _game.TryGetLocalVoiceTargetSnapshot(
                out _pendingVoiceTargetPieceId,
                out _pendingVoiceTargetDistance);
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
            !_game.TryGetLocalVoiceTargetSnapshotAt(
                capturedAtTime,
                out ushort pieceId,
                out float distanceInSquares))
        {
            return;
        }

        _hasUtteranceStartTarget = true;
        _utteranceStartTargetPieceId = pieceId;
        _utteranceStartTargetDistance = distanceInSquares;
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

        List<float> orderedSamples = new(_speechLoudnessSamples);
        orderedSamples.Sort();
        int percentileIndex = Mathf.Clamp(
            Mathf.CeilToInt((orderedSamples.Count - 1) * 0.8f),
            0,
            orderedSamples.Count - 1);
        _lastCommandLoudnessDecibels = orderedSamples[percentileIndex];
        float loudness = Mathf.InverseLerp(
            QuietCommandDecibels,
            LoudCommandDecibels,
            _lastCommandLoudnessDecibels);
        _lastCommandReachInSquares = Mathf.Lerp(
            MinimumCommandReach,
            MaximumCommandReach,
            loudness * loudness);
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
#endif
    }

    private void OnDestroy()
    {
        _isDestroyed = true;
        StopMicrophoneCapture();
        _capturedSpeechPcm.Clear();
        _activeLiveSession?.Dispose();
        _activeLiveSession = null;

        while (_utteranceQueue.TryDequeue(out _))
        {
        }
    }
}
