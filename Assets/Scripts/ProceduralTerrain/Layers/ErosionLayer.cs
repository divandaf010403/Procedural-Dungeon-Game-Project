using UnityEngine;

/// <summary>
/// Layer 2: Erosion — menentukan area mana yang jadi dataran datar (plains)
/// dan mana yang jadi zona pegunungan aktif.
/// 
/// Output: float[,] erosion mask [0, 1]
///   0.0 = dataran super rata (plains, area pemukiman)
///   1.0 = zona pegunungan aktif (PV layer akan bekerja maksimal)
/// </summary>
[System.Serializable]
public class ErosionLayer
{
    // Spline: noise [0,1] → mountain multiplier [0, 1]
    // Noise rendah = pegunungan aktif (erosion belum mengikis)
    // Noise tinggi = dataran rata (erosion sudah meratakan)
    private static readonly float[] SplineX = { 0.00f, 0.15f, 0.30f, 0.45f, 0.58f, 0.72f, 1.00f };
    private static readonly float[] SplineY = { 1.00f, 0.88f, 0.62f, 0.32f, 0.10f, 0.02f, 0.00f };

    /// <summary>
    /// Proses noise erosion menjadi mountain multiplier mask.
    /// </summary>
    /// <param name="erosionNoise">Raw noise [0,1] dari NoiseGenerator</param>
    /// <returns>Erosion mask: 0=flat plains, 1=active mountains</returns>
    public float[,] Apply(float[,] erosionNoise)
    {
        int res = erosionNoise.GetLength(0);
        float[,] erosionMask = new float[res, res];

        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
                erosionMask[x, y] = SplineMapper.Evaluate(erosionNoise[x, y], SplineX, SplineY);

        return erosionMask;
    }
}
