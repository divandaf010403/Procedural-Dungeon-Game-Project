using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tipe district - menentukan style dan aturan kota.
/// Mirip zoning di urban planning.
/// </summary>
public enum DistrictType
    {
        Downtown,       // Pusat kota: gedung tinggi, padat
        Commercial,     // Area komersial: gedung sedang
        Residential,    // Permukiman: rumah/gedung rendah
        Industrial,     // Industri: pabrik besar, sparse
        Suburbs,        // Pinggiran: rumah kecil, garden
        Waterfront,     // Tepi sungai: pemandangan, mixed
        Park            // Taman kota: mostly green
    }

    /// <summary>
    /// District = zona kota dengan aturan spesifik.
    /// Lebih advance dari BlockType - menentukan semua aturan
    /// layout, building style, height, density untuk area tersebut.
    /// </summary>
    [System.Serializable]
    public class District
    {
        public string name;
        public DistrictType type;
        public Vector3 center;
        public float radius;

        // Aturan bangunan
        public Vector2 buildingHeightRange = new Vector2(5f, 15f);
        public Vector2 buildingFootprintRange = new Vector2(4f, 12f);
        public float buildingDensity = 0.7f;
        public int maxBuildingsPerLot = 1;

        // Aturan layout
        public bool useCurvedRoads = false;
        public float roadCurvature = 0.3f;
        public int lotSubdivisionMin = 2;
        public int lotSubdivisionMax = 4;

        // Aturan visual
        public Color districtColor = Color.white;
        public Material roadMaterial;
        public Material sidewalkMaterial;

        // Statistik
        public int blockCount = 0;
        public int buildingCount = 0;

        public bool Contains(Vector3 worldPos)
        {
            return Vector3.Distance(worldPos, center) <= radius;
        }

        /// <summary>
        /// Preset district configs untuk quick setup.
        /// Mempermudah membuat district tanpa harus setting semua parameter.
        /// </summary>
        public static District CreatePreset(DistrictType type, Vector3 center, float radius)
        {
            District d = new District
            {
                name = type.ToString(),
                type = type,
                center = center,
                radius = radius
            };

            switch (type)
            {
                case DistrictType.Downtown:
                    d.buildingHeightRange = new Vector2(20f, 60f);
                    d.buildingFootprintRange = new Vector2(8f, 18f);
                    d.buildingDensity = 0.85f;
                    d.maxBuildingsPerLot = 3;
                    d.useCurvedRoads = true;
                    d.roadCurvature = 0.4f;
                    d.districtColor = new Color(0.5f, 0.5f, 0.6f);
                    break;

                case DistrictType.Commercial:
                    d.buildingHeightRange = new Vector2(10f, 30f);
                    d.buildingFootprintRange = new Vector2(6f, 15f);
                    d.buildingDensity = 0.7f;
                    d.maxBuildingsPerLot = 2;
                    d.useCurvedRoads = false;
                    d.districtColor = new Color(0.7f, 0.6f, 0.4f);
                    break;

                case DistrictType.Residential:
                    d.buildingHeightRange = new Vector2(6f, 18f);
                    d.buildingFootprintRange = new Vector2(5f, 10f);
                    d.buildingDensity = 0.6f;
                    d.maxBuildingsPerLot = 2;
                    d.useCurvedRoads = false;
                    d.lotSubdivisionMin = 3;
                    d.lotSubdivisionMax = 5;
                    d.districtColor = new Color(0.4f, 0.6f, 0.4f);
                    break;

                case DistrictType.Industrial:
                    d.buildingHeightRange = new Vector2(4f, 12f);
                    d.buildingFootprintRange = new Vector2(15f, 30f);
                    d.buildingDensity = 0.4f;
                    d.maxBuildingsPerLot = 1;
                    d.useCurvedRoads = false;
                    d.districtColor = new Color(0.5f, 0.4f, 0.3f);
                    break;

                case DistrictType.Suburbs:
                    d.buildingHeightRange = new Vector2(3f, 8f);
                    d.buildingFootprintRange = new Vector2(4f, 8f);
                    d.buildingDensity = 0.4f;
                    d.maxBuildingsPerLot = 1;
                    d.useCurvedRoads = true;
                    d.roadCurvature = 0.5f;
                    d.lotSubdivisionMin = 4;
                    d.lotSubdivisionMax = 6;
                    d.districtColor = new Color(0.6f, 0.7f, 0.5f);
                    break;

                case DistrictType.Waterfront:
                    d.buildingHeightRange = new Vector2(8f, 25f);
                    d.buildingFootprintRange = new Vector2(6f, 12f);
                    d.buildingDensity = 0.5f;
                    d.maxBuildingsPerLot = 1;
                    d.useCurvedRoads = true;
                    d.roadCurvature = 0.6f;
                    d.districtColor = new Color(0.4f, 0.6f, 0.7f);
                    break;

                case DistrictType.Park:
                    d.buildingHeightRange = Vector2.zero;
                    d.buildingFootprintRange = Vector2.zero;
                    d.buildingDensity = 0f;
                    d.districtColor = new Color(0.3f, 0.7f, 0.3f);
                    break;
            }

            return d;
        }

        /// <summary>
        /// Get color yang lebih gelap untuk roads di district ini
        /// </summary>
        public Color GetRoadColor()
        {
            return districtColor * 0.3f;
        }
    }
