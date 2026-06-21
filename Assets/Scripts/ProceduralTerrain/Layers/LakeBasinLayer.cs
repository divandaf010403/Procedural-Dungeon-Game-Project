using UnityEngine;

/// <summary>
/// Layer 6: Lake Basin — menciptakan danau inland di dataran rendah.
/// Mendeteksi cekungan alami di mana noise dip di area base height rendah
/// tetapi masih di atas garis laut, lalu meratakan area ke ketinggian
/// permukaan danau yang konstan.
/// </summary>
[System.Serializable]
public class LakeBasinLayer
{
    [Tooltip("Aktifkan danau inland.")]
    public bool enabled = true;

    [Range(0f, 0.45f)]
    [Tooltip("Ambang noise untuk danau. Lebih besar = lebih banyak danau.")]
    public float lakeThreshold = 0.24f;

    [Range(0.08f, 0.22f)]
    [Tooltip("Ketinggian permukaan danau (fraksi total terrain).")]
    public float lakeSurfaceLevel = 0.14f;

    [Range(0.10f, 0.30f)]
    [Tooltip("Base height maksimum agar area bisa jadi danau (terlalu tinggi = di gunung).")]
    public float maxBaseHeightForLake = 0.24f;

    [Range(0.00f, 0.20f)]
    [Tooltip("Erosion mask maksimum agar area bisa jadi danau (plains, bukan gunung).")]
    public float maxErosionForLake = 0.18f;

    /// <summary>
    /// Isi cekungan danau pada heightmap (modifikasi in-place).
    /// </summary>
    /// <param name="heightMap">Combined heightmap</param>
    /// <param name="lakeNoise">Lake detection noise [0,1]</param>
    /// <param name="baseHeight">Base continental height (untuk zona deteksi)</param>
    /// <param name="erosionMask">Erosion mask (untuk exclude gunung)</param>
    public void Fill(float[,] heightMap, float[,] lakeNoise, float[,] baseHeight, float[,] erosionMask)
    {
        if (!enabled) return;

        int res = heightMap.GetLength(0);

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float bH = baseHeight[x, y];
                float erosM = erosionMask[x, y];

                // Danau hanya di dataran rendah, bukan di laut dan bukan di gunung
                if (bH < 0.10f || bH > maxBaseHeightForLake) continue;
                if (erosM > maxErosionForLake) continue;

                float lake = lakeNoise[x, y];
                if (lake >= lakeThreshold) continue;

                // Semakin dalam cekungan noise, semakin kuat efek perataan
                float depth = Mathf.InverseLerp(lakeThreshold, 0f, lake);
                float smooth = Mathf.SmoothStep(0f, 1f, depth);

                // Ratakan ke permukaan danau
                heightMap[x, y] = Mathf.Lerp(heightMap[x, y], lakeSurfaceLevel, smooth * 0.85f);
            }
        }
    }
}
