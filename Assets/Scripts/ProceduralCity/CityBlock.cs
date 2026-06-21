using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tipe blok kota - menentukan fungsi area dan style bangunan yang akan ditempatkan.
/// Sesuai artikel Binus: blok bisa berupa bangunan, taman, area publik, dll.
/// </summary>
public enum BlockType
    {
        Residential,    // Rumah-rumah, gedung apartemen
        Commercial,     // Toko, perkantoran, mall
        Industrial,     // Pabrik, gudang
        Park            // Taman, area publik
    }

    /// <summary>
    /// Representasi satu blok kota: area persegi yang dibatasi 4 jalan.
    /// UPGRADED: sekarang punya reference ke District dan List of Lots.
    ///
    /// Workflow:
    /// 1. RoadNetwork.GenerateRoads() → bikin blocks
    /// 2. DistrictManager.AssignBlocksToDistricts() → assign district per block
    /// 3. LotSubdivider.SubdivideBlock() → pecah block jadi lots
    /// 4. BuildingPlacer → place bangunan per lot
    /// </summary>
    [System.Serializable]
    public class CityBlock
    {
        public Vector3 center;       // Posisi tengah blok (world space)
        public Vector2 size;         // Ukuran blok (X dan Z)
        public BlockType blockType;  // Fungsi blok (legacy, di-set dari district)

        // ADVANCED: District reference (untuk styling berdasarkan district rules)
        [System.NonSerialized] public District district;

        // ADVANCED: Lot subdivision - blok dipecah jadi lot kecil
        [System.NonSerialized] public List<Lot> lots = new List<Lot>();

        public int buildingCount = 0;

        public bool CanFitBuilding(float minSize)
        {
            return size.x >= minSize && size.y >= minSize;
        }

        public Vector3 GetRandomBuildingPosition(float margin)
        {
            float halfX = (size.x - margin * 2) * 0.5f;
            float halfZ = (size.y - margin * 2) * 0.5f;

            float x = center.x + Random.Range(-halfX, halfX);
            float z = center.z + Random.Range(-halfZ, halfZ);

            return new Vector3(x, center.y, z);
        }

        /// <summary>
        /// Subdivide blok ini menjadi lot-lot kecil berdasarkan district rules.
        /// Method ini delegated ke LotSubdivider static class.
        /// </summary>
        public void SubdivideIntoLots()
        {
            if (district == null)
            {
                Debug.LogWarning("Block has no district assigned, using default rules");
                district = District.CreatePreset(DistrictType.Residential, center, 50f);
            }

            lots = LotSubdivider.SubdivideBlock(this, district);
        }
    }
