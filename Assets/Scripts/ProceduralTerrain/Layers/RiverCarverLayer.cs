using UnityEngine;

/// <summary>
/// Layer 5: River Carver — mengukir lembah sungai di zona pegunungan.
/// Menggunakan ridged noise dari NoiseGenerator.GenerateRidged()
/// untuk membuat pola garis sungai yang realistis.
/// 
/// Sungai hanya aktif di area dengan erosion mask tinggi (zona pegunungan),
/// sehingga sungai mengalir dari gunung dan menghilang di dataran.
/// </summary>
[System.Serializable]
public class RiverCarverLayer
{
    [Tooltip("Aktifkan lembah sungai.")]
    public bool enabled = true;

    [Range(0f, 1f)]
    [Tooltip("Lebar jalur sungai. Lebih besar = sungai lebih lebar.")]
    public float riverWidth = 0.20f;

    [Range(0f, 0.20f)]
    [Tooltip("Kedalaman ukiran sungai.")]
    public float riverDepth = 0.10f;

    [Range(0.15f, 0.80f)]
    [Tooltip("Batas minimum erosion mask agar sungai muncul. " +
             "Lebih rendah = sungai juga tampil di dataran.")]
    public float minErosionForRiver = 0.30f;

    /// <summary>
    /// Ukir lembah sungai ke heightmap (modifikasi in-place).
    /// </summary>
    /// <param name="heightMap">Combined heightmap to carve</param>
    /// <param name="ridgedNoise">Ridged noise [0,1] dari NoiseGenerator.GenerateRidged</param>
    /// <param name="erosionMask">Erosion mask (0=flat, 1=mountain)</param>
    public void Carve(float[,] heightMap, float[,] ridgedNoise, float[,] erosionMask)
    {
        if (!enabled) return;

        int res = heightMap.GetLength(0);
        float threshold = 1f - riverWidth;

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float erosM = erosionMask[x, y];
                if (erosM < minErosionForRiver) continue;

                float ridged = ridgedNoise[x, y];
                if (ridged <= threshold) continue;

                // Lebar sungai → kedalaman ukiran
                float carveT = Mathf.InverseLerp(threshold, 1f, ridged);
                float smooth = Mathf.SmoothStep(0f, 1f, carveT);

                // Bobot berdasarkan seberapa aktif zonanya
                float mountainWeight = Mathf.InverseLerp(minErosionForRiver, 1.0f, erosM);

                heightMap[x, y] -= smooth * riverDepth * mountainWeight;
            }
        }
    }
}
