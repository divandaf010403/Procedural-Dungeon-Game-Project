using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RoadGridHelper — tile-based road placement (SVS style).
///
/// Setiap cell integer = 1 prefab jalan.
/// PlaceStreetPositions() instantiate roadStraight per tile.
/// FixRoad() klasifikasi tetangga → destroy tile lama, instantiate prefab yang tepat:
///   roadEnd, roadStraight, roadCorner, road3Way, road4Way.
///
/// Identik dengan logika SVS RoadHelper, tapi support skala kota penuh
/// (cellSize != 1, posisi di-scale dari cell ke world).
/// </summary>
public class RoadGridHelper
{
    // Key = cell integer (grid space), value = GameObject prefab ter-instantiate
    public readonly Dictionary<Vector3Int, GameObject> roadDictionary =
        new Dictionary<Vector3Int, GameObject>();

    // Semua cell yang pernah di-place — dipakai FixRoad untuk klasifikasi tetangga
    private readonly HashSet<Vector3Int> gridSnapshot = new HashSet<Vector3Int>();

    // Expose untuk ExportRoadMap di RoadNetwork
    public HashSet<Vector3Int> GridSnapshot => gridSnapshot;

    // Cell ring road — dipakai FixRoad untuk klasifikasi T/+ di ring.
    // Set dari RoadNetwork.PlaceRingRoad().
    public readonly HashSet<Vector3Int> ringCells = new HashSet<Vector3Int>();

    // Batas ring — disalin dari RoadNetwork supaya decorator bisa tahu
    // batas ring tanpa membaca state RoadNetwork.
    private int ringMinX, ringMaxX, ringMinZ, ringMaxZ;
    public void SetRingBounds(int minX, int maxX, int minZ, int maxZ)
    {
        ringMinX = minX; ringMaxX = maxX;
        ringMinZ = minZ; ringMaxZ = maxZ;
    }

    // Hanya ujung segmen + semua cell yang perlu di-fix junctionnya
    private readonly HashSet<Vector3Int> fixRoadCandidates = new HashSet<Vector3Int>();

    private readonly Transform parent;
    public  readonly float     cellSize;

    // Ukuran tile dalam world units — dipakai CellToWorld untuk convert cell → world
    // cellSize = 1 (grid integer), tileWorldSize = ukuran visual prefab
    public float tileWorldSize = 1f;

    // Prefab referensi — di-set dari RoadNetwork
    public GameObject prefabStraight;
    public GameObject prefabCorner;
    public GameObject prefab3Way;
    public GameObject prefab4Way;
    public GameObject prefabEnd;

    // Scale diterapkan ke tiap tile saat instantiate
    public Vector3 tileScale = Vector3.one;

    public RoadGridHelper(Transform parent, float cellSize)
    {
        this.parent   = parent;
        this.cellSize = cellSize;
    }

    public List<Vector3Int> GetRoadPositions() => new List<Vector3Int>(roadDictionary.Keys);

    // -----------------------------------------------------------------------
    // PLACE
    // -----------------------------------------------------------------------

    /// <summary>
    /// Place N tile jalan lurus dari startPosition ke direction.
    /// Instantiate roadStraight per tile, skip jika sudah ada.
    /// Semua tile di-masukkan ke fixRoadCandidates supaya FixRoad
    /// bisa swap ke prefab yang tepat berdasarkan tetangga.
    /// </summary>
    public void PlaceStreetPositions(Vector3Int startPosition, Vector3Int direction, int length)
    {
        if (prefabStraight == null)
        {
            Debug.LogWarning("[RoadGridHelper] prefabStraight belum di-assign!");
            return;
        }

        // Rotasi default: arah Z (North-South) = 90°Y, arah X (East-West) = 180°Y
        // +90 offset karena prefab road punya rotation Y=90 secara default
        Quaternion rotation = (direction.x != 0)
            ? Quaternion.Euler(0f, 180f, 0f)
            : Quaternion.Euler(0f, 90f, 0f);

        for (int i = 0; i < length; i++)
        {
            var pos = startPosition + direction * i;
            if (roadDictionary.ContainsKey(pos)) continue;

            Vector3 worldPos = CellToWorld(pos);
            var tile = Object.Instantiate(prefabStraight, worldPos, rotation, parent);
            tile.transform.localScale = tileScale;
            tile.name = $"Road_{pos.x}_{pos.z}";

            roadDictionary.Add(pos, tile);
            gridSnapshot.Add(pos);

            // Hanya ujung segmen jadi kandidat fix junction
            if (i == 0 || i == length - 1)
                fixRoadCandidates.Add(pos);
        }
    }

