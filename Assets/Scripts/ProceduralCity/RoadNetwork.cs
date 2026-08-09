using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RoadNetwork — road generation single-pipeline (mode selector dihapus).
///
/// PIPELINE (RingAndLSystem):
///   1. Ring road mengelilingi kota (loop tertutup, cell-by-cell)
///   2. 4 spoke radial dari pusat ke ring (jalan utama/arterial)
///   3. L-System turtle mengisi interior (multi-seed, asimetris per seed)
///   4. Lapisan kedua L-System mengisi void (blok kosong ≥ 64 cell)
///   5. ConnectDisconnectedToCore (pulau disambung ke ring via koridor)
///   6. Cleanup BFS (buang jalan yang tidak nyambung ring/spoke)
///   7. connectNearbyEnds (sambung dead-end → loop/blok tertutup)
///   8. FixRoad (klasifikasi tile +/T/I/L/O) + blocks + junctions
///
/// L-System symbols (dari SVS RoadHelper + SimpleVisualizer):
///   F  = maju 1 step, place road
///   f  = maju 1 step, no road
///   +  = belok kanan 90°
///   -  = belok kiri 90°
///   |  = balik 180°
///   [  = push state (cabang)
///   ]  = pop state (kembali ke parent)
///   X  = growth marker (untuk expansion, tidak di-draw)
/// </summary>
[System.Serializable]
public class RoadNetwork : MonoBehaviour
{
    // -----------------------------------------------------------------------
    // Public lists (consumed by pipeline)
    // -----------------------------------------------------------------------
    public List<RoadSegment> roads = new List<RoadSegment>();

    public List<Vector3>      intersections = new List<Vector3>();
    public List<CityBlock>    blocks        = new List<CityBlock>();
    public List<JunctionInfo> junctions     = new List<JunctionInfo>();

    // -----------------------------------------------------------------------
    // Road spacing settings
    // -----------------------------------------------------------------------
    [Range(0f, 1f)]
    [Tooltip("Probabilitas cabang '[...]' L-System benar-benar tumbuh (dipakai di PlaceSegmentsWithDecay case '['). Nilai efektifnya dikunci minimal 0.6 di script (nilai scene yang lebih kecil diabaikan) supaya interior terisi; naikkan ke 0.8+ untuk kota lebih rimbun.")]
    public float lSystemBranchChance = 0.15f;

    [Tooltip("Jarak minimal antar jalan dalam cell (dipakai PathMinClearance). 2 = jalan baru minimal 2 cell dari jalan lama → ada blok kosong yang terbaca. 1 = tanpa batasan → interior padat seperti anyaman.")]
    public int minimumDistanceBetweenRoads = 2;

    // -----------------------------------------------------------------------
    // L-System settings
    // -----------------------------------------------------------------------
    [Header("L-System Road Settings")]

    [Tooltip("Preset L-System yang dipakai.\nCustom = gunakan axiom/rules custom di bawah.")]
    public LSystemPreset lSystemPreset = LSystemPreset.OrganicCity;

    [Tooltip("Panjang 1 step turtle dalam world units. 0 = auto (dihitung dari citySize dan tileSize).")]
    public float lSystemStepSize = 0f;

    [Range(1, 8)]
    [Tooltip("Iterasi ekspansi L-System. Lebih tinggi = lebih banyak jalan, lebih lambat.")]
    public int lSystemIterations = 4;

    [Range(4, 16)]
    [Tooltip("Jarak antar seed L-System (dalam cell). seedPerSide dihitung = innerSize / nilai ini (dipaksa genap, dibatasi 2..12) sehingga kepadatan konsisten di semua ukuran kota. Nilai besar = interior lebih jarang. Default 8 ≈ 1 seed tiap 8 cell.")]
    public int lSystemSeedSpacing = 8;

    [Range(0f, 1f)]
    [Tooltip("Probabilitas skip rule per-karakter (variasi organik). Nilai efektifnya dibatasi maksimal 0.2 di script supaya pohon tidak terlalu tipis (nilai scene yang lebih besar diabaikan).")]
    public float lSystemChanceToIgnore = 0.3f;

    [Tooltip("Custom axiom — hanya dipakai jika preset = Custom.")]
    public string lSystemCustomAxiom = "X";

    [Tooltip("Custom rules — hanya dipakai jika preset = Custom. Format: 'X=F[-FX]+FX'")]
    public string[] lSystemCustomRules = new string[] { "X=F[-FX]+FX" };

    // -----------------------------------------------------------------------
    // Road Prefabs (SVS style — assign di Inspector)
    // -----------------------------------------------------------------------
    [Header("Road Prefabs")]
    [Tooltip("Prefab jalan lurus (default tile).")]
    public GameObject roadStraight;
    [Tooltip("Prefab belokan 90 derajat (L-shape).")]
    public GameObject roadCorner;
    [Tooltip("Prefab T-junction (3 arah).")]
    public GameObject road3Way;
    [Tooltip("Prefab perempatan (4 arah).")]
    public GameObject road4Way;
    [Tooltip("Prefab ujung jalan buntu (dead-end).")]
    public GameObject roadEnd;
    [Tooltip("Scale tile saat di-instantiate. Default (3,1,3) = lebar 3 unit, tidak tebal ke atas.")]
    public Vector3 roadTileScale = new Vector3(3f, 1f, 3f);

    // -----------------------------------------------------------------------
    // Road Add-Ons (SVS style — perlengkapan jalan, opsional)
    // -----------------------------------------------------------------------
    [Header("Road Add-Ons")]
    [Tooltip("Prefab traffic light (komponen TrafficLightBehavior). Dipasang di junction + dan T (SVS style). Jika array kosong, AutoInstallAddOnPrefabs akan membuat prefab dari model FBX AddOns.")]
    public GameObject[] trafficLightPrefabs;
    [Tooltip("Prefab streetlight (komponen StreetlightBehavior). Dipasang di segmen jalan lurus dengan interval tetap. Jika array kosong, AutoInstallAddOnPrefabs akan membuat prefab dari model FBX AddOns.")]
    public GameObject[] streetlightPrefabs;
    [Tooltip("Tempatkan perlengkapan jalan (traffic light & streetlight) setelah FixRoad.")]
    public bool enableRoadAddOns = true;
    [Tooltip("Editor-only: jika trafficLightPrefabs/streetlightPrefabs kosong saat Generate, buat prefab otomatis dari model di Assets/Models/Road/AddOns (disimpan ke Assets/Prefabs/AddOns) lalu assign ke array. Tidak menyentuh scene.")]
    public bool autoInstallAddOnPrefabs = true;

    // -----------------------------------------------------------------------
    // Ring Road Settings (RingAndLSystem mode)
    // -----------------------------------------------------------------------
    [Header("Ring Road Settings")]
    [Tooltip("Jika false, L-System & spokes tidak digenerate — untuk test RING-ONLY. Ring harus menjadi loop tertutup dengan L=4, O/T/+=0.")]
    public bool enableInteriorRoads = true;

    [Tooltip("Inset ring dari tepi kota (dalam cell). Auto = inset 7 cell (ring -18..18 untuk grid -25..25).")]
    public int ringInsetCells = 7;

    [Range(4, 6)]
    [Tooltip("Jumlah maksimum jalan interior yang boleh tersambung ke ring.")]
    public int numberOfRingEntrances = 4;

    [Tooltip("Jaring pengaman: hapus sisa jalan interior yang benar-benar tidak bisa disambung ke ring/spoke (biasanya 0, karena ConnectDisconnectedToCore sudah menyambungkan komponen terisolasi lebih dulu).")]
    public bool removeDisconnectedRoads = true;

    [Tooltip("Sambungkan ujung jalan interior (dead-end) yang berdekatan dengan jalan baru, membentuk loop/blok tertutup — mengurangi O drastis dan membuat jaringan terlihat seperti kota natural. Dijalankan setelah cleanup.")]
    public bool connectNearbyEnds = true;

    [Range(2, 20)]
    [Tooltip("Jarak maksimum (cell) antara dua ujung jalan yang boleh disambung connectNearbyEnds. Kecil (4-5) = loop lokal, blok lebih besar; besar = banyak celah tertutup → padat.")]
    public int connectEndsMaxDistance = 5;

    // -----------------------------------------------------------------------
    // Void Fill Settings (lapisan kedua L-System)
    // -----------------------------------------------------------------------
    [Header("Void Fill (Lapisan Kedua L-System)")]
    [Tooltip("Lapisan kedua L-System: setelah lapisan utama, isi blok kosong besar (void) dengan seed ekstra di pusat komponen kosong terbesar — supaya tidak ada area kosong raksasa (mis. void 872 cell di Huge). Pohon baru otomatis disambung ke ring oleh ConnectDisconnectedToCore.")]
    public bool enableSecondLayerFill = true;

    [Range(16, 256)]
    [Tooltip("Ukuran minimum komponen kosong (cell) yang diisi lapisan kedua. 64 ≈ blok kosong 8×8 cell. Komponen di bawah ini dibiarkan sebagai blok kota normal.")]
    public int secondLayerMinVoidCells = 64;

    [Tooltip("Batas maksimum seed ekstra per generate (pengaman performa). Nilai efektifnya minimal 250 di script — scene bisa menyimpan 100 dari versi lama.")]
    public int secondLayerMaxSeeds = 250;

    // -----------------------------------------------------------------------
    // Private state
    // -----------------------------------------------------------------------
    private CityGenerator   cityGenerator;
    private System.Random   rng;

    private float   halfSize;
    private float   roadWidth;
    private float   cellSize;
    private float   blockSpacing; // dihitung otomatis dari citySize/tileSize

    // Nilai efektif kepadatan L-System (dikunci di script, lihat PlaceLSystemTiles)
    private float   branchChanceEffective = 0.6f;

    private RoadGridHelper gridHelper;

    private RoadAddOnDecorator roadAddOnDecorator;

    private List<Vector3Int> gridPositions = new List<Vector3Int>();

    // -----------------------------------------------------------------------
    // Ring geometry — logical grid, integer cell. citySize 1500 & tileScale 30
    // → tilesPerSide = 50 → range -25..25, ring default -18..18.
    // -----------------------------------------------------------------------
    private int tilesPerSide;      // total cell per sisi
    private int gridMin;           // koordinat cell minimum grid (-half)
    private int gridMax;           // koordinat cell maksimum grid (+half, exclusive boundary)
    private int ringMinX;          // ring kiri  (cell)
    private int ringMaxX;          // ring kanan (cell)
    private int ringMinZ;          // ring bawah (cell)
    private int ringMaxZ;          // ring atas  (cell)

    // ringCells = loop tertutup cell-per-cell; innerRoadCells = semua jalan interior.
    // HasRoad() = ringCells ∪ innerRoadCells. Tidak boleh ada cell terpisah.
    private readonly HashSet<Vector3Int> ringCells      = new HashSet<Vector3Int>();
    private readonly HashSet<Vector3Int> innerRoadCells = new HashSet<Vector3Int>();
    // spokeCells = 4 jalan radial dari pusat ke ring (dianggap bagian jaringan inti,
    // seperti ring: tidak dihapus BFS, di-skip PathMinClearance, bisa ditambat pohon).
    private readonly HashSet<Vector3Int> spokeCells     = new HashSet<Vector3Int>();
    // Jalan interior yang menyambung ke ring (entrance) — dibatasi jumlahnya.
    // Hanya L-System yang menghitung; spoke TIDAK menghabiskan kuota.
    private readonly HashSet<Vector3Int> ringEntrances = new HashSet<Vector3Int>();
    // Spoke yang sudah dipakai oleh pohon L-System sebagai anchor — max 1 pohon/spoke.
    private readonly HashSet<Vector3Int> spokeAnchors  = new HashSet<Vector3Int>();

    // Helper ring
    public bool IsRingCell(Vector3Int c) => ringCells.Contains(c);
    public bool IsRingCell(int x, int z) => ringCells.Contains(new Vector3Int(x, 0, z));
    public int  RingCellCount           => ringCells.Count;
    public int  InnerRoadCellCount      => innerRoadCells.Count;

    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------
    private const float MIN_BLOCK_DIM = 20f;


    // =======================================================================
    // PUBLIC API
    // =======================================================================
    public void Initialize(CityGenerator generator)
    {
        cityGenerator = generator;
        rng = new System.Random(cityGenerator.randomSeed);
    }

