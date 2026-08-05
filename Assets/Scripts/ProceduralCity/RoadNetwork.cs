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
    public List<RoadSegment> radialRoads     = new List<RoadSegment>(); // unused
    public List<RoadSegment> ringRoads       = new List<RoadSegment>(); // unused
    public List<RoadSegment> arterialRoads   = new List<RoadSegment>(); // unused
    public List<RoadSegment> gridRoads       = new List<RoadSegment>(); // unused

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
    // Grid settings (exposed so Inspector bisa tweak)
    // -----------------------------------------------------------------------
    [Header("Grid Road Settings")]
    [Tooltip("Spacing antar jalan dalam world units. Biasanya = blockSize dari CityGenerator.")]
    public float blockSpacing = 80f;

    [Range(0f, 0.4f)]
    [Tooltip("Jitter maksimal per-garis (fraksi dari blockSpacing). 0 = grid sempurna.")]
    public float jitterFraction = 0.15f;

    [Tooltip("Tambah extra jalan di tengah tiap blok (membagi blok jadi 2). Menambah kepadatan.")]
    public bool addMidStreets = false;

    // -----------------------------------------------------------------------
    // L-System settings
    // -----------------------------------------------------------------------
    [Header("L-System Road Settings")]

    [Tooltip("Preset L-System yang dipakai.\nCustom = gunakan axiom/rules custom di bawah.")]
    public LSystemPreset lSystemPreset = LSystemPreset.OrganicCity;

    [Tooltip("Panjang 1 step turtle dalam world units. Biasanya = blockSpacing.")]
    public float lSystemStepSize = 80f;

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
    // Private state
    // -----------------------------------------------------------------------
    private CityGenerator   cityGenerator;
    private System.Random   rng;

    private float   halfSize;
    private float   roadWidth;
    private float   cellSize;

    private GameObject    roadMeshObject;
    private RoadGridHelper gridHelper;

    private List<Vector3Int> gridPositions = new List<Vector3Int>();
    private List<float>      hLines        = new List<float>();   // Z world coords
    private List<float>      vLines        = new List<float>();   // X world coords

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
        halfSize  = cityGenerator.citySize * 0.5f;
        roadWidth = cityGenerator.roadWidth;
        // cellSize = 1 unit di grid = 1 tile road (lebar 1 jalan)
        cellSize  = roadWidth;

        roadMeshObject = new GameObject("RoadMesh");
        roadMeshObject.transform.SetParent(cityGenerator.transform);
        cityGenerator.RegisterSpawnedObject(roadMeshObject);

        Material roadMat = cityGenerator.roadMaterial != null
            ? cityGenerator.roadMaterial
            : CityGenerator.CreateMaterial("Road", new Color(0.18f, 0.18f, 0.20f));

        gridHelper = new RoadGridHelper(roadMeshObject, roadMat, cellSize);

        // Hitung effective spacing — dipakai oleh grid DAN L-System
        // supaya keduanya proporsional terhadap citySize berapapun
        float effectiveSpacing = blockSpacing > 0f ? blockSpacing : cityGenerator.blockSize;

        // Hitung L-System step size proporsional terhadap citySize.
        // Kalau user set lSystemStepSize > 0, pakai itu.
        // Kalau 0 (auto), derive dari spacing supaya konsisten di semua ukuran:
        //   stepSize = spacing → 1 step turtle = 1 blok
        float effectiveLsStep = lSystemStepSize > 0f
            ? lSystemStepSize
            : effectiveSpacing;

        // Dispatch ke mode yang dipilih.
        // PENTING: semua mode hanya PLACE tile ke gridHelper.
        // FixRoad() + RebuildBlocks() + ComputeIntersections() dipanggil
        // SEKALI di FinalizeRoads() setelah semua place selesai.
        // Dengan cara ini junction antara grid road dan L-System road
        // otomatis ter-detect dan mesh menyatu.
        var hWorldZ = new List<float>();
        var vWorldX = new List<float>();

        switch (generationMode)
        {
            case RoadGenerationMode.LSystem:
                PlaceLSystemTiles(effectiveLsStep);
                break;

            case RoadGenerationMode.RingAndLSystem:
                // Pass 1: ring road mengelilingi kota (boundary)
                PlaceRingRoad();
                // Pass 2: L-System isi interior — turtle mulai dari dalam ring
                PlaceLSystemTiles(effectiveLsStep);
                break;

            default: // OrthogonalGrid
                PlaceGridTiles(effectiveSpacing, hWorldZ, vWorldX);
                break;
        }

        // ---- Finalize: FixRoad + Blocks + Junctions — satu kali untuk semua mode ----
        FinalizeRoads(hWorldZ, vWorldX);
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
    // PLACE: RING ROAD
    // 4 sisi boundary mengelilingi kota — jalan luar yang membingkai L-System.
    // Turtle L-System mulai dari dalam ring, FixRoad() menyatukan junction.
    // =======================================================================
    private void PlaceRingRoad()
    {
        // Inset sedikit dari boundary supaya ring road ada di dalam kota,
        // bukan tepat di tepi. Pakai roadWidth sebagai margin.
        float inset    = roadWidth * 0.5f;
        float minCoord = -halfSize + inset;
        float maxCoord =  halfSize - inset;

        int minCell = WorldToCell(minCoord);
        int maxCell = WorldToCell(maxCoord);
        int len     = maxCell - minCell;

        // Sisi Selatan  (Z = min, arah X+)
        gridHelper.PlaceStreetPositions(
            new Vector3Int(minCell, 0, minCell), new Vector3Int(1, 0, 0), len + 1);

        // Sisi Utara    (Z = max, arah X+)
        gridHelper.PlaceStreetPositions(
            new Vector3Int(minCell, 0, maxCell), new Vector3Int(1, 0, 0), len + 1);

        // Sisi Barat    (X = min, arah Z+)
        gridHelper.PlaceStreetPositions(
            new Vector3Int(minCell, 0, minCell), new Vector3Int(0, 0, 1), len + 1);

        // Sisi Timur    (X = max, arah Z+)
        gridHelper.PlaceStreetPositions(
            new Vector3Int(maxCell, 0, minCell), new Vector3Int(0, 0, 1), len + 1);

        // Daftarkan 4 segmen ke roads list supaya district/buildings aware
        float wMin = CellToWorld(minCell);
        float wMax = CellToWorld(maxCell);
        roads.Add(new RoadSegment(new Vector3(wMin, 0, wMin), new Vector3(wMax, 0, wMin), roadWidth)); // S
        roads.Add(new RoadSegment(new Vector3(wMin, 0, wMax), new Vector3(wMax, 0, wMax), roadWidth)); // N
        roads.Add(new RoadSegment(new Vector3(wMin, 0, wMin), new Vector3(wMin, 0, wMax), roadWidth)); // W
        roads.Add(new RoadSegment(new Vector3(wMax, 0, wMin), new Vector3(wMax, 0, wMax), roadWidth)); // E

        Debug.Log($"[RoadNetwork] Ring road placed: {wMin:F0} → {wMax:F0} ({len} cells/side)");
    }

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

        // Iterasi proporsional terhadap citySize
        // citySize 1000 → max 4, 2000 → max 5, 3000 → max 6
        int safeMaxIter = 3 + Mathf.FloorToInt(cityGenerator.citySize / 1000f);
        lsys.iterations = Mathf.Min(lsys.iterations, safeMaxIter);

        string sentence = lsys.Generate();
        Debug.Log($"[RoadNetwork] L-System sentence: {sentence.Length} chars "
                + $"(preset={lSystemPreset}, iter={lsys.iterations}, step={stepSize:F1})");

        Vector3 origin = cityGenerator.transform.position;

        // SINGLE origin dari pusat kota — semua cabang terhubung ke satu pohon
        // Decay factor: setiap level stack mengurangi step size
        // Mirip SVS "Length -= 2" tapi proporsional (tidak hardcoded)
        // decayFactor 0.7 → depth 0=100%, 1=70%, 2=49%, 3=34%
        float decayFactor = 0.7f;

        PlaceSegmentsWithDecay(sentence, origin.x, origin.z, 0, stepSize, decayFactor, origin);

        // Untuk RingAndLSystem: tambah 4 spoke dari pusat ke ring road
        // supaya interior terhubung ke ring
        if (generationMode == RoadGenerationMode.RingAndLSystem)
            PlaceSpokesToRing(origin);
    }

    /// <summary>
    /// Versi PlaceSegmentsFromTurtle dengan depth-aware step decay.
    /// Turtle berjalan dengan step size yang berkurang per level stack depth —
    /// persis seperti SVS Visualizer "Length -= 2" tapi proporsional.
    /// </summary>
    private void PlaceSegmentsWithDecay(string sentence,
                                        float startX, float startZ, int startDir,
                                        float baseStep, float decayFactor,
                                        Vector3 boundsOrigin)
    {
        // Jalankan turtle manual dengan decay — tidak pakai RoadVisualizer
        // karena perlu track stack depth untuk hitung step per segment
        var turtle   = new RoadTurtle(startX, startZ, startDir, baseStep);
        var stack    = new Stack<(float x, float z, int dir, float step)>();
        float curStep = baseStep;

        foreach (char c in sentence)
        {
            switch (c)
            {
                case 'F':
                {
                    turtle.StepSize = curStep;
                    var (from, to)  = turtle.MoveForward();

                    // Clamp ke batas kota
                    Vector3 clampedTo = ClampToBoundsStatic(to, boundsOrigin, halfSize, curStep);
                    if (Vector3.Distance(from, clampedTo) > curStep * 0.1f)
                    {
                        var fromCell = WorldToCell3(from);
                        var toCell   = WorldToCell3(clampedTo);
                        var delta    = toCell - fromCell;
                        int len      = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.z));
                        if (len > 0)
                        {
                            var dir = new Vector3Int(
                                delta.x != 0 ? (int)Mathf.Sign(delta.x) : 0,
                                0,
                                delta.z != 0 ? (int)Mathf.Sign(delta.z) : 0);
                            gridHelper.PlaceStreetPositions(fromCell, dir, len + 1);
                            roads.Add(new RoadSegment(from, clampedTo, roadWidth));
                        }
                        // Clamp turtle position
                        turtle.X = clampedTo.x;
                        turtle.Z = clampedTo.z;
                    }
                    break;
                }
                case 'f':
                    turtle.StepSize = curStep;
                    turtle.MoveForward();
                    break;
                case '+': turtle.TurnRight();   break;
                case '-': turtle.TurnLeft();    break;
                case '|': turtle.TurnAround();  break;
                case '[':
                    // Push — kurangi step size per depth (SVS Length -= 2)
                    stack.Push((turtle.X, turtle.Z, turtle.Dir, curStep));
                    curStep = Mathf.Max(curStep * decayFactor, cellSize * 2f); // min 2 cells
                    turtle.Push();
                    break;
                case ']':
                    // Pop — kembalikan step size ke sebelumnya
                    if (stack.Count > 0)
                    {
                        var (px, pz, pd, ps) = stack.Pop();
                        turtle.X    = px;
                        turtle.Z    = pz;
                        turtle.Dir  = pd;
                        curStep     = ps;
                    }
                    turtle.Pop();
                    break;
            }
        }
    }

    /// <summary>
    /// Tambah 4 spoke (jalan lurus) dari pusat kota ke ring road.
    /// Memastikan interior L-System selalu terhubung ke ring road
    /// di 4 arah (N, S, E, W).
    /// </summary>
    private void PlaceSpokesToRing(Vector3 origin)
    {
        float inset   = roadWidth * 0.5f;
        float ringMin = -halfSize + inset;
        float ringMax =  halfSize - inset;

        int cx = WorldToCell(origin.x);
        int cz = WorldToCell(origin.z);

        int minCell = WorldToCell(ringMin);
        int maxCell = WorldToCell(ringMax);

        // North spoke: dari center ke ring utara (Z+)
        int lenN = maxCell - cz;
        if (lenN > 0)
            gridHelper.PlaceStreetPositions(new Vector3Int(cx, 0, cz), new Vector3Int(0, 0, 1), lenN + 1);

        // South spoke: dari center ke ring selatan (Z-)
        int lenS = cz - minCell;
        if (lenS > 0)
            gridHelper.PlaceStreetPositions(new Vector3Int(cx, 0, minCell), new Vector3Int(0, 0, 1), lenS + 1);

        // East spoke: dari center ke ring timur (X+)
        int lenE = maxCell - cx;
        if (lenE > 0)
            gridHelper.PlaceStreetPositions(new Vector3Int(cx, 0, cz), new Vector3Int(1, 0, 0), lenE + 1);

        // West spoke: dari center ke ring barat (X-)
        int lenW = cx - minCell;
        if (lenW > 0)
            gridHelper.PlaceStreetPositions(new Vector3Int(minCell, 0, cz), new Vector3Int(1, 0, 0), lenW + 1);

        Debug.Log($"[RoadNetwork] Spokes placed: center=({cx},{cz}), ring=[{minCell}..{maxCell}]");
    }

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
    // Dengan satu FixRoad() di sini, junction antara grid road dan L-System road
    // otomatis ter-detect: cell yang bersentuhan langsung di-swap ke mesh
    // corner/T/4way yang benar.
    // =======================================================================
    private void FinalizeRoads(List<float> hWorldZ, List<float> vWorldX)
    {
        // Snapshot semua cell
        gridPositions.Clear();
        foreach (var pos in gridHelper.roadDictionary.Keys)
            gridPositions.Add(pos);

        // SATU FixRoad() untuk semua mode
        gridHelper.FixRoad();

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

    /// <summary>
    /// Buat LSystemGenerator dari preset yang dipilih di Inspector.
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

    /// <summary>World unit → cell integer (snap ke grid 1-unit cell).</summary>
    private int WorldToCell(float worldPos) => Mathf.RoundToInt(worldPos / cellSize);

    /// <summary>World Vector3 → cell Vector3Int.</summary>
    private Vector3Int WorldToCell3(Vector3 worldPos) =>
        new Vector3Int(WorldToCell(worldPos.x), 0, WorldToCell(worldPos.z));

    /// <summary>Cell integer → world center of cell.</summary>
    private float CellToWorld(int cell) => cell * cellSize;

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
        radialRoads.Clear();
        ringRoads.Clear();
        arterialRoads.Clear();
        gridRoads.Clear();
        intersections.Clear();
        blocks.Clear();
        junctions.Clear();
        gridPositions.Clear();
        hLines.Clear();
        vLines.Clear();

        if (gridHelper != null)
        {
            gridHelper.Reset();
            gridHelper = null;
        }
        if (roadMeshObject != null)
        {
            if (Application.isPlaying) Destroy(roadMeshObject);
            else DestroyImmediate(roadMeshObject);
            roadMeshObject = null;
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
