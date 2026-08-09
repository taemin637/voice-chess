using UnityEditor;
using UnityEngine;

public sealed class AzureSpeechSettingsWindow : EditorWindow
{
    private const string KeyPreference = "VoiceChess.AzureSpeech.Key";
    private const string RegionPreference = "VoiceChess.AzureSpeech.Region";

    private string _subscriptionKey = string.Empty;
    private string _region = "koreacentral";

    [MenuItem("Voice Chess/Azure Speech Settings")]
    private static void Open()
    {
        GetWindow<AzureSpeechSettingsWindow>(
            utility: true,
            title: "Azure Speech",
            focus: true);
    }

    private void OnEnable()
    {
        _subscriptionKey = EditorPrefs.GetString(KeyPreference, string.Empty);
        _region = EditorPrefs.GetString(RegionPreference, "koreacentral");
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("한국어 음성 인식", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "값은 프로젝트나 씬에 저장되지 않고 이 컴퓨터의 Unity EditorPrefs에만 저장됩니다.",
            MessageType.Info);
        EditorGUILayout.HelpBox(
            "Windows 빌드 시 저장된 키와 리전이 빌드 폴더의 JSON에 포함됩니다. " +
            "배포받은 사용자가 키를 추출할 수 있다는 전제로 사용하세요.",
            MessageType.Warning);

        _subscriptionKey = EditorGUILayout.PasswordField(
            "Speech resource key",
            _subscriptionKey);
        _region = EditorGUILayout.TextField("Region", _region);

        EditorGUILayout.Space(10f);

        using (new EditorGUI.DisabledScope(
                   string.IsNullOrWhiteSpace(_subscriptionKey) ||
                   string.IsNullOrWhiteSpace(_region)))
        {
            if (GUILayout.Button("Save locally"))
            {
                EditorPrefs.SetString(KeyPreference, _subscriptionKey.Trim());
                EditorPrefs.SetString(RegionPreference, _region.Trim());
                ShowNotification(new GUIContent("Azure Speech settings saved locally."));
            }
        }

        if (GUILayout.Button("Clear local key"))
        {
            EditorPrefs.DeleteKey(KeyPreference);
            _subscriptionKey = string.Empty;
        }
    }
}
