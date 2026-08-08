using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RoadAddOnDecorator — menempatkan perlengkapan jalan (traffic light &
/// streetlight) pada grid tile yang SUDAH di-FixRoad (FixRoad() selesai,
/// roadDictionary berisi prefab final).
///
/// Traffic light (wajib memiliki komponen TrafficLightBehavior):
///   - Per 4-way (+) dan 3-way (T): dipilih acak dari semua traffic light prefab
///     yang tersedia, 1 per selisih mask terbatas (selisih = jumlah lengan
///     koneksi). Ukuran 1 cell, offset (0,0,0).
///   - Di interior: traffic light diletakkan di/sekitar seedPositions yang
///     masih berupa jalan.
///
/// Streetlight (wajib memiliki komponen StreetlightBehavior):
///   - Di sepanjang ring & spoke (kelipatan 3 cell dari ujung) + di seluruh
///     jalan interior (kelipatan 3 cell dari tepi kiri grid).
///   - Posisi: cell jalan dengan tetangga N/S (vertikal) atau E/W (horizontal),
///     offset 0.4 cell ke tepi. Setiap 3 cell sekali; pilih 1 dari (offset kiri/
///     kanan) secara acak. Sisi kiri (W/S) untuk vertikal/horizontal.
///   - Hanya 1 per cell — set menghindari duplikat.
/// </summary>
public class RoadAddOnDecorator
{
    public const float STREETLIGHT_INTERVAL = 3f;

    private readonly RoadGridHelper gridHelper;
    private readonly Transform      parent;
    private readonly float          tileSize;

    private readonly List<GameObject> trafficLightPrefabs = new List<GameObject>();
    private readonly List<GameObject> streetlightPrefabs  = new List<GameObject>();

    public RoadAddOnDecorator(RoadGridHelper gridHelper, Transform parent, float tileSize)
    {
        this.gridHelper = gridHelper;
        this.parent     = parent;
        this.tileSize   = tileSize;
    }

    /// <summary>Daftar prefab add-on yang tersedia (diisi dari RoadNetwork).</summary>
    public void SetTrafficLightPrefabs(IEnumerable<GameObject> prefabs)
    {
        trafficLightPrefabs.Clear();
        if (prefabs != null)
            foreach (var p in prefabs)
                if (p != null && p.GetComponent<TrafficLightBehavior>() != null)
                    trafficLightPrefabs.Add(p);
    }

    public void SetStreetlightPrefabs(IEnumerable<GameObject> prefabs)
    {
        streetlightPrefabs.Clear();
        if (prefabs != null)
            foreach (var p in prefabs)
                if (p != null && p.GetComponent<StreetlightBehavior>() != null)
                    streetlightPrefabs.Add(p);
    }

    public bool HasTrafficLights => trafficLightPrefabs.Count > 0;
    public bool HasStreetlights  => streetlightPrefabs.Count  > 0;

    // -----------------------------------------------------------------------
    // TRAFFIC LIGHT
    // -----------------------------------------------------------------------

    /// <summary>
    /// Letakkan traffic light di semua junction 4-way (+) dan 3-way (T)
    /// dalam snapshot. Satu per cell; arah rotasi dipilih dari lengan koneksi.
    /// </summary>
    public void PlaceTrafficLights(HashSet<Vector3Int> snapshot)
    {
        if (trafficLightPrefabs.Count == 0) return;

        foreach (var cell in snapshot)
        {
            int mask = gridHelper.GetMaskAt(cell);
            int arms = ArmCount(mask);
            if (arms < 3) continue; // hanya + dan T

            var tile = gridHelper.GetTileAt(cell);
            if (tile == null) continue;

            var prefab = PickRandom(trafficLightPrefabs);
            var rot = RotationTowardArms(mask);
            SpawnAddOn(prefab, cell, rot, tile.transform, "TrafficLight");
        }
    }

    /// <summary>
    /// Letakkan traffic light di/sekitar seed L-System yang masih jalan —
    /// untuk interior yang tidak punya junction rapat. Interval 2 cell.
    /// </summary>
    public void PlaceTrafficLightsAtSeeds(List<Vector3Int> seedPositions,
                                          HashSet<Vector3Int> snapshot)
    {
        if (trafficLightPrefabs.Count == 0) return;

        var placed = new HashSet<Vector3Int>();
        foreach (var seed in seedPositions)
        {
            if (placed.Contains(seed)) continue;
            placed.Add(seed);

            var cell = seed;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (snapshot.Contains(cell))
                {
                    var tile = gridHelper.GetTileAt(cell);
                    if (tile != null)
                    {
                        var prefab = PickRandom(trafficLightPrefabs);
                        SpawnAddOn(prefab, cell, Quaternion.identity, tile.transform, "TrafficLight");
                        break;
                    }
                }
                cell += new Vector3Int(2, 0, 0); // geser 2 cell — cari jalan terdekat
            }
        }
    }

    // -----------------------------------------------------------------------
    // STREETLIGHT
    // -----------------------------------------------------------------------

    /// <summary>Letakkan streetlight di seluruh snapshot pada interval tetap.</summary>
    public void PlaceStreetlights(HashSet<Vector3Int> snapshot)
    {
        if (streetlightPrefabs.Count == 0) return;
        var placed = new HashSet<Vector3Int>();

        foreach (var cell in snapshot)
        {
            if (placed.Contains(cell)) continue;
            if (!ShouldPlaceStreetlight(cell)) continue;

            var tile = gridHelper.GetTileAt(cell);
            if (tile == null) continue;

            bool vertical = HasNeighbor(snapshot, cell, 0, 1)
                         && HasNeighbor(snapshot, cell, 0, -1);
            bool horizontal = HasNeighbor(snapshot, cell, 1, 0)
                           && HasNeighbor(snapshot, cell, -1, 0);
            if (!vertical && !horizontal) continue; // bukan jalan lurus

            var prefab = PickRandom(streetlightPrefabs);
            // Offset 0.4 cell ke tepi; vertikal → kiri (W), horizontal → bawah (S)
            var offset = vertical
                ? new Vector3(-0.4f * tileSize, 0f, 0f)
                : new Vector3(0f, 0f, -0.4f * tileSize);

            var rot = vertical
                ? Quaternion.Euler(0f, 90f, 0f)
                : Quaternion.identity;

            SpawnAddOn(prefab, cell, rot, tile.transform, "Streetlight",
                       localOffset: offset);

            // Tandai 3 cell di sekitarnya supaya tidak berdekatan
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                    placed.Add(cell + new Vector3Int(i, 0, j));
        }
    }

    /// <summary>
    /// Rule penempatan: kelipatan STREETLIGHT_INTERVAL dari titik terdekat
    /// pada ring / spoke / tepi kiri grid.
    /// </summary>
    private bool ShouldPlaceStreetlight(Vector3Int cell)
    {
        // Di sepanjang ring — kelipatan 3 dari ujung baris/kolom
        if (gridHelper.IsRingCell(cell.x, cell.z))
            return (cell.x - gridHelper.RingMinX) % (int)STREETLIGHT_INTERVAL == 0
                || (cell.z - gridHelper.RingMinZ) % (int)STREETLIGHT_INTERVAL == 0;

        // Di sepanjang spoke — kelipatan 3 dari pusat
        if (cell.x == 0 && cell.z != 0)
            return Mathf.Abs(cell.z) % (int)STREETLIGHT_INTERVAL == 0;
        if (cell.z == 0 && cell.x != 0)
            return Mathf.Abs(cell.x) % (int)STREETLIGHT_INTERVAL == 0;

        // Interior lain — kelipatan 3 dari tepi kiri grid
        return cell.x % (int)STREETLIGHT_INTERVAL == 0;
    }

    // -----------------------------------------------------------------------
    // HELPERS
    // -----------------------------------------------------------------------

    /// <summary>Rotasi menghadap 2 lengan pertama yang ada (urutan N,E,S,W).</summary>
    private static Quaternion RotationTowardArms(int mask)
    {
        var arms = new[] { 1, 2, 4, 8 }; // N, E, S, W
        int first = -1, second = -1;
        for (int i = 0; i < 4; i++)
        {
            if ((mask & arms[i]) == 0) continue;
            if (first == -1) { first = i; continue; }
            second = i;
            break;
        }
        int mid = (first + second) * 90;
        return Quaternion.Euler(0f, mid, 0f);
    }

    private static int ArmCount(int mask)
    {
        int c = 0;
        for (int bit = 1; bit <= 8; bit <<= 1)
            if ((mask & bit) != 0) c++;
        return c;
    }

    private static bool HasNeighbor(HashSet<Vector3Int> snap, Vector3Int cell,
                                    int dx, int dz) =>
        snap.Contains(cell + new Vector3Int(dx, 0, dz));

    private static GameObject PickRandom(List<GameObject> list) =>
        list[Random.Range(0, list.Count)];

    private void SpawnAddOn(GameObject prefab, Vector3Int cell, Quaternion rotation,
                            Transform tileParent, string namePrefix,
                            Vector3 localOffset = default)
    {
        var worldPos = gridHelper.CellToWorld(cell) + localOffset;
        var addOn = Object.Instantiate(prefab, worldPos, rotation, parent);
        addOn.name = $"{namePrefix}_{cell.x}_{cell.z}";
    }
}
