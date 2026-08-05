using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CityGenerator))]
public class CityGeneratorEditor : Editor
{
    private bool showLSystemHelp = false;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        CityGenerator gen = (CityGenerator)target;
        RoadNetwork   rn  = gen.GetComponent<RoadNetwork>();

        // ---------------------------------------------------------------
        // L-System Quick Presets
        // Hanya tampil jika mode menggunakan L-System
        // ---------------------------------------------------------------
        if (rn != null && rn.generationMode != RoadNetwork.RoadGenerationMode.OrthogonalGrid)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("L-System Presets", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Organic\nCity"))
            {
                rn.lSystemPreset         = LSystemPreset.OrganicCity;
                rn.lSystemIterations     = 4;
                rn.lSystemChanceToIgnore = 0.3f;
                rn.lSystemStepSize       = 0f; // auto dari blockSpacing
                rn.lSystemOriginCount    = 4;
                EditorUtility.SetDirty(rn);
            }
            if (GUILayout.Button("Manhattan\nGrid"))
            {
                rn.lSystemPreset         = LSystemPreset.ManhattanGrid;
                rn.lSystemIterations     = 3;
                rn.lSystemChanceToIgnore = 0.1f;
                rn.lSystemStepSize       = 0f;
                rn.lSystemOriginCount    = 6;
                EditorUtility.SetDirty(rn);
            }
            if (GUILayout.Button("Highway\n+Alley"))
            {
                rn.lSystemPreset         = LSystemPreset.HighwayAndAlley;
                rn.lSystemIterations     = 4;
                rn.lSystemChanceToIgnore = 0.2f;
                rn.lSystemStepSize       = 0f;
                rn.lSystemOriginCount    = 3;
                EditorUtility.SetDirty(rn);
            }
            if (GUILayout.Button("Radial\nSprawl"))
            {
                rn.lSystemPreset         = LSystemPreset.RadialSprawl;
                rn.lSystemIterations     = 3;
                rn.lSystemChanceToIgnore = 0.25f;
                rn.lSystemStepSize       = 0f;
                rn.lSystemOriginCount    = 1; // dari pusat saja
                EditorUtility.SetDirty(rn);
            }
            EditorGUILayout.EndHorizontal();

            // Mode-specific info box
            if (rn.generationMode == RoadNetwork.RoadGenerationMode.RingAndLSystem)
            {
                EditorGUILayout.HelpBox(
                    "RingAndLSystem: Jalan kotak mengelilingi kota sebagai batas luar.\n" +
                    "L-System tumbuh di interior — organik, tidak ada grid di dalam.\n" +
                    "FixRoad() sekali di akhir → junction ring dan L-System menyatu.",
                    MessageType.Info);
            }

            // Symbol reference foldout
            showLSystemHelp = EditorGUILayout.Foldout(showLSystemHelp, "L-System Symbol Reference");
            if (showLSystemHelp)
            {
                EditorGUILayout.LabelField(
                    "F = maju + place road\n" +
                    "f = maju tanpa road\n" +
                    "+ = belok kanan 90°\n" +
                    "- = belok kiri 90°\n" +
                    "| = balik 180°\n" +
                    "[ = push state (cabang)\n" +
                    "] = pop state\n" +
                    "X = growth marker (expansion only)",
                    EditorStyles.helpBox);
            }
        }

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
        // Mode description
        // ---------------------------------------------------------------
        if (rn != null)
        {
            EditorGUILayout.Space(4);
            string modeDesc = rn.generationMode switch
            {
                RoadNetwork.RoadGenerationMode.OrthogonalGrid =>
                    "OrthogonalGrid: Grid H×V klasik. Cepat, rapi, cocok untuk kota modern.",
                RoadNetwork.RoadGenerationMode.LSystem =>
                    "LSystem: Jalan organik dari L-System turtle. Cocok untuk kota medieval/suburban.",
                RoadNetwork.RoadGenerationMode.RingAndLSystem =>
                    "RingAndLSystem: Ring road mengelilingi kota + L-System organik di interior.",
                _ => ""
            };
            if (!string.IsNullOrEmpty(modeDesc))
                EditorGUILayout.HelpBox(modeDesc, MessageType.None);
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
