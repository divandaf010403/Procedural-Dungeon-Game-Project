using UnityEngine;
using UnityEditor;

/// <summary>
/// Custom Inspector untuk TerrainPipeline — menampilkan tombol Generate
/// dengan randomize seed otomatis.
/// </summary>
[CustomEditor(typeof(TerrainPipeline))]
public class TerrainPipelineEditor : Editor
{
    public override void OnInspectorGUI()
    {
        TerrainPipeline pipeline = (TerrainPipeline)target;

        DrawDefaultInspector();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("─────────────────────────────────", EditorStyles.centeredGreyMiniLabel);

        // Current seed info
        EditorGUILayout.HelpBox($"Current Seed: {pipeline.seed}", MessageType.Info);

        EditorGUILayout.Space(4);

        // Main button: randomize seed + generate
        GUI.backgroundColor = new Color(0.4f, 0.85f, 0.4f);
        if (GUILayout.Button("🎲  Generate New Random World", GUILayout.Height(36)))
        {
            Undo.RecordObject(pipeline, "Randomize Seed & Generate");
            pipeline.seed = Random.Range(0, 999999);
            EditorUtility.SetDirty(pipeline);
            pipeline.GenerateTerrain();
        }

        // Secondary button: regenerate same seed
        GUI.backgroundColor = new Color(0.7f, 0.85f, 1.0f);
        if (GUILayout.Button("🔄  Regenerate Same Seed", GUILayout.Height(28)))
        {
            pipeline.GenerateTerrain();
        }

        // Reset terrain button
        GUI.backgroundColor = new Color(1.0f, 0.75f, 0.75f);
        if (GUILayout.Button("🗑  Reset Terrain (Flat)", GUILayout.Height(24)))
        {
            Terrain terrain = pipeline.GetComponent<Terrain>();
            if (terrain != null)
            {
                int res = terrain.terrainData.heightmapResolution;
                terrain.terrainData.SetHeights(0, 0, new float[res, res]);
            }
        }

        GUI.backgroundColor = Color.white;
    }
}
