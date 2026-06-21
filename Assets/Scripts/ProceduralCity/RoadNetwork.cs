using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PROCEDURAL ORTHOGONAL CITY ROAD NETWORK
///
/// Generation order (strictly enforced):
///   Step 1 - Outer ring road   : Perfect closed-loop rectangle. Seed has zero effect.
///   Step 2 - Major roads       : Full-span axis-aligned arteries inside the ring.
///   Step 3 - Block detection   : Rectangular cells formed by current H/V lines.
///   Step 4a - Secondary roads  : Subdivides large blocks; validated before placing.
///   Step 4b - Local roads      : Sparse branches in still-large blocks (~40% skip).
///   Step 5  - Cleanup          : Removes overlaps, out-of-bounds, tiny segments.
///
/// Hard rules (enforced unconditionally):
///   - Every segment is PURELY horizontal (Z=const) or PURELY vertical (X=const).
///   - No diagonals, curves, or angled segments under ANY condition.
///   - Seed affects only spacing/jitter amounts, never road direction.
///   - Every segment is validated before commit (boundary, min-length, near-duplicate).
///   - Intersections are only: straight, 90-degree corner, T-junction, 4-way crossroad.
/// </summary>
[System.Serializable]
public class RoadNetwork : MonoBehaviour
{
    // -----------------------------------------------------------------------
    // Public road lists (consumed by the rest of the city pipeline)
    // -----------------------------------------------------------------------
    public List<RoadSegment> horizontalRoads = new List<RoadSegment>();
    public List<RoadSegment> verticalRoads   = new List<RoadSegment>();

    // Legacy lists kept so other scripts that reference them still compile
    public List<RoadSegment> radialRoads   = new List<RoadSegment>();
    public List<RoadSegment> ringRoads     = new List<RoadSegment>();
    public List<RoadSegment> arterialRoads = new List<RoadSegment>();
    public List<RoadSegment> gridRoads     = new List<RoadSegment>();

    public List<Vector3>   intersections = new List<Vector3>();
    public List<CityBlock> blocks        = new List<CityBlock>();

    // -----------------------------------------------------------------------
    // Private state
    // -----------------------------------------------------------------------
    private CityGenerator cityGenerator;
    private System.Random rng;

    private float   halfSize;
    private Vector3 origin;
    private float   roadWidth;

    // Centre-line axis positions of every committed road
    private List<float> hLines = new List<float>(); // Z of each horizontal road
    private List<float> vLines = new List<float>(); // X of each vertical road

    private GameObject       roadMeshObject;
    private List<GameObject> intersectionMarkers = new List<GameObject>();

    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------
    private const float MIN_SEG_LEN   = 8f;    // Reject segments shorter than this
    private const float MIN_BLOCK_DIM = 18f;   // Blocks thinner than this are skipped
    private const float EPS           = 0.05f; // Float equality epsilon

    // Width multipliers per hierarchy level
    private const float RING_W  = 1.6f;
    private const float MAJOR_W = 1.3f;
    private const float SEC_W   = 1.0f;
    private const float LOC_W   = 0.75f;

    // =======================================================================
    // PUBLIC API
    // =======================================================================
    public void Initialize(CityGenerator generator)
    {
        cityGenerator = generator;
        rng = new System.Random(cityGenerator.randomSeed);
    }

    public void GenerateRoads()
    {
        ClearRoads();
        rng       = new System.Random(cityGenerator.randomSeed);
        halfSize  = cityGenerator.citySize * 0.5f;
        origin    = cityGenerator.transform.position;
        roadWidth = cityGenerator.roadWidth;

        // Step 1 - Outer ring road (deterministic, seed-independent)
        StepOuterRingRoad();

        // Step 2 - Major internal arteries
        StepMajorRoads();

        // Step 3 - Detect cells from current grid
        RebuildBlocks();

        // Step 4a - Secondary roads
        StepSecondaryRoads();
        RebuildBlocks();

        // Step 4b - Local roads
        StepLocalRoads();
        RebuildBlocks();

        // Step 5 - Cleanup
        StepCleanup();
        RebuildBlocks();

        // Build intersection list and render mesh
        ComputeIntersections();
        BuildRoadMesh();

        Debug.Log($"[RoadNetwork] Done: {horizontalRoads.Count}H + {verticalRoads.Count}V roads, "
                + $"{intersections.Count} intersections, {blocks.Count} blocks.");
    }

