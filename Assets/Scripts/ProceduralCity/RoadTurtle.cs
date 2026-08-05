using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turtle graphics state untuk L-System road generation.
/// Turtle bergerak di grid orthogonal (XZ plane).
/// Arah: 0=North(+Z), 1=East(+X), 2=South(-Z), 3=West(-X)
/// </summary>
public class RoadTurtle
{
    // State saat ini
    public float X;
    public float Z;
    public int   Dir;   // 0=N, 1=E, 2=S, 3=W
    public int   Depth; // kedalaman cabang

    // Stack untuk push/pop ([ dan ])
    private readonly Stack<TurtleState> stack = new Stack<TurtleState>();

    // Step size (panjang 1 segmen)
    public float StepSize;

    private struct TurtleState
    {
        public float x, z;
        public int   dir, depth;
    }

    public RoadTurtle(float x, float z, int dir, float stepSize)
    {
        X = x; Z = z; Dir = dir; Depth = 0;
        StepSize = stepSize;
    }

    /// <summary>Push state ke stack (karakter '[').</summary>
    public void Push()
    {
        stack.Push(new TurtleState { x = X, z = Z, dir = Dir, depth = Depth });
    }

    /// <summary>Pop state dari stack (karakter ']'). Return false jika stack kosong.</summary>
    public bool Pop()
    {
        if (stack.Count == 0) return false;
        var s = stack.Pop();
        X = s.x; Z = s.z; Dir = s.dir; Depth = s.depth;
        return true;
    }

    /// <summary>Belok kanan 90 derajat.</summary>
    public void TurnRight() => Dir = (Dir + 1) % 4;

    /// <summary>Belok kiri 90 derajat.</summary>
    public void TurnLeft() => Dir = (Dir + 3) % 4;

    /// <summary>Balik 180 derajat.</summary>
    public void TurnAround() => Dir = (Dir + 2) % 4;

    /// <summary>Posisi setelah maju 1 step ke arah Dir.</summary>
    public Vector2 PeekForward()
    {
        float nx = X + DirX() * StepSize;
        float nz = Z + DirZ() * StepSize;
        return new Vector2(nx, nz);
    }

    /// <summary>Maju 1 step ke arah Dir, return posisi lama dan baru.</summary>
    public (Vector3 from, Vector3 to) MoveForward()
    {
        var from = new Vector3(X, 0f, Z);
        X += DirX() * StepSize;
        Z += DirZ() * StepSize;
        return (from, new Vector3(X, 0f, Z));
    }

    public int DirX() { if (Dir == 1) return  1; if (Dir == 3) return -1; return 0; }
    public int DirZ() { if (Dir == 0) return  1; if (Dir == 2) return -1; return 0; }

    public int StackDepth => stack.Count;
}
