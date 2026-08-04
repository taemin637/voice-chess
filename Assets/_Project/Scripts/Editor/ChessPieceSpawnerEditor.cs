using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ChessPieceSpawner))]
public sealed class ChessPieceSpawnerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();

        ChessPieceSpawner spawner = (ChessPieceSpawner)target;

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate Initial Position"))
            {
                spawner.GenerateInitialPosition();
                EditorUtility.SetDirty(spawner);
            }

            if (GUILayout.Button("Clear Generated Pieces"))
            {
                spawner.ClearGeneratedPieces();
                EditorUtility.SetDirty(spawner);
            }
        }
    }
}
