using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(ProceduralTerrainManager))]
public class ProceduralTerrainManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        ProceduralTerrainManager terrainManager = (ProceduralTerrainManager)target;

        if (DrawDefaultInspector())
        {
            // Optional: Auto-generate when values change
            // terrainManager.GenerateTerrain();
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Generate / Update Terrain"))
        {
            terrainManager.GenerateTerrain();
        }
    }
}
