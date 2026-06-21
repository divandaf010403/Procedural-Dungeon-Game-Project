using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Main controller untuk procedural city generation ADVANCED version.
    ///
    /// UPGRADED FEATURES:
    /// - District System (Downtown, Commercial, Industrial, Residential, Suburbs, Park, Waterfront)
    /// - Lot Subdivision (block dipecah jadi lot kecil dengan shape varied)
    /// - Skyline Variation (gedung tinggi di pusat kota)
    /// - Curved Roads (Bezier-based main avenues)
    /// - River + Bridges (sungai mengalir dengan jembatan otomatis)
    /// - Procedural Building Shapes (L, T, U, Rectangle, Wide, Narrow)
    /// - Vehicles & Traffic Lights
    /// - Material variation per district
    ///
    /// Workflow lengkap:
    /// 1. RoadNetwork.GenerateRoads() - generate jalan straight + curved
    /// 2. DistrictManager.AssignBlocksToDistricts() - zoning
    /// 3. RiverGenerator.GenerateRiver() - sungai + jembatan
    /// 4. BuildingPlacer.PlaceBuildings() - generate buildings per lot
    /// 5. CityEnvironmentDetails.AddDetails() - lights, vehicles, props
    /// </summary>
    [ExecuteAlways]
    public class CityGenerator : MonoBehaviour
    {
        [Header("City Settings")]
        [Range(50f, 1000f)]
        public float citySize = 300f;

        [Range(10f, 80f)]
        public float blockSize = 40f;

        [Range(2f, 12f)]
        public float roadWidth = 6f;

        public int randomSeed = 42;

        [Tooltip("Jika true, Generate City akan otomatis randomize seed setiap kali generate")]
        public bool autoRandomSeed = false;

        [Header("Generation")]
        public bool generateOnStart = true;
        public bool addEnvironmentDetails = true;
        public bool generateRiver = false; // Default OFF - user request

        [Range(0f, 1f)]
        public float buildingDensity = 0.7f;

        [Header("Building Prefabs")]
        public GameObject[] buildingPrefabs;

        [Header("Detail Prefabs")]
        public GameObject streetLightPrefab;
        public GameObject treePrefab;
        public GameObject vehiclePrefab;
        public GameObject[] propPrefabs;

        [Header("Materials")]
        public Material roadMaterial;
        public Material sidewalkMaterial;

        [Header("River Settings")]
        [Range(5f, 50f)]
        public float riverWidth = 18f;

        [Header("Generated References")]
        [SerializeField] private RoadNetwork roadNetwork;
        [SerializeField] private DistrictManager districtManager;
        [SerializeField] private RiverGenerator riverGenerator;
        [SerializeField] private BuildingPlacer buildingPlacer;
        [SerializeField] private CityEnvironmentDetails environmentDetails;

        private List<GameObject> spawnedObjects = new List<GameObject>();

        private void Start()
        {
            if (Application.isPlaying && generateOnStart)
            {
                GenerateCity();
            }
        }

        /// <summary>
        /// Generate kota lengkap dengan semua advanced features.
        /// </summary>
        [ContextMenu("Generate City")]
        public void GenerateCity()
        {
            ClearCity();

            Debug.Log($"[CityGenerator] === Starting ADVANCED city generation with seed {randomSeed}, size {citySize} ===");

            Random.InitState(randomSeed);

            // Tahap 1: Generate roads (straight + curved)
            roadNetwork = GetOrAddComponent<RoadNetwork>();
            roadNetwork.Initialize(this);
            roadNetwork.GenerateRoads();

            // Tahap 2: Setup districts dan assign blocks
            districtManager = new DistrictManager();
            districtManager.Initialize(this, roadNetwork);
            districtManager.AssignBlocksToDistricts();

            // Tahap 0: Ground plane untuk referensi visual
            GenerateGroundPlane();

            // Tahap 3 (optional): Generate river + bridges
            if (generateRiver)
            {
                riverGenerator = new RiverGenerator();
                riverGenerator.Initialize(this, roadNetwork);
                riverGenerator.GenerateRiver();
            }

            // Tahap 4: Place buildings per lot dengan district rules
            // DISABLED - User hanya ingin land dan roads
            // buildingPlacer = GetOrAddComponent<BuildingPlacer>();
            // buildingPlacer.Initialize(this, roadNetwork, districtManager);
            // buildingPlacer.PlaceBuildings();

            // Tahap 5: Add environment details
            // DISABLED - User hanya ingin land dan roads
            // if (addEnvironmentDetails)
            // {
            //     environmentDetails = GetOrAddComponent<CityEnvironmentDetails>();
            //     environmentDetails.vehiclePrefab = vehiclePrefab;
            //     environmentDetails.Initialize(this, roadNetwork, districtManager);
            //     environmentDetails.AddDetails();
            // }

            // Print statistics
            LogStatistics();

            // Setup camera & lighting supaya user bisa langsung melihat kota
            FrameCamera();

            Debug.Log($"[CityGenerator] === Generation complete! {spawnedObjects.Count} top-level objects ===");
        }

        [ContextMenu("Clear City")]
        public void ClearCity()
        {
            foreach (var obj in spawnedObjects)
            {
                if (obj != null)
                {
                    if (Application.isPlaying) Destroy(obj);
                    else DestroyImmediate(obj);
                }
            }
            spawnedObjects.Clear();

            if (roadNetwork != null) roadNetwork.ClearRoads();
            if (riverGenerator != null) riverGenerator.ClearRiver();
            if (buildingPlacer != null) buildingPlacer.ClearBuildings();
            if (environmentDetails != null) environmentDetails.ClearDetails();
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

        private void LogStatistics()
        {
            int totalDistricts = districtManager != null ? districtManager.districts.Count : 0;
            int totalBlocks = roadNetwork != null ? roadNetwork.blocks.Count : 0;
            int totalRoads = roadNetwork != null ? roadNetwork.horizontalRoads.Count + roadNetwork.verticalRoads.Count : 0;

            Debug.Log($"[CityGenerator] Statistics:");
            Debug.Log($"  - Districts: {totalDistricts}");
            Debug.Log($"  - Roads: {totalRoads} (pure orthogonal, no curves)");
            Debug.Log($"  - Blocks: {totalBlocks}");
            if (districtManager != null)
            {
                foreach (var d in districtManager.districts)
                {
                    Debug.Log($"  - District '{d.name}': {d.blockCount} blocks, {d.buildingCount} buildings");
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position, new Vector3(citySize, 0.1f, citySize));

            if (roadNetwork != null) roadNetwork.DrawGizmos();
            if (districtManager != null) districtManager.DrawGizmos();

            // River path
            if (riverGenerator != null && riverGenerator.riverPath != null)
            {
                Gizmos.color = Color.cyan;
                for (int i = 0; i < riverGenerator.riverPath.Count - 1; i++)
                {
                    Gizmos.DrawLine(riverGenerator.riverPath[i], riverGenerator.riverPath[i + 1]);
                }
            }
        }

        /// <summary>
        /// Generate ground plane besar sebagai referensi visual.
        /// PENTING: Top of ground harus di Y=-0.1 (di bawah jalan) supaya tidak z-fighting.
        /// Jalan ada di Y=0, jadi ground top harus lebih rendah.
        /// </summary>
        private void GenerateGroundPlane()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "GroundPlane";
            ground.transform.SetParent(transform);

            // Ground thickness 2 unit, center di Y=-1.1, jadi TOP ada di Y=-0.1
            // (di bawah jalan yang ada di Y=0, sehingga tidak z-fighting)
            float groundThickness = 2f;
            ground.transform.position = transform.position + new Vector3(0, -1.1f, 0);

            // Ground plane ukuran 1.5x city size (supaya lebih lebar dari kota)
            float groundSize = citySize * 1.5f;
            ground.transform.localScale = new Vector3(groundSize, groundThickness, groundSize);

            Renderer rend = ground.GetComponent<Renderer>();
            Material groundMat = CreateMaterial("Ground", new Color(0.35f, 0.5f, 0.25f));
            rend.sharedMaterial = groundMat;

            // Hapus collider
            Collider col = ground.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }

            RegisterSpawnedObject(ground);
        }

        /// <summary>
        /// Setup Main Camera untuk melihat seluruh kota.
        /// Jika belum ada Directional Light, tambahkan satu.
        /// </summary>
        [ContextMenu("Frame Camera")]
        public void FrameCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                GameObject camGO = new GameObject("Main Camera");
                camGO.tag = "MainCamera";
                cam = camGO.AddComponent<Camera>();
                camGO.AddComponent<AudioListener>();
            }

            // Position camera untuk melihat seluruh kota dari sudut pandang atas
            float camDistance = citySize * 1.5f;
            float camHeight = citySize * 0.8f;
            cam.transform.position = transform.position + new Vector3(camDistance * 0.7f, camHeight, -camDistance * 0.7f);
            cam.transform.LookAt(transform.position);
            cam.farClipPlane = citySize * 5f;

            // Tambah directional light kalau belum ada
            if (Object.FindObjectOfType<Light>() == null)
            {
                GameObject lightGO = new GameObject("SunLight");
                Light light = lightGO.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.0f;
                light.color = new Color(1f, 0.96f, 0.84f);
                lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
        }

        /// <summary>
        /// Helper: Buat material dengan fallback shader detection.
        /// Try URP first, fallback ke Standard shader kalau URP tidak ada.
        /// </summary>
        public static Material CreateMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Diffuse");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            Material mat = new Material(shader);
            mat.name = name;
            mat.color = color;
            return mat;
        }
    }
