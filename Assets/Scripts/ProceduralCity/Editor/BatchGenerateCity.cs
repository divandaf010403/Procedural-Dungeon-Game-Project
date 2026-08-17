using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Batch mode entry point untuk generate city dari command line.
/// Dipanggil via:
///   Unity.exe -batchmode -quit -projectPath "..." -executeMethod BatchGenerateCity.Run
/// Optional args: -citySize 3000 -seed 42
/// </summary>
public static class BatchGenerateCity
{
    public static void Run()
    {
        // Parse args
        float citySize = 3000f;
        int seed = 42;
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-citySize" && float.TryParse(args[i + 1], out float cs)) citySize = cs;
            if (args[i] == "-seed"     && int.TryParse(args[i + 1], out int sd))    seed = sd;
        }

        Debug.Log($"[BatchGenerateCity] citySize={citySize} seed={seed}");

        // Load scene
        var scene = EditorSceneManager.OpenScene(
            "Assets/Scenes/SampleSceneCity.unity",
            OpenSceneMode.Single);

        // Cari CityGenerator di scene
        var gen = Object.FindFirstObjectByType<CityGenerator>();
        if (gen == null)
        {
            Debug.LogError("[BatchGenerateCity] CityGenerator tidak ditemukan di scene!");
            EditorApplication.Exit(1);
            return;
        }

        // Set params
        gen.citySize   = citySize;
        gen.randomSeed = seed;

        // Generate
        gen.GenerateCity();

        Debug.Log("[BatchGenerateCity] Done.");
        EditorApplication.Exit(0);
    }
}
