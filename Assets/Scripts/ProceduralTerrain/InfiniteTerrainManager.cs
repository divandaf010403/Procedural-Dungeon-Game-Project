using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// InfiniteTerrainManager — Sistem chunk streaming untuk dunia tak terbatas.
/// 
/// Cara kerja:
/// 1. Dunia dibagi menjadi chunk (kotak terrain kecil, misal 256×256m)
/// 2. Hanya chunk di sekitar player yang aktif (viewDistance)
/// 3. Saat player bergerak, chunk baru di-generate di depan, chunk lama di-unload
/// 4. Noise menggunakan koordinat dunia absolut → chunk berdekatan SELALU nyambung
/// 
/// Semua layer terrain yang sudah ada (Continentalness, Erosion, PV, dll)
/// digunakan ulang tanpa modifikasi.
/// 
/// Setup: Buat Empty GameObject → Add Component → InfiniteTerrainManager
///        Drag player/camera ke slot Player.
/// </summary>
public class InfiniteTerrainManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────
    // GLOBAL
    // ─────────────────────────────────────────────────────────────────
    [Header("=== GLOBAL ===")]
    public int seed = 42;

    [Tooltip("Transform player/camera. Chunk di-generate di sekitar posisi ini.")]
    public Transform player;

    // ─────────────────────────────────────────────────────────────────
    // CHUNK SETTINGS
    // ─────────────────────────────────────────────────────────────────
    [Header("=== CHUNK SETTINGS ===")]
    [Tooltip("Ukuran tiap chunk dalam world-unit. 256 direkomendasikan.")]
    public int chunkSize = 256;

    [Tooltip("Jarak pandang chunk dari player. 4 = 4 chunk di setiap arah = 9×9 grid.")]
    [Range(1, 8)]
    public int viewDistance = 4;

    [Tooltip("Tinggi maksimum terrain. 300-600 direkomendasikan.")]
    public float terrainHeight = 400f;

    [Tooltip("Resolusi heightmap per chunk. 129 = ringan, 257 = detail.")]
    public int heightmapResolution = 129;

    [Tooltip("Detail resolusi terrain per chunk.")]
    public int detailResolution = 128;

    // ─────────────────────────────────────────────────────────────────
    // NOISE PRESETS (null = gunakan default bawaan)
    // ─────────────────────────────────────────────────────────────────
    [Header("=== NOISE PRESETS ===")]
    public NoiseSettings continentalNoise;
    public NoiseSettings erosionNoise;
    public NoiseSettings pvNoise;
    public NoiseSettings riverNoise;
    public NoiseSettings lakeNoise;
    public NoiseSettings badlandsNoise;

    // ─────────────────────────────────────────────────────────────────
    // TERRAIN LAYERS
    // ─────────────────────────────────────────────────────────────────
    [Header("=== LAYERS ===")]
    public ContinentalnessLayer continentalness = new ContinentalnessLayer();
    public ErosionLayer         erosion         = new ErosionLayer();
    public PeaksValleysLayer    peaksValleys    = new PeaksValleysLayer();
    public RiverCarverLayer     riverCarver     = new RiverCarverLayer();
    public LakeBasinLayer       lakeBasin       = new LakeBasinLayer();
    public BadlandsLayer        badlands        = new BadlandsLayer();

    // ─────────────────────────────────────────────────────────────────
    // POST-PROCESSING
    // ─────────────────────────────────────────────────────────────────
    [Header("=== SMOOTHING ===")]
    [Range(0, 8)]
    public int smoothPasses = 3;
    [Range(1, 2)]
    public int smoothRadius = 1;

    // ─────────────────────────────────────────────────────────────────
    // PRIVATE
    // ─────────────────────────────────────────────────────────────────
    private Dictionary<Vector2Int, Terrain> _activeChunks = new Dictionary<Vector2Int, Terrain>();
    private Vector2Int _lastPlayerChunk = new Vector2Int(int.MinValue, int.MinValue);

    // Default noise settings
    private NoiseSettings _defContinental, _defErosion, _defPV, _defRiver, _defLake, _defBadlands;

    // ─────────────────────────────────────────────────────────────────
    // LIFECYCLE
    // ─────────────────────────────────────────────────────────────────
    void Start()
    {
        // Pre-create defaults so we don't allocate every frame
        _defContinental = NoiseSettings.Create(1.2f, 3, 0.45f, 2.0f, 0);
        _defErosion     = NoiseSettings.Create(2.5f, 3, 0.42f, 2.0f, 111);
        _defPV          = NoiseSettings.Create(5.5f, 3, 0.38f, 2.0f, 222);
        _defRiver       = NoiseSettings.Create(4.0f, 2, 0.50f, 2.0f, 333);
        _defLake        = NoiseSettings.Create(3.5f, 2, 0.50f, 2.0f, 444);
        _defBadlands    = NoiseSettings.Create(14.0f, 4, 0.55f, 2.1f, 555);

        if (player == null)
        {
            Debug.LogError("[InfiniteTerrainManager] Player Transform belum di-assign! Drag player/camera ke slot Player.");
            return;
        }

        UpdateChunks(WorldToChunkCoord(player.position));
    }

    void Update()
    {
        if (player == null) return;

        Vector2Int currentChunk = WorldToChunkCoord(player.position);
        if (currentChunk != _lastPlayerChunk)
        {
            UpdateChunks(currentChunk);
            _lastPlayerChunk = currentChunk;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // CHUNK MANAGEMENT
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Hitung chunk mana yang harus ada, generate yang baru, hapus yang jauh.
    /// </summary>
    void UpdateChunks(Vector2Int center)
    {
        // Tentukan chunk yang harus ada
        HashSet<Vector2Int> needed = new HashSet<Vector2Int>();
        for (int z = -viewDistance; z <= viewDistance; z++)
            for (int x = -viewDistance; x <= viewDistance; x++)
                needed.Add(center + new Vector2Int(x, z));

        // Hapus chunk yang terlalu jauh
        List<Vector2Int> toRemove = new List<Vector2Int>();
        foreach (var kv in _activeChunks)
            if (!needed.Contains(kv.Key))
                toRemove.Add(kv.Key);

        foreach (var key in toRemove)
        {
            DestroyImmediate(_activeChunks[key].gameObject);
            _activeChunks.Remove(key);
        }

        // Generate chunk baru
        foreach (var coord in needed)
            if (!_activeChunks.ContainsKey(coord))
                GenerateChunk(coord);

        // Set neighbors agar terrain seamless di perbatasan
        UpdateNeighbors();

        // Stitch border heights agar tidak ada step antar chunk
        StitchBorders();

        _lastPlayerChunk = center;
    }

    /// <summary>
    /// Hubungkan terrain yang berdekatan agar LOD dan edge stitching seamless.
    /// </summary>
    void UpdateNeighbors()
    {
        foreach (var kv in _activeChunks)
        {
            Vector2Int c = kv.Key;
            Terrain terrain = kv.Value;

            _activeChunks.TryGetValue(c + new Vector2Int(-1, 0), out Terrain left);
            _activeChunks.TryGetValue(c + new Vector2Int(1, 0),  out Terrain right);
            _activeChunks.TryGetValue(c + new Vector2Int(0, 1),  out Terrain top);
            _activeChunks.TryGetValue(c + new Vector2Int(0, -1), out Terrain bottom);

            terrain.SetNeighbors(left, top, right, bottom);
        }
    }

    /// <summary>
    /// Rata-ratakan ketinggian di perbatasan chunk yang berdekatan.
    /// Diperlukan karena smoothing per-chunk menciptakan perbedaan kecil di tepi.
    /// </summary>
    void StitchBorders()
    {
        int res = heightmapResolution;
        HashSet<Vector2Int> processed = new HashSet<Vector2Int>();

        foreach (var kv in _activeChunks)
        {
            Vector2Int coord = kv.Key;
            Terrain terrain = kv.Value;

            // Right neighbor (X direction = second dimension)
            Vector2Int rightCoord = coord + new Vector2Int(1, 0);
            if (_activeChunks.TryGetValue(rightCoord, out Terrain rightTerrain)
                && !processed.Contains(coord))
            {
                float[,] hL = terrain.terrainData.GetHeights(0, 0, res, res);
                float[,] hR = rightTerrain.terrainData.GetHeights(0, 0, res, res);

                for (int i = 0; i < res; i++) // first dim = Z
                {
                    float avg = (hL[i, res - 1] + hR[i, 0]) * 0.5f;
                    hL[i, res - 1] = avg;
                    hR[i, 0] = avg;
                }

                terrain.terrainData.SetHeights(0, 0, hL);
                rightTerrain.terrainData.SetHeights(0, 0, hR);
                terrain.Flush();
                rightTerrain.Flush();
            }

            // Top neighbor (Z direction = first dimension)
            Vector2Int topCoord = coord + new Vector2Int(0, 1);
            if (_activeChunks.TryGetValue(topCoord, out Terrain topTerrain)
                && !processed.Contains(coord))
            {
                float[,] hB = terrain.terrainData.GetHeights(0, 0, res, res);
                float[,] hT = topTerrain.terrainData.GetHeights(0, 0, res, res);

                for (int j = 0; j < res; j++) // second dim = X
                {
                    float avg = (hB[res - 1, j] + hT[0, j]) * 0.5f;
                    hB[res - 1, j] = avg;
                    hT[0, j] = avg;
                }

                terrain.terrainData.SetHeights(0, 0, hB);
                topTerrain.terrainData.SetHeights(0, 0, hT);
                terrain.Flush();
                topTerrain.Flush();
            }

            processed.Add(coord);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // CHUNK GENERATION
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generate satu chunk terrain di koordinat grid tertentu.
    /// Pipeline sama dengan TerrainPipeline tetapi menggunakan koordinat dunia.
    /// </summary>
    void GenerateChunk(Vector2Int coord)
    {
        float worldX = coord.x * chunkSize;
        float worldZ = coord.y * chunkSize;
        int res = heightmapResolution;

        // ── Create Unity Terrain ──────────────────────────────────
        TerrainData data = new TerrainData();
        data.heightmapResolution = res;
        data.size = new Vector3(chunkSize, terrainHeight, chunkSize);

        // Set detail resolution to avoid warnings
        data.SetDetailResolution(detailResolution, 16);

        GameObject go = Terrain.CreateTerrainGameObject(data);
        go.transform.position = new Vector3(worldX, 0f, worldZ);
        go.transform.parent = transform; // child of manager
        go.name = $"Chunk_{coord.x}_{coord.y}";

        Terrain terrain = go.GetComponent<Terrain>();

        // ── Generate noise maps (world coordinates) ───────────────
        NoiseSettings cSet = continentalNoise ?? _defContinental;
        NoiseSettings eSet = erosionNoise     ?? _defErosion;
        NoiseSettings pSet = pvNoise          ?? _defPV;

        var cNoise = NoiseGenerator.GenerateRegion(res, cSet, seed, worldX, worldZ, chunkSize);
        var eNoise = NoiseGenerator.GenerateRegion(res, eSet, seed, worldX, worldZ, chunkSize);
        var pNoise = NoiseGenerator.GenerateRegion(res, pSet, seed, worldX, worldZ, chunkSize);

        // ── Apply core layers ─────────────────────────────────────
        var baseHeight  = continentalness.Apply(cNoise);
        var erosionMask = erosion.Apply(eNoise);
        var heightMap   = peaksValleys.Apply(pNoise, erosionMask, baseHeight);

        // ── Detail features ───────────────────────────────────────
        if (riverCarver.enabled)
        {
            NoiseSettings rSet = riverNoise ?? _defRiver;
            var riverMap = NoiseGenerator.GenerateRidgedRegion(res, rSet, seed, worldX, worldZ, chunkSize);
            riverCarver.Carve(heightMap, riverMap, erosionMask);
        }

        if (lakeBasin.enabled)
        {
            NoiseSettings lSet = lakeNoise ?? _defLake;
            var lakeMap = NoiseGenerator.GenerateRegion(res, lSet, seed, worldX, worldZ, chunkSize);
            lakeBasin.Fill(heightMap, lakeMap, baseHeight, erosionMask);
        }

        if (badlands.enabled)
        {
            NoiseSettings bSet = badlandsNoise ?? _defBadlands;
            var badMap = NoiseGenerator.GenerateRegion(res, bSet, seed, worldX, worldZ, chunkSize);
            badlands.Roughen(heightMap, badMap, erosionMask, baseHeight);
        }

        // ── Post-processing ───────────────────────────────────────
        // NO falloff — infinite world has no edges!
        HeightMapProcessor.Clamp01(heightMap);

        if (smoothPasses > 0)
            heightMap = HeightMapProcessor.Smooth(heightMap, smoothPasses, smoothRadius);

        // ── Apply to terrain ──────────────────────────────────────
        data.SetHeights(0, 0, heightMap);
        terrain.Flush(); // Force visual update
        _activeChunks[coord] = terrain;
    }

    // ─────────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Konversi posisi dunia ke koordinat grid chunk.
    /// </summary>
    public Vector2Int WorldToChunkCoord(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.FloorToInt(worldPos.x / chunkSize),
            Mathf.FloorToInt(worldPos.z / chunkSize));
    }

    /// <summary>
    /// Hapus semua chunk (untuk regenerasi atau cleanup).
    /// </summary>
    public void ClearAllChunks()
    {
        foreach (var kv in _activeChunks)
            if (kv.Value != null && kv.Value.gameObject != null)
                DestroyImmediate(kv.Value.gameObject);
        _activeChunks.Clear();
        _lastPlayerChunk = new Vector2Int(int.MinValue, int.MinValue);
    }

    /// <summary>
    /// Regenerate semua chunk yang ada saat ini.
    /// </summary>
    public void RegenerateAll()
    {
        // Pre-create defaults jika belum ada (misal dipanggil dari Editor)
        if (_defContinental == null)
        {
            _defContinental = NoiseSettings.Create(1.2f, 3, 0.45f, 2.0f, 0);
            _defErosion     = NoiseSettings.Create(2.5f, 3, 0.42f, 2.0f, 111);
            _defPV          = NoiseSettings.Create(5.5f, 3, 0.38f, 2.0f, 222);
            _defRiver       = NoiseSettings.Create(4.0f, 2, 0.50f, 2.0f, 333);
            _defLake        = NoiseSettings.Create(3.5f, 2, 0.50f, 2.0f, 444);
            _defBadlands    = NoiseSettings.Create(14.0f, 4, 0.55f, 2.1f, 555);
        }

        ClearAllChunks();

        if (player != null)
            UpdateChunks(WorldToChunkCoord(player.position));
        else
            UpdateChunks(Vector2Int.zero);
    }

    void OnDestroy()
    {
        ClearAllChunks();
    }
}
