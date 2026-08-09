using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RoadAddOnDecorator — menempatkan perlengkapan jalan (traffic light &
/// streetlight) pada grid tile yang SUDAH di-FixRoad (FixRoad() selesai,
/// roadDictionary berisi prefab final). Mengikuti pendekatan SVS (Procedural
/// Town): perlengkapan hanya di cell yang memang layak, bukan asal sebar.
///
/// Traffic light (wajib memiliki komponen TrafficLightBehavior):
///   - Hanya di junction 3-way (T) dan 4-way (+), 1 per cell, rotasi
///     menghadap lengan koneksi. Corner (L), straight (I), dan end (O)
///     TIDAK dapat lampu — SVS style.
///
/// Streetlight (wajib memiliki komponen StreetlightBehavior):
///   - Hanya di segmen LURUS (2 lengan N/S atau E/W), interval 3 cell
///     SEPANJANG arah jalan (jalan vertikal → interval di z, horizontal →
///     interval di x) — bukan kolom grid. Junction/corner/end di-skip.
///   - Ring & spoke memakai interval dari ujung/pusat.
///   - Posisi: offset 0.4 cell ke tepi jalan (kiri W untuk vertikal,
///     bawah S untuk horizontal), rotasi mengikuti arah jalan.
///   - Hanya 1 per cell — set menghindari duplikat + tetangga (±1).
/// </summary>
public class RoadAddOnDecorator
{
    public const float STREETLIGHT_INTERVAL = 3f;

    private readonly RoadGridHelper gridHelper;
    private readonly Transform      parent;
    private readonly float          tileSize;

    private readonly List<GameObject> trafficLightPrefabs = new List<GameObject>();
    private readonly List<GameObject> streetlightPrefabs  = new List<GameObject>();

    /// <summary>Jumlah aktual yang berhasil di-place — untuk log verifikasi.</summary>
    public int TrafficLightCount { get; private set; }
    public int StreetlightCount  { get; private set; }

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
    // TRAFFIC LIGHT — hanya junction 3-way (T) dan 4-way (+)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Letakkan traffic light di semua junction 4-way (+) dan 3-way (T)
    /// dalam snapshot. Satu per cell; arah rotasi dipilih dari lengan koneksi.
    /// </summary>
    public void PlaceTrafficLights(HashSet<Vector3Int> snapshot)
    {
        TrafficLightCount = 0;
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
            TrafficLightCount++;
        }
    }

    // -----------------------------------------------------------------------
    // STREETLIGHT — hanya segmen lurus, interval searah jalan
    // -----------------------------------------------------------------------

    /// <summary>Letakkan streetlight di sepanjang segmen lurus dengan interval tetap.</summary>
    public void PlaceStreetlights(HashSet<Vector3Int> snapshot)
    {
        StreetlightCount = 0;
        if (streetlightPrefabs.Count == 0) return;
        var placed = new HashSet<Vector3Int>();

        foreach (var cell in snapshot)
        {
            if (placed.Contains(cell)) continue;

            // Hanya segmen LURUS: 2 lengan berhadapan (N/S atau E/W).
            // Junction (+/T), corner (L), dan end (O) di-skip — SVS style.
            int mask = gridHelper.GetMaskAt(cell);
            if (!IsStraightSegment(mask)) continue;

            if (!ShouldPlaceStreetlight(cell, mask)) continue;

            var tile = gridHelper.GetTileAt(cell);
            if (tile == null) continue;

            bool vertical   = (mask & 1) != 0 && (mask & 4) != 0; // N && S
            bool horizontal = (mask & 2) != 0 && (mask & 8) != 0; // E && W

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
            StreetlightCount++;

            // Tandai 3 cell di sekitarnya supaya tidak berdekatan
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                    placed.Add(cell + new Vector3Int(i, 0, j));
        }
    }

    /// <summary>
    /// Rule penempatan: kelipatan STREETLIGHT_INTERVAL.
    ///   - Ring: dari ujung baris/kolom ring.
    ///   - Spoke: dari pusat kota.
    ///   - Interior: SEPANJANG arah jalan (vertikal → z, horizontal → x),
    ///     bukan kolom grid — supaya jalan vertikal tidak kebetulan kosong
    ///     total hanya karena kolomnya bukan kelipatan 3.
    /// </summary>
    private bool ShouldPlaceStreetlight(Vector3Int cell, int mask)
    {
        // Di sepanjang ring — kelipatan 3 dari ujung baris/kolom
        if (gridHelper.IsRingCell(cell.x, cell.z))
        {
            bool topBottom = cell.z == gridHelper.RingMinZ || cell.z == gridHelper.RingMaxZ;
            bool leftRight = cell.x == gridHelper.RingMinX || cell.x == gridHelper.RingMaxX;
            if (topBottom)
                return (cell.x - gridHelper.RingMinX) % (int)STREETLIGHT_INTERVAL == 0;
            if (leftRight)
                return (cell.z - gridHelper.RingMinZ) % (int)STREETLIGHT_INTERVAL == 0;
            return false;
        }

        // Di sepanjang spoke — kelipatan 3 dari pusat
        if (cell.x == 0 && cell.z != 0)
            return Mathf.Abs(cell.z) % (int)STREETLIGHT_INTERVAL == 0;
        if (cell.z == 0 && cell.x != 0)
            return Mathf.Abs(cell.x) % (int)STREETLIGHT_INTERVAL == 0;

        // Interior — interval sepanjang arah jalan
        bool vertical   = (mask & 1) != 0 && (mask & 4) != 0;
        bool horizontal = (mask & 2) != 0 && (mask & 8) != 0;
        if (vertical)
            return Mathf.Abs(cell.z) % (int)STREETLIGHT_INTERVAL == 0;
        if (horizontal)
            return Mathf.Abs(cell.x) % (int)STREETLIGHT_INTERVAL == 0;
        return false;
    }

    /// <summary>Segmen lurus = tepat 2 lengan berhadapan (N/S atau E/W).</summary>
    private static bool IsStraightSegment(int mask)
    {
        bool n = (mask & 1) != 0, e = (mask & 2) != 0;
        bool s = (mask & 4) != 0, w = (mask & 8) != 0;
        bool vertical   = n && s && !e && !w;
        bool horizontal = e && w && !n && !s;
        return vertical || horizontal;
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
