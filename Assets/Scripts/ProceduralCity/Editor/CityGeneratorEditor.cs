using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CityGenerator))]
public class CityGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        CityGenerator gen = (CityGenerator)target;
        RoadNetwork   rn  = gen.GetComponent<RoadNetwork>();

        // ---------------------------------------------------------------
        // Seed controls
        // ---------------------------------------------------------------
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Seed", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Random Seed"))
        {
            gen.randomSeed = Random.Range(0, int.MaxValue);
            EditorUtility.SetDirty(gen);
        }
        EditorGUILayout.LabelField($"Current: {gen.randomSeed}", GUILayout.Width(160));
        EditorGUILayout.EndHorizontal();

        // ---------------------------------------------------------------
        // City size presets
        // ---------------------------------------------------------------
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("City Size Presets", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Small\n1000"))  { gen.citySize = 1000f; gen.blockSize = 35f; EditorUtility.SetDirty(gen); }
        if (GUILayout.Button("Medium\n1500")) { gen.citySize = 1500f; gen.blockSize = 40f; EditorUtility.SetDirty(gen); }
        if (GUILayout.Button("Large\n2000"))  { gen.citySize = 2000f; gen.blockSize = 45f; EditorUtility.SetDirty(gen); }
        if (GUILayout.Button("Huge\n3000"))   { gen.citySize = 3000f; gen.blockSize = 50f; EditorUtility.SetDirty(gen); }
        EditorGUILayout.EndHorizontal();

        // ---------------------------------------------------------------
        // Pipeline description
        // ---------------------------------------------------------------
        if (rn != null)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "RingAndLSystem: Ring road mengelilingi kota + L-System organik di interior.",
                MessageType.None);
        }

        // ---------------------------------------------------------------
        // Actions
        // ---------------------------------------------------------------
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Generate City", GUILayout.Height(32)))
            gen.GenerateCity();
        if (GUILayout.Button("Clear City", GUILayout.Height(32)))
            gen.ClearCity();
        EditorGUILayout.EndHorizontal();
        if (GUILayout.Button("Frame Camera", GUILayout.Height(24)))
            gen.FrameCamera();
    }
}
