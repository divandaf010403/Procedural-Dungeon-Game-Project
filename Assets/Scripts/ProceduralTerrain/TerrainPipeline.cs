using UnityEngine;

/// <summary>
/// TerrainPipeline — Orchestrator utama yang menjalankan semua layer secara berurutan.
/// 
/// Analog dengan DungeonGenerator.cs di Dungeon system:
/// DungeonGenerator mengkoordinasikan BSP → RoomGenerator → CorridorsGenerator.
/// TerrainPipeline mengkoordinasikan NoiseGenerator → Continentalness → Erosion → PV
///     → Rivers → Lakes → Badlands → Volcanos → Falloff → Smooth → SetHeights.
/// 
/// Pasang komponen ini pada GameObject yang memiliki Terrain component.
/// </summary>
[RequireComponent(typeof(Terrain))]
public class TerrainPipeline : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────
    // GLOBAL
    // ─────────────────────────────────────────────────────────────────
    [Header("=== GLOBAL ===")]
    public int seed = 42;

    // ─────────────────────────────────────────────────────────────────
    // NOISE PRESETS
    // Jika null, akan dibuat default saat runtime.
    // Untuk customization: buat melalui Assets → Create → Terrain → Noise Settings
    // ─────────────────────────────────────────────────────────────────
    [Header("=== NOISE PRESETS ===")]
    [Tooltip("Preset noise untuk benua. Null = default (freq 1.2, 3 oct).")]
    public NoiseSettings continentalNoise;
    [Tooltip("Preset noise untuk erosi. Null = default (freq 2.5, 3 oct).")]
    public NoiseSettings erosionNoise;
    [Tooltip("Preset noise untuk PV. Null = default (freq 5.5, 3 oct).")]
    public NoiseSettings pvNoise;
    [Tooltip("Preset noise untuk sungai. Null = default (freq 4.0, 2 oct).")]
    public NoiseSettings riverNoise;
    [Tooltip("Preset noise untuk danau. Null = default (freq 3.5, 2 oct).")]
    public NoiseSettings lakeNoise;
    [Tooltip("Preset noise untuk badlands. Null = default (freq 14.0, 4 oct).")]
    public NoiseSettings badlandsNoise;

    // ─────────────────────────────────────────────────────────────────
    // DOMAIN WARPING
    // ─────────────────────────────────────────────────────────────────
    [Header("=== DOMAIN WARPING ===")]
    [Tooltip("Aktifkan distorsi organik pada terrain.")]
    public bool enableWarp = true;
    [Tooltip("Preset noise untuk warp. Null = default.")]
    public NoiseSettings warpNoise;
    [Range(0f, 40f)]
    [Tooltip("Kekuatan distorsi. 12-18 direkomendasikan.")]
    public float warpStrength = 15f;

    // ─────────────────────────────────────────────────────────────────
    // TERRAIN LAYERS (each is a [Serializable] class with own parameters)
    // ─────────────────────────────────────────────────────────────────
    [Header("=== LAYERS ===")]
    public ContinentalnessLayer continentalness = new ContinentalnessLayer();
    public ErosionLayer         erosion         = new ErosionLayer();
    public PeaksValleysLayer    peaksValleys    = new PeaksValleysLayer();
    public RiverCarverLayer     riverCarver     = new RiverCarverLayer();
    public LakeBasinLayer       lakeBasin       = new LakeBasinLayer();
    public BadlandsLayer        badlands        = new BadlandsLayer();
    public VolcanoStamper       volcanoStamper  = new VolcanoStamper();

    // ─────────────────────────────────────────────────────────────────
    // ISLAND FALLOFF
    // ─────────────────────────────────────────────────────────────────
    [Header("=== ISLAND FALLOFF ===")]
    public bool useFalloff = true;
    [Range(0f, 0.6f)]
    [Tooltip("Kekuatan falloff. Lebih besar = lautan di tepi lebih luas.")]
    public float falloffStrength = 0.28f;

    // ─────────────────────────────────────────────────────────────────
    // SMOOTHING
    // ─────────────────────────────────────────────────────────────────
    [Header("=== SMOOTHING ===")]
    [Range(0, 12)]
    [Tooltip("Jumlah pass smoothing. 4-6 direkomendasikan.")]
    public int smoothPasses = 5;
    [Range(1, 3)]
    [Tooltip("Radius blur per pass (1=3×3, 2=5×5).")]
    public int smoothRadius = 1;

    // ─────────────────────────────────────────────────────────────────
    // PRIVATE
    // ─────────────────────────────────────────────────────────────────
    private Terrain     _terrain;
    private TerrainData _data;

    // Default noise settings (created at runtime if preset slots are empty)
    private NoiseSettings DefaultContinental => NoiseSettings.Create(1.2f, 3, 0.45f, 2.0f, 0);
    private NoiseSettings DefaultErosion     => NoiseSettings.Create(2.5f, 3, 0.42f, 2.0f, 111);
    private NoiseSettings DefaultPV          => NoiseSettings.Create(5.5f, 3, 0.38f, 2.0f, 222);
    private NoiseSettings DefaultRiver       => NoiseSettings.Create(4.0f, 2, 0.50f, 2.0f, 333);
    private NoiseSettings DefaultLake        => NoiseSettings.Create(3.5f, 2, 0.50f, 2.0f, 444);
    private NoiseSettings DefaultBadlands    => NoiseSettings.Create(14.0f, 4, 0.55f, 2.1f, 555);
    private NoiseSettings DefaultWarp        => NoiseSettings.Create(2.0f, 2, 0.50f, 2.0f, 666);

    // ─────────────────────────────────────────────────────────────────
    // MAIN PIPELINE
    // ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// Jalankan seluruh pipeline terrain generation.
    /// Urutan: Noise → Layers → Features → Falloff → Smooth → SetHeights
    /// </summary>
    public void GenerateTerrain()
    {
        _terrain = GetComponent<Terrain>();
        _data    = _terrain.terrainData;
        int res  = _data.heightmapResolution;

        // ── STEP 1: Generate raw noise maps ────────────────────────
        var cNoiseMap = NoiseGenerator.Generate(res, continentalNoise ?? DefaultContinental, seed);
        var eNoiseMap = NoiseGenerator.Generate(res, erosionNoise     ?? DefaultErosion,     seed);
        var pNoiseMap = NoiseGenerator.Generate(res, pvNoise          ?? DefaultPV,          seed);

        // ── STEP 2: Domain Warping (optional) ──────────────────────
        if (enableWarp)
        {
            var wxMap = NoiseGenerator.Generate(res, warpNoise ?? DefaultWarp, seed);
            var wyMap = NoiseGenerator.Generate(res, warpNoise ?? DefaultWarp, seed + 777);

            cNoiseMap = ApplyWarp(cNoiseMap, wxMap, wyMap, res);
            eNoiseMap = ApplyWarp(eNoiseMap, wxMap, wyMap, res);
            pNoiseMap = ApplyWarp(pNoiseMap, wxMap, wyMap, res);
        }

        // ── STEP 3: Core layers ────────────────────────────────────
        var baseHeight  = continentalness.Apply(cNoiseMap);       // → [0, ~0.35]
        var erosionMask = erosion.Apply(eNoiseMap);               // → [0, 1.0]
        var heightMap   = peaksValleys.Apply(pNoiseMap, erosionMask, baseHeight); // → [0, ~0.95]

        // ── STEP 4: Detail features ────────────────────────────────
        // River valleys
        if (riverCarver.enabled)
        {
            var riverMap = NoiseGenerator.GenerateRidged(res, riverNoise ?? DefaultRiver, seed);
            if (enableWarp)
            {
                var wxMap = NoiseGenerator.Generate(res, warpNoise ?? DefaultWarp, seed);
                var wyMap = NoiseGenerator.Generate(res, warpNoise ?? DefaultWarp, seed + 777);
                riverMap = ApplyWarp(riverMap, wxMap, wyMap, res);
            }
            riverCarver.Carve(heightMap, riverMap, erosionMask);
        }

        // Lake basins
        if (lakeBasin.enabled)
        {
            var lakeMap = NoiseGenerator.Generate(res, lakeNoise ?? DefaultLake, seed);
            lakeBasin.Fill(heightMap, lakeMap, baseHeight, erosionMask);
        }

        // Badlands
        if (badlands.enabled)
        {
            var badMap = NoiseGenerator.Generate(res, badlandsNoise ?? DefaultBadlands, seed);
            badlands.Roughen(heightMap, badMap, erosionMask, baseHeight);
        }

        // Volcanic peaks + calderas
        volcanoStamper.Stamp(heightMap, seed);

        // ── STEP 5: Post-processing ────────────────────────────────
        if (useFalloff)
            HeightMapProcessor.ApplyFalloff(heightMap, falloffStrength);

        HeightMapProcessor.Clamp01(heightMap);

        if (smoothPasses > 0)
            heightMap = HeightMapProcessor.Smooth(heightMap, smoothPasses, smoothRadius);

        // ── STEP 6: Apply to Unity Terrain ─────────────────────────
        _data.SetHeights(0, 0, heightMap);

        // ── Debug: log height statistics ────────────────────────────
        float hMin = float.MaxValue, hMax = float.MinValue, hSum = 0f;
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float v = heightMap[x, y];
                if (v < hMin) hMin = v;
                if (v > hMax) hMax = v;
                hSum += v;
            }
        float hAvg = hSum / (res * res);
        Debug.Log($"[TerrainPipeline] Heightmap stats: min={hMin:F3}, max={hMax:F3}, avg={hAvg:F3} | " +
                  $"TerrainData.size = {_data.size} | Actual height range = {hMin * _data.size.y:F1}m - {hMax * _data.size.y:F1}m");
    }

    // ─────────────────────────────────────────────────────────────────
    // DOMAIN WARPING HELPER
    // ─────────────────────────────────────────────────────────────────
    private float[,] ApplyWarp(float[,] source, float[,] warpX, float[,] warpY, int res)
    {
        float[,] warped = new float[res, res];
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                int nx = Mathf.Clamp(
                    Mathf.RoundToInt(x + (warpX[x, y] - 0.5f) * 2f * warpStrength), 0, res - 1);
                int ny = Mathf.Clamp(
                    Mathf.RoundToInt(y + (warpY[x, y] - 0.5f) * 2f * warpStrength), 0, res - 1);
                warped[x, y] = source[nx, ny];
            }
        }
        return warped;
    }
}
