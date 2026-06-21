using UnityEngine;

/// <summary>
/// Utility untuk post-processing heightmap: smoothing, normalisasi, clamp.
/// Semua method bersifat static dan bekerja in-place atau mengembalikan map baru.
/// </summary>
public static class HeightMapProcessor
{
    /// <summary>
    /// Gaussian box blur — menghaluskan heightmap untuk menghapus patahan tajam.
    /// Setiap pass mengganti setiap piksel dengan rata-rata tetangganya.
    /// </summary>
    /// <param name="heightMap">Input heightmap [0,1]</param>
    /// <param name="passes">Jumlah iterasi blur (3-8 direkomendasikan)</param>
    /// <param name="radius">Radius kernel (1=3×3, 2=5×5)</param>
    /// <returns>Smoothed heightmap</returns>
    public static float[,] Smooth(float[,] heightMap, int passes, int radius)
    {
        int res = heightMap.GetLength(0);
        float[,] buffer = new float[res, res];
        float[,] source = heightMap;

        for (int p = 0; p < passes; p++)
        {
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float sum = 0f;
                    int count = 0;

                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        int ny = Mathf.Clamp(y + dy, 0, res - 1);
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int nx = Mathf.Clamp(x + dx, 0, res - 1);
                            sum += source[nx, ny];
                            count++;
                        }
                    }

                    buffer[x, y] = sum / count;
                }
            }

            // Swap buffers
            float[,] temp = source;
            source = buffer;
            buffer = temp;
        }

        return source;
    }

    /// <summary>
    /// Clamp seluruh heightmap ke [0, 1].
    /// </summary>
    public static void Clamp01(float[,] heightMap)
    {
        int res = heightMap.GetLength(0);
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
                heightMap[x, y] = Mathf.Clamp01(heightMap[x, y]);
    }

    /// <summary>
    /// Island falloff — tarik tepi peta ke ketinggian 0 (laut).
    /// Menggunakan formula S-curve cubic untuk transisi halus.
    /// </summary>
    /// <param name="heightMap">Heightmap to apply falloff to (modified in-place)</param>
    /// <param name="strength">Seberapa kuat efek falloff (0.2-0.4 direkomendasikan)</param>
    public static void ApplyFalloff(float[,] heightMap, float strength)
    {
        int res = heightMap.GetLength(0);
        float invRes = 1f / res;

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                // Distance from edge normalized [0, 1]
                float nx = Mathf.Abs(x * invRes * 2f - 1f);
                float ny = Mathf.Abs(y * invRes * 2f - 1f);
                float edgeDist = Mathf.Max(nx, ny);

                // S-curve: smooth transition from center (0) to edge (1)
                float falloff = edgeDist * edgeDist * edgeDist /
                    (edgeDist * edgeDist * edgeDist + Mathf.Pow(1f - edgeDist, 3f));

                heightMap[x, y] -= falloff * strength;
                heightMap[x, y] = Mathf.Clamp01(heightMap[x, y]);
            }
        }
    }
}