    // -----------------------------------------------------------------------
    // FIX ROAD — SVS style
    // -----------------------------------------------------------------------

    /// <summary>
    /// Untuk setiap kandidat cell: hitung jumlah tetangga (N/S/E/W),
    /// destroy tile yang ada, instantiate prefab yang sesuai.
    ///   1 tetangga  → roadEnd
    ///   2 lurus     → roadStraight (dengan rotasi yang benar)
    ///   2 belok     → roadCorner
    ///   3 tetangga  → road3Way
    ///   4 tetangga  → road4Way
    /// </summary>
    public void FixRoad()
    {
        // Sweep semua cell — bukan hanya ujung segmen —
        // supaya junction di tengah segmen juga ter-detect.
        foreach (var pos in gridSnapshot)
        {
            if (!roadDictionary.ContainsKey(pos)) continue;

            bool hasN = gridSnapshot.Contains(pos + new Vector3Int( 0, 0,  1));
            bool hasS = gridSnapshot.Contains(pos + new Vector3Int( 0, 0, -1));
            bool hasE = gridSnapshot.Contains(pos + new Vector3Int( 1, 0,  0));
            bool hasW = gridSnapshot.Contains(pos + new Vector3Int(-1, 0,  0));

            int count = (hasN ? 1 : 0) + (hasS ? 1 : 0)
                      + (hasE ? 1 : 0) + (hasW ? 1 : 0);

            // Isolated tile (0 koneksi) — buang, bukan jadikan straight/end palsu.
            // FIX: ring cell TIDAK boleh dihapus — ring adalah loop tertutup,
            // tetap jadikan straight (bukan O) supaya tidak ada gap di ring.
            if (count == 0)
            {
                if (ringCells.Contains(pos))
                {
                    // Ring isolated — tetap jalan (straight), bukan endpoint.
                    // Orientasi ditentukan dari arah tetangga ring.
                    bool ringN = ringCells.Contains(pos + new Vector3Int( 0, 0,  1));
                    bool ringS = ringCells.Contains(pos + new Vector3Int( 0, 0, -1));
                    bool ringE = ringCells.Contains(pos + new Vector3Int( 1, 0,  0));
                    bool ringW = ringCells.Contains(pos + new Vector3Int(-1, 0,  0));
                    bool horiz = (ringE || ringW) && !(ringN || ringS);
                    var worldPos0 = CellToWorld(pos);
                    var keepTile = Object.Instantiate(prefabStraight != null ? prefabStraight : prefabEnd,
                        worldPos0,
                        horiz ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity,
                        parent);
                    keepTile.transform.localScale = tileScale;
                    keepTile.name = $"Road_{pos.x}_{pos.z}";
                    Object.DestroyImmediate(roadDictionary[pos]);
                    roadDictionary[pos] = keepTile;
                    continue;
                }
                Object.DestroyImmediate(roadDictionary[pos]);
                roadDictionary.Remove(pos);
                gridSnapshot.Remove(pos);
                continue;
            }

            // Hapus tile lama
            Object.DestroyImmediate(roadDictionary[pos]);

            Vector3    worldPos = CellToWorld(pos);
            Quaternion rot      = Quaternion.identity;
            GameObject prefab;

            if (ringCells.Contains(pos))
            {
                // ===== Ring cell: wajib menyambung ring, tidak boleh jadi O =====
                // Hitung koneksi yang mengikuti arah ring vs yang dari interior
                bool ringN = ringCells.Contains(pos + new Vector3Int( 0, 0,  1));
                bool ringS = ringCells.Contains(pos + new Vector3Int( 0, 0, -1));
                bool ringE = ringCells.Contains(pos + new Vector3Int( 1, 0,  0));
                bool ringW = ringCells.Contains(pos + new Vector3Int(-1, 0,  0));

                int ringArms = (ringN?1:0) + (ringS?1:0) + (ringE?1:0) + (ringW?1:0);

                if (ringArms >= 2 && count >= 4)
                {
                    // Ring + interior 2 arah = perempatan
                    prefab = prefab4Way != null ? prefab4Way : prefabStraight;
                }
                else if (ringArms >= 2 && count >= 3)
                {
                    // Ring 2 arah + 1 interior = T-junction
                    prefab = prefab3Way != null ? prefab3Way : prefabStraight;
                    if      (!hasN) rot = Quaternion.Euler(0f,   0f, 0f);
                    else if (!hasE) rot = Quaternion.Euler(0f,  90f, 0f);
                    else if (!hasS) rot = Quaternion.Euler(0f, 180f, 0f);
                    else            rot = Quaternion.Euler(0f, 270f, 0f);
                }
                else if (ringArms >= 1 && count >= 2)
                {
                    // Ring 1 arah + 1 interior = corner di ring
                    prefab = prefabCorner != null ? prefabCorner : prefabStraight;
                    if      (hasN && hasE) rot = Quaternion.Euler(0f,   0f, 0f);
                    else if (hasE && hasS) rot = Quaternion.Euler(0f,  90f, 0f);
                    else if (hasS && hasW) rot = Quaternion.Euler(0f, 180f, 0f);
                    else if (hasW && hasN) rot = Quaternion.Euler(0f, 270f, 0f);
                    else
                    {
                        // Ring 1 arah tanpa interior — straight di ring
                        prefab = prefabStraight;
                        rot    = (hasE && hasW) || (!hasN && !hasS && (ringE || ringW))
                            ? Quaternion.Euler(0f, 90f, 0f)
                            : Quaternion.identity;
                    }
                }
                else
                {
                    // Ring cell terisolasi di ring — tetap straight mengikuti ring
                    prefab = prefabStraight;
                    rot    = (hasE || hasW)
                        ? Quaternion.Euler(0f, 90f, 0f)
                        : Quaternion.identity;
                }
            }
            else if (count >= 4)
            {
                // Perempatan — tidak perlu rotasi
                prefab = prefab4Way != null ? prefab4Way : prefabStraight;
            }
            else if (count == 3)
            {
                // T-junction — rotasi berdasarkan arah yang tidak ada
                prefab = prefab3Way != null ? prefab3Way : prefabStraight;
                if      (!hasN) rot = Quaternion.Euler(0f,   0f, 0f); // T menghadap S
                else if (!hasE) rot = Quaternion.Euler(0f,  90f, 0f); // T menghadap W
                else if (!hasS) rot = Quaternion.Euler(0f, 180f, 0f); // T menghadap N
                else            rot = Quaternion.Euler(0f, 270f, 0f); // T menghadap E (!hasW)
            }
            else if (count == 2)
            {
                bool straight = (hasN && hasS) || (hasE && hasW);
                if (straight)
                {
                    // Lurus — H atau V
                    prefab = prefabStraight != null ? prefabStraight : prefabStraight;
                    rot    = hasE && hasW
                        ? Quaternion.Euler(0f, 90f, 0f)  // horizontal E-W
                        : Quaternion.identity;            // vertical N-S
                }
                else
                {
                    // Corner — 4 kemungkinan L-shape
                    prefab = prefabCorner != null ? prefabCorner : prefabStraight;
                    if      (hasN && hasE) rot = Quaternion.Euler(0f,   0f, 0f);
                    else if (hasE && hasS) rot = Quaternion.Euler(0f,  90f, 0f);
                    else if (hasS && hasW) rot = Quaternion.Euler(0f, 180f, 0f);
                    else                   rot = Quaternion.Euler(0f, 270f, 0f); // hasW && hasN
                }
            }
            else
            {
                // Dead-end (1 tetangga)
                prefab = prefabEnd != null ? prefabEnd : prefabStraight;
                if      (hasS) rot = Quaternion.Euler(0f,   0f, 0f); // ujung mengarah S
                else if (hasW) rot = Quaternion.Euler(0f,  90f, 0f);
                else if (hasN) rot = Quaternion.Euler(0f, 180f, 0f);
                else if (hasE) rot = Quaternion.Euler(0f, 270f, 0f);
            }

            if (prefab == null)
            {
                Debug.LogWarning($"[RoadGridHelper] Prefab null untuk pos {pos}, count={count}");
                roadDictionary[pos] = null;
                continue;
            }

            // +90° offset di Y karena prefab road punya rotation Y=90 secara default
            rot = rot * Quaternion.Euler(0f, 90f, 0f);

            var newTile = Object.Instantiate(prefab, worldPos, rot, parent);
            newTile.transform.localScale = tileScale;
            newTile.name = $"Road_{pos.x}_{pos.z}";
            roadDictionary[pos] = newTile;
        }
    }

