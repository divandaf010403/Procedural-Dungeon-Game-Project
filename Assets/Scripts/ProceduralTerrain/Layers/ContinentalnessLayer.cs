using UnityEngine;

/// <summary>
/// Layer 1: Continentalness — membentuk benua dan lautan.
/// Mengubah noise [0,1] menjadi elevasi dasar melalui spline,
/// termasuk Continental Shelf (landas kontinen dangkal).
/// 
/// Output: float[,] base height [0, ~0.50]
/// </summary>
[System.Serializable]
public class ContinentalnessLayer
{
    [Tooltip("Aktifkan Continental Shelf (landas kontinen) di tepi pantai.")]
    public bool enableShelf = true;

    [Range(0.04f, 0.12f)]
    [Tooltip("Ketinggian landas kontinen. Lebih tinggi = shelf lebih dangkal.")]
    public float shelfHeight = 0.07f;

    // Spline: noise [0,1] → base elevation [0, 0.50]
    // ~25% noise menjadi laut, ~75% menjadi daratan
    // Laut dalam = 0.0, pantai = 0.18, plains = 0.25-0.35, dataran tinggi = 0.50  
    private static readonly float[] SplineX = { 0.00f, 0.08f, 0.18f, 0.28f, 0.40f, 0.55f, 0.75f, 1.00f };
    private static readonly float[] SplineY = { 0.00f, 0.02f, 0.12f, 0.22f, 0.30f, 0.38f, 0.44f, 0.50f };

    /// <summary>
    /// Proses noise continental menjadi base height map.
    /// </summary>
    /// <param name="continentalNoise">Raw noise [0,1] dari NoiseGenerator</param>
    /// <returns>Base elevation map [0, ~0.35]</returns>
    public float[,] Apply(float[,] continentalNoise)
    {
        int res = continentalNoise.GetLength(0);
        float[,] baseHeight = new float[res, res];

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float noise = continentalNoise[x, y];
                float h = SplineMapper.Evaluate(noise, SplineX, SplineY);

                // Continental Shelf: di zona laut dangkal, buat lereng lebih gradual
                if (enableShelf && h < 0.15f && h > 0.02f)
                {
                    // Jarak dari pantai (0 = dekat pantai, 1 = laut dalam)
                    float oceanDepth = Mathf.InverseLerp(0.15f, 0.02f, h);
                    // Shelf menurun perlahan sebelum terjun ke laut dalam
                    float shelfH = Mathf.Lerp(h, shelfHeight, 1f - oceanDepth * oceanDepth);
                    h = Mathf.Lerp(h, shelfH, 1f - oceanDepth);
                }

                baseHeight[x, y] = h;
            }
        }

        return baseHeight;
    }
}
