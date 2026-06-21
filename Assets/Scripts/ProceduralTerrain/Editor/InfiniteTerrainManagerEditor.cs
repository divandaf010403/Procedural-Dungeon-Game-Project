using UnityEngine;
using UnityEditor;

/// <summary>
/// Custom Inspector untuk InfiniteTerrainManager.
/// </summary>
[CustomEditor(typeof(InfiniteTerrainManager))]
public class InfiniteTerrainManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        InfiniteTerrainManager manager = (InfiniteTerrainManager)target;

        DrawDefaultInspector();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("─────────────────────────────────", EditorStyles.centeredGreyMiniLabel);

        // Info
        EditorGUILayout.HelpBox(
            $"Seed: {manager.seed}\n" +
            $"Chunk: {manager.chunkSize}×{manager.chunkSize}m\n" +
            $"View: {manager.viewDistance} chunks = {manager.viewDistance * 2 + 1}×{manager.viewDistance * 2 + 1} grid",
            MessageType.Info);

        EditorGUILayout.Space(4);

        // Generate with new seed
        GUI.backgroundColor = new Color(0.4f, 0.85f, 0.4f);
        if (GUILayout.Button("🎲  Generate New Random World", GUILayout.Height(36)))
        {
            Undo.RecordObject(manager, "Randomize Seed");
            manager.seed = Random.Range(0, 999999);
            EditorUtility.SetDirty(manager);
            manager.RegenerateAll();
        }

        // Regenerate same seed
        GUI.backgroundColor = new Color(0.7f, 0.85f, 1.0f);
        if (GUILayout.Button("🔄  Regenerate Same Seed", GUILayout.Height(28)))
        {
            manager.RegenerateAll();
        }

        // Clear all
        GUI.backgroundColor = new Color(1.0f, 0.75f, 0.75f);
        if (GUILayout.Button("🗑  Clear All Chunks", GUILayout.Height(24)))
        {
            manager.ClearAllChunks();
        }

        GUI.backgroundColor = Color.white;
    }
}