    public void GenerateRoads()
    {
        ClearRoads();
        rng       = new System.Random(cityGenerator.randomSeed);
        halfSize  = cityGenerator.citySize * 0.5f;  // world units, selalu
        roadWidth = cityGenerator.roadWidth;

        // cellSize = tileWorldSize → 1 cell = 1 tile prefab di world
        float tileSize = roadTileScale.x > 0f ? roadTileScale.x : 3f;
        cellSize = tileSize;

        // Berapa tile per sisi kota — default grid logical (di-override per-mode bila perlu)
        int tilesPerSide = Mathf.FloorToInt(cityGenerator.citySize / tileSize);
        int halfCells = tilesPerSide / 2;
        gridMin = -halfCells;
        gridMax =  halfCells;

        // Interval blok: ~15% dari tilesPerSide → ~7 blok per sisi
        int tilesPerBlock  = Mathf.Clamp(tilesPerSide / 7, 4, 40);
        blockSpacing       = tilesPerBlock * tileSize; // world units

        // L-System step: ~3-4 tile per step (bukan 1 blok penuh)
        // supaya interior terisi detail dan tidak simetris
        float effectiveLsStep = lSystemStepSize > 0f
            ? lSystemStepSize
            : Mathf.Clamp(tilesPerBlock / 3, 2, 5) * tileSize;

        Debug.Log($"[RoadNetwork] citySize={cityGenerator.citySize}, tileSize={tileSize}, "
                + $"tilesPerSide={tilesPerSide}, tilesPerBlock={tilesPerBlock}, "
                + $"blockSpacing={blockSpacing}wu, lsStep={effectiveLsStep}wu");

        // Parent container untuk semua tile prefab — diangkat 0.2 di Y supaya
        // tile tidak z-fighting dengan ground plane (y=0).
        var roadContainer = new GameObject("RoadTiles");
        roadContainer.transform.SetParent(cityGenerator.transform);
        roadContainer.transform.localPosition = new Vector3(0f, 0.31f, 0f);
        cityGenerator.RegisterSpawnedObject(roadContainer);

        gridHelper = new RoadGridHelper(roadContainer.transform, cellSize);
        gridHelper.SetRingBounds(ringMinX, ringMaxX, ringMinZ, ringMaxZ);

        // tileWorldSize = ukuran visual 1 tile dalam world units
        gridHelper.tileWorldSize  = tileSize;
        gridHelper.prefabStraight = roadStraight;
        gridHelper.prefabCorner   = roadCorner;
        gridHelper.prefab3Way     = road3Way;
        gridHelper.prefab4Way     = road4Way;
        gridHelper.prefabEnd      = roadEnd;
        gridHelper.tileScale      = roadTileScale;

        // Add-on decorator — jalan perlengkapan (traffic light & streetlight)
#if UNITY_EDITOR
        // Scene lama menyimpan array prefab kosong — isi otomatis dari model
        // FBX AddOns supaya lampu benar-benar muncul tanpa edit scene manual.
        AutoInstallAddOnPrefabs();
#endif
        roadAddOnDecorator = new RoadAddOnDecorator(gridHelper, roadContainer.transform, tileSize);
        roadAddOnDecorator.SetTrafficLightPrefabs(trafficLightPrefabs);
        roadAddOnDecorator.SetStreetlightPrefabs(streetlightPrefabs);

        // Pipeline tunggal (RingAndLSystem):
        //   ring road → spokes → L-System interior → cleanup → connectNearbyEnds
        GenerateRingRoad();
        if (enableInteriorRoads)
        {
            PlaceSpokesToRing(transform.position);   // jalan utama/radial
            PlaceLSystemTiles(effectiveLsStep);      // L-System interior
            if (enableSecondLayerFill)
                PlaceSecondLayerFill(effectiveLsStep); // isi void besar (lapisan kedua)
        }
        else
        {
            Debug.Log($"[RoadNetwork] RING-ONLY test — interior disabled. Ring cells: {ringCells.Count}");
        }

        // Hubungkan komponen jalan yang terisolasi ke jaringan inti (ring/spoke).
        // Bukan dihapus — dihubungkan lewat koridor terpendek, jadi SEMUA jalan
        // bisa ditelusuri sampai ke ring (tidak ada pulau yang terisolasi).
        if (enableInteriorRoads)
            ConnectDisconnectedToCore();

        // Cleanup BFS = jaring pengaman: hapus sisa yang benar-benar tidak bisa
        // disambung (koridor penuh / keluar interior). Normalnya 0 yang dihapus.
        if (enableInteriorRoads && removeDisconnectedRoads)
            RemoveDisconnectedInnerRoads();

        // Sambungkan endpoint yang berdekatan → loop/blok tertutup.
        // Setelah koneksi + cleanup, semua endpoint ada di komponen utama, jadi
        // setiap koneksi O→O membentuk siklus = blok kota natural.
        if (enableInteriorRoads && connectNearbyEnds)
            ConnectNearbyEnds();

        // Hitung ulang mask N/E/S/W setelah cleanup + koneksi
        RecalculateAllMasks();

        // Validasi simetri koneksi N/E/S/W
        ValidateRoadConnections();

        // ---- Finalize: FixRoad + Blocks + Junctions ----
        FinalizeRoads();

        // ---- Add-ons — setelah FixRoad, supaya tile sudah prefab final ----
        if (enableRoadAddOns)
            PlaceRoadAddOns();
    }

    // =======================================================================
    // ROAD ADD-ONS — traffic light & streetlight (SVS AddOns).
    // Harus dipanggil SETELAH FixRoad (tile sudah prefab final).
    // =======================================================================
    private void PlaceRoadAddOns()
    {
        if (roadAddOnDecorator == null) return;

        var snap = HasRoadSnapshot(); // ring ∪ inner — state akhir

        // 1. Traffic light di junction + dan T (SVS style — hanya junction)
        roadAddOnDecorator.PlaceTrafficLights(snap);

        // 2. Streetlight di sepanjang segmen lurus (ring, spoke, interior)
        roadAddOnDecorator.PlaceStreetlights(snap);

        Debug.Log($"[RoadNetwork] Add-ons selesai — traffic lights: "
                + $"{roadAddOnDecorator.TrafficLightCount} terpasang "
                + $"(prefab {roadAddOnDecorator.HasTrafficLights}), streetlights: "
                + $"{roadAddOnDecorator.StreetlightCount} terpasang "
                + $"(prefab {roadAddOnDecorator.HasStreetlights})");
    }

#if UNITY_EDITOR
    // =======================================================================
    // AUTO-INSTALL ADD-ON PREFABS (editor-only — tidak menyentuh scene)
    //
    // Scene lama menyimpan trafficLightPrefabs/streetlightPrefabs kosong,
    // jadi lampu tidak pernah muncul. Jika array kosong saat Generate:
    //   1. Cari prefab yang sudah punya komponen marker + mesh (punya user)
    //   2. Jika tidak ada, buat prefab dari model FBX di Assets/Models/Road/AddOns
    //      (root + komponen marker + child model), simpan ke Assets/Prefabs/AddOns
    //   3. Assign ke array + SetDirty supaya tersimpan untuk generate berikutnya.
    // =======================================================================
    private void AutoInstallAddOnPrefabs()
    {
        if (!autoInstallAddOnPrefabs) return;

        bool dirty = false;
        if (trafficLightPrefabs == null || trafficLightPrefabs.Length == 0)
        {
            var found = FindVisibleAddOnPrefabs<TrafficLightBehavior>();
            if (found.Count == 0)
                found = BuildAddOnPrefabsFromModels<TrafficLightBehavior>(
                    new[] { "TrafficLight.fbx", "TrafficLight_2.fbx" });
            if (found.Count > 0)
            {
                trafficLightPrefabs = found.ToArray();
                dirty = true;
                Debug.Log($"[RoadNetwork] Auto-install: {found.Count} prefab traffic light siap "
                        + $"({found[0].name}, ...)");
            }
        }
        if (streetlightPrefabs == null || streetlightPrefabs.Length == 0)
        {
            var found = FindVisibleAddOnPrefabs<StreetlightBehavior>();
            if (found.Count == 0)
                found = BuildAddOnPrefabsFromModels<StreetlightBehavior>(
                    new[] { "Streetlight_Single.fbx", "Streetlight_Double.fbx", "Streetlight_Triple.fbx" });
            if (found.Count > 0)
            {
                streetlightPrefabs = found.ToArray();
                dirty = true;
                Debug.Log($"[RoadNetwork] Auto-install: {found.Count} prefab streetlight siap "
                        + $"({found[0].name}, ...)");
            }
        }
        if (dirty)
        {
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssets();
        }
    }

    /// <summary>
    /// Semua prefab di project yang memiliki komponen marker T DAN mesh
    /// (prefab kosong tanpa mesh dilewati — tidak akan terlihat di scene).
    /// </summary>
    private static List<GameObject> FindVisibleAddOnPrefabs<T>() where T : Component
    {
        var result = new List<GameObject>();
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab"))
        {
            var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || prefab.GetComponent<T>() == null) continue;
            if (prefab.GetComponentInChildren<MeshRenderer>() == null) continue; // kosong
            result.Add(prefab);
        }
        return result;
    }

    /// <summary>
    /// Buat prefab add-on dari model FBX AddOns: root GameObject + komponen
    /// marker T + child model FBX. Disimpan ke Assets/Prefabs/AddOns/
    /// (folder dibuat otomatis). Prefab yang sudah ada ditimpa (idempotent).
    /// </summary>
    private static List<GameObject> BuildAddOnPrefabsFromModels<T>(string[] modelNames)
        where T : Component
    {
        const string outputDir = "Assets/Prefabs/AddOns";
        if (!UnityEditor.AssetDatabase.IsValidFolder(outputDir))
            UnityEditor.AssetDatabase.CreateFolder("Assets/Prefabs", "AddOns");

        var result = new List<GameObject>();
        foreach (var modelName in modelNames)
        {
            var modelPath = FindModelPath(modelName);
            if (modelPath == null)
            {
                Debug.LogWarning($"[RoadNetwork] Auto-install: model {modelName} tidak ditemukan.");
                continue;
            }

            var model = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) continue;

            var name = System.IO.Path.GetFileNameWithoutExtension(modelName);
            var outPath = $"{outputDir}/{name}.prefab";

            var root = new GameObject(name);
            root.AddComponent<T>();
            var modelInstance = (GameObject)Object.Instantiate(model);
            modelInstance.name = name;
            modelInstance.transform.SetParent(root.transform, false);
            modelInstance.transform.localPosition = Vector3.zero;

            var prefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, outPath);
            Object.DestroyImmediate(root);
            if (prefab != null) result.Add(prefab);
        }
        return result;
    }

    private static string FindModelPath(string fileName)
    {
        var search = fileName.Replace(".fbx", "") + " t:Model";
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets(search))
        {
            var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith("/" + fileName, System.StringComparison.OrdinalIgnoreCase))
                return path;
        }
        return null;
    }
