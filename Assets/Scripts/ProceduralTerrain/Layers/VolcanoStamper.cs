using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Layer 3/4: Volcano Stamper — menempatkan gunung api kerucut dan kawah caldera.
/// Posisi ditentukan secara deterministik dari seed.
/// Gaussian cone exp(-dist² × sharpness) menghasilkan profil gunung realistis.
/// </summary>
[System.Serializable]
public class VolcanoStamper
{
    [Tooltip("Aktifkan gunung api.")]
    public bool enabled = true;

    [Range(1, 12)]
    [Tooltip("Jumlah gunung api di peta.")]
    public int volcanoCount = 4;

    [Range(0.04f, 0.18f)]
    [Tooltip("Radius dasar gunung api (fraksi ukuran peta).")]
    public float baseRadius = 0.08f;

    [Range(0.08f, 0.45f)]
    [Tooltip("Tinggi puncak gunung api.")]
    public float peakHeight = 0.25f;

    [Range(1f, 8f)]
    [Tooltip("Ketajaman cone. Lebih besar = gunung lebih lancip.")]
    public float sharpness = 4.5f;

    [Header("Caldera (Kawah)")]
    [Tooltip("Aktifkan kawah di puncak gunung api.")]
    public bool enableCaldera = true;

    [Range(0.1f, 0.5f)]
    [Tooltip("Radius kawah relatif terhadap radius gunung.")]
    public float calderaRadiusFraction = 0.28f;

    [Range(0.02f, 0.20f)]
    [Tooltip("Kedalaman kawah.")]
    public float calderaDepth = 0.10f;

    /// <summary>
    /// Stamp gunung api ke heightmap (modifikasi in-place).
    /// </summary>
    /// <param name="heightMap">Heightmap to stamp onto</param>
    /// <param name="seed">World seed for deterministic placement</param>
    public void Stamp(float[,] heightMap, int seed)
    {
        if (!enabled) return;

        int res = heightMap.GetLength(0);

        // Deterministic placement di 60% tengah peta (hindari edge)
        var rng = new System.Random(seed + 9999);
        var positions = new List<Vector2>();
        for (int i = 0; i < volcanoCount; i++)
        {
            float vx = 0.2f + (float)rng.NextDouble() * 0.6f;
            float vy = 0.2f + (float)rng.NextDouble() * 0.6f;
            positions.Add(new Vector2(vx, vy));
        }

        foreach (var pos in positions)
        {
            StampSingle(heightMap, res, pos);
        }
    }

    private void StampSingle(float[,] heightMap, int res, Vector2 normalizedPos)
    {
        int cx = Mathf.RoundToInt(normalizedPos.x * (res - 1));
        int cy = Mathf.RoundToInt(normalizedPos.y * (res - 1));

        // Hanya tempatkan di daratan (bukan di laut)
        if (heightMap[cx, cy] < 0.12f) return;

        int radiusPx = Mathf.RoundToInt(baseRadius * res);
        float calderaR = calderaRadiusFraction * radiusPx;

        for (int y = cy - radiusPx; y <= cy + radiusPx; y++)
        {
            if (y < 0 || y >= res) continue;
            for (int x = cx - radiusPx; x <= cx + radiusPx; x++)
            {
                if (x < 0 || x >= res) continue;

                float dx = (x - cx) / (float)radiusPx;
                float dy = (y - cy) / (float)radiusPx;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                if (dist > 1f) continue;

                // Gaussian cone profile
                float cone = Mathf.Exp(-dist * dist * sharpness);
                float addH = peakHeight * cone;

                // Caldera: cekungan kawah di puncak
                if (enableCaldera)
                {
                    float distPx = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (distPx < calderaR)
                    {
                        float calderaT = distPx / calderaR; // 0=center, 1=rim
                        float rimShape = Mathf.Pow(calderaT, 0.6f);
                        float craterDip = (1f - rimShape) * calderaDepth;
                        addH -= craterDip;
                    }
                }

                heightMap[x, y] = Mathf.Clamp01(heightMap[x, y] + addH);
            }
        }
    }
}
