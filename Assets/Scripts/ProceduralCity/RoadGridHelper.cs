using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RoadGridHelper — grid-based road placement.
///
/// Setiap cell integer = 1 tile jalan (lebar = cellSize).
/// Dictionary&lt;Vector3Int, GameObject&gt; menyimpan placeholder per cell.
/// FixRoad() klasifikasi neighbour → bangun 1 mesh gabungan terpadu.
///
/// BUG FIXES dibanding versi lama:
///   - gridSnapshot di-clear di Reset() → tidak ada stale data antar generate
///   - Straight tile pakai Square (bukan AddQuad) → tidak ada gap di junction
///   - Corner mesh pakai 4 vertex unik (bukan duplikat SW)
///   - Tile world position pakai origin offset dari parent jika ada
/// </summary>
public class RoadGridHelper
{
    // Key = cell integer (grid space), value = placeholder (bisa null setelah FixRoad)
    public readonly Dictionary<Vector3Int, GameObject> roadDictionary =
        new Dictionary<Vector3Int, GameObject>();

    // Semua cell yang pernah di-place — dipakai FixRoad untuk klasifikasi
    private HashSet<Vector3Int> gridSnapshot = new HashSet<Vector3Int>();

    // Hanya ujung-ujung segmen yang perlu di-fix junction
    private readonly HashSet<Vector3Int> fixRoadCandidates = new HashSet<Vector3Int>();

    private readonly GameObject parent;
    private readonly Material   roadMat;
    public  readonly float      cellSize;

    public RoadGridHelper(GameObject parent, Material roadMat, float cellSize)
    {
        this.parent   = parent;
        this.roadMat  = roadMat;
        this.cellSize = cellSize;
    }

    public List<Vector3Int> GetRoadPositions() => new List<Vector3Int>(roadDictionary.Keys);

    // -----------------------------------------------------------------------
    // PLACE
    // -----------------------------------------------------------------------

    /// <summary>
    /// Place N tile jalan lurus dari startPosition ke direction.
    /// Tile yang sudah ada di-skip (tidak double-place).
    /// </summary>
    public void PlaceStreetPositions(Vector3Int startPosition, Vector3Int direction, int length)
    {
        for (int i = 0; i < length; i++)
        {
            var pos = startPosition + direction * i;
            if (roadDictionary.ContainsKey(pos)) continue;

            // Placeholder kosong — FixRoad akan bangun mesh sesudahnya
            var tile = new GameObject($"RoadCell_{pos.x}_{pos.z}");
            tile.transform.SetParent(parent.transform);
            roadDictionary.Add(pos, tile);
            gridSnapshot.Add(pos);

            if (i == 0 || i == length - 1)
                fixRoadCandidates.Add(pos);
        }
    }

    // -----------------------------------------------------------------------
    // FIX / MESH BUILD
    // -----------------------------------------------------------------------

    /// <summary>
    /// Hapus semua placeholder, klasifikasi tiap cell berdasarkan 4-neighbour,
    /// lalu bangun 1 combined mesh untuk seluruh jaringan jalan.
    /// </summary>
    public void FixRoad()
    {
        // Hapus placeholder
        foreach (var go in roadDictionary.Values)
            if (go != null) Object.DestroyImmediate(go);
        roadDictionary.Clear();

        if (gridSnapshot.Count == 0) return;

        var verts = new List<Vector3>();
        var tris  = new List<int>();
        var uvs   = new List<Vector2>();

        foreach (var pos in gridSnapshot)
        {
            int bits = GetNeighbourBits(pos);
            // World center cell — ingat: pos adalah cell integer, world = pos * cellSize
            Vector3 center = new Vector3(pos.x * cellSize, 0.002f, pos.z * cellSize);
            float   h      = cellSize * 0.5f;

            // Semua tile pakai Square penuh → tidak ada gap di pertemuan sel manapun.
            // Pendekatan "full square per cell" adalah cara yang benar untuk tile-based road:
            //   - Straight H/V: square penuh = ok karena lebarnya sama dengan cellSize
            //   - Junction: square penuh = sudah benar
            // Gap hanya muncul jika straight tile dipersempit (rect < cellSize) lalu
            // bertemu junction — kita hindari dengan selalu pakai square.
            AddSquare(center, h, verts, tris, uvs);
        }

        BuildMesh(verts, tris, uvs, "RoadMesh_Combined");
    }

    // -----------------------------------------------------------------------
    // NEIGHBOUR CLASSIFICATION
    // -----------------------------------------------------------------------

