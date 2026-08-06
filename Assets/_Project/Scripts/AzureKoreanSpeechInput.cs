using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
    private const float VoiceStartHoldSeconds = 0.1f;
    private const float VoiceEndSilenceSeconds = 0.35f;
    private const float MaximumAutomaticUtteranceSeconds = 3f;
    private const float NoiseCalibrationSeconds = 1.5f;

    private readonly ConcurrentQueue<RecognitionOutcome> _outcomes = new();
    private readonly object _speechStreamLock = new();
    private readonly List<float> _speechLoudnessSamples = new();
    private readonly List<float> _noiseCalibrationSamples = new();
    private readonly float[] _preRollSamples = new float[PreRollSampleCount];

    private SpeechRecognizer _recognizer;
    private AudioConfig _audioConfig;
    private AudioStreamFormat _speechStreamFormat;
    private PushAudioInputStream _speechPushStream;
    private AudioClip _microphoneClip;
    private float[] _microphoneSamples;
    private byte[] _speechPcmBuffer;
    private string[] _microphoneDevices = Array.Empty<string>();
    private string _activeMicrophoneDevice;
    private int _selectedMicrophoneIndex;
    private int _lastMicrophonePosition = -1;
    private int _preRollWriteIndex;
    private int _preRollCount;
    private NetworkChessGame _game;
    private bool _microphoneRunning;
    private bool _recognitionInProgress;
    private bool _isCapturingSpeech;
    private bool _speechStreamClosed;
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
    private bool _automaticStartRequested;
    private bool _automaticStopRequested;
    private bool _currentRecognitionIsAutomatic;

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

    private readonly struct RecognitionOutcome
    {
        public readonly bool Accepted;
        public readonly bool ExecuteCommand;
        public readonly PieceVoiceCommand Command;
        public readonly string Text;
        public readonly double Confidence;
        public readonly string Error;

        private RecognitionOutcome(
            bool accepted,
            bool executeCommand,
            PieceVoiceCommand command,
            string text,
            double confidence,
            string error)
        {
            Accepted = accepted;
            ExecuteCommand = executeCommand;
            Command = command;
            Text = text;
            Confidence = confidence;
            Error = error;
        }

        public static RecognitionOutcome Success(
            bool executeCommand,
            PieceVoiceCommand command,
            string text,
            double confidence)
        {
            return new RecognitionOutcome(
                true,
                executeCommand,
                command,
                text,
                confidence,
                null);
        }

        public static RecognitionOutcome Rejected(
            bool executeCommand,
            string text,
            double confidence,
            string error)
        {
            return new RecognitionOutcome(
                false,
                executeCommand,
                default,
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
            !InGameVoiceSettingsUI.IsOpen)
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
        if (!_recognitionInProgress || !_isCapturingSpeech)
        {
            return;
        }

        CapturePendingCommandContext();
        FinalizeCommandLoudness();
        _isCapturingSpeech = false;
        _status = "입력 종료 · 결과 분석 중...";

        lock (_speechStreamLock)
        {
            if (_speechPushStream == null || _speechStreamClosed)
            {
                return;
            }

            _speechPushStream.Close();
            _speechStreamClosed = true;
        }
    }

    private void ToggleRecognition(bool executeCommand)
    {
        if (_isCapturingSpeech)
        {
            FinishSpeechInput();
            return;
        }

        if (!_recognitionInProgress)
        {
            BeginRecognition(executeCommand, includePreRoll: false);
        }
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

    private async void BeginRecognition(
        bool executeCommand,
        bool includePreRoll)
    {
        if (_recognitionInProgress)
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

        _recognitionInProgress = true;
        _currentRecognitionIsAutomatic = includePreRoll;
        _speechLoudnessSamples.Clear();
        _hasPendingVoiceTarget = false;
        _pendingCommandContextCaptured = false;
        _pendingVoiceTargetPieceId = 0;
        _pendingVoiceTargetDistance = 0f;
        _lastCommandLoudnessDecibels = -80f;
        _lastCommandReachInSquares = 0f;

        if (includePreRoll && _microphoneDecibels > -60f)
        {
            _speechLoudnessSamples.Add(_microphoneDecibels);
        }
        _status = executeCommand
            ? "듣는 중... 명령을 말하세요."
            : "마이크 테스트 중... 문장을 말하세요.";

        try
        {
            CreateRecognizer();
            _speechStreamClosed = false;
            _isCapturingSpeech = true;

            if (includePreRoll)
            {
                FlushPreRollToSpeechStream();
            }

            SpeechRecognitionResult result = await _recognizer.RecognizeOnceAsync();

            if (executeCommand)
            {
                CapturePendingCommandContext();
                FinalizeCommandLoudness();
            }

            _outcomes.Enqueue(CreateOutcome(result, executeCommand));
        }
        catch (Exception exception)
        {
            _outcomes.Enqueue(RecognitionOutcome.Rejected(
                executeCommand,
                string.Empty,
                0d,
                $"음성 인식 오류: {exception.Message}"));
        }
        finally
        {
            _isCapturingSpeech = false;
            DisposeRecognizer();
        }
    }

    private void CreateRecognizer()
    {
        DisposeRecognizer();
        GetCredentials(out string subscriptionKey, out string region);

        if (string.IsNullOrWhiteSpace(subscriptionKey) ||
            string.IsNullOrWhiteSpace(region))
        {
            throw new InvalidOperationException(
                "Azure Speech Key/Region이 없습니다. 에디터의 " +
                "Voice Chess > Azure Speech Settings에서 저장하세요.");
        }

        SpeechConfig speechConfig = SpeechConfig.FromSubscription(subscriptionKey, region);
        speechConfig.SpeechRecognitionLanguage = "ko-KR";
        speechConfig.OutputFormat = OutputFormat.Detailed;
        speechConfig.SetProfanity(ProfanityOption.Raw);
        speechConfig.SetProperty(
            PropertyId.SpeechServiceConnection_InitialSilenceTimeoutMs,
            "5000");
        speechConfig.SetProperty(
            PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs,
            "10000");

        _speechStreamFormat = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
        _speechPushStream = AudioInputStream.CreatePushStream(_speechStreamFormat);
        _audioConfig = AudioConfig.FromStreamInput(_speechPushStream);
        _recognizer = new SpeechRecognizer(speechConfig, _audioConfig);

        PhraseListGrammar phraseList = PhraseListGrammar.FromRecognizer(_recognizer);

        foreach (string phrase in KoreanVoiceCommandParser.PhraseHints)
        {
            phraseList.AddPhrase(phrase);
        }

        phraseList.SetWeight(2.0);
    }

    private static RecognitionOutcome CreateOutcome(
        SpeechRecognitionResult result,
        bool executeCommand)
    {
        if (result.Reason == ResultReason.Canceled)
        {
            CancellationDetails cancellation = CancellationDetails.FromResult(result);
            return RecognitionOutcome.Rejected(
                executeCommand,
                result.Text,
                0d,
                $"음성 서비스 취소: {cancellation.Reason} {cancellation.ErrorDetails}");
        }

        if (result.Reason != ResultReason.RecognizedSpeech)
        {
            return RecognitionOutcome.Rejected(
                executeCommand,
                result.Text,
                0d,
                "말을 인식하지 못했습니다. 다시 말해 주세요.");
        }

        List<DetailedSpeechRecognitionResult> candidates = result.Best()
            .OrderByDescending(candidate => candidate.Confidence)
            .ToList();

        foreach (DetailedSpeechRecognitionResult candidate in candidates)
        {
            KoreanVoiceParseResult parse = KoreanVoiceCommandParser.Parse(candidate.Text);

            if (candidate.Confidence >= MinimumConfidence && parse.Accepted)
            {
                return RecognitionOutcome.Success(
                    executeCommand,
                    parse.Command,
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
            executeCommand,
            text,
            confidence,
            error);
    }

    private void HandleOutcome(RecognitionOutcome outcome)
    {
        _recognitionInProgress = false;
        _currentRecognitionIsAutomatic = false;
        ResetVoiceActivationState();
        _lastTranscript = outcome.Text;

        if (!outcome.Accepted)
        {
            _status = string.IsNullOrWhiteSpace(_lastTranscript)
                ? outcome.Error
                : $"“{_lastTranscript}” — {outcome.Error}";

            if (outcome.ExecuteCommand)
            {
                _game?.ShowLocalVoiceFailure(
                    _hasPendingVoiceTarget ? _pendingVoiceTargetPieceId : null);
            }

            return;
        }

        string commandName = KoreanVoiceCommand.GetDisplayName(outcome.Command);

        if (!outcome.ExecuteCommand)
        {
            _status =
                $"테스트: “{outcome.Text}” → {commandName} ({outcome.Confidence:P0})";
            return;
        }

        string rejection = string.Empty;

        if (!_hasPendingVoiceTarget)
        {
            _status = "명령이 끝날 때 화면에 보이는 아군 말이 없었습니다.";
            return;
        }

        if (_game == null ||
            !_game.TryExecuteLocalVoiceCommand(
                _pendingVoiceTargetPieceId,
                _pendingVoiceTargetDistance,
                _lastCommandReachInSquares,
                outcome.Command,
                out rejection))
        {
            _status = string.IsNullOrWhiteSpace(rejection)
                ? "명령을 실행할 수 없습니다."
                : rejection;
            return;
        }

        _status =
            $"“{outcome.Text}” → {commandName} ({outcome.Confidence:P0}) · " +
            $"음량 {_lastCommandLoudnessDecibels:F1} dBFS / " +
            $"전달 {_lastCommandReachInSquares:F1}칸";
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
            ProcessMicrophoneChunk(readPosition, chunkFrames);
            readPosition = (readPosition + chunkFrames) % clipFrames;
            remainingFrames -= chunkFrames;
        }

        _lastMicrophonePosition = position;
    }

    private void ProcessMicrophoneChunk(int offsetFrames, int frameCount)
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

        if (!_isCapturingSpeech && !_recognitionInProgress)
        {
            QueuePreRoll(_microphoneSamples, frameCount, channels);
        }

        UpdateNoiseFloor(_microphoneDecibels, chunkDuration);
        UpdateAutomaticVoiceDetection(_microphoneDecibels, chunkDuration);

        if (_isCapturingSpeech && _microphoneDecibels > -60f)
        {
            if (_speechLoudnessSamples.Count >= 512)
            {
                _speechLoudnessSamples.RemoveAt(0);
            }

            _speechLoudnessSamples.Add(_microphoneDecibels);
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

        WriteToSpeechStream(_microphoneSamples, frameCount, channels);
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

            if (!_recognitionInProgress)
            {
                BeginRecognition(executeCommand: true, includePreRoll: true);
            }
        }
    }

    private void UpdateAutomaticVoiceDetection(float decibels, float duration)
    {
        bool automaticGameplayActive =
            _inputMode == VoiceInputMode.Automatic &&
            NetworkPlayer.MatchStarted &&
            !SessionManager.IsFrontEndVisible &&
            !InGameVoiceSettingsUI.IsOpen &&
            HasSpeechCredentials;

        if (!automaticGameplayActive || IsNoiseCalibrating)
        {
            if (_currentRecognitionIsAutomatic &&
                _recognitionInProgress &&
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

        if (_recognitionInProgress)
        {
            if (!_currentRecognitionIsAutomatic || !_isCapturingSpeech)
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

            if (_voiceSilenceDuration >= VoiceEndSilenceSeconds ||
                _automaticUtteranceDuration >= MaximumAutomaticUtteranceSeconds)
            {
                _automaticStopRequested = true;
            }

            return;
        }

        if (decibels >= VoiceActivationThresholdDecibels)
        {
            _voiceAboveThresholdDuration += duration;
        }
        else
        {
            _voiceAboveThresholdDuration = Mathf.Max(
                0f,
                _voiceAboveThresholdDuration - duration * 2f);
        }

        if (_voiceAboveThresholdDuration >= VoiceStartHoldSeconds)
        {
            _voiceAboveThresholdDuration = 0f;
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

        if (!_automaticNoiseCalibration || _recognitionInProgress ||
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

    private void FlushPreRollToSpeechStream()
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

        WriteToSpeechStream(orderedSamples, orderedSamples.Length, 1);
        _preRollCount = 0;
    }

    private void ResetVoiceActivationState()
    {
        _voiceAboveThresholdDuration = 0f;
        _voiceSilenceDuration = 0f;
        _automaticUtteranceDuration = 0f;
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
        _hasPendingVoiceTarget = _game != null && _game.TryGetLocalVoiceTargetSnapshot(
            out _pendingVoiceTargetPieceId,
            out _pendingVoiceTargetDistance);
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

    private void WriteToSpeechStream(float[] samples, int frameCount, int channels)
    {
        lock (_speechStreamLock)
        {
            if (_speechPushStream == null || _speechStreamClosed)
            {
                return;
            }

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

            _speechPushStream.Write(_speechPcmBuffer, byteCount);
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

    private void DisposeRecognizer()
    {
        if (_recognizer != null)
        {
            _recognizer.Dispose();
            _recognizer = null;
        }

        _audioConfig?.Dispose();
        _audioConfig = null;

        lock (_speechStreamLock)
        {
            if (_speechPushStream != null)
            {
                if (!_speechStreamClosed)
                {
                    _speechPushStream.Close();
                }

                _speechPushStream.Dispose();
                _speechPushStream = null;
            }
        }

        _speechStreamFormat?.Dispose();
        _speechStreamFormat = null;
        _speechPcmBuffer = null;
        _speechStreamClosed = false;
    }

    private void OnDestroy()
    {
        StopMicrophoneCapture();
        DisposeRecognizer();
    }
}
