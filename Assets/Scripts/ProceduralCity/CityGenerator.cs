using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Main controller untuk procedural city generation.
/// Fokus saat ini: road generation.
///
/// ROAD PIPELINE (RingAndLSystem — satu-satunya mode):
/// Ring road mengelilingi kota + 4 spoke ke pusat + L-System organic di interior
/// (multi-seed, clearance, BFS cleanup, connectNearbyEnds).
///
/// WORKFLOW:
/// 1. RoadNetwork.GenerateRoads() → jalan
/// 2. GenerateGroundPlane()       → ground visual
/// 3. FrameCamera()               → setup kamera
/// </summary>
[ExecuteAlways]
public class CityGenerator : MonoBehaviour
{
    [Header("City Settings")]
    [Range(1000f, 3000f)]
    public float citySize = 1500f;

    [Range(20f, 120f)]
    public float blockSize = 40f;

    [Range(2f, 12f)]
    public float roadWidth = 3f;

    public int randomSeed = 42;

    [Tooltip("Jika true, Generate City akan otomatis randomize seed setiap kali generate")]
    public bool autoRandomSeed = false;

    [Header("Generation")]
    public bool generateOnStart = true;

    [Header("Materials")]
    public Material roadMaterial;

    [Header("Generated References")]
    [SerializeField] private RoadNetwork roadNetwork;

    private List<GameObject> spawnedObjects = new List<GameObject>();

    private void Start()
    {
        if (Application.isPlaying && generateOnStart)
            GenerateCity();
    }

    [ContextMenu("Generate City")]
    public void GenerateCity()
    {
        if (autoRandomSeed)
            randomSeed = Random.Range(0, int.MaxValue);

        ClearCity();

        Debug.Log($"[CityGenerator] Generating city — seed={randomSeed}, size={citySize}, mode={GetRoadMode()}");

        Random.InitState(randomSeed);

        // Step 1: Roads
        roadNetwork = GetOrAddComponent<RoadNetwork>();
        roadNetwork.Initialize(this);
        roadNetwork.GenerateRoads();

        // Step 2: Ground plane
        GenerateGroundPlane();

        // Step 3: Camera
        FrameCamera();

        Debug.Log($"[CityGenerator] Done — {roadNetwork.roads.Count} roads, "
                + $"{roadNetwork.blocks.Count} blocks, "
                + $"{roadNetwork.junctions.Count} junctions");
    }

    [ContextMenu("Clear City")]
    public void ClearCity()
    {
        foreach (var obj in spawnedObjects)
            if (obj != null)
            {
                if (Application.isPlaying) Destroy(obj);
                else DestroyImmediate(obj);
            }
        spawnedObjects.Clear();

        // Sweep direct children (catches objects from previous runs)
        var children = new List<GameObject>();
        foreach (Transform child in transform)
            children.Add(child.gameObject);
        foreach (var child in children)
            if (child != null)
            {
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }

        if (roadNetwork != null) roadNetwork.ClearRoads();
    }

    public void RegisterSpawnedObject(GameObject obj)
    {
        if (obj != null) spawnedObjects.Add(obj);
    }

    public int GetObjectCount() => spawnedObjects.Count;

    private T GetOrAddComponent<T>() where T : Component
    {
        T comp = gameObject.GetComponent<T>();
        if (comp == null) comp = gameObject.AddComponent<T>();
        return comp;
    }

    private string GetRoadMode() => "RingAndLSystem";

    private void OnDrawGizmosSelected()
    {
        // City boundary
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position, new Vector3(citySize, 0.1f, citySize));

        if (roadNetwork != null) roadNetwork.DrawGizmos();
    }

    /// <summary>
    /// Ground plane di bawah jalan (Y=-0.1) supaya tidak z-fighting.
    /// Jalan ada di Y=0, ground top di Y=-0.1.
    /// </summary>
    private void GenerateGroundPlane()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "GroundPlane";
        ground.transform.SetParent(transform);

        // Center di Y=-1.1, thickness=2 → top di Y=-0.1
        ground.transform.position  = transform.position + new Vector3(0, -1.1f, 0);
        ground.transform.localScale = new Vector3(citySize * 1.5f, 2f, citySize * 1.5f);

        Renderer rend = ground.GetComponent<Renderer>();
        rend.sharedMaterial = CreateMaterial("Ground", new Color(0.35f, 0.5f, 0.25f));

        Collider col = ground.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
        }

        RegisterSpawnedObject(ground);
    }

    [ContextMenu("Frame Camera")]
    public void FrameCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            var camGO = new GameObject("Main Camera");
            camGO.tag = "MainCamera";
            cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<AudioListener>();
        }

        float dist   = citySize * 1.5f;
        float height = citySize * 0.8f;
        cam.transform.position  = transform.position + new Vector3(dist * 0.7f, height, -dist * 0.7f);
        cam.transform.LookAt(transform.position);
        cam.farClipPlane = citySize * 5f;

        if (Object.FindAnyObjectByType<Light>() == null)
        {
            var lightGO = new GameObject("SunLight");
            var light   = lightGO.AddComponent<Light>();
            light.type      = LightType.Directional;
            light.intensity = 1.0f;
            light.color     = new Color(1f, 0.96f, 0.84f);
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }
    }

    /// <summary>Buat material dengan fallback shader (URP → Standard → Diffuse).</summary>
    public static Material CreateMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard")
                     ?? Shader.Find("Diffuse")
                     ?? Shader.Find("Sprites/Default");

        var mat   = new Material(shader);
        mat.name  = name;
        mat.color = color;
        return mat;
    }
}