    // -----------------------------------------------------------------------
    // HELPERS
    // -----------------------------------------------------------------------

    /// <summary>
    /// Cell integer → world position center of cell.
    /// Y memakai posisi Y parent (local Y = 0 relatif container) — jadi jalan
    /// ikut ketinggian cityGenerator/container. X/Z tetap grid world (tidak
    /// terpengaruh rotasi/scale parent).
    /// </summary>
    public Vector3 CellToWorld(Vector3Int cell)
    {
        float y = parent != null ? parent.position.y : 0f;
        return new Vector3(cell.x * tileWorldSize, y, cell.z * tileWorldSize);
    }

    /// <summary>Bitmask koneksi cell (N=1,E=2,S=4,W=8) dari snapshot — untuk decorator.</summary>
    public int GetMaskAt(Vector3Int cell)
    {
        int mask = 0;
        if (gridSnapshot.Contains(cell + new Vector3Int( 0, 0,  1))) mask |= 1;
        if (gridSnapshot.Contains(cell + new Vector3Int( 1, 0,  0))) mask |= 2;
        if (gridSnapshot.Contains(cell + new Vector3Int( 0, 0, -1))) mask |= 4;
        if (gridSnapshot.Contains(cell + new Vector3Int(-1, 0,  0))) mask |= 8;
        return mask;
    }

