using UnityEngine;

/// <summary>
/// Lot = area kecil tempat 1 bangunan (atau beberapa) ditempatkan.
/// Konsep城市规划 (urban planning): setiap bangunan punya lot-nya sendiri.
    ///
    /// Lebih granular dari CityBlock:
    /// - 1 Block = banyak Lot (tergantung district.lotSubdivisionMin/Max)
    /// - Tiap Lot punya shape (rectangle/L-shape/etc), boundary, akses ke jalan
    ///
    /// Dengan sistem lot, kita bisa:
    /// - Punya variasi ukuran lot dalam 1 blok
    /// - Atur setback (jarak dari jalan) per district
    /// - Generate bentuk bangunan non-rectangle (L, T, U shape)
    /// </summary>
    [System.Serializable]
    public class Lot
    {
        public Vector3 center;          // Posisi tengah lot
        public Vector2 size;            // Ukuran lot (X dan Z)
        public LotShape shape = LotShape.Rectangle;
        public bool hasRoadAccess = true;
        public Vector3 roadFacingDirection; // Arah hadap jalan

        // Parent block reference
        public CityBlock parentBlock;

        // Bangunan yang sudah ditempatkan di lot ini
        public int buildingCount = 0;

        // Cek apakah masih muat untuk 1 bangunan lagi
        public bool CanFitBuilding(float minFootprint)
        {
            return size.x >= minFootprint && size.y >= minFootprint;
        }

        // Posisi random dalam lot (dengan margin)
        public Vector3 GetRandomPosition(float margin)
        {
            float halfX = Mathf.Max(0, (size.x * 0.5f) - margin);
            float halfZ = Mathf.Max(0, (size.y * 0.5f) - margin);
            return new Vector3(
                center.x + Random.Range(-halfX, halfX),
                center.y,
                center.z + Random.Range(-halfZ, halfZ)
            );
        }

        // Mendapatkan footprint bangunan dalam lot ini
        // Untuk L/T/U shape, ini adalah bounding box utama
        public Vector2 GetBuildingFootprint()
        {
            // Default: 70% ukuran lot
            return new Vector2(size.x * 0.7f, size.y * 0.7f);
        }
    }

    /// <summary>
    /// Bentuk lot - menentukan layout dan tipe bangunan
    /// </summary>
    public enum LotShape
    {
        Rectangle,     // Standar 4 sisi
        LShape,        // Bentuk L (corner lot)
        TShape,        // Bentuk T
        UShape,        // Bentuk U (premium, dengan courtyard)
        Wide,          // Lebar tapi dangkal
        Narrow         // Sempit tapi dalam
    }
