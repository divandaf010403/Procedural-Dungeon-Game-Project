using UnityEngine;

/// <summary>
/// Penghasil noise FBM murni, resolution-independent.
/// Semua koordinat dinormalisasi ke [0,1] sebelum sampling,
/// sehingga hasil identik mau resolusi 1000 atau 4000.
/// 
/// Analog dengan StructureHelper.cs di Dungeon system.
/// </summary>
public static class NoiseGenerator
{
    /// <summary>
    /// Generate noise map [0,1] dari NoiseSettings.
    /// </summary>
    /// <param name="resolution">Heightmap resolution (pixel count per axis)</param>
    /// <param name="settings">Noise configuration preset</param>
    /// <param name="seed">World seed</param>
    /// <returns>float[res, res] normalized to [0,1] via local min/max</returns>
    public static float[,] Generate(int resolution, NoiseSettings settings, int seed)
    {
        int res = resolution;
        int oct = settings.octaves;
        float freq = settings.frequency;
        float pers = settings.persistence;
        float lac  = settings.lacunarity;

        // Deterministic offsets from seed — kept small for float precision safety
        var rng = new System.Random(seed + settings.seedOffset);
        var offsets = new Vector2[oct];
        for (int i = 0; i < oct; i++)
        {
            offsets[i] = new Vector2(
                (float)(rng.NextDouble() * 1000.0 - 500.0),
                (float)(rng.NextDouble() * 1000.0 - 500.0));
        }

        float[,] map = new float[res, res];
        float min = float.MaxValue;
        float max = float.MinValue;
        float invRes = 1f / res;

        for (int y = 0; y < res; y++)
        {
            // Normalized coordinate [0, 1]
            float ny = y * invRes;
            for (int x = 0; x < res; x++)
            {
                float nx = x * invRes;
                float amplitude = 1f;
                float frequency = 1f;
                float value = 0f;

                for (int i = 0; i < oct; i++)
                {
                    // Scale-independent: nx * freq maps [0,1] to [0, freq] Perlin units
                    float sx = (nx * freq + offsets[i].x) * frequency;
                    float sy = (ny * freq + offsets[i].y) * frequency;

                    // PerlinNoise returns [0,1], remap to [-1, 1]
                    float sample = Mathf.PerlinNoise(sx, sy) * 2f - 1f;
                    value += sample * amplitude;

                    amplitude *= pers;
                    frequency *= lac;
                }

                map[x, y] = value;
                if (value < min) min = value;
                if (value > max) max = value;
            }
        }

        // Local normalization to [0, 1]
        float range = Mathf.Max(max - min, 0.0001f);
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
                map[x, y] = (map[x, y] - min) / range;

        return map;
    }

    /// <summary>
    /// Generate ridged noise — peaks along a center line, valleys at edges.
    /// Used by RiverCarverLayer.
    /// </summary>
    public static float[,] GenerateRidged(int resolution, NoiseSettings settings, int seed)
    {
        float[,] baseNoise = Generate(resolution, settings, seed);
        int res = resolution;

        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                // Ridged: 0 at center of "ridge", 1 at sides
                float v = baseNoise[x, y];
                baseNoise[x, y] = 1f - Mathf.Abs(v - 0.5f) * 2f;
            }

