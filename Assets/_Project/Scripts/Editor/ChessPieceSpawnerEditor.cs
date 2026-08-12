using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ChessPieceSpawner))]
public sealed class ChessPieceSpawnerEditor : Editor
{
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
            "'보드 및 기물 표시'에서 조절합니다.",
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
