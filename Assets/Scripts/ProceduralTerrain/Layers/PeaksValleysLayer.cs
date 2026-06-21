using UnityEngine;

/// <summary>
/// Layer 3: Peaks & Valleys — menambahkan detail ketinggian gunung.
/// PV noise dikalikan erosion mask sehingga hanya aktif di zona pegunungan,
/// lalu ditambahkan ke base height.
/// 
/// Termasuk fitur Plateau / Mesa: meratakan puncak di zona erosion rendah.
/// 
/// Output: modifikasi in-place heightmap yang sudah mencakup base + mountains.
/// </summary>
[System.Serializable]
public class PeaksValleysLayer
{
    [Header("Plateau / Mesa (Dataran Tinggi)")]
    [Tooltip("Aktifkan dataran tinggi rata di zona pegunungan.")]
    public bool enablePlateaus = true;

    [Range(0.30f, 0.65f)]
    [Tooltip("Di atas ketinggian ini, puncak akan diratakan.")]
    public float plateauCutoff = 0.42f;

    [Range(0f, 1f)]
    [Tooltip("Kekuatan perataan. 1 = rata sempurna.")]
    public float plateauStrength = 0.70f;

    // Spline: PV noise [0,1] → height bonus [0, 0.50]
    // Dikali erosion mask sebelum ditambahkan, sehingga max efektif = 0.50 × 1.0 = 0.50
    // Total max height: 0.50 (continental) + 0.50 (PV) = 1.00 → full terrain range
    private static readonly float[] SplineX = { 0.00f, 0.15f, 0.30f, 0.45f, 0.60f, 0.78f, 1.00f };
    private static readonly float[] SplineY = { 0.00f, 0.03f, 0.10f, 0.22f, 0.36f, 0.44f, 0.50f };

    /// <summary>
    /// Gabungkan base height + PV × erosion → combined heightmap.
    /// </summary>
    /// <param name="pvNoise">Raw PV noise [0,1]</param>
    /// <param name="erosionMask">Erosion mask (0=flat, 1=mountain)</param>
    /// <param name="baseHeight">Base continental height</param>
    /// <returns>Combined heightmap (base + mountains)</returns>
    public float[,] Apply(float[,] pvNoise, float[,] erosionMask, float[,] baseHeight)
    {
        int res = pvNoise.GetLength(0);
        float[,] combined = new float[res, res];

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float bH = baseHeight[x, y];
                float erosM = erosionMask[x, y];
                float pvH = SplineMapper.Evaluate(pvNoise[x, y], SplineX, SplineY) * erosM;

                float h = bH + pvH;

                // Plateau / Mesa: ratakan puncak di zona erosion RENDAH
                // erosM rendah = dataran luas. Jika di dataran itu tinggi, jadikan plateau
                if (enablePlateaus && erosM < 0.40f && h > plateauCutoff)
                {
                    float overshoot = Mathf.InverseLerp(plateauCutoff, plateauCutoff + 0.15f, h);
                    float plateauMask = Mathf.InverseLerp(0.40f, 0.0f, erosM);
                    float flatH = plateauCutoff + 0.03f;
                    h = Mathf.Lerp(h, Mathf.Lerp(h, flatH, overshoot * plateauStrength), plateauMask);
                }

                combined[x, y] = h;
            }
        }

        return combined;
    }
}