    // =======================================================================
    // STEP 1 - OUTER RING ROAD
    // Four axis-aligned segments forming a closed loop rectangle.
    // Position derived from citySize only; randomness has ZERO influence.
    // =======================================================================
    private void StepOuterRingRoad()
    {
        float rw   = roadWidth * RING_W;
        float half = rw * 0.5f;

        float zS = origin.z - halfSize + half; // South
        float zN = origin.z + halfSize - half; // North
        float xW = origin.x - halfSize + half; // West
        float xE = origin.x + halfSize - half; // East

        ForceH(zS, xW, xE, rw); // South edge
        ForceH(zN, xW, xE, rw); // North edge
        ForceV(xW, zS, zN, rw); // West edge
        ForceV(xE, zS, zN, rw); // East edge

        Debug.Log("[RoadNetwork] Step 1 - Ring road placed.");
    }

    // =======================================================================
    // STEP 2 - MAJOR ROADS
    // Full-span straight roads that cross the full interior.
    // Count and spacing are seeded; direction is always strictly H or V.
    // =======================================================================
    private void StepMajorRoads()
    {
        float rw = roadWidth * MAJOR_W;

        float innerZmin = origin.z - halfSize + roadWidth * RING_W;
        float innerZmax = origin.z + halfSize - roadWidth * RING_W;
        float innerXmin = origin.x - halfSize + roadWidth * RING_W;
        float innerXmax = origin.x + halfSize - roadWidth * RING_W;

        float span  = innerZmax - innerZmin;
        float step  = cityGenerator.blockSize * RandF(2.2f, 3.2f);
        int   count = Mathf.Clamp(Mathf.RoundToInt(span / step), 2, 7);

        EvenRoads(true,  count, innerZmin, innerZmax, innerXmin, innerXmax, rw);
        EvenRoads(false, count, innerXmin, innerXmax, innerZmin, innerZmax, rw);

        Debug.Log("[RoadNetwork] Step 2 - Major roads placed.");
    }

    /// <summary>
    /// Places count roads evenly in [posMin, posMax] with up to 15% seeded jitter.
    /// isHorizontal=true  -> horizontal roads (pos = Z, span = X range)
    /// isHorizontal=false -> vertical roads   (pos = X, span = Z range)
    /// </summary>
    private void EvenRoads(bool isHorizontal, int count,
                           float posMin, float posMax,
                           float spanMin, float spanMax, float width)
    {
        if (count <= 0) return;
        float gap    = (posMax - posMin) / (count + 1);
        float jitter = gap * 0.15f;

        for (int i = 1; i <= count; i++)
        {
            float pos = posMin + gap * i + RandF(-jitter, jitter);
            pos = Mathf.Clamp(pos, posMin + width, posMax - width);

            if (isHorizontal) TryH(pos, spanMin, spanMax, width);
            else              TryV(pos, spanMin, spanMax, width);
        }
    }

    // =======================================================================
    // STEP 4a - SECONDARY ROADS
    // Splits blocks wider than ~1.8x blockSize. ~25% of blocks left open.
    // =======================================================================
    private void StepSecondaryRoads()
    {
        float rw        = roadWidth * SEC_W;
        float threshold = cityGenerator.blockSize * 1.8f;
        var   snap      = new List<CityBlock>(blocks);

        foreach (var b in snap)
        {
            if (RandF(0f, 1f) < 0.25f) continue; // preserve open space

            if (b.size.x > threshold)
            {
                float x = b.center.x + b.size.x * RandF(-0.15f, 0.15f);
                TryV(x,
                     b.center.z - b.size.y * 0.5f,
                     b.center.z + b.size.y * 0.5f,
                     rw);
            }

            if (b.size.y > threshold)
            {
                float z = b.center.z + b.size.y * RandF(-0.15f, 0.15f);
                TryH(z,
                     b.center.x - b.size.x * 0.5f,
                     b.center.x + b.size.x * 0.5f,
                     rw);
            }
        }

        Debug.Log("[RoadNetwork] Step 4a - Secondary roads placed.");
    }

