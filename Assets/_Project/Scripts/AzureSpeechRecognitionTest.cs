using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public sealed class AzureSpeechRecognitionTest : MonoBehaviour
{
    private const string SpeechKeyVariable = "AZURE_SPEECH_KEY";
    private const string SpeechRegionVariable = "AZURE_SPEECH_REGION";
    private const string EditorSpeechKeyPreference = "VoiceChess.AzureSpeech.Key";
    private const string EditorSpeechRegionPreference = "VoiceChess.AzureSpeech.Region";
    private const int MaximumHistoryCount = 12;

    [SerializeField, Range(0f, 1f)]
    private float minimumSpeechConfidence = 0.55f;

    private readonly ConcurrentQueue<UiMessage> _messages = new();
    private readonly List<string> _history = new();
    private readonly object _speechStreamLock = new();

    private SpeechRecognizer _recognizer;
    private AudioConfig _audioConfig;
    private AudioStreamFormat _speechStreamFormat;
    private PushAudioInputStream _speechPushStream;
    private AudioClip _microphoneMonitorClip;
    private float[] _microphoneSamples;
    private byte[] _speechPcmBuffer;
    private string[] _microphoneDevices = Array.Empty<string>();
    private string _activeMicrophoneDevice;
    private int _selectedMicrophoneIndex;
    private int _lastMicrophonePosition = -1;
    private bool _recognitionInProgress;
    private bool _microphoneMonitorRunning;
    private float _microphoneLevel;
    private float _microphoneDecibels = -80f;
    private float _maximumMicrophoneDecibels = -80f;
    private string _status = "아래 버튼을 누르고 한국어 명령을 말하세요.";
    private string _partialText = "-";
    private string _rawText = "-";
    private string _normalizedText = "-";
    private string _parseSummary = "-";
    private string _candidateSummary = "-";
    private string _credentialSummary = "확인 중...";
    private string _microphoneSummary = "확인 중...";
    private string _microphoneDeviceList = "확인 중...";
    private string _microphoneMonitorStatus = "레벨 테스트를 시작하지 않았습니다.";
    private Vector2 _scrollPosition;

    private sealed class RecognitionReport
    {
        public string RawText;
        public string CandidateSummary;
        public string SelectedText;
        public double SpeechConfidence;
        public KoreanVoiceParseResult ParseResult;
        public bool UsesPhraseHints;
    }

    private readonly struct UiMessage
    {
        public readonly string PartialText;
        public readonly RecognitionReport Report;
        public readonly string Error;

        private UiMessage(
            string partialText,
            RecognitionReport report,
            string error)
        {
            PartialText = partialText;
            Report = report;
            Error = error;
        }

        public static UiMessage Partial(string text)
        {
            return new UiMessage(text, null, null);
        }

        public static UiMessage Final(RecognitionReport report)
        {
            return new UiMessage(null, report, null);
        }

        public static UiMessage Failed(string error)
        {
            return new UiMessage(null, null, error);
        }
    }

    private void Awake()
    {
        RefreshEnvironmentSummary();
    }

    private IEnumerator Start()
    {
        // The meter is a permanent diagnostic for VoiceTestScene. Start it as
        // soon as the scene runs and keep it alive during Azure recognition.
        yield return StartMicrophoneMonitor();
    }

    private void Update()
    {
        while (_messages.TryDequeue(out UiMessage message))
        {
            if (message.PartialText != null)
            {
                _partialText = string.IsNullOrWhiteSpace(message.PartialText)
                    ? "-"
                    : message.PartialText;
            }
            else if (message.Report != null)
            {
                ApplyReport(message.Report);
            }
            else if (message.Error != null)
            {
                _recognitionInProgress = false;
                _status = message.Error;
                AddHistory($"오류 | {message.Error}");
            }
        }

        UpdateMicrophoneMonitor();

        Keyboard keyboard = Keyboard.current;

        if (!_recognitionInProgress &&
            keyboard != null &&
            keyboard.spaceKey.wasPressedThisFrame)
        {
            BeginRecognition(usePhraseHints: true);
        }
    }

    private void OnGUI()
    {
        GUIStyle titleStyle = new(GUI.skin.label)
        {
            fontSize = 24,
            fontStyle = FontStyle.Bold
        };
        GUIStyle sectionStyle = new(GUI.skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold
        };
        GUIStyle wrapStyle = new(GUI.skin.label)
        {
            fontSize = 14,
            wordWrap = true,
            richText = true
        };

        Rect panel = new(20f, 20f, Screen.width - 40f, Screen.height - 40f);
        GUILayout.BeginArea(panel, GUI.skin.box);
        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

        GUILayout.Label("Azure 한국어 음성 인식 테스트", titleStyle);
        GUILayout.Space(4f);
        GUILayout.Label($"<b>Azure 설정:</b> {_credentialSummary}", wrapStyle);
        GUILayout.Label($"<b>마이크:</b> {_microphoneSummary}", wrapStyle);
        GUILayout.Label($"<b>감지된 장치:</b> {_microphoneDeviceList}", wrapStyle);
        GUILayout.Label($"<b>상태:</b> {_status}", wrapStyle);

        GUILayout.Space(10f);
        GUILayout.Label("마이크 입력 진단", sectionStyle);
        DrawMicrophoneDeviceSelector(wrapStyle);
        GUILayout.Label(_microphoneMonitorStatus, wrapStyle);
        DrawMicrophoneMeter(wrapStyle);

        GUILayout.BeginHorizontal();
        bool previousEnabled = GUI.enabled;
        GUI.enabled = !_recognitionInProgress;

        if (GUILayout.Button(
                "마이크 진단 다시 시작",
                GUILayout.Height(38f)))
        {
            RestartMicrophoneMonitor();
        }

        if (GUILayout.Button("장치 목록 새로고침", GUILayout.Width(150f), GUILayout.Height(38f)))
        {
            RefreshMicrophoneDevices(restartMonitor: true);
        }

        GUI.enabled = previousEnabled;
        GUILayout.EndHorizontal();

        GUILayout.Space(10f);
        GUILayout.Label("Azure 음성 인식", sectionStyle);
        GUILayout.BeginHorizontal();
        previousEnabled = GUI.enabled;
        GUI.enabled = !_recognitionInProgress;

        if (GUILayout.Button("명령 인식 · 힌트 사용 (Space)", GUILayout.Height(38f)))
        {
            BeginRecognition(usePhraseHints: true);
        }

        if (GUILayout.Button("일반 받아쓰기 · 힌트 없음", GUILayout.Height(38f)))
        {
            BeginRecognition(usePhraseHints: false);
        }

        GUI.enabled = previousEnabled;

        if (GUILayout.Button("기록 지우기", GUILayout.Width(110f), GUILayout.Height(38f)))
        {
            _history.Clear();
        }

        GUILayout.EndHorizontal();

        GUILayout.Space(12f);
        GUILayout.Label("실시간 중간 인식", sectionStyle);
        GUILayout.Label(_partialText, wrapStyle);

        GUILayout.Space(8f);
        GUILayout.Label("Azure 최종 원문", sectionStyle);
        GUILayout.Label(_rawText, wrapStyle);

        GUILayout.Space(8f);
        GUILayout.Label("정규화 및 명령 파싱", sectionStyle);
        GUILayout.Label($"정규화: {_normalizedText}", wrapStyle);
        GUILayout.Label(_parseSummary, wrapStyle);

        GUILayout.Space(8f);
        GUILayout.Label("N-best 인식 후보", sectionStyle);
        GUILayout.Label(_candidateSummary, wrapStyle);

        GUILayout.Space(8f);
        GUILayout.Label("최근 테스트 기록", sectionStyle);

        if (_history.Count == 0)
        {
            GUILayout.Label("아직 기록이 없습니다.", wrapStyle);
        }
        else
        {
            foreach (string entry in _history)
            {
                GUILayout.Label(entry, wrapStyle);
            }
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private async void BeginRecognition(bool usePhraseHints)
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

        if (!_microphoneMonitorRunning || _microphoneMonitorClip == null)
        {
            _status = "선택한 마이크가 아직 준비되지 않았습니다. 잠시 후 다시 시도하세요.";
            RestartMicrophoneMonitor();
            return;
        }

        _recognitionInProgress = true;
        _partialText = "...";
        _status = usePhraseHints
            ? "듣는 중... 명령 문장 힌트를 사용합니다."
            : "듣는 중... 일반 한국어 받아쓰기 모드입니다.";

        try
        {
            EnsureRecognizer(usePhraseHints);
            SpeechRecognitionResult result = await _recognizer.RecognizeOnceAsync();
            _messages.Enqueue(UiMessage.Final(BuildReport(result, usePhraseHints)));
        }
        catch (Exception exception)
        {
            _messages.Enqueue(UiMessage.Failed($"음성 인식 오류: {exception.Message}"));
        }
        finally
        {
            DisposeRecognizer();
        }
    }

    private void EnsureRecognizer(bool usePhraseHints)
    {
        DisposeRecognizer();
        GetCredentials(out string subscriptionKey, out string region);

        if (string.IsNullOrWhiteSpace(subscriptionKey) ||
            string.IsNullOrWhiteSpace(region))
        {
            throw new InvalidOperationException(
                "Azure Speech Key/Region이 없습니다. " +
                "Voice Chess > Azure Speech Settings에서 Save locally를 먼저 누르세요.");
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
            "900");

        // Feed Azure from the same Unity microphone stream that drives the
        // on-screen meter. This guarantees that transcription and diagnostics
        // use the exact same selected device.
        _speechStreamFormat = AudioStreamFormat.GetWaveFormatPCM(
            samplesPerSecond: 16000,
            bitsPerSample: 16,
            channels: 1);
        _speechPushStream = AudioInputStream.CreatePushStream(_speechStreamFormat);
        _audioConfig = AudioConfig.FromStreamInput(_speechPushStream);
        _recognizer = new SpeechRecognizer(speechConfig, _audioConfig);
        _recognizer.Recognizing += HandleRecognizing;

        if (!usePhraseHints)
        {
            return;
        }

        PhraseListGrammar grammar = PhraseListGrammar.FromRecognizer(_recognizer);

        foreach (string phrase in KoreanVoiceCommandParser.PhraseHints)
        {
            grammar.AddPhrase(phrase);
        }

        grammar.SetWeight(2.0);
    }

    private void HandleRecognizing(object sender, SpeechRecognitionEventArgs eventArgs)
    {
        _messages.Enqueue(UiMessage.Partial(eventArgs.Result.Text));
    }

    private static RecognitionReport BuildReport(
        SpeechRecognitionResult result,
        bool usePhraseHints)
    {
        if (result.Reason == ResultReason.Canceled)
        {
            CancellationDetails cancellation = CancellationDetails.FromResult(result);
            throw new InvalidOperationException(
                $"Azure 요청 취소: {cancellation.Reason} / {cancellation.ErrorDetails}");
        }

        if (result.Reason != ResultReason.RecognizedSpeech)
        {
            throw new InvalidOperationException("말을 인식하지 못했습니다. 마이크와 입력 볼륨을 확인하세요.");
        }

        List<DetailedSpeechRecognitionResult> candidates = result.Best()
            .OrderByDescending(candidate => candidate.Confidence)
            .Take(5)
            .ToList();

        if (candidates.Count == 0)
        {
            KoreanVoiceParseResult fallbackParse = KoreanVoiceCommandParser.Parse(result.Text);
            return new RecognitionReport
            {
                RawText = result.Text,
                CandidateSummary = "상세 후보 없음",
                SelectedText = result.Text,
                SpeechConfidence = 0d,
                ParseResult = fallbackParse,
                UsesPhraseHints = usePhraseHints
            };
        }

        DetailedSpeechRecognitionResult selectedCandidate = candidates[0];
        KoreanVoiceParseResult selectedParse = KoreanVoiceCommandParser.Parse(selectedCandidate.Text);
        double selectedCombinedScore = GetCombinedScore(
            selectedCandidate.Confidence,
            selectedParse);
        List<string> candidateLines = new(candidates.Count);

        for (int index = 0; index < candidates.Count; index++)
        {
            DetailedSpeechRecognitionResult candidate = candidates[index];
            KoreanVoiceParseResult parse = KoreanVoiceCommandParser.Parse(candidate.Text);
            string commandText = parse.Accepted
                ? KoreanVoiceCommand.GetDisplayName(parse.Command)
                : "미확정";

            candidateLines.Add(
                $"{index + 1}. “{candidate.Text}” | 음성 {candidate.Confidence:P0} | " +
                $"파싱 {parse.Score:P0} | {commandText}");

            double combinedScore = GetCombinedScore(candidate.Confidence, parse);

            if (parse.Accepted && combinedScore > selectedCombinedScore)
            {
                selectedCandidate = candidate;
                selectedParse = parse;
                selectedCombinedScore = combinedScore;
            }
        }

        return new RecognitionReport
        {
            RawText = result.Text,
            CandidateSummary = string.Join("\n", candidateLines),
            SelectedText = selectedCandidate.Text,
            SpeechConfidence = selectedCandidate.Confidence,
            ParseResult = selectedParse,
            UsesPhraseHints = usePhraseHints
        };
    }

    private void ApplyReport(RecognitionReport report)
    {
        _recognitionInProgress = false;
        _partialText = "-";
        _rawText = string.IsNullOrWhiteSpace(report.RawText) ? "-" : report.RawText;
        _candidateSummary = report.CandidateSummary;
        _normalizedText = string.IsNullOrWhiteSpace(report.ParseResult.NormalizedText)
            ? "-"
            : report.ParseResult.NormalizedText;

        string mode = report.UsesPhraseHints ? "힌트 사용" : "일반 받아쓰기";
        string confidenceWarning = report.SpeechConfidence < minimumSpeechConfidence
            ? $" / 주의: 음성 신뢰도가 기준 {minimumSpeechConfidence:P0} 미만"
            : string.Empty;

        if (report.ParseResult.Accepted)
        {
            string commandName = KoreanVoiceCommand.GetDisplayName(report.ParseResult.Command);
            _parseSummary =
                $"<b>판정: {commandName}</b> | 파싱 {report.ParseResult.Score:P0} | " +
                $"음성 {report.SpeechConfidence:P0}\n" +
                $"선택 후보: “{report.SelectedText}”\n" +
                $"근거: {report.ParseResult.Reason} / 기준 문장: {report.ParseResult.MatchedPhrase}" +
                confidenceWarning;
            _status = $"인식 완료 ({mode}) — {commandName}";
            AddHistory(
                $"{DateTime.Now:HH:mm:ss} | {mode} | “{report.SelectedText}” → " +
                $"{commandName} | 음성 {report.SpeechConfidence:P0} / 파싱 {report.ParseResult.Score:P0}");
        }
        else
        {
            _parseSummary =
                $"<b>판정: 명령 미확정</b> | 파싱 {report.ParseResult.Score:P0} | " +
                $"음성 {report.SpeechConfidence:P0}\n" +
                $"선택 후보: “{report.SelectedText}”\n" +
                $"근거: {report.ParseResult.Reason} / 가장 가까운 문장: " +
                $"{report.ParseResult.MatchedPhrase}" + confidenceWarning;
            _status = $"인식 완료 ({mode}) — 명령을 확정하지 못했습니다.";
            AddHistory(
                $"{DateTime.Now:HH:mm:ss} | {mode} | “{report.SelectedText}” → 미확정 | " +
                $"음성 {report.SpeechConfidence:P0} / 파싱 {report.ParseResult.Score:P0}");
        }
    }

    private void RefreshEnvironmentSummary()
    {
        GetCredentials(out string key, out string region);
        _credentialSummary = string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(region)
            ? "저장된 Key/Region 없음"
            : $"저장됨 / Region: {region} (키 값은 표시하지 않음)";

        RefreshMicrophoneDevices(restartMonitor: false);
    }

    private void RefreshMicrophoneDevices(bool restartMonitor)
    {
        bool wasRunning = _microphoneMonitorRunning;
        string previousSelection = GetSelectedMicrophoneDevice();

        if (wasRunning)
        {
            StopMicrophoneMonitor();
        }

        _microphoneDevices = Microphone.devices;

        if (_microphoneDevices.Length == 0)
        {
            _selectedMicrophoneIndex = 0;
        }
        else
        {
            int previousIndex = Array.IndexOf(_microphoneDevices, previousSelection);
            _selectedMicrophoneIndex = previousIndex >= 0
                ? previousIndex
                : Mathf.Clamp(_selectedMicrophoneIndex, 0, _microphoneDevices.Length - 1);
        }

        _microphoneSummary = _microphoneDevices.Length == 0
            ? "Windows에서 사용할 수 있는 마이크를 찾지 못했습니다."
            : $"레벨 테스트와 Azure 모두 아래에서 선택한 동일 장치 사용 / " +
              $"{_microphoneDevices.Length}개 장치 감지";
        _microphoneDeviceList = _microphoneDevices.Length == 0
            ? "없음"
            : string.Join(
                " / ",
                _microphoneDevices.Select((device, index) => $"{index + 1}. {device}"));

        if (wasRunning && restartMonitor && _microphoneDevices.Length > 0)
        {
            StartCoroutine(StartMicrophoneMonitor());
        }
    }

    private void DrawMicrophoneDeviceSelector(GUIStyle wrapStyle)
    {
        if (_microphoneDevices.Length == 0)
        {
            GUILayout.Label("선택 가능한 마이크가 없습니다.", wrapStyle);
            return;
        }

        GUILayout.Label(
            "레벨 확인과 Azure 받아쓰기에 함께 사용할 장치를 선택하세요.",
            wrapStyle);

        int columnCount = Mathf.Clamp(_microphoneDevices.Length, 1, 2);
        int nextIndex = GUILayout.SelectionGrid(
            _selectedMicrophoneIndex,
            _microphoneDevices,
            columnCount,
            GUILayout.MinHeight(34f));

        if (nextIndex == _selectedMicrophoneIndex)
        {
            return;
        }

        bool restartMonitor = _microphoneMonitorRunning;
        StopMicrophoneMonitor();
        _selectedMicrophoneIndex = nextIndex;
        _microphoneMonitorStatus =
            $"진단 장치를 ‘{GetSelectedMicrophoneDevice()}’(으)로 변경했습니다.";

        if (restartMonitor)
        {
            StartCoroutine(StartMicrophoneMonitor());
        }
    }

    private void RestartMicrophoneMonitor()
    {
        StopMicrophoneMonitor();
        StartCoroutine(StartMicrophoneMonitor());
    }

    private IEnumerator StartMicrophoneMonitor()
    {
        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            _microphoneMonitorStatus = "마이크 권한을 요청하는 중...";
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
        }

        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            _microphoneMonitorStatus =
                "마이크 권한이 없습니다. Windows 개인정보 설정에서 Unity Editor를 허용하세요.";
            yield break;
        }

        string selectedDevice = GetSelectedMicrophoneDevice();

        if (string.IsNullOrWhiteSpace(selectedDevice))
        {
            _microphoneMonitorStatus = "연결된 마이크를 찾지 못했습니다.";
            yield break;
        }

        _activeMicrophoneDevice = selectedDevice;
        _microphoneMonitorClip = Microphone.Start(
            deviceName: _activeMicrophoneDevice,
            loop: true,
            lengthSec: 10,
            frequency: 16000);

        if (_microphoneMonitorClip == null)
        {
            _microphoneMonitorStatus =
                "기본 마이크 녹음을 시작하지 못했습니다. 다른 프로그램이 마이크를 독점 중인지 확인하세요.";
            yield break;
        }

        _microphoneSamples = new float[256 * _microphoneMonitorClip.channels];
        _lastMicrophonePosition = -1;
        _microphoneLevel = 0f;
        _microphoneDecibels = -80f;
        _maximumMicrophoneDecibels = -80f;
        _microphoneMonitorRunning = true;
        _microphoneMonitorStatus =
            $"‘{_activeMicrophoneDevice}’ 장치를 계속 감시 중입니다. 평소 목소리로 말해 보세요.";
    }

    private void UpdateMicrophoneMonitor()
    {
        if (!_microphoneMonitorRunning || _microphoneMonitorClip == null)
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

        int clipFrameCount = _microphoneMonitorClip.samples;
        int availableFrameCount = position >= _lastMicrophonePosition
            ? position - _lastMicrophonePosition
            : clipFrameCount - _lastMicrophonePosition + position;

        if (availableFrameCount <= 0)
        {
            return;
        }

        int readPosition = _lastMicrophonePosition;
        int remainingFrames = Mathf.Min(availableFrameCount, clipFrameCount);

        while (remainingFrames > 0)
        {
            int framesBeforeWrap = clipFrameCount - readPosition;
            int chunkFrameCount = Mathf.Min(remainingFrames, Mathf.Min(framesBeforeWrap, 1024));
            ProcessMicrophoneChunk(readPosition, chunkFrameCount);
            readPosition = (readPosition + chunkFrameCount) % clipFrameCount;
            remainingFrames -= chunkFrameCount;
        }

        _lastMicrophonePosition = position;
    }

    private void ProcessMicrophoneChunk(int offsetFrames, int frameCount)
    {
        int channelCount = _microphoneMonitorClip.channels;
        int requiredSampleCount = frameCount * channelCount;

        if (_microphoneSamples == null || _microphoneSamples.Length != requiredSampleCount)
        {
            _microphoneSamples = new float[requiredSampleCount];
        }

        if (!_microphoneMonitorClip.GetData(_microphoneSamples, offsetFrames))
        {
            return;
        }

        double sumOfSquares = 0d;

        foreach (float sample in _microphoneSamples)
        {
            sumOfSquares += sample * sample;
        }

        float rootMeanSquare = Mathf.Sqrt(
            (float)(sumOfSquares / Math.Max(1, _microphoneSamples.Length)));
        _microphoneDecibels = 20f * Mathf.Log10(Mathf.Max(rootMeanSquare, 0.0001f));
        _maximumMicrophoneDecibels = Mathf.Max(
            _maximumMicrophoneDecibels,
            _microphoneDecibels);

        // Voice levels commonly sit around -45 to -10 dBFS. Map that range to
        // a readable meter while keeping the dBFS value visible for diagnosis.
        float targetLevel = Mathf.InverseLerp(-55f, -8f, _microphoneDecibels);
        _microphoneLevel = Mathf.Lerp(_microphoneLevel, targetLevel, 0.35f);

        _microphoneMonitorStatus = _microphoneDecibels < -55f
            ? "신호가 거의 없습니다. 말했는데도 계속 이 상태라면 입력 장치나 권한을 확인하세요."
            : _recognitionInProgress
                ? "마이크 신호를 Azure로 전송하면서 인식 중입니다."
                : "마이크 신호가 정상적으로 들어오고 있습니다.";

        WriteChunkToSpeechStream(_microphoneSamples, frameCount, channelCount);
    }

    private void WriteChunkToSpeechStream(
        float[] interleavedSamples,
        int frameCount,
        int channelCount)
    {
        lock (_speechStreamLock)
        {
            if (_speechPushStream == null)
            {
                return;
            }

            int requiredByteCount = frameCount * 2;

            if (_speechPcmBuffer == null || _speechPcmBuffer.Length < requiredByteCount)
            {
                _speechPcmBuffer = new byte[requiredByteCount];
            }

            for (int frame = 0; frame < frameCount; frame++)
            {
                float monoSample = 0f;
                int sampleOffset = frame * channelCount;

                for (int channel = 0; channel < channelCount; channel++)
                {
                    monoSample += interleavedSamples[sampleOffset + channel];
                }

                monoSample /= channelCount;
                short pcmSample = (short)Mathf.RoundToInt(
                    Mathf.Clamp(monoSample, -1f, 1f) * short.MaxValue);
                int byteOffset = frame * 2;
                _speechPcmBuffer[byteOffset] = (byte)(pcmSample & 0xff);
                _speechPcmBuffer[byteOffset + 1] = (byte)((pcmSample >> 8) & 0xff);
            }

            _speechPushStream.Write(_speechPcmBuffer, requiredByteCount);
        }
    }

    private void DrawMicrophoneMeter(GUIStyle wrapStyle)
    {
        Rect meter = GUILayoutUtility.GetRect(10f, 24f, GUILayout.ExpandWidth(true));
        GUI.Box(meter, GUIContent.none);

        Rect fill = meter;
        fill.width *= Mathf.Clamp01(_microphoneLevel);
        Color previousColor = GUI.color;
        GUI.color = _microphoneDecibels > -12f
            ? new Color(1f, 0.35f, 0.25f)
            : new Color(0.25f, 0.85f, 0.35f);
        GUI.DrawTexture(fill, Texture2D.whiteTexture);
        GUI.color = previousColor;

        string current = _microphoneMonitorRunning
            ? $"현재 {_microphoneDecibels:F1} dBFS / 최고 {_maximumMicrophoneDecibels:F1} dBFS"
            : "중지됨";
        GUILayout.Label(current, wrapStyle);
    }

    private void StopMicrophoneMonitor()
    {
        if (!string.IsNullOrWhiteSpace(_activeMicrophoneDevice) &&
            Microphone.IsRecording(_activeMicrophoneDevice))
        {
            Microphone.End(_activeMicrophoneDevice);
        }

        _microphoneMonitorRunning = false;
        _microphoneMonitorClip = null;
        _microphoneSamples = null;
        _lastMicrophonePosition = -1;
        _activeMicrophoneDevice = null;
        _microphoneLevel = 0f;
        _microphoneMonitorStatus = "마이크 레벨 테스트가 중지되었습니다.";
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

    private static double GetCombinedScore(
        double speechConfidence,
        KoreanVoiceParseResult parseResult)
    {
        if (!parseResult.Accepted)
        {
            return speechConfidence * 0.25d;
        }

        return speechConfidence * 0.55d + parseResult.Score * 0.45d;
    }

    private void AddHistory(string entry)
    {
        _history.Insert(0, entry);

        if (_history.Count > MaximumHistoryCount)
        {
            _history.RemoveAt(_history.Count - 1);
        }
    }

    private void DisposeRecognizer()
    {
        if (_recognizer != null)
        {
            _recognizer.Recognizing -= HandleRecognizing;
            _recognizer.Dispose();
            _recognizer = null;
        }

        _audioConfig?.Dispose();
        _audioConfig = null;

        lock (_speechStreamLock)
        {
            if (_speechPushStream != null)
            {
                _speechPushStream.Close();
                _speechPushStream.Dispose();
                _speechPushStream = null;
            }
        }

        _speechStreamFormat?.Dispose();
        _speechStreamFormat = null;
        _speechPcmBuffer = null;
    }

    private void OnDestroy()
    {
        StopMicrophoneMonitor();
        DisposeRecognizer();
    }
}
