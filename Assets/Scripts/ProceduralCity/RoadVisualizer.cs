using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Membaca string hasil L-System dan menjalankan turtle untuk menghasilkan
/// daftar road segments. Setiap segmen = 1 pasang (start, end) di grid XZ.
///
/// Simbol yang dikenali:
///   F  = maju 1 step, buat jalan
///   f  = maju 1 step, tidak buat jalan
///   +  = belok kanan 90°
///   -  = belok kiri 90°
///   |  = balik 180°
///   [  = push state
///   ]  = pop state
///   X  = growth marker (tidak dieksekusi, hanya untuk L-system expansion)
/// </summary>
public class RoadVisualizer
{
    public struct SegmentData
    {
        public Vector3 start;
        public Vector3 end;
        public int     depth;   // kedalaman cabang saat dibuat
    }

    private readonly List<SegmentData> segments = new List<SegmentData>();
    private readonly float             stepSize;
    private readonly float             cityHalfSize;
    private readonly Vector3           origin;

    public RoadVisualizer(float stepSize, float cityHalfSize, Vector3 origin)
    {
        this.stepSize     = stepSize;
        this.cityHalfSize = cityHalfSize;
        this.origin       = origin;
    }

    public List<SegmentData> Visualize(string lsystemString, float startX, float startZ, int startDir)
    {
        segments.Clear();

        var turtle = new RoadTurtle(startX, startZ, startDir, stepSize);

        foreach (char c in lsystemString)
        {
            switch (c)
            {
                case 'F':
                    var (from, to) = turtle.MoveForward();
                    if (IsInBounds(to))
                        segments.Add(new SegmentData { start = from, end = to, depth = turtle.StackDepth });
                    else
                    {
                        // Clamp ke batas kota
                        var clamped = ClampToBounds(from, to);
                        if (Vector3.Distance(from, clamped) > stepSize * 0.1f)
                            segments.Add(new SegmentData { start = from, end = clamped, depth = turtle.StackDepth });
                        // Reset turtle ke batas agar tidak keluar lebih jauh
                        turtle.X = clamped.x;
                        turtle.Z = clamped.z;
                    }
                    break;

                case 'f':
                    turtle.MoveForward(); // maju tanpa buat jalan
                    break;

                case '+':
                    turtle.TurnRight();
                    break;

                case '-':
                    turtle.TurnLeft();
                    break;

                case '|':
                    turtle.TurnAround();
                    break;

                case '[':
                    turtle.Push();
                    break;

                case ']':
                    turtle.Pop();
                    break;

                // X, Y, Z, dll = growth markers, tidak dieksekusi
                default:
                    break;
            }
        }

        return segments;
    }

    private bool IsInBounds(Vector3 p)
    {
        float margin = stepSize * 0.5f;
        return p.x >= origin.x - cityHalfSize + margin
            && p.x <= origin.x + cityHalfSize - margin
            && p.z >= origin.z - cityHalfSize + margin
            && p.z <= origin.z + cityHalfSize - margin;
    }

    private Vector3 ClampToBounds(Vector3 from, Vector3 to)
    {
        float minX = origin.x - cityHalfSize + stepSize * 0.5f;
        float maxX = origin.x + cityHalfSize - stepSize * 0.5f;
        float minZ = origin.z - cityHalfSize + stepSize * 0.5f;
        float maxZ = origin.z + cityHalfSize - stepSize * 0.5f;

        return new Vector3(
            Mathf.Clamp(to.x, minX, maxX),
            0f,
            Mathf.Clamp(to.z, minZ, maxZ));
    }

    public List<SegmentData> GetSegments() => segments;
}
