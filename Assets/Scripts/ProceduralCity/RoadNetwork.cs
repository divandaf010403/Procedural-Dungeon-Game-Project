using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RoadNetwork — dual-mode road generation.
///
/// MODE: OrthogonalGrid (default)
///   Pipeline klasik: H-lines × V-lines → mesh terpadu.
///
/// MODE: LSystem
///   L-System turtle graphics menghasilkan segmen jalan organik.
///   Setiap segmen di-snap ke grid cell → FixRoad() bangun mesh.
///   Bisa di-overlay dengan grid dasar (useLSystemOverGrid = true).
///
/// MODE: Hybrid
///   Grid dasar + L-System overlay untuk jalan sekunder/gang.
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
    // Road generation mode
    // -----------------------------------------------------------------------
    public enum RoadGenerationMode
    {
        OrthogonalGrid, // Grid H×V klasik (default)
        LSystem,        // L-System turtle saja
        RingAndLSystem  // Ring road mengelilingi kota + L-System isi interior
    }

    // -----------------------------------------------------------------------
    // Public lists (consumed by pipeline)
    // -----------------------------------------------------------------------
    public List<RoadSegment> roads           = new List<RoadSegment>();
    public List<RoadSegment> horizontalRoads = new List<RoadSegment>();
    public List<RoadSegment> verticalRoads   = new List<RoadSegment>();

    public List<Vector3>     intersections = new List<Vector3>();
    public List<CityBlock>   blocks        = new List<CityBlock>();
    public List<JunctionInfo> junctions    = new List<JunctionInfo>();

    // -----------------------------------------------------------------------
    // Mode selector
    // -----------------------------------------------------------------------
    [Header("Road Generation Mode")]
    [Tooltip("OrthogonalGrid = grid klasik H×V.\nLSystem = jalan organik dari L-System saja.\nRingAndLSystem = ring road mengelilingi kota + L-System isi interior.")]
    public RoadGenerationMode generationMode = RoadGenerationMode.OrthogonalGrid;

    // -----------------------------------------------------------------------
    // Grid settings
    // -----------------------------------------------------------------------
    [Header("Grid Road Settings")]
    [Range(0f, 0.4f)]
    [Tooltip("Jitter maksimal per-garis (fraksi dari block interval). 0 = grid sempurna.")]
    public float jitterFraction = 0.15f;

    [Tooltip("Tambah extra jalan di tengah tiap blok (membagi blok jadi 2). Menambah kepadatan.")]
    public bool addMidStreets = false;

    [Range(0f, 1f)]
    [Tooltip("Probabilitas branch per '[' L-System. 0.15 = jarang bercabang → lebih banyak blok kosong.")]
    public float lSystemBranchChance = 0.15f;

    [Tooltip("Jarak minimal antar jalan dalam cell. 2 = jalan baru minimal 2 cell dari jalan lama.")]
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

    [Range(0f, 1f)]
    [Tooltip("Probabilitas skip rule per-karakter (variasi organik). Dari SVS RoadHelper pattern.")]
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
    [Tooltip("Prefab traffic light (komponen TrafficLightBehavior). Dipasang di junction + dan T, serta di dekat seed L-System.")]
    public GameObject[] trafficLightPrefabs;
    [Tooltip("Prefab streetlight (komponen StreetlightBehavior). Dipasang di sepanjang ring/spoke/interior dengan interval tetap.")]
    public GameObject[] streetlightPrefabs;
    [Tooltip("Tempatkan perlengkapan jalan (traffic light & streetlight) setelah FixRoad.")]
    public bool enableRoadAddOns = true;

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

    [Tooltip("Jika true, jalan interior boleh berakhir di tengah kota (dead-end O).\nJika false, segmen yang tidak mencapai ring dihentikan sebelum endpoint terisolasi dibuat.")]
    public bool allowRoadsToEndInside = false;

    [Tooltip("Hapus semua jalan interior yang TIDAK dapat dicapai dari ring (BFS dari ringCells). Prioritaskan penghapusan, bukan koneksi acak, agar endpoint O berkurang.")]
    public bool removeDisconnectedRoads = true;

    [Tooltip("Reserved — sambungkan dua endpoint yang berdekatan. Belum diimplementasikan; gunakan removal dulu.")]
    public bool connectNearbyEnds = false;

    // -----------------------------------------------------------------------
    // Private state
    // -----------------------------------------------------------------------
    private CityGenerator   cityGenerator;
    private System.Random   rng;

    private float   halfSize;
    private float   roadWidth;
    private float   cellSize;
    private float   blockSpacing; // dihitung otomatis dari citySize/tileSize

    private RoadGridHelper gridHelper;

    private RoadAddOnDecorator roadAddOnDecorator;

    private List<Vector3Int> gridPositions = new List<Vector3Int>();
    private List<float>      hLines        = new List<float>();   // Z world coords
    private List<float>      vLines        = new List<float>();   // X world coords

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
    // Jalan interior yang menyambung ke ring (entrance) — dibatasi jumlahnya
    private readonly HashSet<Vector3Int> ringEntrances = new HashSet<Vector3Int>();

    // Helper ring
    public bool IsRingCell(Vector3Int c) => ringCells.Contains(c);
    public bool IsRingCell(int x, int z) => ringCells.Contains(new Vector3Int(x, 0, z));
    public int  RingCellCount           => ringCells.Count;
    public int  InnerRoadCellCount      => innerRoadCells.Count;

    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------
    private const float EPS           = 0.05f;
    private const float MIN_BLOCK_DIM = 20f;

    private const float SEC_W  = 1.0f;

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
        float effectiveLsStep  = lSystemStepSize > 0f
            ? lSystemStepSize
            : Mathf.Clamp(tilesPerBlock / 3, 2, 5) * tileSize;
        float effectiveSpacing = blockSpacing;

        Debug.Log($"[RoadNetwork] citySize={cityGenerator.citySize}, tileSize={tileSize}, "
                + $"tilesPerSide={tilesPerSide}, tilesPerBlock={tilesPerBlock}, "
                + $"blockSpacing={blockSpacing}wu, lsStep={effectiveLsStep}wu");

        // Parent container untuk semua tile prefab
        var roadContainer = new GameObject("RoadTiles");
        roadContainer.transform.SetParent(cityGenerator.transform);
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
        roadAddOnDecorator = new RoadAddOnDecorator(gridHelper, roadContainer.transform, tileSize);
        roadAddOnDecorator.SetTrafficLightPrefabs(trafficLightPrefabs);
        roadAddOnDecorator.SetStreetlightPrefabs(streetlightPrefabs);

        // Dispatch ke mode yang dipilih.
        var hWorldZ = new List<float>();
        var vWorldX = new List<float>();

        switch (generationMode)
        {
            case RoadGenerationMode.LSystem:
                // Mode tanpa ring — grid logical penuh (tanpa inset)
                tilesPerSide = Mathf.FloorToInt(cityGenerator.citySize / cellSize);
                gridMin = -(tilesPerSide / 2);
                gridMax =  (tilesPerSide / 2);
                ringMinX = ringMinZ = gridMin + 1;
                ringMaxX = ringMaxZ = gridMax - 1;
                PlaceLSystemTiles(effectiveLsStep);
                break;

            case RoadGenerationMode.RingAndLSystem:
                GenerateRingRoad();
                if (enableInteriorRoads)
                {
                    PlaceSpokesToRing(transform.position);   // 3. jalan utama/radial
                    PlaceLSystemTiles(effectiveLsStep);      // 4. L-System interior
                }
                else
                {
                    Debug.Log($"[RoadNetwork] RING-ONLY test — interior disabled. Ring cells: {ringCells.Count}");
                }

                // 6. Hapus jalan interior yang tidak tercapai dari ring (BFS).
                //    DILUAR if(enableInteriorRoads): spokes harus selalu selamat
                //    meskipun L-System tidak menghasilkan apa-apa. Hanya pohon
                //    L-System terisolasi yang boleh dihapus.
                if (enableInteriorRoads && removeDisconnectedRoads)
                    RemoveDisconnectedInnerRoads();

                // 7. Hitung ulang semua mask N/E/S/W setelah cleanup
                RecalculateAllMasks();

                // 8. Validasi simetri koneksi N/E/S/W
                ValidateRoadConnections();
                break;

            default: // OrthogonalGrid
                PlaceGridTiles(effectiveSpacing, hWorldZ, vWorldX);
                break;
        }

        // ---- Finalize: FixRoad + Blocks + Junctions — satu kali untuk semua mode ----
        FinalizeRoads(hWorldZ, vWorldX);

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

        // 1. Traffic light di junction + dan T
        roadAddOnDecorator.PlaceTrafficLights(snap);

        // 2. Traffic light tambahan di sekitar seed L-System (interior)
        roadAddOnDecorator.PlaceTrafficLightsAtSeeds(seedPositions, snap);

        // 3. Streetlight di sepanjang ring, spoke, dan interior
        roadAddOnDecorator.PlaceStreetlights(snap);

        Debug.Log($"[RoadNetwork] Add-ons selesai — traffic lights: "
                + $"{roadAddOnDecorator.HasTrafficLights}, streetlights: "
                + $"{roadAddOnDecorator.HasStreetlights}");
    }

    // =======================================================================
    // PLACE: GRID TILES
    // Hanya place tile ke gridHelper — tidak ada FixRoad/Blocks/Junctions.
    // Mengisi hWorldZ dan vWorldX yang dibutuhkan FinalizeRoads().
    // =======================================================================
    private void PlaceGridTiles(float spacing, List<float> hWorldZ, List<float> vWorldX)
    {
        float maxJitter = spacing * jitterFraction;

        hWorldZ.Add(-halfSize);
        hWorldZ.Add( halfSize);
        vWorldX.Add(-halfSize);
        vWorldX.Add( halfSize);

        for (float z = -halfSize + spacing; z < halfSize - spacing * 0.5f; z += spacing)
        {
            float jitter = (float)(rng.NextDouble() * 2.0 - 1.0) * maxJitter;
            hWorldZ.Add(Mathf.Clamp(z + jitter, -halfSize + roadWidth, halfSize - roadWidth));
        }
        for (float x = -halfSize + spacing; x < halfSize - spacing * 0.5f; x += spacing)
        {
            float jitter = (float)(rng.NextDouble() * 2.0 - 1.0) * maxJitter;
            vWorldX.Add(Mathf.Clamp(x + jitter, -halfSize + roadWidth, halfSize - roadWidth));
        }

        if (addMidStreets)
        {
            var midH = new List<float>();
            hWorldZ.Sort();
            for (int i = 0; i < hWorldZ.Count - 1; i++)
                midH.Add((hWorldZ[i] + hWorldZ[i + 1]) * 0.5f);
            hWorldZ.AddRange(midH);

            var midV = new List<float>();
            vWorldX.Sort();
            for (int i = 0; i < vWorldX.Count - 1; i++)
                midV.Add((vWorldX[i] + vWorldX[i + 1]) * 0.5f);
            vWorldX.AddRange(midV);
        }

        hWorldZ.Sort();
        vWorldX.Sort();

        int xMinCell = WorldToCell(-halfSize);
        int xMaxCell = WorldToCell( halfSize);
        int roadLenH = xMaxCell - xMinCell;

        foreach (float wz in hWorldZ)
        {
            int zCell = WorldToCell(wz);
            gridHelper.PlaceStreetPositions(new Vector3Int(xMinCell, 0, zCell),
                                            new Vector3Int(1, 0, 0), roadLenH);
        }

        int zMinCell = WorldToCell(-halfSize);
        int zMaxCell = WorldToCell( halfSize);
        int roadLenV = zMaxCell - zMinCell;

        foreach (float wx in vWorldX)
        {
            int xCell = WorldToCell(wx);
            gridHelper.PlaceStreetPositions(new Vector3Int(xCell, 0, zMinCell),
                                            new Vector3Int(0, 0, 1), roadLenV);
        }
    }

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

        // Iterasi max 3, branch chance 0.15 — sparse, ada blok kosong
        lsys.iterations     = Mathf.Clamp(lSystemIterations, 1, 3);
        lsys.chanceToIgnore = lSystemBranchChance;

        // Step kecil supaya interior terisi tapi tidak padat
        float effectiveStep = stepSize > 0f ? stepSize : blockSpacing * 0.5f;

        Vector3 origin = cityGenerator.transform.position;
        float decay = 0.8f;

        // Sebar seed point DI DALAM INTERIOR RING — inset 2 cell dari ring.
        // Gunakan koordinat cell integer, bukan world float.
        int innerMin = ringMinX + 2;
        int innerMax = ringMaxX - 2;
        int innerSize = innerMax - innerMin; // ~32 untuk ring -18..18

        // Seed grid: ~5x5 titik di dalam interior
        int seedPerSide = Mathf.Max(3, Mathf.Min(5, innerSize / 8));
        var rngLs = new System.Random(cityGenerator.randomSeed + 99);
        float spacing = (float)innerSize / seedPerSide;

        int seedCount = 0;
        seedPositions.Clear();
        for (int gx = 0; gx < seedPerSide; gx++)
        {
            for (int gz = 0; gz < seedPerSide; gz++)
            {
                // Posisi seed dalam cell integer, jitter 0.5 cell — pasti interior
                float sx = innerMin + spacing * (gx + 0.5f) + (float)(rngLs.NextDouble() * 0.5f);
                float sz = innerMin + spacing * (gz + 0.5f) + (float)(rngLs.NextDouble() * 0.5f);

                var startCell = new Vector3Int(
                    Mathf.RoundToInt(sx), 0, Mathf.RoundToInt(sz));
                if (!IsInsideRingInterior(startCell)) continue;

                int startDir = rngLs.Next(0, 4);
                lsys.Init(cityGenerator.randomSeed + gx * 131 + gz * 17);
                string sentence = lsys.Generate();
                PlaceSegmentsWithDecay(sentence, startCell.x, startCell.z, startDir,
                                       effectiveStep, decay, origin);
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
                                        Vector3 boundsOrigin)
    {
        var   turtle  = new RoadTurtle(startX, startZ, startDir, baseStep);
        var   stack   = new Stack<(float x, float z, int dir, float step)>();
        float curStep = baseStep;
        bool  firstMove = true; // seed adalah anchor — segmen pertama diizinkan

        foreach (char c in sentence)
        {
            switch (c)
            {
                case 'F':
                {
                    if (curStep <= 0) break;

                    turtle.StepSize = curStep;
                    var (from, to) = turtle.MoveForward();

                    var fromCell   = WorldToCell3(from);
                    var toCellRaw  = WorldToCell3(to);

                    // Connect-and-stop: potong segmen di ring (tidak melewati)
                    var toCell = ClampToRingCell(fromCell, toCellRaw);

                    var delta = toCell - fromCell;
                    int len   = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.z));

                    if (len > 0)
                    {
                        var dir = new Vector3Int(
                            delta.x != 0 ? (int)Mathf.Sign(delta.x) : 0,
                            0,
                            delta.z != 0 ? (int)Mathf.Sign(delta.z) : 0);

                        bool endsAtRing = ringCells.Contains(toCell);

                        // FIX #2: segmen yang berhenti TEPAT satu cell sebelum ring
                        // (searah gerak) diperpanjang untuk tersambung ke ring —
                        // tidak boleh berhenti sebelum ring dan jadi endpoint O palsu.
                        if (!endsAtRing)
                        {
                            var nextCell = toCell + dir;
                            if (ringCells.Contains(nextCell))
                            {
                                toCell     = nextCell;
                                delta      = toCell - fromCell;
                                len        = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.z));
                                endsAtRing = true;
                            }
                        }

                        // Batasi jumlah entrance ring (max numberOfRingEntrances).
                        // FIX #1: jika penuh, tolak segmen SEBELUM memasuki koridor
                        // menuju ring — bukan mundur 1 cell yang membuat O palsu.
                        if (endsAtRing && ringEntrances.Count >= numberOfRingEntrances)
                        {
                            MoveTurtleTo(turtle, fromCell);
                            curStep = Mathf.Max(curStep - 2f * cellSize, cellSize);
                            break;
                        }

                        // Tolak segmen yang membuat endpoint terisolasi.
                        // allowRoadsToEndInside=true → segmen boleh berhenti di interior.
                        // endsAtRing → koneksi ke ring, bukan isolasi.
                        // Segmen PERTAMA (firstMove) selalu diizinkan — seed adalah
                        // anchor pohon; tanpa ini pohon tidak pernah bisa lahir
                        // (map jadi kosong). Pohon yang tidak tersambung spoke/ring
                        // ditangani oleh RemoveDisconnectedInnerRoads (BFS) nanti.
                        if (!endsAtRing && !allowRoadsToEndInside && !firstMove
                            && WouldCreateIsolatedEndpoint(fromCell, toCell, dir))
                        {
                            MoveTurtleTo(turtle, toCell);
                            curStep = Mathf.Max(curStep - 2f * cellSize, cellSize);
                            break;
                        }

                        // Tolak segmen yang terlalu dekat dengan jalan lama.
                        // Di dekat ring, cell yang hanya "jembatan" menuju ring
                        // (diabaikan oleh PathMinClearance) tidak menghalangi.
                        bool closeToRing = endsAtRing || ringCells.Contains(fromCell);
                        if (PathMinClearance(fromCell, toCell, dir, minimumDistanceBetweenRoads,
                                             skipsToRingSeparate: closeToRing))
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

                        // Catat semua cell interior (termasuk yang menyambung ring)
                        for (int i = 0; i <= len; i++)
                            innerRoadCells.Add(fromCell + dir * i);

                        if (endsAtRing)
                        {
                            ringEntrances.Add(toCell);
                            Debug.Log($"[RoadNetwork] Interior→ring entrance di {toCell} "
                                    + $"({ringEntrances.Count}/{numberOfRingEntrances})");
                        }
                    }

                    MoveTurtleTo(turtle, toCell);
                    firstMove = false;

                    // SVS Length -= 2 per draw
                    curStep -= 2f * cellSize;
                    if (curStep < cellSize) curStep = cellSize;
                    break;
                }
                case 'f':
                {
                    turtle.StepSize = curStep;
                    var (fFrom, fTo) = turtle.MoveForward();
                    var fToCell = ClampToRingCell(WorldToCell3(fFrom), WorldToCell3(fTo));
                    MoveTurtleTo(turtle, fToCell);
                    break;
                }
                case '+': turtle.TurnRight();  break;
                case '-': turtle.TurnLeft();   break;
                case '|': turtle.TurnAround(); break;
                case '[':
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
    /// Clamp segmen axis-aligned ke ring — "connect and stop".
    /// Berjalan cell-per-cell dari fromCell ke toCell, berhenti pada ring cell
    /// (menyambung, tidak melewati). Jika tidak ada ring, clamp ke batas kota.
    ///
    /// PERILAKU RING:
    /// 1. Jika fromCell SUDAH ring cell → segmen BERADA di ring → tidak diizinkan
    ///    berjalan keluar/berlanjut melewati ring → return fromCell (null-step).
    /// 2. Jika next cell adalah ring → return next (connect & stop).
    /// 3. Jika dariCell ring dan toCell arah KELUAR (lewat ring menuju luar) →
    ///    return fromCell (tolak), bukan lanjut ke luar.
    /// </summary>
    private Vector3Int ClampToRingCell(Vector3Int fromCell, Vector3Int toCell)
    {
        if (ringCells.Count == 0)
        {
            // Mode tanpa ring — clamp ke grid kota saja
            int half = tilesPerSide / 2;
            return new Vector3Int(
                Mathf.Clamp(toCell.x, -half, half - 1),
                0,
                Mathf.Clamp(toCell.z, -half, half - 1));
        }

        // FIX #1: segmen yang MULAI dari cell ring tidak boleh berjalan KELUAR
        // dari ring. Perbaiki kebocoran pertama dari PlaceSegmentsWithDecay:
        // turtle berada di ring, F ke arah luar → dari=ring, to=luar.
        if (ringCells.Contains(fromCell))
        {
            // Walk arah segmen; jika keluar dari batas ring → tolak (return fromCell).
            int dirX = Mathf.Clamp(toCell.x - fromCell.x, -1, 1);
            int dirZ = Mathf.Clamp(toCell.z - fromCell.z, -1, 1);
            int curX = fromCell.x, curZ = fromCell.z;
            int steps = Mathf.Abs(toCell.x - fromCell.x) + Mathf.Abs(toCell.z - fromCell.z);
            for (int i = 0; i < steps; i++)
            {
                curX += dirX; curZ += dirZ;
                var next = new Vector3Int(curX, 0, curZ);
                if (ringCells.Contains(next)) continue; // masih di ring (tangensial)
                if (IsInsideRingInterior(next)) break;  // masuk ke interior — ok
                return fromCell;                        // keluar ring → tolak
            }
            return fromCell; // ujung di ring/tangensial — tetap null-step
        }

        // Axis-aligned walk — satu langkah per arah dominan.
        // Berhenti saat next adalah ring cell (connect & stop).
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

            current = next;
        }
        return current;
    }

    /// <summary>
    /// True jika segmen dari fromCell ke toCell (arah dir) membuat endpoint
    /// terisolasi: ujung bukan ring dan tidak bersentuhan jalan/junction lama.
    /// </summary>
    private bool WouldCreateIsolatedEndpoint(Vector3Int fromCell, Vector3Int toCell, Vector3Int dir)
    {
        if (ringCells.Contains(toCell)) return false;

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
    /// Ring cell TIDAK dihitung sebagai jarak dekat — ring boleh didekati
    /// untuk entrance. SkipsToRingSeparate juga diabaikan: pendekatan tegak
    /// lurus ke ring adalah entrance yang sah (connect & stop).
    /// </summary>
    private bool PathMinClearance(Vector3Int fromCell, Vector3Int toCell, Vector3Int dir,
                                  int margin, bool skipsToRingSeparate)
    {
        if (margin <= 1) return false;

        int minX = Mathf.Min(fromCell.x, toCell.x) - margin;
        int maxX = Mathf.Max(fromCell.x, toCell.x) + margin;
        int minZ = Mathf.Min(fromCell.z, toCell.z) - margin;
        int maxZ = Mathf.Max(fromCell.z, toCell.z) + margin;

        foreach (var cell in gridHelper.GridSnapshot)
        {
            if (cell.x < minX || cell.x > maxX || cell.z < minZ || cell.z > maxZ)
                continue;

            if (ringCells.Contains(cell))
                continue;

            // Abaikan cell yang berada di garis segmen itu sendiri
            if (dir.x != 0 && cell.z == fromCell.z
                && cell.x >= Mathf.Min(fromCell.x, toCell.x)
                && cell.x <= Mathf.Max(fromCell.x, toCell.x))
                continue;
            if (dir.z != 0 && cell.x == fromCell.x
                && cell.z >= Mathf.Min(fromCell.z, toCell.z)
                && cell.z <= Mathf.Max(fromCell.z, toCell.z))
                continue;

            if (skipsToRingSeparate && ringCells.Contains(cell + dir))
                continue; // cell ini hanya "jembatan" menuju ring — bukan koridor

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

        string modeName = generationMode.ToString();
        Debug.Log($"[RoadNetwork] === {modeName} STATS ===");
        Debug.Log($"[RoadNetwork] citySize={cityGenerator.citySize}, tileScale={cellSize:F1}, "
                + $"grid X[{gridMin}..{gridMax}] Z[{gridMin}..{gridMax}] ({tilesPerSide}x{tilesPerSide} cells)");
        Debug.Log($"[RoadNetwork] ring: X[{ringMinX}..{ringMaxX}] Z[{ringMinZ}..{ringMaxZ}] "
                + $"ringCells={ringCells.Count}, innerRoadCells={innerRoadCells.Count}, "
                + $"entrances={ringEntrances.Count}/{numberOfRingEntrances}");
        Debug.Log($"[RoadNetwork] tiles: + {c4}  T {c3}  I {cI}  L {cL}  O {cO}  "
                + $"total={c4 + c3 + cI + cL + cO}");
        Debug.Log($"[RoadNetwork] invalid connections={invalidConnections}");

        if (generationMode == RoadGenerationMode.RingAndLSystem && !enableInteriorRoads)
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
            if (ringEntrances.Count >= numberOfRingEntrances) break;

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

            // Catat cell spoke sebagai jalan interior + entrance ring
            for (int i = 0; i <= len; i++)
                innerRoadCells.Add(startCell + dir * i);
            ringEntrances.Add(ringTarget);
            Debug.Log($"[RoadNetwork] Spoke→ring entrance di {ringTarget} "
                    + $"({ringEntrances.Count}/{numberOfRingEntrances})");
        }

        Debug.Log($"[RoadNetwork] Spokes: center=({cx},{cz}), ring X[{ringMinX}..{ringMaxX}] "
                + $"Z[{ringMinZ}..{ringMaxZ}], entrances={ringEntrances.Count}/{numberOfRingEntrances}");
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
    /// True jika cell dapat dicapai dari ring melalui jaringan jalan
    /// (BFS di ringCells ∪ innerRoadCells). Ring sendiri selalu reachable.
    /// </summary>
    private bool ValidateReachabilityFromRing(Vector3Int start)
    {
        if (ringCells.Contains(start)) return true;
        var seen  = new HashSet<Vector3Int>();
        var queue = new Queue<Vector3Int>();
        foreach (var ringCell in ringCells)
        {
            seen.Add(ringCell);
            queue.Enqueue(ringCell);
        }
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var nb in GetRoadNeighbors(cur))
            {
                if (seen.Add(nb)) queue.Enqueue(nb);
            }
        }
        return seen.Contains(start);
    }

    /// <summary>
    /// Hapus semua innerRoadCells yang TIDAK tercapai dari ring (BFS).
    /// BFS-nya jalan penuh dari ring → semua reachable. Sisanya dihapus.
    /// </summary>
    public void RemoveDisconnectedInnerRoads()
    {
        if (!removeDisconnectedRoads) return;

        // Guard: tanpa ring, BFS tidak punya titik awal — jangan hapus semua jalan.
        // Mode LSystem (tanpa ring) tidak pernah menjalankan cleanup ini.
        if (ringCells.Count == 0) return;

        int before = innerRoadCells.Count;

        // BFS sekali dari semua ring cell — reachable = semua jalan interior terhubung ring.
        var reachable = new HashSet<Vector3Int>();
        var queue     = new Queue<Vector3Int>();
        foreach (var ringCell in ringCells)
        {
            reachable.Add(ringCell);
            queue.Enqueue(ringCell);
        }
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var nb in GetRoadNeighbors(cur))
            {
                if (reachable.Add(nb)) queue.Enqueue(nb);
            }
        }

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
                + $"(dihapus {removed}, reachable dari ring {reachable.Count - ringCells.Count})");
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
    private void FinalizeRoads(List<float> hWorldZ, List<float> vWorldX)
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

        // Rebuild road segments dan blocks
        if (hWorldZ.Count > 0 && vWorldX.Count > 0)
        {
            // Grid mode atau Hybrid: pakai hLines/vLines untuk road segments dan blok rapi
            RebuildRoadsFromGrid(hWorldZ, vWorldX);
            RebuildBlocks();
        }
        else
        {
            // LSystem mode: rebuild dari grid cells langsung
            RebuildBlocksFromCells();
        }

        ComputeIntersections();

        string modeName = generationMode.ToString();
        Debug.Log($"[RoadNetwork] {modeName}: {roads.Count} roads, "
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
        sb.AppendLine($"Mode: {generationMode}  |  citySize: {cityGenerator.citySize}  |  tileScale: {roadTileScale.x}");
        sb.AppendLine($"Grid: {cols} x {rows} cells  |  Total road tiles: {cellSet.Count}");
        sb.AppendLine($"Cell range: X[{minX}..{maxX}]  Z[{minZ}..{maxZ}]");
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
            $"RoadMap_{generationMode}_{System.DateTime.Now:yyyyMMdd_HHmmss}.txt");
        System.IO.File.WriteAllText(file, sb.ToString());

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
    /// Rebuild CityBlock list langsung dari grid cells yang terisi
    /// (dipakai di L-System mode karena tidak ada hLines/vLines eksplisit).
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
    // REBUILD ROADS FROM GRID
    // =======================================================================
    private void RebuildRoadsFromGrid(List<float> hWorldZ, List<float> vWorldX)
    {
        roads.Clear();
        horizontalRoads.Clear();
        verticalRoads.Clear();
        hLines.Clear();
        vLines.Clear();

        float xMin = -halfSize;
        float xMax =  halfSize;
        float zMin = -halfSize;
        float zMax =  halfSize;

        // Horizontal roads — satu segment per H-line, full lebar kota
        foreach (float wz in hWorldZ)
        {
            var seg = new RoadSegment(
                new Vector3(xMin, 0f, wz),
                new Vector3(xMax, 0f, wz),
                roadWidth * SEC_W);
            roads.Add(seg);
            horizontalRoads.Add(seg);
            if (!hLines.Contains(wz)) hLines.Add(wz);
        }

        // Vertical roads — satu segment per V-line, full tinggi kota
        foreach (float wx in vWorldX)
        {
            var seg = new RoadSegment(
                new Vector3(wx, 0f, zMin),
                new Vector3(wx, 0f, zMax),
                roadWidth * SEC_W);
            roads.Add(seg);
            verticalRoads.Add(seg);
            if (!vLines.Contains(wx)) vLines.Add(wx);
        }
    }

    // =======================================================================
    // BLOCKS
    // =======================================================================
    private void RebuildBlocks()
    {
        blocks.Clear();
        var sortH = new List<float>(hLines); sortH.Sort();
        var sortV = new List<float>(vLines); sortV.Sort();

        for (int i = 0; i < sortH.Count - 1; i++)
        {
            for (int j = 0; j < sortV.Count - 1; j++)
            {
                float z0 = sortH[i]     + roadWidth * 0.5f;
                float z1 = sortH[i + 1] - roadWidth * 0.5f;
                float x0 = sortV[j]     + roadWidth * 0.5f;
                float x1 = sortV[j + 1] - roadWidth * 0.5f;

                float bw = x1 - x0;
                float bh = z1 - z0;
                if (bw < MIN_BLOCK_DIM || bh < MIN_BLOCK_DIM) continue;

                blocks.Add(new CityBlock
                {
                    center    = new Vector3((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f),
                    size      = new Vector2(bw, bh),
                    blockType = BlockType.Residential
                });
            }
        }
    }

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
        var seen = new HashSet<long>();

        // Setiap pasangan (H-line, V-line) = intersection
        foreach (float hz in hLines)
        {
            foreach (float vx in vLines)
            {
                // Deduplicate pakai hash
                long key = (long)(hz * 1000f + 500000f) * 10000000L
                         + (long)(vx * 1000f + 500000f);
                if (!seen.Add(key)) continue;

                var pt = new Vector3(vx, 0f, hz);
                intersections.Add(pt);

                // Cari tetangga H dan V
                bool hasE = false, hasW = false, hasN = false, hasS = false;
                // Semua intersection di grid ortogonal punya 4 arah
                // kecuali di boundary (periksa apakah ada road di sisi itu)
                hasE = vLines.Exists(x => x > vx + EPS);
                hasW = vLines.Exists(x => x < vx - EPS);
                hasN = hLines.Exists(z => z > hz + EPS);
                hasS = hLines.Exists(z => z < hz - EPS);

                int arms = (hasE ? 1 : 0) + (hasW ? 1 : 0)
                         + (hasN ? 1 : 0) + (hasS ? 1 : 0);

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
    }

    // =======================================================================
    // CLEAR / RESET
    // =======================================================================
    public void ClearRoads()
    {
        roads.Clear();
        horizontalRoads.Clear();
        verticalRoads.Clear();
        intersections.Clear();
        blocks.Clear();
        junctions.Clear();
        gridPositions.Clear();
        hLines.Clear();
        vLines.Clear();
        ringCells.Clear();
        innerRoadCells.Clear();
        ringEntrances.Clear();

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