        return baseNoise;
    }

    // ─────────────────────────────────────────────────────────────────
    // REGION-BASED GENERATION (untuk Infinite Terrain / Chunk System)
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generate noise untuk satu chunk menggunakan koordinat dunia absolut.
    /// Menggunakan normalisasi GLOBAL (bukan lokal min/max) sehingga
    /// chunk yang berdampingan menghasilkan ketinggian yang nyambung seamless.
    /// </summary>
    /// <param name="resolution">Heightmap resolution per chunk</param>
    /// <param name="settings">Noise configuration preset</param>
    /// <param name="seed">World seed (sama untuk semua chunk)</param>
    /// <param name="worldOffsetX">Posisi X dunia sudut kiri-bawah chunk</param>
    /// <param name="worldOffsetZ">Posisi Z dunia sudut kiri-bawah chunk</param>
    /// <param name="chunkWorldSize">Ukuran chunk dalam world-unit (misal 256)</param>
    /// <returns>float[res, res] normalized to [0,1] via global bounds</returns>
    public static float[,] GenerateRegion(int resolution, NoiseSettings settings, int seed,
        float worldOffsetX, float worldOffsetZ, float chunkWorldSize)
    {
        int res = resolution;
        int oct = settings.octaves;
        float freq = settings.frequency;
        float pers = settings.persistence;
        float lac  = settings.lacunarity;

        // Same deterministic offsets as Generate() — same seed = same world
        var rng = new System.Random(seed + settings.seedOffset);
        var offsets = new Vector2[oct];
        for (int i = 0; i < oct; i++)
            offsets[i] = new Vector2(
                (float)(rng.NextDouble() * 1000.0 - 500.0),
                (float)(rng.NextDouble() * 1000.0 - 500.0));

        // Theoretical max amplitude for global normalization
        // FBM: sum of amplitudes = 1 + p + p² + ... + p^(oct-1)
        float maxAmp = 0f;
        { float a = 1f; for (int i = 0; i < oct; i++) { maxAmp += a; a *= pers; } }

        float[,] map = new float[res, res];
        // FIX 1: Unity heightmap has (res) samples spanning (res-1) intervals
        // over chunkWorldSize. Using (res-1) ensures pixel (res-1) of chunk A
        // samples the exact same world position as pixel 0 of chunk (A+1).
        float pixelToWorld = chunkWorldSize / (res - 1);
        // Reference size: frequency 1.2 = 1.2 noise periods per 1000 world units
        float refSize = 1000f;

        // FIX 2: Unity's SetHeights expects float[z_index, x_index].
        // Our array is map[first_dim, second_dim].
        // So first_dim (our loop "x") → Unity Z axis
        //    second_dim (our loop "y") → Unity X axis
        // We must sample world coords accordingly:
        //   y → worldX (second dim → X axis)
        //   x → worldZ (first dim → Z axis)
         for (int y = 0; y < res; y++)
        {
            // y = second dimension = Unity X axis
            float wx = worldOffsetX + y * pixelToWorld;
            float nx = wx / refSize;

            for (int x = 0; x < res; x++)
            {
                // x = first dimension = Unity Z axis
                float wz = worldOffsetZ + x * pixelToWorld;
                float nz = wz / refSize;

                float amplitude = 1f;
                float frequency = 1f;
                float value = 0f;

                for (int i = 0; i < oct; i++)
                {
                    float sx = (nx * freq + offsets[i].x) * frequency;
                    float sy = (nz * freq + offsets[i].y) * frequency;

                    // Remap Perlin [0,1] to [-1,1] for wider value distribution
                    float sample = Mathf.PerlinNoise(sx, sy) * 2f - 1f;
                    value += sample * amplitude;
                    amplitude *= pers;
                    frequency *= lac;
                }

                // Global normalization: [-maxAmp, maxAmp] → [0, 1]
                // This gives ~2x wider spread than [0,1] accumulation
                map[x, y] = Mathf.Clamp01((value + maxAmp) / (2f * maxAmp));
            }
        }

        return map;
    }

    /// <summary>
    /// Generate ridged noise untuk satu chunk (untuk River Carver di infinite mode).
    /// </summary>
    public static float[,] GenerateRidgedRegion(int resolution, NoiseSettings settings, int seed,
        float worldOffsetX, float worldOffsetZ, float chunkWorldSize)
    {
        float[,] noise = GenerateRegion(resolution, settings, seed,
            worldOffsetX, worldOffsetZ, chunkWorldSize);
        int res = resolution;

        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
                noise[x, y] = 1f - Mathf.Abs(noise[x, y] - 0.5f) * 2f;

        return noise;
    }
}