    // =======================================================================
    // STEP 4b - LOCAL ROADS
    // Sparse branches in blocks still larger than ~1.4x blockSize.
    // ~40% skip rate to keep open areas.
    // =======================================================================
    private void StepLocalRoads()
    {
        float rw        = roadWidth * LOC_W;
        float threshold = cityGenerator.blockSize * 1.4f;
        var   snap      = new List<CityBlock>(blocks);

        foreach (var b in snap)
        {
            if (b.size.x < threshold && b.size.y < threshold) continue;
            if (RandF(0f, 1f) < 0.40f) continue; // preserve open space

            if (b.size.x > threshold)
            {
                float x = b.center.x + b.size.x * RandF(-0.2f, 0.2f);
                TryV(x,
                     b.center.z - b.size.y * 0.5f,
                     b.center.z + b.size.y * 0.5f,
                     rw);
            }

            if (b.size.y > threshold)
            {
                float z = b.center.z + b.size.y * RandF(-0.2f, 0.2f);
                TryH(z,
                     b.center.x - b.size.x * 0.5f,
                     b.center.x + b.size.x * 0.5f,
                     rw);
            }
        }

        Debug.Log("[RoadNetwork] Step 4b - Local roads placed.");
    }

    // =======================================================================
    // STEP 5 - CLEANUP
    // Removes out-of-bounds, too-short, and duplicate segments.
    // Rebuilds hLines/vLines from surviving segments.
    // =======================================================================
    private void StepCleanup()
    {
        horizontalRoads = Cleaned(horizontalRoads);
        verticalRoads   = Cleaned(verticalRoads);

        hLines.Clear();
        vLines.Clear();
        foreach (var s in horizontalRoads) hLines.Add(s.start.z);
        foreach (var s in verticalRoads)   vLines.Add(s.start.x);
    }

    private List<RoadSegment> Cleaned(List<RoadSegment> src)
    {
        var clean = new List<RoadSegment>();
        var seen  = new HashSet<string>();

        float minX = origin.x - halfSize - EPS;
        float maxX = origin.x + halfSize + EPS;
        float minZ = origin.z - halfSize - EPS;
        float maxZ = origin.z + halfSize + EPS;

        foreach (var s in src)
        {
            if (s.start.x < minX || s.start.x > maxX) continue;
            if (s.end.x   < minX || s.end.x   > maxX) continue;
            if (s.start.z < minZ || s.start.z > maxZ) continue;
            if (s.end.z   < minZ || s.end.z   > maxZ) continue;

            if (Vector3.Distance(s.start, s.end) < MIN_SEG_LEN) continue;

            string key = $"{Mathf.Round(s.start.x * 2f)},{Mathf.Round(s.start.z * 2f)}"
                       + $"->{Mathf.Round(s.end.x * 2f)},{Mathf.Round(s.end.z * 2f)}";
            if (seen.Contains(key)) continue;
            seen.Add(key);

            clean.Add(s);
        }
        return clean;
    }

    // =======================================================================
    // COMMIT METHODS
    // ALL road placement goes through these - orthogonality enforced here.
    // =======================================================================

    // Force-commit (ring road only, bypasses FarEnough check)
    private void ForceH(float z, float x0, float x1, float w)
    {
        hLines.Add(z);
        horizontalRoads.Add(new RoadSegment(
            new Vector3(Mathf.Min(x0, x1), 0f, z),
            new Vector3(Mathf.Max(x0, x1), 0f, z), w));
    }

    private void ForceV(float x, float z0, float z1, float w)
    {
        vLines.Add(x);
        verticalRoads.Add(new RoadSegment(
            new Vector3(x, 0f, Mathf.Min(z0, z1)),
            new Vector3(x, 0f, Mathf.Max(z0, z1)), w));
    }

    // Validated commit - returns true if segment was placed
    private bool TryH(float z, float x0, float x1, float w)
    {
        if (!FarEnough(z, hLines))                 return false;
        if (!InBounds(x0, z) || !InBounds(x1, z)) return false;
        if (Mathf.Abs(x1 - x0) < MIN_SEG_LEN)     return false;

        hLines.Add(z);
        horizontalRoads.Add(new RoadSegment(
            new Vector3(Mathf.Min(x0, x1), 0f, z),
            new Vector3(Mathf.Max(x0, x1), 0f, z), w));
        return true;
    }

