using UnityEngine;

/// <summary>
/// Layer 7: Badlands — menambahkan micro-terrain kasar di zona erosi menengah.
/// Menghasilkan perbukitan kecil tidak beraturan (bukan gunung tinggi, bukan plains rata).
/// Area ini terlihat seperti "tanah tererosi" — mirip Grand Canyon atau Bromo.
/// </summary>
[System.Serializable]
public class BadlandsLayer
{
    [Tooltip("Aktifkan badlands micro-terrain.")]
    public bool enabled = true;

    [Range(0f, 0.06f)]
    [Tooltip("Amplitudo gelombang kecil badlands.")]
    public float amplitude = 0.04f;

    [Range(0.15f, 0.50f)]
    [Tooltip("Batas erosion bawah agar badlands muncul.")]
    public float erosionMin = 0.18f;

    [Range(0.40f, 0.75f)]
    [Tooltip("Batas erosion atas. Badlands paling kuat di tengah range ini.")]
    public float erosionMax = 0.55f;

    /// <summary>
    /// Tambahkan micro-terrain di zona erosi menengah (modifikasi in-place).
    /// </summary>
    /// <param name="heightMap">Combined heightmap</param>
    /// <param name="badlandsNoise">High-frequency noise [0,1]</param>
    /// <param name="erosionMask">Erosion mask</param>
    /// <param name="baseHeight">Base height (exclude laut)</param>
    public void Roughen(float[,] heightMap, float[,] badlandsNoise, float[,] erosionMask, float[,] baseHeight)
    {
        if (!enabled) return;

        int res = heightMap.GetLength(0);
        float erosionMid = (erosionMin + erosionMax) * 0.5f;
        float erosionHalfRange = (erosionMax - erosionMin) * 0.5f;

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float erosM = erosionMask[x, y];

                // Hanya di zona erosi menengah
                if (erosM < erosionMin || erosM > erosionMax) continue;
                // Hanya di daratan (bukan laut)
                if (baseHeight[x, y] < 0.10f) continue;

                // Mask berbentuk bell curve: paling kuat di tengah range
                float bellMask = 1f - Mathf.Abs(erosM - erosionMid) / erosionHalfRange;

                // Noise [-1, 1] × amplitude × mask
                float noise = (badlandsNoise[x, y] - 0.5f) * 2f;
                heightMap[x, y] += noise * amplitude * bellMask;
            }
        }
    }
}
