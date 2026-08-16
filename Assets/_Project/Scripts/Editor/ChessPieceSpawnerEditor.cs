using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ChessPieceSpawner))]
public sealed class ChessPieceSpawnerEditor : Editor
{
    [MenuItem("Tools/Voice Chess/Regenerate Board Preview")]
    private static void RegenerateBoardPreview()
    {
        ChessPieceSpawner spawner =
            Object.FindFirstObjectByType<ChessPieceSpawner>();

        if (spawner == null)
        {
            Debug.LogWarning("Cannot generate the board preview: no ChessPieceSpawner exists in the open scene.");
            return;
        }

        spawner.GenerateInitialPosition();
        EditorUtility.SetDirty(spawner);
        Selection.activeObject = spawner;
        EditorGUIUtility.PingObject(spawner);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.LabelField("씬 연결", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("placementOrigin"),
            new GUIContent("보드 배치 기준 Transform"));
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("pieceParent"),
            new GUIContent("생성 기물 부모 Transform"));
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.HelpBox(
            "기물 프리팹, 보드 간격, 회전, 장외 연출, 선택 표시 값은 " +
            "NetworkChessGame에 연결된 게임 설정 대시보드의 " +
            "'보드 및 기물 표시'에서 조절합니다. 플레이어 킹 모드에서도 " +
            "에디터 미리보기에는 양 팀 킹이 표시되며, 여기에 지정한 킹 " +
            "프리팹이 실제 플레이어 외형으로 사용됩니다.",
            MessageType.Info);
        EditorGUILayout.Space();

        ChessPieceSpawner spawner = (ChessPieceSpawner)target;

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("초기 기물 배치 생성"))
            {
                spawner.GenerateInitialPosition();
                EditorUtility.SetDirty(spawner);
            }

            if (GUILayout.Button("생성된 기물 지우기"))
            {
                spawner.ClearGeneratedPieces();
                EditorUtility.SetDirty(spawner);
            }
        }
    }
}