    private bool TryV(float x, float z0, float z1, float w)
    {
        if (!FarEnough(x, vLines))                 return false;
        if (!InBounds(x, z0) || !InBounds(x, z1)) return false;
        if (Mathf.Abs(z1 - z0) < MIN_SEG_LEN)     return false;

        vLines.Add(x);
        verticalRoads.Add(new RoadSegment(
            new Vector3(x, 0f, Mathf.Min(z0, z1)),
            new Vector3(x, 0f, Mathf.Max(z0, z1)), w));
        return true;
    }

    // =======================================================================
    // VALIDATION
    // =======================================================================

    /// <summary>
    /// Ensures proposed position is at least 3 road-widths from every
    /// existing line on the same axis, preventing near-duplicate parallels.
    /// </summary>
    private bool FarEnough(float pos, List<float> existing)
    {
        float minGap = roadWidth * 3f;
        foreach (float e in existing)
            if (Mathf.Abs(e - pos) < minGap) return false;
        return true;
    }

    private bool InBounds(float x, float z)
    {
        float pad = roadWidth * 0.5f;
        return x >= origin.x - halfSize + pad - EPS
            && x <= origin.x + halfSize - pad + EPS
            && z >= origin.z - halfSize + pad - EPS
            && z <= origin.z + halfSize - pad + EPS;
    }

    // =======================================================================
    // INTERSECTION COMPUTATION
    // Pure orthogonal grid: every (H road, V road) pair that spatially
    // overlaps produces exactly one intersection at (v.x, h.z).
    // =======================================================================
    private void ComputeIntersections()
    {
        intersections.Clear();
        var seen = new HashSet<long>();

        foreach (var h in horizontalRoads)
        {
            float hMinX = Mathf.Min(h.start.x, h.end.x) - EPS;
            float hMaxX = Mathf.Max(h.start.x, h.end.x) + EPS;
            float hz    = h.start.z;

            foreach (var v in verticalRoads)
            {
                float vMinZ = Mathf.Min(v.start.z, v.end.z) - EPS;
                float vMaxZ = Mathf.Max(v.start.z, v.end.z) + EPS;
                float vx    = v.start.x;

                if (vx < hMinX || vx > hMaxX) continue;
                if (hz < vMinZ || hz > vMaxZ) continue;

                long key = ((long)(Mathf.Round(vx * 10f) + 32768)) * 1_000_000L
                         +  (long)(Mathf.Round(hz * 10f) + 32768);
                if (seen.Contains(key)) continue;
                seen.Add(key);

                intersections.Add(new Vector3(vx, 0f, hz));
            }
        }
    }

