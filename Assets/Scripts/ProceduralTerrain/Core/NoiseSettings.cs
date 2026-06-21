using UnityEngine;

/// <summary>
/// ScriptableObject preset untuk konfigurasi noise.
/// Bisa disimpan sebagai .asset file dan digunakan ulang untuk berbagai layer.
/// 
/// Buat via: Assets → Create → Terrain → Noise Settings
/// </summary>
[CreateAssetMenu(fileName = "NewNoiseSettings", menuName = "Terrain/Noise Settings")]
public class NoiseSettings : ScriptableObject
{
    [Tooltip("Frekuensi noise. Lebih besar = lebih banyak fitur per peta.\n" +
             "Continental: 1.0-2.0 | Erosion: 2.0-4.0 | Detail: 5.0-10.0")]
    public float frequency = 2f;

    [Tooltip("Jumlah lapisan noise ditumpuk. Lebih banyak = lebih detail.")]
    [Range(1, 8)]
    public int octaves = 3;

    [Tooltip("Penurunan amplitudo per octave. 0.5 = standar.")]
    [Range(0f, 1f)]
    public float persistence = 0.45f;

    [Tooltip("Peningkatan frekuensi per octave. 2.0 = standar.")]
    [Range(1f, 4f)]
    public float lacunarity = 2.0f;

    [Tooltip("Offset seed tambahan agar layer berbeda punya noise berbeda.")]
    public int seedOffset = 0;

    /// <summary>
    /// Factory method untuk membuat NoiseSettings sementara di runtime (tanpa .asset file).
    /// </summary>
    public static NoiseSettings Create(float frequency, int octaves, float persistence, float lacunarity, int seedOffset = 0)
    {
        var settings = ScriptableObject.CreateInstance<NoiseSettings>();
        settings.frequency = frequency;
        settings.octaves = octaves;
        settings.persistence = persistence;
        settings.lacunarity = lacunarity;
        settings.seedOffset = seedOffset;
        return settings;
    }
}
