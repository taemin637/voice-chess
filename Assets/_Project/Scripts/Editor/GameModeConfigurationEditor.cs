using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GameModeConfiguration))]
public sealed class GameModeConfigurationEditor : Editor
{
    [MenuItem("Voice Chess/게임 설정 대시보드 열기", priority = 1)]
    private static void OpenGameSettingsDashboard()
    {
        NetworkChessGame sceneGame =
            Object.FindFirstObjectByType<NetworkChessGame>();
        GameModeConfiguration configuration = sceneGame?.GameMode;

        if (configuration == null)
        {
            string[] guids = AssetDatabase.FindAssets("t:GameModeConfiguration");

            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                configuration = AssetDatabase.LoadAssetAtPath<GameModeConfiguration>(path);
            }
        }

        if (configuration == null)
        {
            EditorUtility.DisplayDialog(
                "게임 설정 대시보드",
                "GameModeConfiguration 에셋을 찾지 못했습니다.",
                "확인");
            return;
        }

        Selection.activeObject = configuration;
        EditorGUIUtility.PingObject(configuration);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.LabelField("VOICE CHESS 게임 설정 대시보드", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "게임 규칙의 스위치와 공통 조절값은 이 에셋에서 관리합니다. " +
            "씬 컴포넌트에는 Transform 같은 오브젝트 연결만 남아 있습니다.",
            MessageType.Info);
        EditorGUILayout.Space();

        DrawProperty("displayName", "설정 이름");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("경기 규칙 조합", EditorStyles.boldLabel);
        DrawProperty("clock", "시간 제한");
        DrawProperty("commands", "명령 방식 · 코스트 시스템");
        DrawProperty("victory", "게임 종료 조건");
        DrawProperty("captureMode", "점령전 규칙");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("기물과 보드", EditorStyles.boldLabel);
        DrawProperty("pieceArchetypes", "기물별 이동 · 무게 · 점수 · 스킬");
        DrawProperty("boardSetup", "초기 기물 배치");
        DrawProperty("boardPresentation", "보드 및 기물 표시");
        DrawProperty("collisions", "충돌 규칙");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("플레이어와 화면", EditorStyles.boldLabel);
        DrawProperty("players", "플레이어 킹 · 시작 위치 · 시점 · 입력 키");
        DrawProperty("voiceRecognition", "음성 인식");
        DrawProperty("interfaceAndSession", "UI · 세션");
        DrawProperty("presentation", "게임 종료 연출");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("에디터 테스트", EditorStyles.boldLabel);
        DrawProperty("editorSoloTest", "1인 자동 플레이 테스트");
        serializedObject.ApplyModifiedProperties();

        GameModeConfiguration configuration = (GameModeConfiguration)target;

        EditorGUILayout.Space();

        if (GUILayout.Button("표준 32기물 배치를 편집 가능하게 복사"))
        {
            Undo.RecordObject(configuration, "Create Editable Starting Position");
            configuration.MakeStandardPositionEditable();
            EditorUtility.SetDirty(configuration);
        }

        if (GUILayout.Button("기물별 설정을 기본값으로 초기화"))
        {
            Undo.RecordObject(configuration, "Reset Piece Archetypes");
            configuration.ResetPieceArchetypesToDefaults();
            EditorUtility.SetDirty(configuration);
        }
    }

    private void DrawProperty(string propertyName, string koreanLabel)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);

        if (property != null)
        {
            EditorGUILayout.PropertyField(
                property,
                new GUIContent(koreanLabel),
                includeChildren: true);
        }
    }
}

[CustomEditor(typeof(NetworkChessGame))]
public sealed class NetworkChessGameEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("gameMode"),
            new GUIContent("게임 설정 대시보드"));
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("pieceSpawner"),
            new GUIContent("체스 기물 스포너"));
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.HelpBox(
            "규칙 스위치와 공통 조절값은 연결된 대시보드 에셋에서 관리합니다. " +
            "이 컴포넌트의 구버전 값은 호환용 폴백으로만 남아 있습니다.",
            MessageType.Info);
    }
}