    // =======================================================================
    // BLOCK LIST REBUILD
    // Scans every adjacent (H-line pair) x (V-line pair) to find cells.
    // =======================================================================
    private void RebuildBlocks()
    {
        blocks.Clear();

        var sortH = new List<float>(hLines); sortH.Sort();
        var sortV = new List<float>(vLines); sortV.Sort();

        for (int i = 0; i < sortH.Count - 1; i++)
        {
            for (int j = 0; j < sortV.Count - 1; j++)
            {
                // Interior edges (strip away road surface)
                float z0 = sortH[i    ] + roadWidth * 0.5f;
                float z1 = sortH[i + 1] - roadWidth * 0.5f;
                float x0 = sortV[j    ] + roadWidth * 0.5f;
                float x1 = sortV[j + 1] - roadWidth * 0.5f;

                float w = x1 - x0;
                float h = z1 - z0;

                if (w < MIN_BLOCK_DIM || h < MIN_BLOCK_DIM) continue;

                blocks.Add(new CityBlock
                {
                    center    = new Vector3((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f),
                    size      = new Vector2(w, h),
                    blockType = BlockType.Residential
                });
            }
        }
    }

    // =======================================================================
    // ROAD MESH GENERATION
    // Simple reliable approach:
    //   1. Full-length flat quad for every road segment (same as before).
    //   2. Per intersection: solid centre box (uses actual road widths).
    //   3. Per intersection corner: quarter-circle fan fills the notch in
    //      the block corner so kerbs look rounded.
    // The road quads and intersection box overlap intentionally - the box
    // sits on top and covers the join.  No trimming needed.
    // =======================================================================
    private void BuildRoadMesh()
    {
        if (roadMeshObject != null)
        {
            if (Application.isPlaying) Destroy(roadMeshObject);
            else DestroyImmediate(roadMeshObject);
        }

        roadMeshObject = new GameObject("RoadMesh");
        roadMeshObject.transform.SetParent(cityGenerator.transform);
        cityGenerator.RegisterSpawnedObject(roadMeshObject);

        MeshFilter   mf = roadMeshObject.AddComponent<MeshFilter>();
        MeshRenderer mr = roadMeshObject.AddComponent<MeshRenderer>();

        mr.sharedMaterial = cityGenerator.roadMaterial != null
            ? cityGenerator.roadMaterial
            : CityGenerator.CreateMaterial("Road", new Color(0.18f, 0.18f, 0.20f));

        var verts = new List<Vector3>();
        var tris  = new List<int>();
        var uvs   = new List<Vector2>();
        int vi    = 0;

        // 1. Full road quads
        foreach (var seg in horizontalRoads) vi = AddSegMesh(seg, verts, tris, uvs, vi);
        foreach (var seg in verticalRoads)   vi = AddSegMesh(seg, verts, tris, uvs, vi);

        // 2. Intersection centre boxes + 3. corner fillets
        foreach (var pt in intersections)
        {
            vi = AddCrossMesh(pt, verts, tris, uvs, vi);
            vi = AddCornerFillets(pt, verts, tris, uvs, vi);
        }

        var mesh = new Mesh
        {
            name        = "CityRoadMesh",
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mf.mesh = mesh;
    }

    /// <summary>One flat quad for a full road segment.</summary>
    private int AddSegMesh(RoadSegment seg,
                           List<Vector3> verts, List<int> tris,
                           List<Vector2> uvs, int si)
    {
        Vector3 dir  = (seg.end - seg.start).normalized;
        Vector3 perp = Vector3.Cross(dir, Vector3.up) * (seg.width * 0.5f);

        verts.Add(seg.start - perp);
        verts.Add(seg.start + perp);
        verts.Add(seg.end   + perp);
        verts.Add(seg.end   - perp);

        float uvLen = Mathf.Max(Vector3.Distance(seg.start, seg.end) / seg.width, 0.01f);
        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(1f, 0f));
        uvs.Add(new Vector2(1f, uvLen));
        uvs.Add(new Vector2(0f, uvLen));

        tris.Add(si); tris.Add(si + 1); tris.Add(si + 2);
        tris.Add(si); tris.Add(si + 2); tris.Add(si + 3);
        return si + 4;
    }

    /// <summary>
    /// Solid rectangle fill at intersection centre, sized to the actual road widths.
    /// </summary>
    private int AddCrossMesh(Vector3 c,
                             List<Vector3> verts, List<int> tris,
                             List<Vector2> uvs, int si)
    {
        float hW = roadWidth, vW = roadWidth;
        foreach (var h in horizontalRoads)
        {
            if (Mathf.Abs(h.start.z - c.z) > EPS) continue;
            float minX = Mathf.Min(h.start.x, h.end.x) - EPS;
            float maxX = Mathf.Max(h.start.x, h.end.x) + EPS;
            if (c.x >= minX && c.x <= maxX) { hW = h.width; break; }
        }
        foreach (var v in verticalRoads)
        {
            if (Mathf.Abs(v.start.x - c.x) > EPS) continue;
            float minZ = Mathf.Min(v.start.z, v.end.z) - EPS;
            float maxZ = Mathf.Max(v.start.z, v.end.z) + EPS;
            if (c.z >= minZ && c.z <= maxZ) { vW = v.width; break; }
        }

        float hw = hW * 0.5f; // half-width in Z
        float vw = vW * 0.5f; // half-width in X

        verts.Add(c + new Vector3(-vw, 0f, -hw));
        verts.Add(c + new Vector3(-vw, 0f,  hw));
        verts.Add(c + new Vector3( vw, 0f,  hw));
        verts.Add(c + new Vector3( vw, 0f, -hw));
        uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(0f, 1f));
        uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(1f, 0f));
        tris.Add(si); tris.Add(si + 1); tris.Add(si + 2);
        tris.Add(si); tris.Add(si + 2); tris.Add(si + 3);
        return si + 4;
    }

    /// <summary>
    /// Adds quarter-circle fillet fans at each corner of an intersection.
    ///
    /// Visual idea:
    ///   The intersection centre box has 4 corners.  At each corner the road
    ///   surface currently has a sharp right-angle notch cut into the block.
    ///   We fill that notch with a quarter-circle fan so the kerb looks rounded.
    ///
    ///   Each fan:
    ///     pivot  = intersection corner point (±hw, ±vw from centre)
    ///     radius = fillet radius (slightly less than half road width)
    ///     arc    = 90° sweep pointing INTO the block (away from road centre)
    ///     fan centre = pivot, triangles fan outward along the arc
    ///
    ///   This is ONLY a mesh change - road logic is not affected at all.
    /// </summary>
    private int AddCornerFillets(Vector3 center,
                                 List<Vector3> verts, List<int> tris,
                                 List<Vector2> uvs, int si)
    {
        const int STEPS = 8;

        // Find actual widths of the H and V roads at this intersection
        float hW = roadWidth, vW = roadWidth;
        bool  hasH = false, hasV = false;

        foreach (var h in horizontalRoads)
        {
            if (Mathf.Abs(h.start.z - center.z) > EPS) continue;
            float minX = Mathf.Min(h.start.x, h.end.x) - EPS;
            float maxX = Mathf.Max(h.start.x, h.end.x) + EPS;
            if (center.x < minX || center.x > maxX) continue;
            hasH = true; hW = h.width; break;
        }
        foreach (var v in verticalRoads)
        {
            if (Mathf.Abs(v.start.x - center.x) > EPS) continue;
            float minZ = Mathf.Min(v.start.z, v.end.z) - EPS;
            float maxZ = Mathf.Max(v.start.z, v.end.z) + EPS;
            if (center.z < minZ || center.z > maxZ) continue;
            hasV = true; vW = v.width; break;
        }

        if (!hasH || !hasV) return si;

        float hw = hW * 0.5f;  // half-width of the horizontal road (extends in ±Z)
        float vw = vW * 0.5f;  // half-width of the vertical   road (extends in ±X)

        // Fillet radius: 80% of the smaller half-width
        float radius = Mathf.Min(hw, vw) * 0.8f;
        if (radius < 0.3f) return si;

        // ----------------------------------------------------------------
        // Four corners of the intersection box in XZ:
        //   corner position = center + (±vw, ±hw)   [X, Z offsets]
        //
        // At each corner, the fillet arc sweeps 90° AWAY from the centre
        // (i.e. into the block).  The arc goes from the Z-edge of the corner
        // to the X-edge of the corner.
        //
        // Corner layout (top-down, X right, Z up):
        //   NW(-vw,+hw)   NE(+vw,+hw)
        //       ┌─────────────┐
        //       │   centre    │
        //       └─────────────┘
        //   SW(-vw,-hw)   SE(+vw,-hw)
        //
        // For NE corner: pivot is the actual box corner (+vw, +hw).
        //   The fillet arc bulges OUTWARD (toward +X+Z), so the pivot is
        //   shifted inward by radius along BOTH axes:
        //     arcPivot = (corner.x - radius, corner.z - radius)
        //   The arc sweeps from angle 0° (+X) to 90° (+Z).
        //
        // Similarly for the other three corners.
        // ----------------------------------------------------------------

        // (signX, signZ) = direction from centre to corner
        int[,] signs = { { -1, -1 }, { 1, -1 }, { 1, 1 }, { -1, 1 } };
        // For each corner the arc sweeps from angle startDeg to endDeg
        // The arc fills the gap between the two road edges at that corner.
        // Angles measured CCW from +X axis in XZ plane.
        float[,] arcAngles = {
            // SW: from 270° down to 180° (sweeping through the block corner)
            { 270f, 180f },
            // SE: from   0° down to 270°
            { 360f, 270f },
            // NE: from  90° down to   0°
            {  90f,   0f },
            // NW: from 180° down to  90°
            { 180f,  90f }
        };

        for (int c = 0; c < 4; c++)
        {
            int sx = signs[c, 0]; // ±1 in X
            int sz = signs[c, 1]; // ±1 in Z

            // Corner of the intersection box
            float cornerX = center.x + sx * vw;
            float cornerZ = center.z + sz * hw;

            // Arc pivot is the corner shifted INWARD by radius on both axes
            float pivotX = cornerX - sx * radius;
            float pivotZ = cornerZ - sz * radius;
            Vector3 pivot = new Vector3(pivotX, 0f, pivotZ);

            float aDeg0 = arcAngles[c, 0];
            float aDeg1 = arcAngles[c, 1];

            // Fan: pivot vertex + arc ring vertices
            int fanBase = si;
            verts.Add(pivot);
            uvs.Add(new Vector2(0.5f, 0.5f));
            si++;

            for (int s = 0; s <= STEPS; s++)
            {
                float t   = (float)s / STEPS;
                float ang = Mathf.Lerp(aDeg0, aDeg1, t) * Mathf.Deg2Rad;
                float px  = pivotX + Mathf.Cos(ang) * radius;
                float pz  = pivotZ + Mathf.Sin(ang) * radius;
                verts.Add(new Vector3(px, 0f, pz));
                uvs.Add(new Vector2(t, (float)s / STEPS));
                si++;
            }

            // Fan triangles (each tri: pivot, arc[s], arc[s+1])
            for (int s = 0; s < STEPS; s++)
            {
                tris.Add(fanBase);
                tris.Add(fanBase + 1 + s);
                tris.Add(fanBase + 2 + s);
            }
        }

        return si;
    }

    // =======================================================================
    // UTILITY
    // =======================================================================
    private float RandF(float min, float max)
        => min + (float)rng.NextDouble() * (max - min);

    // =======================================================================
    // CLEAR
    // =======================================================================
    public void ClearRoads()
    {
        horizontalRoads.Clear();
        verticalRoads.Clear();
        radialRoads.Clear();
        ringRoads.Clear();
        arterialRoads.Clear();
        gridRoads.Clear();
        intersections.Clear();
        blocks.Clear();
        hLines.Clear();
        vLines.Clear();

        if (roadMeshObject != null)
        {
            if (Application.isPlaying) Destroy(roadMeshObject);
            else DestroyImmediate(roadMeshObject);
            roadMeshObject = null;
        }

        foreach (var m in intersectionMarkers)
            if (m != null)
            {
                if (Application.isPlaying) Destroy(m);
                else DestroyImmediate(m);
            }
        intersectionMarkers.Clear();
    }

    // =======================================================================
    // GIZMOS (Scene-view debug visualisation)
    // =======================================================================
    public void DrawGizmos()
    {
        if (cityGenerator == null) return;

        float   hs  = cityGenerator.citySize * 0.5f;
        Vector3 org = cityGenerator.transform.position;

        // City boundary
        Gizmos.color = Color.yellow;
        Vector3 sw = org + new Vector3(-hs, 0f, -hs);
        Vector3 se = org + new Vector3( hs, 0f, -hs);
        Vector3 ne = org + new Vector3( hs, 0f,  hs);
        Vector3 nw = org + new Vector3(-hs, 0f,  hs);
        Gizmos.DrawLine(sw, se); Gizmos.DrawLine(se, ne);
        Gizmos.DrawLine(ne, nw); Gizmos.DrawLine(nw, sw);

        // Horizontal roads (gray)
        Gizmos.color = Color.gray;
        foreach (var r in horizontalRoads) Gizmos.DrawLine(r.start, r.end);

        // Vertical roads (blue)
        Gizmos.color = Color.blue;
        foreach (var r in verticalRoads) Gizmos.DrawLine(r.start, r.end);

        // Intersections (red spheres)
        Gizmos.color = Color.red;
        foreach (var pt in intersections)
            Gizmos.DrawSphere(pt, cityGenerator.roadWidth * 0.3f);

        // Blocks (cyan wireframes)
        Gizmos.color = Color.cyan;
        foreach (var b in blocks)
            Gizmos.DrawWireCube(b.center, new Vector3(b.size.x, 0.1f, b.size.y));
    }
}

// ===========================================================================
// DATA STRUCT
// ===========================================================================
[System.Serializable]
public struct RoadSegment
{
    public Vector3 start;
    public Vector3 end;
    public float   width;

    public RoadSegment(Vector3 start, Vector3 end, float width)
    {
        this.start = start;
        this.end   = end;
        this.width = width;
    }
}
