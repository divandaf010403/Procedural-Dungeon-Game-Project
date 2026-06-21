using UnityEngine;
using UnityEditor;

/// <summary>
/// Custom Inspector untuk CityGenerator.
/// Menambahkan tombol besar yang mudah diakses di Inspector.
/// File HARUS ada di folder bernama "Editor" agar Unity compile sebagai Editor script.
/// </summary>
[CustomEditor(typeof(CityGenerator))]
public class CityGeneratorEditor : Editor
{
    private GUIStyle bigButton;
    private GUIStyle sectionTitle;
    private GUIStyle smallButton;
    private bool inited = false;

    private void InitStyles()
    {
        if (inited) return;

        bigButton = new GUIStyle(GUI.skin.button)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(10, 10, 15, 15),
            margin = new RectOffset(5, 5, 8, 8)
        };

        smallButton = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            padding = new RectOffset(8, 8, 8, 8),
            margin = new RectOffset(3, 3, 3, 3)
        };

        sectionTitle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            margin = new RectOffset(5, 5, 10, 5)
        };

        inited = true;
    }

    /// <summary>
    /// Safe generate: clear dulu, lalu generate. Mencegah duplikat object.
    /// </summary>
    private void SafeGenerate(CityGenerator cityGen)
    {
        cityGen.ClearCity();
        cityGen.GenerateCity();
        EditorUtility.SetDirty(cityGen);
        SceneView.RepaintAll();
    }

    public override void OnInspectorGUI()
    {
        InitStyles();

        // Tampilkan semua field default (citySize, blockSize, dll)
        DrawDefaultInspector();

        CityGenerator cityGen = (CityGenerator)target;

        EditorGUILayout.Space(15);

        // Section: Actions
        EditorGUILayout.LabelField("═══ TOMBOL AKSI ═══", sectionTitle);
        EditorGUILayout.Space(5);

        // Tombol GENERATE (HIJAU)
        GUI.backgroundColor = new Color(0.4f, 0.85f, 0.4f);
        if (GUILayout.Button("▶  GENERATE CITY (Buat Kota)", bigButton, GUILayout.Height(50)))
        {
            Undo.RecordObject(cityGen, "Generate City");

            // FIX: Kalau autoRandomSeed true, randomize seed SEBELUM generate
            // Supaya setiap klik = kota BERBEDA
            if (cityGen.autoRandomSeed)
            {
                cityGen.randomSeed = Random.Range(int.MinValue, int.MaxValue);
                Debug.Log($"[CityGenerator] 🎲 Auto-randomized seed: {cityGen.randomSeed}");
            }

            // Safe generate: clear dulu, lalu generate (cegah duplikat kalau di-spam)
            cityGen.ClearCity();
            cityGen.GenerateCity();
            EditorUtility.SetDirty(cityGen);
            SceneView.RepaintAll();
            Debug.Log($"[CityGenerator] Kota berhasil di-generate dengan seed {cityGen.randomSeed}!");
        }

        EditorGUILayout.Space(3);

        // Tombol RANDOMIZE SEED (KECIL)
        GUI.backgroundColor = new Color(0.85f, 0.75f, 1f);
        if (GUILayout.Button("🎲  RANDOMIZE SEED SAJA (Tanpa Generate)", smallButton, GUILayout.Height(28)))
        {
            Undo.RecordObject(cityGen, "Randomize Seed");
            cityGen.randomSeed = Random.Range(int.MinValue, int.MaxValue);
            EditorUtility.SetDirty(cityGen);
            Debug.Log($"[CityGenerator] 🎲 Seed di-randomize: {cityGen.randomSeed}");
        }

        EditorGUILayout.Space(3);

        // Tombol CLEAR (MERAH)
        GUI.backgroundColor = new Color(0.95f, 0.5f, 0.5f);
        if (GUILayout.Button("🗑  CLEAR CITY (Hapus Semua)", bigButton, GUILayout.Height(45)))
        {
            if (EditorUtility.DisplayDialog("Hapus Kota?",
                "Yakin ingin menghapus semua objek kota?",
                "Ya, Hapus", "Batal"))
            {
                Undo.RecordObject(cityGen, "Clear City");
                cityGen.ClearCity();
                EditorUtility.SetDirty(cityGen);
                SceneView.RepaintAll();
                Debug.Log("[CityGenerator] Kota berhasil dihapus!");
            }
        }

        EditorGUILayout.Space(3);

        // Tombol FRAME CAMERA (BIRU)
        GUI.backgroundColor = new Color(0.5f, 0.7f, 1f);
        if (GUILayout.Button("📷  FRAME CAMERA (Lihat Kota)", bigButton, GUILayout.Height(45)))
        {
            cityGen.FrameCamera();
            SceneView.RepaintAll();
            Debug.Log("[CityGenerator] Camera di-frame untuk lihat kota!");
        }

        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(15);

        // Section: Info
        EditorGUILayout.LabelField("═══ INFO ═══", sectionTitle);
        EditorGUILayout.Space(5);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField($"Seed: {cityGen.randomSeed}");
        EditorGUILayout.LabelField($"City Size: {cityGen.citySize:F0}");
        EditorGUILayout.LabelField($"Block Size: {cityGen.blockSize:F0}");
        EditorGUILayout.LabelField($"Object Count: {cityGen.GetObjectCount()}");
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(15);

        // Section: Quick Presets
        EditorGUILayout.LabelField("═══ PRESET UKURAN ═══", sectionTitle);
        EditorGUILayout.Space(5);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Kecil\n50", GUILayout.Height(40)))
        {
            cityGen.citySize = 50f;
            cityGen.blockSize = 15f;
            cityGen.GenerateCity();
            EditorUtility.SetDirty(cityGen);
            SceneView.RepaintAll();
        }
        if (GUILayout.Button("Sedang\n150", GUILayout.Height(40)))
        {
            cityGen.citySize = 150f;
            cityGen.blockSize = 25f;
            cityGen.GenerateCity();
            EditorUtility.SetDirty(cityGen);
            SceneView.RepaintAll();
        }
        if (GUILayout.Button("Besar\n300", GUILayout.Height(40)))
        {
            cityGen.citySize = 300f;
            cityGen.blockSize = 40f;
            cityGen.GenerateCity();
            EditorUtility.SetDirty(cityGen);
            SceneView.RepaintAll();
        }
        EditorGUILayout.EndHorizontal();
    }
}