    /// <summary>Prefab tile yang ada di cell (null jika kosong) — untuk parent add-on.</summary>
    public GameObject GetTileAt(Vector3Int cell) =>
        roadDictionary.TryGetValue(cell, out var go) ? go : null;

    /// <summary>Apakah cell adalah bagian ring road.</summary>
    public bool IsRingCell(int x, int z) => ringCells.Contains(new Vector3Int(x, 0, z));

    /// <summary>Batas ring — untuk penempatan add-on di sepanjang ring.</summary>
    public int RingMinX => ringMinX;
    public int RingMinZ => ringMinZ;
    public int RingMaxX => ringMaxX;
    public int RingMaxZ => ringMaxZ;

    // -----------------------------------------------------------------------
    // REMOVE
    // -----------------------------------------------------------------------

    /// <summary>
    /// Hapus satu cell jalan (prefab + dari snapshot). Dipakai cleanup
    /// RemoveDisconnectedInnerRoads — cell terisolasi yang tidak reachable.
    /// </summary>
    public void RemoveRoadCell(Vector3Int pos)
    {
        if (roadDictionary.TryGetValue(pos, out var go) && go != null)
            Object.DestroyImmediate(go);
        roadDictionary.Remove(pos);
        gridSnapshot.Remove(pos);
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
        gridSnapshot.Clear();
        ringCells.Clear();
        ringMinX = ringMaxX = ringMinZ = ringMaxZ = 0;
    }
}
