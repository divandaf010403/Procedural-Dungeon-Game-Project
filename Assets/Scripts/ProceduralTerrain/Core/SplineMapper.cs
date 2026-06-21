using UnityEngine;

/// <summary>
/// Evaluasi Hermite spline universal — mengubah nilai noise [0,1]
/// menjadi nilai terrain [0,1] melalui kurva kontrol.
/// 
/// Digunakan oleh semua layer untuk memetakan noise ke elevasi.
/// Smoothstep interpolation menjamin transisi halus tanpa patahan.
/// </summary>
public static class SplineMapper
{
    /// <summary>
    /// Evaluasi Hermite spline pada titik t.
    /// xs dan ys harus punya panjang sama dan xs terurut ascending.
    /// </summary>
    /// <param name="t">Input value [0,1]</param>
    /// <param name="xs">Control point X positions (ascending)</param>
    /// <param name="ys">Control point Y values (output)</param>
    /// <returns>Interpolated Y value at t</returns>
    public static float Evaluate(float t, float[] xs, float[] ys)
    {
        int n = xs.Length;

        // Clamp to endpoints
        if (t <= xs[0]) return ys[0];
        if (t >= xs[n - 1]) return ys[n - 1];

        // Find segment
        for (int i = 0; i < n - 1; i++)
        {
            if (t <= xs[i + 1])
            {
                float segmentT = (t - xs[i]) / (xs[i + 1] - xs[i]);
                // Smoothstep hermite: eliminates sharp joints between segments
                segmentT = segmentT * segmentT * (3f - 2f * segmentT);
                return Mathf.Lerp(ys[i], ys[i + 1], segmentT);
            }
        }

        return ys[n - 1];
    }

    /// <summary>
    /// Evaluasi dan klem hasil ke [0, 1].
    /// </summary>
    public static float EvaluateClamped(float t, float[] xs, float[] ys)
    {
        return Mathf.Clamp01(Evaluate(t, xs, ys));
    }
}