#endif

    // =======================================================================
    // GENERATE RING ROAD — cell-by-cell, loop tertutup, simetris.
    // citySize 1500 & tileScale 30 → tilesPerSide=50 → range -25..25.
    // Ring default = -18..18 (di dalam kota). Dibuat SEBELUM interior roads.
    //
    // Ring cell-by-cell:
    //   S sisi: z=ringMinZ, x dari ringMinX..ringMaxX
    //   N sisi: z=ringMaxZ, x dari ringMinX..ringMaxX
    //   W sisi: x=ringMinX, z dari ringMinZ..ringMaxZ
    //   E sisi: x=ringMaxX, z dari ringMinZ..ringMaxZ
    // Keempat sudut tercakup dua kali di set → dedupe otomatis.
    // Jumlah ring cell untuk ring X=-7..7, Z=-7..7 (15 x 15) = 56.
    // =======================================================================
    private void GenerateRingRoad()
    {
        // Grid logical simetris — koordinat cell integer, bukan world float.
        tilesPerSide = Mathf.FloorToInt(cityGenerator.citySize / cellSize);
        int halfCells = tilesPerSide / 2;   // 25 untuk 50 tile/side
        gridMin = -halfCells;                // -25
        gridMax =  halfCells;                // +25 (boundary eksklusif)

        // Ring default di -18..18 — inset 7 cell dari tepi kota.
        // Dipakai juga oleh L-System spokes (ringTarget) supaya semua konsisten.
        int inset = Mathf.Clamp(ringInsetCells, 2, halfCells - 2);
        ringMinX = ringMinZ = gridMin + inset; // -18
        ringMaxX = ringMaxZ = gridMax - inset; // +18
        if (gridHelper != null)
            gridHelper.SetRingBounds(ringMinX, ringMaxX, ringMinZ, ringMaxZ);

        // Ring cell-by-cell — setiap sisi. Sudut otomatis milik 2 sisi.
        ringCells.Clear();
        for (int i = ringMinX; i <= ringMaxX; i++)
        {
            AddRingCell(i, ringMinZ); // S
            AddRingCell(i, ringMaxZ); // N
        }
        for (int i = ringMinZ; i <= ringMaxZ; i++)
        {
            AddRingCell(ringMinX, i); // W
            AddRingCell(ringMaxX, i); // E
        }

        int len = ringMaxX - ringMinX; // sama dengan ringMaxZ - ringMinZ (persegi)

        // Daftarkan 4 segmen ke roads list supaya district/buildings aware
        float wMin = CellToWorld(ringMinX);
        float wMax = CellToWorld(ringMaxX);
        roads.Add(new RoadSegment(new Vector3(wMin, 0, wMin), new Vector3(wMax, 0, wMin), roadWidth)); // S
        roads.Add(new RoadSegment(new Vector3(wMin, 0, wMax), new Vector3(wMax, 0, wMax), roadWidth)); // N
        roads.Add(new RoadSegment(new Vector3(wMin, 0, wMin), new Vector3(wMin, 0, wMax), roadWidth)); // W
        roads.Add(new RoadSegment(new Vector3(wMax, 0, wMin), new Vector3(wMax, 0, wMax), roadWidth)); // E

        int expected = (ringMaxX - ringMinX + 1) * 4 - 4; // 2*W + 2*H - 4 sudut ganda
        int actual   = ringCells.Count;
        Debug.Log($"[RoadNetwork] Ring road: X[{ringMinX}..{ringMaxX}] Z[{ringMinZ}..{ringMaxZ}], "
                + $"{len + 1} cell/side, grid [{gridMin}..{gridMax}], ringCells={actual} "
                + $"(expected {expected})");

        // Validasi ring saja — tanpa interior, ring harus menjadi loop murni.
        if (!enableInteriorRoads)
        {
            ValidateRingContinuity(ringCells, "RingRoad");
        }
        else
        {
            // Kontinuitas cell-to-cell (Connectivity lintasan ring), tanpa klasifikasi.
            int ringBroken = 0;
            foreach (var c in ringCells)
            {
                bool n = ringCells.Contains(c + new Vector3Int( 0, 0,  1));
                bool s = ringCells.Contains(c + new Vector3Int( 0, 0, -1));
                bool e = ringCells.Contains(c + new Vector3Int( 1, 0,  0));
                bool w = ringCells.Contains(c + new Vector3Int(-1, 0,  0));
                int  arms = (n?1:0) + (s?1:0) + (e?1:0) + (w?1:0);
                if (arms != 2) ringBroken++;
            }
            Debug.Log($"[RoadNetwork] Ring continuity: {ringCells.Count - ringBroken}/{ringCells.Count} "
                    + $"cell ber-tetangga 2 (non-corner {ringBroken} — ok untuk 4 sudut).");
        }
    }

    /// <summary>Tambah satu cell ring ke set + gridHelper (tile visual).</summary>
    private void AddRingCell(int x, int z)
    {
        var cell = new Vector3Int(x, 0, z);
        if (ringCells.Add(cell))
            gridHelper.PlaceStreetPositions(cell, new Vector3Int(1, 0, 0), 1);
    }

    /// <summary>
    /// True jika cell berada di interior ring (ringMinX < x < ringMaxX,
    /// ringMinZ < z < ringMaxZ) — eksklusif, ring sendiri tidak termasuk.
    /// </summary>
    private bool IsInsideRingInterior(Vector3Int cell) =>
        cell.x > ringMinX && cell.x < ringMaxX
        && cell.z > ringMinZ && cell.z < ringMaxZ;

    /// <summary>
    /// HasRoad(): true jika cell merupakan ringCells ATAU innerRoadCells.
    /// Satu-satunya sumber kebenaran "cell ini jalan".
    /// </summary>
    private bool HasRoad(Vector3Int cell) =>
        ringCells.Contains(cell) || innerRoadCells.Contains(cell);

    // =======================================================================
    // PLACE: L-SYSTEM TILES
    // Pendekatan SVS Visualizer.cs:
    //   - SINGLE origin dari pusat kota → semua cabang tumbuh dari 1 akar
    //     → connected by design, tidak perlu connectivity repair
    //   - Length decay per depth → semakin dalam cabang, semakin pendek step
    //     → seperti SVS "Length -= 2" tapi proporsional terhadap citySize
    //   - Hasilnya: satu pohon jalan yang terhubung sempurna
    // =======================================================================
    private void PlaceLSystemTiles(float stepSize)
    {
        var lsys = BuildLSystemFromPreset();
        lsys.Init(cityGenerator.randomSeed);

        // Density tuning (script-side): scene bisa menyimpan nilai lama yang
        // membuat interior terlalu jarang (branch 0.15 = 85% cabang di-skip,
        // ignore 0.3). Nilai efektif di-clamp ke rentang sehat supaya hasil
        // tetap kota berisi apa pun nilai di scene.
        float effIgnore = Mathf.Clamp(lSystemChanceToIgnore, 0.05f, 0.2f);
        branchChanceEffective = Mathf.Clamp(lSystemBranchChance, 0.6f, 1f);
        lsys.chanceToIgnore = effIgnore;
        lsys.iterations = Mathf.Clamp(lSystemIterations, 1, 4);
        Debug.Log($"[RoadNetwork] L-System tuning: branch={branchChanceEffective:F2} (scene {lSystemBranchChance:F2}), "
                + $"ignore={effIgnore:F2} (scene {lSystemChanceToIgnore:F2}), iter={lsys.iterations}");

        // Step kecil supaya interior terisi tapi tidak padat
        float effectiveStep = stepSize > 0f ? stepSize : blockSpacing * 0.5f;

        Vector3 origin = cityGenerator.transform.position;
        float decay = 0.8f;

        // Sebar seed point DI DALAM INTERIOR RING — inset 2 cell dari ring.
        // Gunakan koordinat cell integer, bukan world float.
        int innerMin = ringMinX + 2;
        int innerMax = ringMaxX - 2;
        int innerSize = innerMax - innerMin; // ~32 untuk ring -18..18

        // Seed grid: seedPerSide = innerSize / lSystemSeedSpacing — proporsional
        // dengan ukuran kota supaya kepadatan konsisten di semua preset
        // (Small tidak jadi gumpalan, Huge tidak jarang). Dipaksa GENAP agar
        // baris/kolom tengah tidak jatuh tepat di spoke/pusat (x=0 / z=0) yang
        // mematikan pohon (fromCell = spoke → semua langkah pertama ditolak).
        // MINIMUM 4 per sisi (16 pohon): dengan pohon terlalu sedikit (mis. 2×2)
        // sisa jalan yang selamat bisa membentuk pola pinwheel seperti swastika
        // di sekitar pusat — lebih banyak pohon mencegah pola itu mendominasi.
        int seedPerSide = Mathf.Clamp(Mathf.RoundToInt(innerSize / (float)lSystemSeedSpacing), 4, 12);
        if (seedPerSide % 2 != 0) seedPerSide++;
        var rngLs = new System.Random(cityGenerator.randomSeed + 99);
        float spacing = (float)innerSize / seedPerSide;

        // Offset acak untuk SELURUH grid seed — memecah simetri kuadran.
        // Tanpa ini, seed duduk di posisi cermin (x↔-x, z↔-z) sehingga pohon
        // yang selamat selalu simetris dan hasilnya mirip untuk semua seed.
        float gridOffsetX = (float)(rngLs.NextDouble() * spacing);
        float gridOffsetZ = (float)(rngLs.NextDouble() * spacing);

        int seedCount = 0;
        seedPositions.Clear();
        for (int gx = 0; gx < seedPerSide; gx++)
        {
            for (int gz = 0; gz < seedPerSide; gz++)
            {
                // Posisi seed: grid + offset acak global + jitter per-seed.
                // (IsInsideRingInterior menyaring seed yang keluar batas.)
                float sx = innerMin + gridOffsetX + spacing * (gx + 0.5f) + (float)(rngLs.NextDouble() * 0.5f);
                float sz = innerMin + gridOffsetZ + spacing * (gz + 0.5f) + (float)(rngLs.NextDouble() * 0.5f);

                var startCell = new Vector3Int(
                    Mathf.RoundToInt(sx), 0, Mathf.RoundToInt(sz));
                if (!IsInsideRingInterior(startCell)) continue;

                int startDir = rngLs.Next(0, 4);
                lsys.Init(cityGenerator.randomSeed + gx * 131 + gz * 17);
                string sentence = lsys.Generate();
                // PENTING: turtle memakai koordinat WORLD (X/Z = world units,
                // lihat MoveTurtleTo). startCell adalah koordinat CELL — kalikan
                // cellSize. Tanpa ini semua seed duduk di cell (0,0) = perpotongan
                // spoke, semua langkah ditolak → L-System kosong.
                PlaceSegmentsWithDecay(sentence,
                                       startCell.x * cellSize, startCell.z * cellSize,
                                       startDir, effectiveStep, decay, origin);
                seedPositions.Add(startCell);
                seedCount++;
            }
        }

        Debug.Log($"[RoadNetwork] L-System: {seedCount} seeds, iter={lsys.iterations}, "
                + $"step={effectiveStep:F1}, ring X[{ringMinX}..{ringMaxX}] Z[{ringMinZ}..{ringMaxZ}]");
    }

    /// <summary>
    /// Posisi seed L-System terakhir yang dipakai (cell) — dipakai
    /// RoadAddOnDecorator untuk menempatkan traffic light di interior.
    /// </summary>
    private readonly List<Vector3Int> seedPositions = new List<Vector3Int>();

    // Ringkasan lapisan kedua (ditulis ke header file peta supaya verifikasi
    // tidak bergantung pada Editor.log yang bisa ter-rotate).
    private string secondLayerSummary = "";

    // =======================================================================
    // VOID FILL — LAPISAN KEDUA L-SYSTEM
    // Isi blok kosong besar (void) yang tersisa setelah lapisan utama:
    //  1. BFS komponen cell KOSONG di interior (bukan jalan)
    //  2. Ambil komponen terbesar yang ≥ secondLayerMinVoidCells
    //  3. Tanam pohon L-System di pusat komponen itu (batang + cabang)
    //  4. Ulangi sampai tidak ada lagi komponen besar (void terpecah jadi blok)
    // Pohon baru adalah komponen terisolasi → ConnectDisconnectedToCore
    // menyambungkannya ke ring lewat koridor (yang ikut mengisi void).
    // =======================================================================

    /// <summary>
    /// Lapisan kedua L-System — isi void (blok kosong besar) di interior.
    /// Target: tidak ada komponen kosong ≥ secondLayerMinVoidCells yang tersisa
    /// (untuk Huge, void 872 cell → terpecah jadi blok < 64 cell ≈ < 5% interior).
    /// </summary>
    private void PlaceSecondLayerFill(float stepSize)
    {
        if (!enableSecondLayerFill || !enableInteriorRoads) return;
        if (ringCells.Count == 0) return;

        var lsys = BuildLSystemFromPreset();
        lsys.chanceToIgnore = Mathf.Clamp(lSystemChanceToIgnore, 0.05f, 0.2f);
        lsys.iterations     = Mathf.Clamp(lSystemIterations, 1, 4);
        float decay         = 0.8f;
        Vector3 origin      = cityGenerator.transform.position;

        int largestVoidBefore = 0;
        int emptyBefore = 0;
        foreach (var comp in FindEmptyInteriorComponents())
        {
            emptyBefore += comp.Count;
            if (comp.Count > largestVoidBefore) largestVoidBefore = comp.Count;
        }

        int seedsPlaced = 0;
        int emptyProgress = 0; // iterasi beruntun tanpa progres (anti infinite loop)
        var rng2 = new System.Random(cityGenerator.randomSeed + 4242);

        // Target void: berhenti mengisi begitu void terbesar < max(minVoidCells,
        // 5% interior). JANGAN memaksakan < 64 cell di semua ukuran — itu yang
        // membuat Huge jadi 57% jalan (anyaman, 175 fragmen < 4 cell) pada batch
        // 07:26. 5% interior = 361 cell utk Huge, 130 utk Large, 64 utk Medium.
        int interiorCells = (ringMaxX - ringMinX - 1) * (ringMaxZ - ringMinZ - 1);
        int voidTarget = Mathf.Max(secondLayerMinVoidCells,
                                   Mathf.RoundToInt(interiorCells * 0.05f));

        // Nilai efektif minimal 250 — scene bisa menyimpan 100 dari versi lama.
        int maxSeeds = Mathf.Max(secondLayerMaxSeeds, 250);
        for (int iter = 0; iter < maxSeeds; iter++)
        {
            // 1. Komponen kosong terbesar saat ini
            HashSet<Vector3Int> largest = null;
            foreach (var comp in FindEmptyInteriorComponents())
            {
                if (largest == null || comp.Count > largest.Count)
                    largest = comp;
            }
            if (largest == null || largest.Count < voidTarget)
                break;

            // 2. Seed = cell PALING DALAM (jarak terjauh dari jalan) di komponen
            //    — bukan centroid. Titik terdalam memberi pohon ruang tumbuh
            //    maksimal di semua arah dan tidak pernah mendarat di dekat jalan
            //    yang mematikan segmen pertama.
            Vector3Int seed = DeepestCellInComponent(largest, HasRoadSnapshot());
            if (!IsInsideRingInterior(seed)) break; // safety

            // 3. Tumbuhkan pohon L-System dari seed (turtle sama dengan lapisan 1).
            //    Percobaan 1: clearance normal (margin field) supaya blok tetap
            //    terbaca. Jika pohon lemah (< 8 cell — biasanya karena void berupa
            //    koridor sempit yang dikelilingi jalan), percobaan 2: margin 1
            //    (clearance dimatikan) supaya pohon bisa mengisi koridor sempit
            //    dan menutup loop → void pecah. PlaceStreetPositions idempotent,
            //    jadi percobaan 2 aman menimpa jalur percobaan 1.
            lsys.Init(cityGenerator.randomSeed + 5000 + iter * 7919);
            string sentence = lsys.Generate();
            int before = innerRoadCells.Count;
            int startDir = rng2.Next(0, 4);
            PlaceSegmentsWithDecay(sentence,
                                   seed.x * cellSize, seed.z * cellSize,
                                   startDir, stepSize, decay, origin);
            int placed = innerRoadCells.Count - before;
            if (placed < 8)
            {
                // Percobaan 2: pohon KECIL (iterasi 2) dengan clearance dimatikan
                // (margin 1) — cukup untuk menembus koridor sempit & menutup loop,
                // TANPA memadati seluruh void jadi anyaman (iterasi 4 + margin 1
                // = 57% jalan di Huge pada batch 07:26). Iterasi dipulihkan
                // setelahnya supaya seed berikutnya memakai ukuran normal.
                int normalIter = lsys.iterations;
                lsys.iterations = Mathf.Min(2, normalIter);
                PlaceSegmentsWithDecay(sentence,
                                       seed.x * cellSize, seed.z * cellSize,
                                       rng2.Next(0, 4), stepSize, decay, origin,
                                       clearanceMargin: 1);
                lsys.iterations = normalIter;
                placed = innerRoadCells.Count - before;
            }

            // Tidak ada progres → coba komponen berikutnya (jangan break total:
            // komponen lain yang lebih kecil mungkin masih bisa diisi). Berhenti
            // total hanya jika 5 komponen beruntun gagal (anti infinite loop).
            if (placed == 0)
            {
                if (++emptyProgress >= 5) break;
                continue;
            }
            emptyProgress = 0;
            seedPositions.Add(seed);
            seedsPlaced++;
        }

        // 4. Ukur sisa void terbesar + persentase terhadap luas interior
        int largestVoidAfter = 0;
        int emptyAfter = 0;
        foreach (var comp in FindEmptyInteriorComponents())
        {
            emptyAfter += comp.Count;
            if (comp.Count > largestVoidAfter) largestVoidAfter = comp.Count;
        }
        float pct = interiorCells > 0 ? 100f * largestVoidAfter / interiorCells : 0f;

        secondLayerSummary = $"SecondLayerFill: {seedsPlaced} seed ekstra di void "
                + $"(target {voidTarget} cell = max({secondLayerMinVoidCells}, 5% interior)), "
                + $"empty {emptyBefore} → {emptyAfter} cell, "
                + $"void terbesar {largestVoidBefore} → {largestVoidAfter} cell ({pct:F1}% interior, target < 5%)";
        Debug.Log($"[RoadNetwork] {secondLayerSummary}");
    }

    /// <summary>
    /// Cell di komponen kosong dengan jarak terjauh dari jalan (multi-source BFS
    /// dari cell komponen yang menempel jalan). Seed paling dalam → pohon punya
    /// ruang tumbuh maksimal dan tidak pernah lahir di dekat jalan lama.
    /// </summary>
    private Vector3Int DeepestCellInComponent(HashSet<Vector3Int> comp, HashSet<Vector3Int> snap)
    {
        var dist = new Dictionary<Vector3Int, int>();
        var queue = new Queue<Vector3Int>();
        foreach (var c in comp)
        {
            for (int i = 0; i < 4; i++)
            {
                if (snap.Contains(c + Dirs[i]))
                {
                    dist[c] = 0;
                    queue.Enqueue(c);
                    break;
                }
            }
        }

        Vector3Int best = default;
        int bestDist = -1;
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            int d = dist[cur];
            if (d > bestDist) { bestDist = d; best = cur; }
            for (int i = 0; i < 4; i++)
            {
                var nb = cur + Dirs[i];
                if (!comp.Contains(nb) || dist.ContainsKey(nb)) continue;
                dist[nb] = d + 1;
                queue.Enqueue(nb);
            }
        }

        // Fallback: komponen tanpa cell yang menempel jalan (mustahil di dalam
        // ring yang dikelilingi jalan, tapi aman) → centroid terdekat.
        if (bestDist < 0)
        {
            float sx = 0f, sz = 0f;
            foreach (var c in comp) { sx += c.x; sz += c.z; }
            sx /= comp.Count; sz /= comp.Count;
            float bd = float.MaxValue;
            foreach (var c in comp)
            {
                float dx = c.x - sx, dz = c.z - sz;
                float dd = dx * dx + dz * dz;
                if (dd < bd) { bd = dd; best = c; }
            }
        }
        return best;
    }

    /// <summary>
    /// Komponen cell KOSONG (bukan jalan) di dalam interior ring — BFS 4-arah.
    /// Ring/spoke/inner tidak dihitung; cell di luar interior tidak dihitung.
    /// </summary>
    private List<HashSet<Vector3Int>> FindEmptyInteriorComponents()
    {
        var result = new List<HashSet<Vector3Int>>();
        var visited = new HashSet<Vector3Int>();
        var snap = HasRoadSnapshot(); // ring ∪ inner (spoke sudah di dalam inner)

        for (int x = ringMinX + 1; x < ringMaxX; x++)
        {
            for (int z = ringMinZ + 1; z < ringMaxZ; z++)
            {
                var cell = new Vector3Int(x, 0, z);
                if (snap.Contains(cell)) continue;
                if (!visited.Add(cell)) continue;

                var comp = new HashSet<Vector3Int> { cell };
                var queue = new Queue<Vector3Int>();
                queue.Enqueue(cell);
                while (queue.Count > 0)
                {
                    var cur = queue.Dequeue();
                    for (int i = 0; i < 4; i++)
                    {
                        var nb = cur + Dirs[i];
                        if (nb.x <= ringMinX || nb.x >= ringMaxX) continue;
                        if (nb.z <= ringMinZ || nb.z >= ringMaxZ) continue;
                        if (snap.Contains(nb)) continue;
                        if (visited.Add(nb))
                        {
                            comp.Add(nb);
                            queue.Enqueue(nb);
                        }
                    }
                }
                result.Add(comp);
            }
        }
        return result;
    }

    /// <summary>
    /// Turtle L-System dengan ring-aware placement.
    /// - Turtle bergerak di world space; setiap F = curStep world units.
    /// - Segmen di-snap ke cell integer, di-clamp "connect-and-stop" di ring.
    /// - Segmen yang membuat endpoint terisolasi atau terlalu dekat jalan lama ditolak.
    /// - Jalan interior tidak pernah melewati ring (ringCells = batas keras).
    /// </summary>
    private void PlaceSegmentsWithDecay(string sentence,
                                        float startX, float startZ, int startDir,
                                        float baseStep, float decayFactor,
                                        Vector3 boundsOrigin,
                                        int clearanceMargin = -1)
    {
        var   turtle  = new RoadTurtle(startX, startZ, startDir, baseStep);
        var   stack   = new Stack<(float x, float z, int dir, float step)>();
        float curStep = baseStep;
        for (int si = 0; si < sentence.Length; si++)
        {
            char c = sentence[si];
            switch (c)
            {
                case 'F':
                {
                    if (curStep <= 0) break;

                    turtle.StepSize = curStep;
                    var (from, to) = turtle.MoveForward();

                    var fromCell   = WorldToCell3(from);
                    var toCellRaw  = WorldToCell3(to);

                    // Connect-and-stop: potong segmen di ring ATAU spoke (tidak melewati)
                    var toCell = ClampToNetworkCell(fromCell, toCellRaw);

                    var delta = toCell - fromCell;
                    int len   = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.z));

                    if (len > 0)
                    {
                        var dir = new Vector3Int(
                            delta.x != 0 ? (int)Mathf.Sign(delta.x) : 0,
                            0,
                            delta.z != 0 ? (int)Mathf.Sign(delta.z) : 0);

                        bool endsAtRing  = ringCells.Contains(toCell);
                        bool endsAtSpoke = !endsAtRing && spokeCells.Contains(toCell);

                        // FIX #2: segmen yang berhenti TEPAT satu cell sebelum ring
                        // (searah gerak) diperpanjang untuk tersambung ke ring —
                        // tidak boleh berhenti sebelum ring dan jadi endpoint O palsu.
                        if (!endsAtRing && !endsAtSpoke)
                        {
                            var nextCell = toCell + dir;
                            if (ringCells.Contains(nextCell))
                            {
                                toCell     = nextCell;
                                delta      = toCell - fromCell;
                                len        = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.z));
                                endsAtRing = true;
                            }
                            else if (spokeCells.Contains(nextCell))
                            {
                                toCell     = nextCell;
                                delta      = toCell - fromCell;
                                len        = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.z));
                                endsAtSpoke = true;
                            }
                        }

                        // Kuota entrance ring KHUSUS L-System — spoke tidak pernah
                        // masuk ringEntrances (PlaceSpokesToRing tidak menambah kuota).
                        // Spoke-anchor (endsAtSpoke) juga tidak menghabiskan kuota.
                        if (endsAtRing && ringEntrances.Count >= numberOfRingEntrances)
                        {
                            MoveTurtleTo(turtle, fromCell);
                            curStep = Mathf.Max(curStep - 2f * cellSize, cellSize);
                            break;
                        }

                        // Satu pohon per spoke — spoke sudah punya anchor lain.
                        if (endsAtSpoke && spokeAnchors.Contains(toCell))
                        {
                            MoveTurtleTo(turtle, fromCell);
                            curStep = Mathf.Max(curStep - 2f * cellSize, cellSize);
                            break;
                        }

                        // Tolak segmen yang terlalu dekat dengan jalan lama.
                        // Di dekat ring/spoke, cell yang hanya "jembatan" menuju
                        // jaringan inti (diabaikan oleh PathMinClearance) tidak menghalangi.
                        bool closeToNetwork = endsAtRing || endsAtSpoke
                                           || ringCells.Contains(fromCell) || spokeCells.Contains(fromCell);
                        // clearanceMargin >= 1 meng-override jarak clearance
                        // (1 = tanpa clearance — dipakai lapisan kedua utk mengisi
                        // koridor sempit di void). -1 = pakai nilai Inspector.
                        int effMargin = clearanceMargin >= 1 ? clearanceMargin : minimumDistanceBetweenRoads;
                        if (PathMinClearance(fromCell, toCell, dir, effMargin,
                                             skipsToRingSeparate: closeToNetwork))
                        {
                            MoveTurtleTo(turtle, toCell);
                            curStep = Mathf.Max(curStep - 2f * cellSize, cellSize);
                            break;
                        }

                        gridHelper.PlaceStreetPositions(fromCell, dir, len + 1);
                        roads.Add(new RoadSegment(
                            new Vector3(fromCell.x * cellSize, 0, fromCell.z * cellSize),
                            new Vector3(toCell.x * cellSize,   0, toCell.z * cellSize),
                            roadWidth));

                        // Catat semua cell interior (termasuk yang menyambung ring/spoke)
                        for (int i = 0; i <= len; i++)
                            innerRoadCells.Add(fromCell + dir * i);

                        if (endsAtRing)
                        {
                            ringEntrances.Add(toCell);
                            Debug.Log($"[RoadNetwork] Interior→ring entrance di {toCell} "
                                    + $"({ringEntrances.Count}/{numberOfRingEntrances})");
                        }
                        else if (endsAtSpoke)
                        {
                            spokeAnchors.Add(toCell);
                            Debug.Log($"[RoadNetwork] Pohon interior→spoke anchor di {toCell} "
                                    + $"({spokeAnchors.Count}/4) — pohon terselamatkan dari cleanup");
                        }
                    }

                    MoveTurtleTo(turtle, toCell);

                    // SVS Length -= 2 per draw
                    curStep -= 2f * cellSize;
                    if (curStep < cellSize) curStep = cellSize;
                    break;
                }
                case 'f':
                {
                    turtle.StepSize = curStep;
                    var (fFrom, fTo) = turtle.MoveForward();
                    var fToCell = ClampToNetworkCell(WorldToCell3(fFrom), WorldToCell3(fTo));
                    MoveTurtleTo(turtle, fToCell);
                    break;
                }
                case '+': turtle.TurnRight();  break;
                case '-': turtle.TurnLeft();   break;
                case '|': turtle.TurnAround(); break;
                case '[':
                    // branchChanceEffective = probabilitas cabang benar-benar tumbuh.
                    // Jika gagal, lompat ke ']' penutup (seluruh cabang di-skip).
                    if (rng.NextDouble() > branchChanceEffective)
                    {
                        int depth = 1;
                        while (depth > 0 && si + 1 < sentence.Length)
                        {
                            si++;
                            if (sentence[si] == '[') depth++;
                            else if (sentence[si] == ']') depth--;
                        }
                        break;
                    }
                    stack.Push((turtle.X, turtle.Z, turtle.Dir, curStep));
                    turtle.Push();
                    break;
                case ']':
                    if (stack.Count > 0)
                    {
                        var (px, pz, pd, ps) = stack.Pop();
                        turtle.X  = px;
                        turtle.Z  = pz;
                        turtle.Dir = pd;
                        curStep   = ps;
                    }
                    turtle.Pop();
                    break;
            }
        }
    }

    /// <summary>Pindahkan turtle ke world position dari cell tertentu.</summary>
    private void MoveTurtleTo(RoadTurtle turtle, Vector3Int cell)
    {
        turtle.X = cell.x * cellSize;
        turtle.Z = cell.z * cellSize;
    }

    /// <summary>
    /// Clamp segmen axis-aligned ke jaringan inti (ring ∪ spoke) — "connect and stop".
    /// Berjalan cell-per-cell dari fromCell ke toCell, berhenti pada ring/spoke cell
    /// (menyambung, tidak melewati). Jika jaringan inti kosong, clamp ke batas kota.
    ///
    /// PERILAKU RING / SPOKE:
    /// 1. Jika fromCell SUDAH ring/spoke → segmen BERADA di jaringan inti → tidak
    ///    diizinkan berjalan keluar/berlanjut melewatinya → return fromCell (null-step).
    /// 2. Jika next cell adalah ring/spoke → return next (connect & stop).
    /// 3. Jika dariCell ring dan toCell arah KELUAR (lewat ring menuju luar) →
    ///    return fromCell (tolak), bukan lanjut ke luar.
    /// </summary>
    private Vector3Int ClampToNetworkCell(Vector3Int fromCell, Vector3Int toCell)
    {
        if (ringCells.Count == 0 && spokeCells.Count == 0)
        {
            // Mode tanpa jaringan inti — clamp ke grid kota saja
            int half = tilesPerSide / 2;
            return new Vector3Int(
                Mathf.Clamp(toCell.x, -half, half - 1),
                0,
                Mathf.Clamp(toCell.z, -half, half - 1));
        }

        // FIX #1: segmen yang MULAI dari cell ring/spoke tidak boleh berjalan KELUAR
        // dari jaringan inti. Perbaiki kebocoran pertama dari PlaceSegmentsWithDecay:
        // turtle berada di ring/spoke, F ke arah luar → dari=ring, to=luar.
        if (ringCells.Contains(fromCell) || spokeCells.Contains(fromCell))
        {
            // Walk arah segmen; jika keluar dari batas jaringan inti → tolak (return fromCell).
            int dirX = Mathf.Clamp(toCell.x - fromCell.x, -1, 1);
            int dirZ = Mathf.Clamp(toCell.z - fromCell.z, -1, 1);
            int curX = fromCell.x, curZ = fromCell.z;
            int steps = Mathf.Abs(toCell.x - fromCell.x) + Mathf.Abs(toCell.z - fromCell.z);
            for (int i = 0; i < steps; i++)
            {
                curX += dirX; curZ += dirZ;
                var next = new Vector3Int(curX, 0, curZ);
                if (ringCells.Contains(next)) continue; // masih di ring (tangensial)
                if (spokeCells.Contains(next) && !ringCells.Contains(fromCell))
                    continue; // masih di spoke (tangensial) — spoke hanya untuk interior
                if (IsInsideRingInterior(next)) break;  // masuk ke interior — ok
                return fromCell;                        // keluar jaringan inti → tolak
            }
            return fromCell; // ujung di jaringan inti/tangensial — tetap null-step
        }

        // Axis-aligned walk — satu langkah per arah dominan.
        // Berhenti saat next adalah ring/spoke (connect & stop).
        var step = new Vector3Int(
            Mathf.Clamp(toCell.x - fromCell.x, -1, 1),
            0,
            Mathf.Clamp(toCell.z - fromCell.z, -1, 1));

        var current = fromCell;
        int maxSteps = Mathf.Abs(toCell.x - fromCell.x)
                     + Mathf.Abs(toCell.z - fromCell.z) + 2;
        for (int i = 0; i < maxSteps; i++)
        {
            if (step.x == 0 && step.z == 0) break;
            var next = current + step;

            if (ringCells.Contains(next))
                return next; // sambung ke ring, stop
            if (spokeCells.Contains(next))
                return next; // sambung ke spoke, stop

            current = next;
        }
        return current;
    }

    /// <summary>
    /// True jika segmen dari fromCell ke toCell (arah dir) membuat endpoint
    /// terisolasi: ujung bukan ring/spoke dan tidak bersentuhan jalan/junction lama.
    /// </summary>
    private bool WouldCreateIsolatedEndpoint(Vector3Int fromCell, Vector3Int toCell, Vector3Int dir)
    {
        if (ringCells.Contains(toCell)) return false;
        if (spokeCells.Contains(toCell)) return false;

        var snap = gridHelper.GridSnapshot;

        // Ujung menyentuh jalan lama (bukan lewat segmen ini)?
        bool touchesExisting =
               snap.Contains(toCell + new Vector3Int( 0, 0,  1))
            || snap.Contains(toCell + new Vector3Int( 0, 0, -1))
            || snap.Contains(toCell + new Vector3Int( 1, 0,  0))
            || snap.Contains(toCell + new Vector3Int(-1, 0,  0));

        // Segmen ini sendiri menyediakan koneksi ke arah -dir → bukan isolasi
        var backCell = toCell - dir;
        bool hasBackPath = snap.Contains(backCell)
                        || (backCell == fromCell) // start segmen (belum di-place, tapi berurutan)
                        || IsBetween(fromCell, toCell, backCell);

        return !touchesExisting && !hasBackPath;
    }

    /// <summary>True jika cell c terletak di antara a dan b (axis-aligned).</summary>
    private static bool IsBetween(Vector3Int a, Vector3Int b, Vector3Int c)
    {
        int minX = Mathf.Min(a.x, b.x), maxX = Mathf.Max(a.x, b.x);
        int minZ = Mathf.Min(a.z, b.z), maxZ = Mathf.Max(a.z, b.z);
        return c.x >= minX && c.x <= maxX && c.z >= minZ && c.z <= maxZ;
    }

    /// <summary>
    /// True jika ada jalan lama dalam jarak margin cell dari segmen
    /// (perpendicular), selain cell yang searah segmen itu sendiri.
    /// Ring/spoke cell TIDAK dihitung sebagai jarak dekat — jaringan inti boleh
    /// didekati untuk entrance/anchor. SkipsToRingSeparate juga diabaikan:
    /// pendekatan tegak lurus ke ring adalah entrance yang sah (connect & stop).
    ///
    /// CROSSING: jalan TEGAK LURUS yang dilintasi segmen BUKAN pelanggaran —
    /// segmen boleh menembusnya dan membentuk perempatan +/T. Cell sayap dari
    /// jalan yang dilintasi (mis. (3,4)/(3,6) untuk segmen horizontal di z=5
    /// yang menyeberang jalan vertikal di x=3) diproyeksikan ke garis segmen;
    /// jika proyeksinya adalah cell jalan yang sudah ada → itu bagian dari
    /// persilangan → di-skip. Tanpa ini, tiap persilangan ditolak clearance dan
    /// jaringan nyaris tidak pernah membentuk '+' (4-way) — statistik junction
    /// tetap berbentuk pohon (T ≫ +), bukan kota (mesh).
    /// </summary>
    private bool PathMinClearance(Vector3Int fromCell, Vector3Int toCell, Vector3Int dir,
                                  int margin, bool skipsToRingSeparate)
    {
        if (margin <= 1) return false;

        int minX = Mathf.Min(fromCell.x, toCell.x) - margin;
        int maxX = Mathf.Max(fromCell.x, toCell.x) + margin;
        int minZ = Mathf.Min(fromCell.z, toCell.z) - margin;
        int maxZ = Mathf.Max(fromCell.z, toCell.z) + margin;
        var snap = gridHelper.GridSnapshot;

        foreach (var cell in snap)
        {
            if (cell.x < minX || cell.x > maxX || cell.z < minZ || cell.z > maxZ)
                continue;

            if (ringCells.Contains(cell))
                continue;
            if (spokeCells.Contains(cell))
                continue;

            // Abaikan cell yang COLINEAR dengan segmen (baris/kolom yang sama)
            // — itu bagian dari garis jalan yang sama, termasuk TRAIL turtle di
            // belakang pangkal. Tanpa ini, segmen pendek (step decay ke 1 cell)
            // selalu ditolak oleh trail-nya sendiri setelah 2-3 langkah → pohon
            // mati muda, interior & void tidak pernah terisi (bukti: lapisan
            // kedua hanya menanam 1-6 seed dan tiap pohon cuma 2-14 cell).
            if (dir.x != 0 && cell.z == fromCell.z)
                continue;
            if (dir.z != 0 && cell.x == fromCell.x)
                continue;

            // CROSSING — lihat doc comment di atas. Proyeksikan cell konflik ke
            // garis segmen; jika proyeksinya adalah cell jalan yang sudah ada
            // ATAU fromCell (pangkal segmen — sayap tikungan/trail di baris/kolom
            // lain), konflik ini bagian dari persilangan/junction → bukan
            // pelanggaran. (proj == fromCell DIPERBOLEHKAN: tanpa itu, tiap
            // belokan ditolak oleh sayap trail di dekat pangkal — pohon berhenti
            // di segmen pertama yang berbelok.)
            if (dir.x != 0)
            {
                var proj = new Vector3Int(
                    Mathf.Clamp(cell.x, Mathf.Min(fromCell.x, toCell.x), Mathf.Max(fromCell.x, toCell.x)),
                    0, fromCell.z);
                if (snap.Contains(proj) || proj == fromCell)
                    continue;
            }
            else if (dir.z != 0)
            {
                var proj = new Vector3Int(
                    fromCell.x, 0,
                    Mathf.Clamp(cell.z, Mathf.Min(fromCell.z, toCell.z), Mathf.Max(fromCell.z, toCell.z)));
                if (snap.Contains(proj) || proj == fromCell)
                    continue;
            }

            if (skipsToRingSeparate && (ringCells.Contains(cell + dir) || spokeCells.Contains(cell + dir)))
                continue; // cell ini hanya "jembatan" menuju jaringan inti — bukan koridor

            return true; // ada jalan lain di dekat segmen
        }
        return false;
    }

    /// <summary>
    /// Bitmask koneksi cell: N=1, E=2, S=4, W=8.
    /// Dihitung dari gridSnapshot final — basis klasifikasi semua tile.
    /// </summary>
    private int GetRoadMask(Vector3Int pos)
    {
        int mask = 0;
        var snap = gridHelper.GridSnapshot;
        if (snap.Contains(pos + new Vector3Int( 0, 0,  1))) mask |= 1; // N
        if (snap.Contains(pos + new Vector3Int( 1, 0,  0))) mask |= 2; // E
        if (snap.Contains(pos + new Vector3Int( 0, 0, -1))) mask |= 4; // S
        if (snap.Contains(pos + new Vector3Int(-1, 0,  0))) mask |= 8; // W
        return mask;
    }

    /// <summary>Klasifikasi tile berdasarkan mask: 0=# 1,2,4,8=O 5,10=I 3,6,9,12=L 7,11,13,14=T 15=+.</summary>
    private static char ClassifyTile(int mask)
    {
        switch (mask)
        {
            case 0:             return '#';
            case 1: case 2:
            case 4: case 8:     return 'O'; // endpoint
            case 5: case 10:    return 'I'; // straight
            case 3: case 6:
            case 9: case 12:    return 'L'; // corner
            case 7: case 11:
            case 13: case 14:   return 'T'; // 3-way
            case 15:            return '+'; // 4-way
            default:            return '?';
        }
    }

    /// <summary>
    /// Validasi kontinuitas set cell jalan — tiap cell harus punya minimal
    /// 2 tetangga searah (di sepanjang jalur). Return jumlah cell bermasalah.
    /// </summary>
    private static int ValidateRingContinuity(HashSet<Vector3Int> cellSet, string label)
    {
        int issues = 0;
        foreach (var c in cellSet)
        {
            int n = cellSet.Contains(c + new Vector3Int( 0, 0,  1)) ? 1 : 0;
            int s = cellSet.Contains(c + new Vector3Int( 0, 0, -1)) ? 1 : 0;
            int e = cellSet.Contains(c + new Vector3Int( 1, 0,  0)) ? 1 : 0;
            int w = cellSet.Contains(c + new Vector3Int(-1, 0,  0)) ? 1 : 0;
            int arms = n + s + e + w;
            if (arms < 2)
            {
                issues++;
                Debug.LogWarning($"[ValidateRingContinuity] {label}: cell {c} hanya {arms} tetangga "
                               + $"(N={n} S={s} E={e} W={w}) — ring tidak kontinu di sini");
            }
        }
        Debug.Log($"[ValidateRingContinuity] {label}: {cellSet.Count - issues}/{cellSet.Count} "
                + "cell OK, non-continu = " + issues);
        return issues;
    }

    /// <summary>
    /// Validasi simetri koneksi:
    ///   E(x,z) harus cocok W(x+1,z);  N(x,z) harus cocok S(x,z+1).
    /// Return jumlah koneksi asimetris.
    /// </summary>
    public int ValidateConnectivity()
    {
        if (gridHelper == null) return 0;
        var snap = gridHelper.GridSnapshot;
        int asym = 0;
        foreach (var pos in snap)
        {
            bool hasE = snap.Contains(pos + new Vector3Int( 1, 0, 0));
            bool hasW = snap.Contains(pos + new Vector3Int(-1, 0, 0));
            bool hasN = snap.Contains(pos + new Vector3Int( 0, 0, 1));
            bool hasS = snap.Contains(pos + new Vector3Int( 0, 0,-1));

            // E di (x,z) ⇔ W di (x+1,z)
            bool eastSym = hasE == snap.Contains(new Vector3Int(pos.x + 1, 0, pos.z) + new Vector3Int(-1, 0, 0));
            // N di (x,z) ⇔ S di (x,z+1)
            bool northSym = hasN == snap.Contains(new Vector3Int(pos.x, 0, pos.z + 1) + new Vector3Int(0, 0, -1));

            if (!eastSym) { asym++; Debug.LogWarning($"[Connectivity] E/W asimetris di {pos}"); }
            if (!northSym) { asym++; Debug.LogWarning($"[Connectivity] N/S asimetris di {pos}"); }
        }
        Debug.Log($"[Connectivity] Validasi selesai — {asym} koneksi asimetris");
        return asym;
    }

    /// <summary>
    /// Log ring/interior/tile stats untuk verifikasi target:
    /// Ring-only harus menghasilkan O=0, T=0, +=0, L=4, I=sisanya.
    /// </summary>
    private void LogRoadStats(int invalidConnections)
    {
        int c4 = 0, c3 = 0, cI = 0, cL = 0, cO = 0;
        foreach (var pos in HasRoadSnapshot()) // ring ∪ inner — hasil akhir setelah cleanup
        {
            switch (ClassifyTile(GetRoadMask(pos)))
            {
                case '+': c4++; break;
                case 'T': c3++; break;
                case 'I': cI++; break;
                case 'L': cL++; break;
                case 'O': cO++; break;
            }
        }

        Debug.Log($"[RoadNetwork] === RingAndLSystem STATS ===");
        Debug.Log($"[RoadNetwork] citySize={cityGenerator.citySize}, tileScale={cellSize:F1}, "
                + $"grid X[{gridMin}..{gridMax}] Z[{gridMin}..{gridMax}] ({tilesPerSide}x{tilesPerSide} cells)");
        Debug.Log($"[RoadNetwork] ring: X[{ringMinX}..{ringMaxX}] Z[{ringMinZ}..{ringMaxZ}] "
                + $"ringCells={ringCells.Count}, spokes={spokeCells.Count}, "
                + $"innerRoadCells={innerRoadCells.Count}, "
                + $"entrances={ringEntrances.Count}/{numberOfRingEntrances}, "
                + $"spokeAnchors={spokeAnchors.Count}/4");
        Debug.Log($"[RoadNetwork] tiles: + {c4}  T {c3}  I {cI}  L {cL}  O {cO}  "
                + $"total={c4 + c3 + cI + cL + cO}");
        float ratio = c3 > 0 ? (float)c4 / c3 : 0f;
        Debug.Log($"[RoadNetwork] +/T ratio = {ratio:F2}  (target ≈ 1.0 — kota natural: '+' sebanding dengan 'T')");
        Debug.Log($"[RoadNetwork] invalid connections={invalidConnections}");

        if (!enableInteriorRoads)
        {
            bool pass = (c4 == 0 && c3 == 0 && cO == 0 && cL == 4 && invalidConnections == 0);
            Debug.Log($"[RoadNetwork] RING-ONLY TEST: {(pass ? "PASS ✅" : "FAIL ❌")} "
                    + $"(expect +0 T0 O0 L4, got +{c4} T{c3} O{cO} L{cL})");
        }
    }

    /// <summary>
    /// Tambah spoke (jalan lurus) dari pusat kota ke ring road.
    /// Pakai cell integer ring-aware; berhenti DI ring (connect and stop),
    /// dan menghormati batas entrance (max 6 total).
    /// </summary>
    private void PlaceSpokesToRing(Vector3 origin)
    {
        int cx = WorldToCell(origin.x);
        int cz = WorldToCell(origin.z);

        // 4 arah: (dx,dz) dan target cell ring
        var dirs = new (Vector3Int dir, Vector3Int ringTarget)[]
        {
            (new Vector3Int(0, 0,  1), new Vector3Int(cx, 0, ringMaxZ)), // N
            (new Vector3Int(0, 0, -1), new Vector3Int(cx, 0, ringMinZ)), // S
            (new Vector3Int(1, 0,  0), new Vector3Int(ringMaxX, 0, cz)), // E
            (new Vector3Int(-1,0,  0), new Vector3Int(ringMinX, 0, cz)), // W
        };

        foreach (var (dir, ringTarget) in dirs)
        {
            // FIX: spoke TIDAK menghitung ke numberOfRingEntrances — kuota itu
            // khusus L-System. Spoke selalu ada (4 arah) sebagai tulang kota.
            // Panjang spoke dari center ke ring (cell integer)
            int len = Mathf.Abs(ringTarget.x - cx) + Mathf.Abs(ringTarget.z - cz);
            if (len <= 0) continue;

            // CATATAN: spokes dijalankan SEBELUM L-System, jadi satu-satunya jalan
            // non-ring adalah spoke-spoke lain yang berpotongan di pusat.
            // PathMinClearance TIDAK dipakai di sini — nanti menjadikan
            // spoke perpendicular ditolak di dekat pusat. Ring sudah di-skip
            // di PathMinClearance. Ujung spoke = ring cell (connect & stop).
            var startCell = new Vector3Int(cx, 0, cz);
            gridHelper.PlaceStreetPositions(startCell, dir, len + 1);
            roads.Add(new RoadSegment(
                new Vector3(cx * cellSize, 0, cz * cellSize),
                new Vector3(ringTarget.x * cellSize, 0, ringTarget.z * cellSize),
                roadWidth));

            // Catat cell spoke sebagai jalan interior. Spoke TIDAK masuk
            // ringEntrances — kuota numberOfRingEntrances khusus untuk jalan
            // L-System yang menyambung ke ring (lihat PlaceSegmentsWithDecay).
            for (int i = 0; i <= len; i++)
            {
                var cell = startCell + dir * i;
                innerRoadCells.Add(cell);
                spokeCells.Add(cell); // spoke = jaringan inti, selamat dari cleanup
            }
            Debug.Log($"[RoadNetwork] Spoke→ring connection di {ringTarget} "
                    + $"({spokeCells.Count}/{len + 1} cells)");
        }

        Debug.Log($"[RoadNetwork] Spokes: center=({cx},{cz}), ring X[{ringMinX}..{ringMaxX}] "
                + $"Z[{ringMinZ}..{ringMaxZ}] — 4 spoke terpasang, kuota L-System "
                + $"entrance tersisa {ringEntrances.Count}/{numberOfRingEntrances}");
    }

    // =======================================================================
    // INTERIOR CLEANUP — BFS dari ringCells, hapus jalan terisolasi.
    // Ring TIDAK pernah dihapus. Klasifikasi (FixRoad) dilakukan SETELAH cleanup.
    // =======================================================================

    /// <summary>
    /// 4 tetangga orthogonal (N/S/E/W) yang merupakan jalan (ring ∪ inner).
    /// </summary>
    private List<Vector3Int> GetRoadNeighbors(Vector3Int cell)
    {
        var result = new List<Vector3Int>(4);
        for (int i = 0; i < 4; i++)
        {
            var nb = cell + Dirs[i];
            if (HasRoad(nb)) result.Add(nb);
        }
        return result;
    }

    /// <summary>BFS dari jaringan inti (ring ∪ spoke) — jangkauan jalan interior.</summary>
    private void FloodFillReachable(HashSet<Vector3Int> reachable, Queue<Vector3Int> queue)
    {
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var nb in GetRoadNeighbors(cur))
            {
                if (reachable.Add(nb)) queue.Enqueue(nb);
            }
        }
    }

    /// <summary>Endpoint jalan interior (mask punya tepat 1 lengan) — untuk log/validasi.</summary>
    private List<Vector3Int> FindInnerRoadEndpoints()
    {
        var endpoints = new List<Vector3Int>();
        foreach (var cell in innerRoadCells)
        {
            if (ArmCount(GetRoadMask(cell)) == 1)
                endpoints.Add(cell);
        }
        return endpoints;
    }

    /// <summary>Jumlah lengan koneksi dari bitmask (bit count 1-4).</summary>
    private static int ArmCount(int mask)
    {
        int count = 0;
        for (int bit = 1; bit <= 8; bit <<= 1)
            if ((mask & bit) != 0) count++;
        return count;
    }

    /// <summary>
    /// True jika cell dapat dicapai dari jaringan inti melalui jaringan jalan
    /// (BFS di ringCells ∪ spokeCells ∪ innerRoadCells). Jaringan inti selalu reachable.
    /// </summary>
    private bool ValidateReachabilityFromRing(Vector3Int start)
    {
        if (ringCells.Contains(start)) return true;
        if (spokeCells.Contains(start)) return true;
        var seen  = new HashSet<Vector3Int>();
        var queue = new Queue<Vector3Int>();
        foreach (var coreCell in ringCells)
        {
            seen.Add(coreCell);
            queue.Enqueue(coreCell);
        }
        foreach (var coreCell in spokeCells)
        {
            seen.Add(coreCell);
            queue.Enqueue(coreCell);
        }
        FloodFillReachable(seen, queue);
        return seen.Contains(start);
    }

    /// <summary>
    /// Hapus semua innerRoadCells yang TIDAK tercapai dari ring (BFS).
    /// BFS-nya jalan penuh dari ring → semua reachable. Sisanya dihapus.
    /// </summary>
    public void RemoveDisconnectedInnerRoads()
    {
        if (!removeDisconnectedRoads) return;

        // Guard: tanpa jaringan inti (ring/spoke), BFS tidak punya titik awal —
        // jangan hapus semua jalan. Mode LSystem (tanpa ring) tidak pernah
        // menjalankan cleanup ini.
        if (ringCells.Count == 0 && spokeCells.Count == 0) return;

        int before = innerRoadCells.Count;

        // BFS sekali dari semua ring + spoke cell — reachable = semua jalan
        // interior yang terhubung jaringan inti (ring ATAU spoke).
        var reachable = new HashSet<Vector3Int>();
        var queue     = new Queue<Vector3Int>();
        foreach (var coreCell in ringCells)
        {
            reachable.Add(coreCell);
            queue.Enqueue(coreCell);
        }
        foreach (var coreCell in spokeCells)
        {
            reachable.Add(coreCell);
            queue.Enqueue(coreCell);
        }
        FloodFillReachable(reachable, queue);

        // Hapus dari innerRoadCells semua yang tidak reachable
        var removeList = new List<Vector3Int>();
        foreach (var cell in innerRoadCells)
            if (!reachable.Contains(cell))
                removeList.Add(cell);

        int removed = removeList.Count;
        foreach (var cell in removeList)
            innerRoadCells.Remove(cell);

        // Bersihkan prefab tile yang dihapus dari gridHelper supaya visual konsisten
        if (gridHelper != null)
            foreach (var cell in removeList)
                gridHelper.RemoveRoadCell(cell);

        Debug.Log($"[Cleanup] RemoveDisconnectedInnerRoads: inner {before} → {innerRoadCells.Count} "
                + $"(dihapus {removed}, reachable dari jaringan inti {reachable.Count - ringCells.Count - spokeCells.Count})");
    }

    /// <summary>Hitung ulang bitmask N/E/S/W dari state terbaru (setelah cleanup).
    /// Statistik dihitung dari HasRoad() = ring ∪ inner, sehingga mencerminkan
    /// jalan yang benar-benar hidup (bukan hanya innerRoadCells).</summary>
    private void RecalculateAllMasks()
    {
        int c4 = 0, c3 = 0, cI = 0, cL = 0, cO = 0;
        foreach (var pos in HasRoadSnapshot())
        {
            switch (ClassifyTile(GetRoadMask(pos)))
            {
                case '+': c4++; break;
                case 'T': c3++; break;
                case 'I': cI++; break;
                case 'L': cL++; break;
                case 'O': cO++; break;
            }
        }
        Debug.Log($"[RecalcMasks] Road tiles (ring+inner): + {c4}  T {c3}  I {cI}  L {cL}  O {cO}");
    }

    /// <summary>
    /// Validasi simetri koneksi N/E/S/W — E(x,z)⇔W(x+1,z), N(x,z)⇔S(x,z+1).
    /// Logging per-endpoint untuk endpoint O interior.
    /// </summary>
    public void ValidateRoadConnections()
    {
        var snap = HasRoadSnapshot();
        int asym = 0;
        foreach (var pos in snap)
        {
            bool hasE = snap.Contains(pos + new Vector3Int( 1, 0, 0));
            bool hasW = snap.Contains(pos + new Vector3Int(-1, 0, 0));
            bool hasN = snap.Contains(pos + new Vector3Int( 0, 0, 1));
            bool hasS = snap.Contains(pos + new Vector3Int( 0, 0,-1));

            bool eastSym  = hasE == snap.Contains(pos + new Vector3Int( 1, 0, 0) + new Vector3Int(-1, 0, 0));
            bool westSym  = hasW == snap.Contains(pos + new Vector3Int(-1, 0, 0) + new Vector3Int( 1, 0, 0));
            bool northSym = hasN == snap.Contains(pos + new Vector3Int( 0, 0, 1) + new Vector3Int( 0, 0,-1));
            bool southSym = hasS == snap.Contains(pos + new Vector3Int( 0, 0,-1) + new Vector3Int( 0, 0, 1));

            if (!eastSym)  { asym++; Debug.LogWarning($"[Connectivity] E/W asimetris di {pos}"); }
            if (!westSym)  { asym++; Debug.LogWarning($"[Connectivity] W/E asimetris di {pos}"); }
            if (!northSym) { asym++; Debug.LogWarning($"[Connectivity] N/S asimetris di {pos}"); }
            if (!southSym) { asym++; Debug.LogWarning($"[Connectivity] S/N asimetris di {pos}"); }
        }
        Debug.Log($"[Connectivity] Validasi selesai — {asym} koneksi asimetris");

        // Log tiap endpoint interior yang tersisa (yang bukan ring)
        foreach (var pos in FindInnerRoadEndpoints())
        {
            int mask = GetRoadMask(pos);
            bool reachable = ValidateReachabilityFromRing(pos);
            var owners = new System.Text.StringBuilder();
            if (innerRoadCells.Contains(pos)) owners.Append("inner");
            if (ringCells.Contains(pos)) { if (owners.Length > 0) owners.Append("+"); owners.Append("ring"); }
            Debug.LogWarning($"[Endpoint] {pos} mask={mask} ({owners}) "
                           + $"reachable={reachable} neighbors={GetRoadNeighbors(pos).Count}");
        }
    }

    // =======================================================================
    // CONNECT DISCONNECTED TO CORE — jembatani pulau ke jaringan inti
    // =======================================================================

    /// <summary>
    /// Hubungkan setiap komponen jalan interior yang terisolasi ke jaringan inti
    /// (ring ∪ spoke) lewat koridor orthogonal terpendek yang kosong — jadi SEMUA
    /// jalan bisa ditelusuri sampai ke ring (tidak ada pulau terisolasi).
    /// Komponen yang tidak punya koridor kosong dibuang langsung (setara
    /// cleanup BFS) supaya loop lanjut ke komponen lain yang masih bisa
    /// disambung.
    /// </summary>
    private void ConnectDisconnectedToCore()
    {
        if (gridHelper == null) return;

        int connected = 0;
        int removed  = 0;

        // Loop sampai stabil: tiap iterasi satu komponen disambung (koridor
        // baru memperluas jaringan inti) atau dibuang — dua-duanya progres,
        // jadi dijamin berhenti. TIDAK pakai cap iterasi: cap 50 dulu membuat
        // kota besar (Huge, 100 seed) cuma menyambung 50 komponen pertama,
        // sisanya dihapus RemoveDisconnectedInnerRoads → separuh interior
        // hilang (void raksasa 1737 cell di sisi timur).
        while (true)
        {
            // BFS dari ring ∪ spoke — jangkauan jaringan inti saat ini
            var reachable = new HashSet<Vector3Int>();
            var queue = new Queue<Vector3Int>();
            foreach (var c in ringCells)  { if (reachable.Add(c)) queue.Enqueue(c); }
            foreach (var c in spokeCells) { if (reachable.Add(c)) queue.Enqueue(c); }
            FloodFillReachable(reachable, queue);

            // Cari satu cell interior yang belum reachable → ada komponen terisolasi
            Vector3Int? seedCell = null;
            foreach (var cell in innerRoadCells)
            {
                if (!reachable.Contains(cell)) { seedCell = cell; break; }
            }
            if (!seedCell.HasValue) break; // semua sudah terhubung ke ring

            // Kumpulkan komponen terisolasi (BFS lewat innerRoadCells saja)
            var comp = new HashSet<Vector3Int> { seedCell.Value };
            var q = new Queue<Vector3Int>();
            q.Enqueue(seedCell.Value);
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                foreach (var nb in GetRoadNeighbors(cur))
                {
                    if (!reachable.Contains(nb) && innerRoadCells.Contains(nb) && comp.Add(nb))
                        q.Enqueue(nb);
                }
            }

            // Cari sambungan terbaik: (p ∈ komponen, q ∈ reachable) dengan jarak
            // Manhattan terkecil yang masih punya koridor kosong (lurus / L).
            var snap = gridHelper.GridSnapshot;
            Vector3Int bestP = default, bestQ = default;
            int bestDist = int.MaxValue;
            List<Vector3Int> bestPath = null;

            foreach (var p in comp)
            {
                foreach (var q2 in reachable)
                {
                    int d = Mathf.Abs(p.x - q2.x) + Mathf.Abs(p.z - q2.z);
                    if (d >= bestDist) continue;
                    if (TryGetConnectorPath(p, q2, snap, out var path))
                    {
                        bestP = p; bestQ = q2; bestDist = d; bestPath = path;
                    }
                }
            }

            if (bestPath != null)
            {
                PlaceConnectorPath(bestP, bestQ, bestPath);
                connected++;
            }
            else
            {
                // Tidak ada koridor kosong — komponen ini tidak bisa disambung.
                // Buang langsung (sama seperti cleanup BFS) supaya loop bisa
                // melanjutkan ke komponen LAIN yang masih bisa disambung —
                // bukan break (yang menghentikan semua koneksi berikutnya
                // dan membuat separuh kota dihapus cleanup berikutnya).
                foreach (var cell in comp)
                {
                    if (innerRoadCells.Remove(cell))
                    {
                        gridHelper.RemoveRoadCell(cell);
                        removed++;
                    }
                }
            }
        }

        Debug.Log($"[RoadNetwork] ConnectDisconnectedToCore: {connected} komponen disambung ke "
                + $"jaringan inti, {removed} cell dibuang (komponen tanpa koridor kosong)");
    }

    // =======================================================================
    // CONNECT NEARBY ENDS — sambungkan dead-end interior yang berdekatan.
    // O→O membentuk siklus → blok tertutup muncul, dead-end (O) berkurang.
    // =======================================================================

    /// <summary>
    /// Untuk tiap endpoint interior (mask 1 lengan), cari endpoint lain
    /// terdekat (≤ connectEndsMaxDistance cell) yang bisa disambung dengan
    /// path orthogonal kosong (lurus atau L). Place jalan penghubung.
    /// Setiap endpoint hanya dipakai sekali (greedy nearest).
    /// </summary>
    private void ConnectNearbyEnds()
    {
        if (gridHelper == null) return;

        var endpoints = FindInnerRoadEndpoints();
        if (endpoints.Count < 2)
        {
            Debug.Log($"[RoadNetwork] ConnectNearbyEnds: skip — endpoint interior hanya {endpoints.Count} (field={connectNearbyEnds})");
            return;
        }

        var snap = gridHelper.GridSnapshot;
        var used = new HashSet<Vector3Int>();
        int connected = 0;

        foreach (var a in endpoints)
        {
            if (used.Contains(a)) continue;
            // Endpoint bisa berubah status setelah koneksi sebelumnya
            if (ArmCount(GetRoadMask(a)) != 1) continue;

            Vector3Int? best = null;
            int bestDist = int.MaxValue;
            List<Vector3Int> bestPath = null;

            foreach (var b in endpoints)
            {
                if (b == a || used.Contains(b)) continue;
                int dist = Mathf.Abs(b.x - a.x) + Mathf.Abs(b.z - a.z);
                if (dist > connectEndsMaxDistance || dist >= bestDist) continue;
                if (TryGetConnectorPath(a, b, snap, out var path))
                {
                    best = b;
                    bestDist = dist;
                    bestPath = path;
                }
            }

            if (best.HasValue && bestPath != null)
            {
                PlaceConnectorPath(a, best.Value, bestPath);
                used.Add(a);
                used.Add(best.Value);
                connected++;
            }
        }

        Debug.Log($"[RoadNetwork] ConnectNearbyEnds: {connected} pasang endpoint disambung "
                + $"(endpoint awal {endpoints.Count}, max dist {connectEndsMaxDistance})");
    }

    /// <summary>
    /// Path orthogonal kosong antara a dan b (cell perantara, eksklusif a & b):
    /// lurus jika sebaris/sekola, atau L (coba 2 sudut). Semua cell di path
    /// harus kosong dan di dalam interior ring. Return null jika tidak ada
    /// path yang bersih.
    /// </summary>
    private bool TryGetConnectorPath(Vector3Int a, Vector3Int b,
                                     HashSet<Vector3Int> snap, out List<Vector3Int> path)
    {
        path = null;
        if (a == b) return false;

        if (a.x == b.x || a.z == b.z)
        {
            path = BuildStraightPath(a, b, snap);
            return path != null;
        }

        // L-shaped: coba dua kemungkinan sudut.
        // PENTING: cell SUDUT harus ikut di-place (dan dicek kosong) — jika
        // tidak, kedua kaki putus di sudut dan tiap koneksi L justru membuat
        // 2 dead-end baru (O tidak berkurang).
        var corner1 = new Vector3Int(b.x, 0, a.z);
        var leg1a = BuildStraightPath(a, corner1, snap);
        if (leg1a != null && !snap.Contains(corner1) && IsInsideRingInterior(corner1))
        {
            var leg1b = BuildStraightPath(corner1, b, snap);
            if (leg1b != null)
            {
                path = leg1a;
                path.Add(corner1);
                path.AddRange(leg1b);
                return true;
            }
        }

        var corner2 = new Vector3Int(a.x, 0, b.z);
        var leg2a = BuildStraightPath(a, corner2, snap);
        if (leg2a != null && !snap.Contains(corner2) && IsInsideRingInterior(corner2))
        {
            var leg2b = BuildStraightPath(corner2, b, snap);
            if (leg2b != null)
            {
                path = leg2a;
                path.Add(corner2);
                path.AddRange(leg2b);
                return true;
            }
        }
        return false;
    }

    /// <summary>Cell perantara dari..to (eksklusif ujung) yang kosong & interior.</summary>
    private List<Vector3Int> BuildStraightPath(Vector3Int from, Vector3Int to,
                                               HashSet<Vector3Int> snap)
    {
        int dx = to.x - from.x, dz = to.z - from.z;
        if (dx != 0 && dz != 0) return null; // diagonal — bukan jalur orthogonal

        int steps = Mathf.Abs(dx) + Mathf.Abs(dz);
        if (steps == 0) return null;

        var dir = new Vector3Int(Mathf.Clamp(dx, -1, 1), 0, Mathf.Clamp(dz, -1, 1));
        var result = new List<Vector3Int>();
        var cur = from;
        for (int i = 0; i < steps; i++)
        {
            cur += dir;
            if (cur == to) break; // ujung tujuan — tidak dihitung
            if (snap.Contains(cur)) return null;         // ada jalan lama di path
            if (!IsInsideRingInterior(cur)) return null; // keluar interior / kena ring
            result.Add(cur);
        }
        return result;
    }

    /// <summary>
    /// Place cell penghubung (path) sebagai jalan interior + RoadSegment
    /// per ruas lurus (path bisa lurus atau L = 2 ruas, sudut dipakai dua ruas).
    /// </summary>
    private void PlaceConnectorPath(Vector3Int a, Vector3Int b, List<Vector3Int> path)
    {
        foreach (var cell in path)
        {
            gridHelper.PlaceStreetPositions(cell, new Vector3Int(1, 0, 0), 1);
            innerRoadCells.Add(cell);
        }

        // Polyline a → path → b, lalu pecah jadi ruas-ruas lurus
        var poly = new List<Vector3Int> { a };
        poly.AddRange(path);
        poly.Add(b);

        int runStart = 0;
        Vector3Int? prevDir = null;
        for (int i = 1; i < poly.Count; i++)
        {
            var d = poly[i] - poly[i - 1];
            if (prevDir.HasValue && d != prevDir.Value)
            {
                AddRoadSegmentRun(poly, runStart, i - 1);
                runStart = i - 1; // cell sudut dipakai kedua ruas
            }
            prevDir = d;
        }
        AddRoadSegmentRun(poly, runStart, poly.Count - 1);
    }

    /// <summary>Tambah satu RoadSegment dari poly[fromIdx] ke poly[toIdx] (ruas lurus).</summary>
    private void AddRoadSegmentRun(List<Vector3Int> poly, int fromIdx, int toIdx)
    {
        var s = poly[fromIdx];
        var e = poly[toIdx];
        roads.Add(new RoadSegment(
            new Vector3(s.x * cellSize, 0, s.z * cellSize),
            new Vector3(e.x * cellSize, 0, e.z * cellSize),
            roadWidth));
    }

    /// <summary>Snapshot gabungan ring ∪ inner — kebenaran jalan (mirip HasRoad).</summary>
    private HashSet<Vector3Int> HasRoadSnapshot()
    {
        var set = new HashSet<Vector3Int>(ringCells);
        set.UnionWith(innerRoadCells);
        return set;
    }

    private static readonly Vector3Int[] Dirs =
    {
        new Vector3Int( 0, 0,  1), // N
        new Vector3Int( 1, 0,  0), // E
        new Vector3Int( 0, 0, -1), // S
        new Vector3Int(-1, 0,  0), // W
    };

    /// <summary>Clamp posisi ke batas kota.</summary>
    private static Vector3 ClampToBoundsStatic(Vector3 pos, Vector3 origin, float halfSize, float margin)
    {
        float m = margin * 0.5f;
        return new Vector3(
            Mathf.Clamp(pos.x, origin.x - halfSize + m, origin.x + halfSize - m),
            0f,
            Mathf.Clamp(pos.z, origin.z - halfSize + m, origin.z + halfSize - m));
    }

    // =======================================================================
    // FINALIZE: FixRoad + RebuildBlocks + ComputeIntersections
    // Dipanggil SEKALI setelah semua place selesai — berlaku untuk semua mode.
    // FixRoad() klasifikasi tetangga tiap cell → swap prefab straight ke
    // corner/3way/4way/end yang tepat (SVS RoadHelper style).
    // =======================================================================
    private void FinalizeRoads()
    {
        // Snapshot semua cell
        gridPositions.Clear();
        foreach (var pos in gridHelper.roadDictionary.Keys)
            gridPositions.Add(pos);

        // Sync ring cells ke gridHelper — FixRoad butuh tahu mana ring cell
        // untuk klasifikasi T/+ di ring (bukan O).
        gridHelper.ringCells.Clear();
        gridHelper.ringCells.UnionWith(ringCells);

        // SATU FixRoad() untuk semua mode — klasifikasi SETELAH cleanup selesai.
        // (RemoveDisconnectedInnerRoads sudah dijalankan di dispatch RingAndLSystem.)
        gridHelper.FixRoad();

        // Validasi simetri koneksi N/E/S/W setelah semua jalan selesai
        int invalid = ValidateConnectivity();

        // Log ring + interior + tile stats setelah FixRoad
        LogRoadStats(invalid);

        // Export peta ASCII ke file txt untuk debugging visual
        Debug.Log($"[RoadNetwork] ExportRoadMap: GridSnapshot.Count={gridHelper.GridSnapshot.Count}, roadDictionary.Count={gridHelper.roadDictionary.Count}");
        ExportRoadMap();

        // Rebuild blocks dari grid cells (satu-satunya pipeline: RingAndLSystem)
        RebuildBlocksFromCells();

        ComputeIntersections();

        Debug.Log($"[RoadNetwork] RingAndLSystem: {roads.Count} roads, "
                + $"{junctions.Count} junctions, {blocks.Count} blocks, "
                + $"{gridPositions.Count} cells");
    }

    // =======================================================================
    // EXPORT ROAD MAP — ASCII visual untuk debugging
    // =======================================================================

    /// <summary>
    /// Export peta jaringan jalan sebagai file txt di Assets/RoadMapLogs/.
    /// Simbol:
    ///   +  = perempatan (4-way)
    ///   T  = pertigaan (3-way, salah satu dari T_N/S/E/W)
    ///   I  = jalan lurus (straight H atau V)
    ///   L  = tikungan (corner)
    ///   O  = ujung buntu (end)
    ///   .  = ada jalan (fallback)
    ///   #  = tidak ada jalan (empty)
    /// Grid ditampilkan dari Z+ (atas) ke Z- (bawah), X- (kiri) ke X+ (kanan).
    /// </summary>
    private void ExportRoadMap()
    {
        var snap = gridHelper.GridSnapshot;
        if (snap.Count == 0) return;

        // cellSet = gabungan ring + innerRoadCells (logical graph).
        // Bukan hanya map tile — cell yang dihapus oleh cleanup tidak ikut.
        var cellSet = new HashSet<Vector3Int>(snap);
        cellSet.UnionWith(innerRoadCells);
        if (cellSet.Count == 0) return;

        // Cari bounding box cell
        int minX = int.MaxValue, maxX = int.MinValue;
        int minZ = int.MaxValue, maxZ = int.MinValue;
        foreach (var pos in cellSet)
        {
            if (pos.x < minX) minX = pos.x;
            if (pos.x > maxX) maxX = pos.x;
            if (pos.z < minZ) minZ = pos.z;
            if (pos.z > maxZ) maxZ = pos.z;
        }

        int cols = maxX - minX + 1;
        int rows = maxZ - minZ + 1;

        var sb = new System.Text.StringBuilder();

        // Header info
        sb.AppendLine($"Road Map — {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Mode: RingAndLSystem  |  citySize: {cityGenerator.citySize}  |  tileScale: {roadTileScale.x}");
        sb.AppendLine($"Grid: {cols} x {rows} cells  |  Total road tiles: {cellSet.Count}");
        sb.AppendLine($"Cell range: X[{minX}..{maxX}]  Z[{minZ}..{maxZ}]");
        if (secondLayerSummary.Length > 0)
            sb.AppendLine(secondLayerSummary);
        sb.AppendLine();

        // Legenda
        sb.AppendLine("Legenda: + = 4-way  T = 3-way  I = straight  L = corner  O = end  # = empty");
        sb.AppendLine(new string('-', Mathf.Min(cols + 2, 120)));

        // Batas kiri-kanan (kolom header setiap 10 cell)
        if (cols <= 200)
        {
            sb.Append("  ");
            for (int x = 0; x < cols; x++)
                sb.Append((x % 10 == 0) ? "|" : " ");
            sb.AppendLine();
        }

        // Render grid — Z+ di atas, Z- di bawah
        for (int row = rows - 1; row >= 0; row--)
        {
            int z = minZ + row;

            // Row number setiap 10 row
            if (rows <= 200)
                sb.Append((row % 10 == 0) ? $"{z,2}" : "  ");
            else
                sb.Append("  ");

            for (int col = 0; col < cols; col++)
            {
                int x = minX + col;
                var pos = new Vector3Int(x, 0, z);

                if (!cellSet.Contains(pos))
                {
                    sb.Append('#');
                    continue;
                }

                // Klasifikasi mask N/E/S/W — satu aturan untuk ring & interior.
                // Ring-only harus menghasilkan L=4 (sudut) + I (lurus), tanpa O/T/+.
                char c = ClassifyTile(GetRoadMask(pos));
                if (c == '#') c = ringCells.Contains(pos) ? 'I' : '.';
                sb.Append(c);
            }
            sb.AppendLine();
        }

        sb.AppendLine(new string('-', Mathf.Min(cols + 2, 120)));

        // Statistik — pakai mask yang sama dengan render (ring ikut dihitung)
        int count4way = 0, count3way = 0, countStraight = 0, countCorner = 0, countEnd = 0;
        foreach (var pos in cellSet)
        {
            switch (ClassifyTile(GetRoadMask(pos)))
            {
                case '+': count4way++;    break;
                case 'T': count3way++;    break;
                case 'I': countStraight++; break;
                case 'L': countCorner++;  break;
                case 'O': countEnd++;     break;
            }
        }
        sb.AppendLine($"Stats: + {count4way}  T {count3way}  I {countStraight}  L {countCorner}  O {countEnd}");

        // Tulis ke file
        string dir  = System.IO.Path.Combine(UnityEngine.Application.dataPath, "RoadMapLogs");
        System.IO.Directory.CreateDirectory(dir);
        string file = System.IO.Path.Combine(dir,
            $"RoadMap_RingAndLSystem_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt");
        System.IO.File.WriteAllText(file, sb.ToString());

#if UNITY_EDITOR
        // File ditulis langsung ke disk (bukan lewat ImportAsset) — Unity tidak
        // otomatis memunculkannya di Project window sampai AssetDatabase di-refresh.
        UnityEditor.AssetDatabase.Refresh();
#endif

        Debug.Log($"[RoadNetwork] Road map exported → {file}");
    }
    /// Mengadaptasi pattern dari SVS LSystemGenerator (rootSentence + rules + iterationLimit).
    /// </summary>
    private LSystemGenerator BuildLSystemFromPreset()
    {
        var lsys = new LSystemGenerator();
        lsys.iterations     = lSystemIterations;
        lsys.chanceToIgnore = lSystemChanceToIgnore;

        switch (lSystemPreset)
        {
            // -----------------------------------------------------------------
            // OrganicCity — dari SVS SimpleVisualizer default
            // Axiom: X → F[-FX]+FX (percabangan 45° → dikuantisasi ke 90°)
            // Menghasilkan jalan bercabang organik dengan dead-end
            // -----------------------------------------------------------------
            case LSystemPreset.OrganicCity:
                lsys.axiom = "X";
                lsys.rules = new LSystemGenerator.Rule[]
                {
                    new LSystemGenerator.Rule { input = 'X', output = "F[-FX]+FX",     chance = 1.0f },
                };
                break;

            // -----------------------------------------------------------------
            // ManhattanGrid — grid orthogonal rapat ala NYC
            // Axiom: FX → F[+F]F[-F]FX (bercabang kanan-kiri di tiap step)
            // Menghasilkan grid teratur dengan variasi jitter minimal
            // -----------------------------------------------------------------
            case LSystemPreset.ManhattanGrid:
                lsys.axiom = "FX";
                lsys.chanceToIgnore = 0.1f; // sedikit ignore → grid lebih rapi
                lsys.rules = new LSystemGenerator.Rule[]
                {
                    new LSystemGenerator.Rule { input = 'X', output = "[+FX][-FX]FX", chance = 1.0f },
                    new LSystemGenerator.Rule { input = 'F', output = "FF",            chance = 0.3f },
                };
                break;

            // -----------------------------------------------------------------
            // HighwayAndAlley — arterial road + gang sempit
            // Axiom: FFF[+FF]FFF[-FF]FX (jalan panjang lurus, cabang pendek di sisi)
            // Step panjang untuk main road, cabang pendek = gang/alley
            // -----------------------------------------------------------------
            case LSystemPreset.HighwayAndAlley:
                lsys.axiom = "FFFX";
                lsys.chanceToIgnore = 0.2f;
                lsys.rules = new LSystemGenerator.Rule[]
                {
                    new LSystemGenerator.Rule { input = 'X', output = "FFF[+FX][-FX]X", chance = 1.0f },
                    new LSystemGenerator.Rule { input = 'F', output = "FF",              chance = 0.15f },
                };
                break;

            // -----------------------------------------------------------------
            // RadialSprawl — jalan memancar dari pusat seperti kota Eropa
            // 4 arah utama + diagonal dikuantisasi, menghasilkan pola bintang
            // -----------------------------------------------------------------
            case LSystemPreset.RadialSprawl:
                lsys.axiom = "X";
                lsys.chanceToIgnore = 0.25f;
                lsys.rules = new LSystemGenerator.Rule[]
                {
                    new LSystemGenerator.Rule { input = 'X', output = "F[+FX]F[-FX]F[+FX]X", chance = 1.0f },
                    new LSystemGenerator.Rule { input = 'F', output = "FF",                    chance = 0.2f },
                };
                break;

            // -----------------------------------------------------------------
            // Custom — pakai axiom/rules dari Inspector field
            // -----------------------------------------------------------------
            case LSystemPreset.Custom:
            default:
                lsys.axiom = lSystemCustomAxiom;
                var customRules = new List<LSystemGenerator.Rule>();
                if (lSystemCustomRules != null)
                {
                    foreach (var ruleStr in lSystemCustomRules)
                    {
                        // Parse format "X=F[-FX]+FX"
                        if (string.IsNullOrEmpty(ruleStr)) continue;
                        int eqIdx = ruleStr.IndexOf('=');
                        if (eqIdx < 1 || eqIdx >= ruleStr.Length - 1) continue;
                        char   inp = ruleStr[0];
                        string outp = ruleStr.Substring(eqIdx + 1);
                        customRules.Add(new LSystemGenerator.Rule
                            { input = inp, output = outp, chance = 1.0f });
                    }
                }
                lsys.rules = customRules.ToArray();
                break;
        }

        return lsys;
    }

    /// <summary>
    /// Rebuild CityBlock list langsung dari grid cells yang terisi.
    /// Scan semua cell, cari rectangular region kosong di antara jalan.
    /// </summary>
    private void RebuildBlocksFromCells()
    {
        blocks.Clear();
        if (gridPositions.Count == 0) return;

        var cellSet = new HashSet<Vector3Int>(gridPositions);

        // Kumpulkan unique X dan Z dari semua road cells
        var xs = new SortedSet<int>();
        var zs = new SortedSet<int>();
        foreach (var c in gridPositions) { xs.Add(c.x); zs.Add(c.z); }

        var xList = new List<int>(xs);
        var zList = new List<int>(zs);

        // Cari rectangular gap di antara road cells sebagai blok
        for (int xi = 0; xi < xList.Count - 1; xi++)
        {
            for (int zi = 0; zi < zList.Count - 1; zi++)
            {
                int x0 = xList[xi];
                int x1 = xList[xi + 1];
                int z0 = zList[zi];
                int z1 = zList[zi + 1];

                // Blok minimal: ada jalan di keempat sisi
                bool hasRoadW = cellSet.Contains(new Vector3Int(x0, 0, z0));
                bool hasRoadE = cellSet.Contains(new Vector3Int(x1, 0, z0));
                bool hasRoadS = cellSet.Contains(new Vector3Int(x0, 0, z1));

                if (!hasRoadW || !hasRoadE || !hasRoadS) continue;

                float wx0 = CellToWorld(x0);
                float wx1 = CellToWorld(x1);
                float wz0 = CellToWorld(z0);
                float wz1 = CellToWorld(z1);

                float bw = Mathf.Abs(wx1 - wx0) - roadWidth;
                float bh = Mathf.Abs(wz1 - wz0) - roadWidth;

                if (bw < MIN_BLOCK_DIM || bh < MIN_BLOCK_DIM) continue;

                var block = new CityBlock
                {
                    center = new Vector3((wx0 + wx1) * 0.5f, 0f, (wz0 + wz1) * 0.5f),
                    size   = new Vector2(bw, bh)
                };
                blocks.Add(block);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>World unit → cell integer. Pakai tileWorldSize bukan cellSize.</summary>
    private int WorldToCell(float worldPos) =>
        Mathf.RoundToInt(worldPos / (gridHelper != null ? gridHelper.tileWorldSize : 1f));

    /// <summary>World Vector3 → cell Vector3Int.</summary>
    private Vector3Int WorldToCell3(Vector3 worldPos) =>
        new Vector3Int(WorldToCell(worldPos.x), 0, WorldToCell(worldPos.z));

    /// <summary>Cell integer → world center of cell.</summary>
    private float CellToWorld(int cell) => cell * (gridHelper != null ? gridHelper.tileWorldSize : 1f);

    // =======================================================================
    // JUNCTIONS
    // =======================================================================
    public enum JunctionType { Cross, T_North, T_South, T_East, T_West, Corner, Straight }

    public struct JunctionInfo
    {
        public Vector3      position;
        public JunctionType type;
        public float        width;
        public bool         hasN, hasS, hasE, hasW;
    }

    private void ComputeIntersections()
    {
        intersections.Clear();
        junctions.Clear();
        var snap = gridHelper.GridSnapshot;

        // Tiap cell jalan dengan mask bukan straight/empty = junction.
        // (Satu-satunya pipeline: RingAndLSystem — hitung dari cell, bukan hLines/vLines.)
        foreach (var pos in snap)
        {
            int  mask = GetRoadMask(pos);
            char cls  = ClassifyTile(mask);
            if (cls == 'I' || cls == '#') continue;

            var pt = new Vector3(CellToWorld(pos.x), 0f, CellToWorld(pos.z));
            intersections.Add(pt);

            bool hasN = (mask & 1) != 0;
            bool hasE = (mask & 2) != 0;
            bool hasS = (mask & 4) != 0;
            bool hasW = (mask & 8) != 0;
            int  arms = (hasN ? 1 : 0) + (hasE ? 1 : 0) + (hasS ? 1 : 0) + (hasW ? 1 : 0);

            JunctionType jt;
            if      (arms >= 4)    jt = JunctionType.Cross;
            else if (arms == 3)
            {
                if      (!hasN) jt = JunctionType.T_North;
                else if (!hasS) jt = JunctionType.T_South;
                else if (!hasE) jt = JunctionType.T_East;
                else            jt = JunctionType.T_West;
            }
            else if (arms == 2 && ((hasN || hasS) && (hasE || hasW)))
                jt = JunctionType.Corner;
            else
                jt = JunctionType.Straight;

            junctions.Add(new JunctionInfo
            {
                position = pt,
                type     = jt,
                width    = roadWidth,
                hasN = hasN, hasS = hasS, hasE = hasE, hasW = hasW
            });
        }
    }

    // =======================================================================
    // CLEAR / RESET
    // =======================================================================
    public void ClearRoads()
    {
        roads.Clear();
        intersections.Clear();
        blocks.Clear();
        junctions.Clear();
        gridPositions.Clear();
        ringCells.Clear();
        innerRoadCells.Clear();
        spokeCells.Clear();
        ringEntrances.Clear();
        spokeAnchors.Clear();

        if (gridHelper != null)
        {
            gridHelper.Reset();
            gridHelper = null;
        }
    }

    // =======================================================================
    // GIZMOS
    // =======================================================================
    public void DrawGizmos()
    {
        if (cityGenerator == null) return;

        Gizmos.color = Color.white;
        foreach (var r in roads)
            if (r.path != null)
                for (int i = 0; i < r.path.Count - 1; i++)
                    Gizmos.DrawLine(r.path[i], r.path[i + 1]);

        Gizmos.color = Color.red;
        foreach (var j in junctions)
            Gizmos.DrawSphere(j.position, cityGenerator.roadWidth * 0.3f);

        Gizmos.color = Color.cyan;
        foreach (var b in blocks)
            Gizmos.DrawWireCube(b.center, new Vector3(b.size.x, 0.1f, b.size.y));
    }
}

// ===========================================================================
// ENUMS
// ===========================================================================

/// <summary>
/// Preset L-System untuk berbagai karakter kota.
/// Terinspirasi dari SVS Procedural Town example (LSystemGenerator + SimpleVisualizer).
/// </summary>
public enum LSystemPreset
{
    OrganicCity,    // Jalan bercabang organik — default SVS pattern X→F[-FX]+FX
    ManhattanGrid,  // Grid rapat orthogonal ala NYC
    HighwayAndAlley,// Arterial road panjang + gang pendek di sisi
    RadialSprawl,   // Jalan memancar dari pusat seperti kota Eropa
    Custom          // Pakai axiom/rules dari Inspector field
}

// ===========================================================================
// DATA STRUCTS
// ===========================================================================
[System.Serializable]
public struct RoadSegment
{
    public Vector3       start;
    public Vector3       end;
    public float         width;
    public List<Vector3> path;
    public int           hierarchy;

    public RoadSegment(Vector3 start, Vector3 end, float width)
    {
        this.start     = start;
        this.end       = end;
        this.width     = width;
        this.path      = new List<Vector3> { start, end };
        this.hierarchy = 0;
    }

    public RoadSegment(List<Vector3> path, float width, int hierarchy)
    {
        this.start     = path[0];
        this.end       = path[path.Count - 1];
        this.width     = width;
        this.path      = path;
        this.hierarchy = hierarchy;
    }
}