    // Bit mask: N=bit0, E=bit1, S=bit2, W=bit3
    private int GetNeighbourBits(Vector3Int pos)
    {
        int bits = 0;
        if (gridSnapshot.Contains(pos + new Vector3Int( 0, 0,  1))) bits |= 1; // N
        if (gridSnapshot.Contains(pos + new Vector3Int( 1, 0,  0))) bits |= 2; // E
        if (gridSnapshot.Contains(pos + new Vector3Int( 0, 0, -1))) bits |= 4; // S
        if (gridSnapshot.Contains(pos + new Vector3Int(-1, 0,  0))) bits |= 8; // W
        return bits;
    }

    /// <summary>
    /// Klasifikasi tipe tile dari neighbour bits.
    /// Return: 0=End, 1=Straight_H, 2=Straight_V, 3=Corner, 4=T, 5=Cross
    /// </summary>
    public static int Classify(int bits)
    {
        int count = CountBits(bits);
        if (count >= 4) return 5; // Cross
        if (count == 3) return 4; // T
        if (count == 2)
        {
            // Straight: N+S atau E+W
            if (bits == 0b0101) return 2; // N+S = straight vertikal
            if (bits == 0b1010) return 1; // E+W = straight horizontal
            return 3;                      // diagonal pair = corner
        }
        // count 0 atau 1 = dead end
        return 0;
    }

    private static int CountBits(int n)
    {
        int c = 0;
        while (n != 0) { c += n & 1; n >>= 1; }
        return c;
    }

    // -----------------------------------------------------------------------
    // MESH HELPERS
    // -----------------------------------------------------------------------

    /// <summary>Square penuh di cell center c dengan half-extent h.</summary>
    private void AddSquare(Vector3 c, float h,
        List<Vector3> v, List<int> t, List<Vector2> u)
    {
        int b = v.Count;
        // 4 vertex CCW dari bawah-kiri (SW), searah jarum jam dari atas
        v.Add(new Vector3(c.x - h, c.y, c.z - h)); // 0 SW
        v.Add(new Vector3(c.x + h, c.y, c.z - h)); // 1 SE
        v.Add(new Vector3(c.x + h, c.y, c.z + h)); // 2 NE
        v.Add(new Vector3(c.x - h, c.y, c.z + h)); // 3 NW

        u.Add(new Vector2(0f, 0f));
        u.Add(new Vector2(1f, 0f));
        u.Add(new Vector2(1f, 1f));
        u.Add(new Vector2(0f, 1f));

        // Dua triangle, winding CW dari atas (Unity Y-up, front face Y+)
        t.Add(b + 0); t.Add(b + 2); t.Add(b + 1); // SW, NE, SE
        t.Add(b + 0); t.Add(b + 3); t.Add(b + 2); // SW, NW, NE
    }

    private void BuildMesh(List<Vector3> verts, List<int> tris, List<Vector2> uvs, string meshName)
    {
        // Unity mesh vertex limit = 65535 per sub-mesh untuk 16-bit index
        // Split jika perlu (tiap square = 4 vert, 6 index)
        const int MAX_VERTS = 65000;

        int offset = 0;
        int part   = 0;
        while (offset < verts.Count)
        {
            int count    = Mathf.Min(MAX_VERTS, verts.Count - offset);
            int triStart = (offset / 4) * 6; // 4 vert per quad → 6 index
            int triCount = (count / 4) * 6;
            triCount     = Mathf.Min(triCount, tris.Count - triStart);

            var subVerts = verts.GetRange(offset, count);
            var subUvs   = uvs.GetRange(offset, count);

            // Re-index triangles relatif ke sub-mesh
            var subTris = new List<int>(triCount);
            for (int i = triStart; i < triStart + triCount; i++)
                subTris.Add(tris[i] - offset);

            var mesh = new Mesh { name = $"{meshName}_p{part}" };
            mesh.SetVertices(subVerts);
            mesh.SetTriangles(subTris, 0);
            mesh.SetUVs(0, subUvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject($"RoadMeshPart_{part}");
            go.transform.SetParent(parent.transform);
            go.transform.localPosition = Vector3.zero;

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh    = mesh;
            mr.sharedMaterial = roadMat;

            // Daftarkan ke dictionary dengan key dummy agar Reset bisa hapus
            roadDictionary[new Vector3Int(-(99999 + part), 0, 0)] = go;

            offset += count;
            part++;
        }
    }

    // -----------------------------------------------------------------------
    // RESET
    // -----------------------------------------------------------------------

    public void Reset()
    {
        foreach (var go in roadDictionary.Values)
            if (go != null) Object.DestroyImmediate(go);

        roadDictionary.Clear();
        fixRoadCandidates.Clear();
        gridSnapshot.Clear(); // FIX: wajib clear agar generate ulang tidak campur data lama
    }
}
