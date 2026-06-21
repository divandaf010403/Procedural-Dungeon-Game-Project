using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manager untuk District system.
/// ADVANCED UPGRADE: Mengelola beberapa district dalam satu kota.
    ///
    /// Cara kerja:
    /// 1. Initialize default districts (Downtown, Residential, Industrial, dll)
    ///    atau terima custom districts dari inspector
    /// 2. Assign setiap CityBlock ke district berdasarkan posisi
    /// 3. District yang "menang" (closest/overlapping) akan punya blok tersebut
    /// 4. District menentukan semua aturan layout, height, density
    ///
    /// Sistem ini mirip dengan "zoning" di城市规划 (urban planning).
    /// </summary>
    [System.Serializable]
    public class DistrictManager
    {
        private CityGenerator cityGenerator;
        private RoadNetwork roadNetwork;

        // Semua district dalam kota
        public List<District> districts = new List<District>();

        // District assignment per block index
        public Dictionary<int, District> blockToDistrict = new Dictionary<int, District>();

        public void Initialize(CityGenerator generator, RoadNetwork roads)
        {
            cityGenerator = generator;
            roadNetwork = roads;
            SetupDefaultDistricts();
        }

        /// <summary>
        /// Setup default layout district untuk kota.
        /// Anda bisa customize lebih lanjut dengan custom districts.
        /// </summary>
        private void SetupDefaultDistricts()
        {
            districts.Clear();

            float citySize = cityGenerator.citySize;
            Vector3 origin = cityGenerator.transform.position;

            // Downtown: pusat kota
            districts.Add(District.CreatePreset(
                DistrictType.Downtown,
                origin,
                citySize * 0.25f
            ));

            // Commercial: 4 area di sekitar downtown
            districts.Add(District.CreatePreset(
                DistrictType.Commercial,
                origin + new Vector3(citySize * 0.2f, 0, 0),
                citySize * 0.2f
            ));
            districts.Add(District.CreatePreset(
                DistrictType.Commercial,
                origin + new Vector3(-citySize * 0.2f, 0, 0),
                citySize * 0.2f
            ));
            districts.Add(District.CreatePreset(
                DistrictType.Commercial,
                origin + new Vector3(0, 0, citySize * 0.2f),
                citySize * 0.2f
            ));
            districts.Add(District.CreatePreset(
                DistrictType.Commercial,
                origin + new Vector3(0, 0, -citySize * 0.2f),
                citySize * 0.2f
            ));

            // Industrial: satu area di pinggir
            districts.Add(District.CreatePreset(
                DistrictType.Industrial,
                origin + new Vector3(citySize * 0.35f, 0, citySize * 0.35f),
                citySize * 0.15f
            ));
            districts.Add(District.CreatePreset(
                DistrictType.Industrial,
                origin + new Vector3(-citySize * 0.35f, 0, -citySize * 0.35f),
                citySize * 0.15f
            ));

            // Residential: area suburban
            districts.Add(District.CreatePreset(
                DistrictType.Residential,
                origin + new Vector3(citySize * 0.3f, 0, -citySize * 0.25f),
                citySize * 0.2f
            ));
            districts.Add(District.CreatePreset(
                DistrictType.Residential,
                origin + new Vector3(-citySize * 0.3f, 0, citySize * 0.25f),
                citySize * 0.2f
            ));

            // Suburbs: pinggir kota
            districts.Add(District.CreatePreset(
                DistrictType.Suburbs,
                origin + new Vector3(citySize * 0.4f, 0, -citySize * 0.4f),
                citySize * 0.18f
            ));
            districts.Add(District.CreatePreset(
                DistrictType.Suburbs,
                origin + new Vector3(-citySize * 0.4f, 0, citySize * 0.4f),
                citySize * 0.18f
            ));

            // Park: taman kota besar
            districts.Add(District.CreatePreset(
                DistrictType.Park,
                origin + new Vector3(citySize * 0.15f, 0, citySize * 0.15f),
                citySize * 0.08f
            ));
        }

        /// <summary>
        /// Assign setiap blok ke district berdasarkan posisi.
        /// District yang jaraknya paling kecil (atau overlap terdalam) yang menang.
        /// </summary>
        public void AssignBlocksToDistricts()
        {
            blockToDistrict.Clear();

            // Reset statistics
            foreach (var d in districts)
            {
                d.blockCount = 0;
                d.buildingCount = 0;
            }

            for (int i = 0; i < roadNetwork.blocks.Count; i++)
            {
                CityBlock block = roadNetwork.blocks[i];
                District closest = GetDistrictForPosition(block.center);

                if (closest != null)
                {
                    blockToDistrict[i] = closest;
                    closest.blockCount++;

                    // Set blockType dari district type untuk backward compatibility
                    block.blockType = ConvertToBlockType(closest.type);
                }
            }
        }

        /// <summary>
        /// Get district untuk posisi tertentu.
        /// Prioritas: district yang overlap terdalam, lalu yang terdekat.
        /// </summary>
        public District GetDistrictForPosition(Vector3 worldPos)
        {
            District best = null;
            float bestScore = float.MaxValue;

            foreach (var d in districts)
            {
                float dist = Vector3.Distance(worldPos, d.center);
                float overlap = Mathf.Max(0, d.radius - dist);

                // Score: kombinasi overlap dan distance
                // District yang overlap dapat prioritas
                float score;
                if (overlap > 0)
                {
                    score = -overlap * 10f; // Negative = prioritas tinggi
                }
                else
                {
                    score = dist;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    best = d;
                }
            }

            return best;
        }

        /// <summary>
        /// Convert DistrictType ke BlockType lama (untuk backward compatibility)
        /// </summary>
        private BlockType ConvertToBlockType(DistrictType type)
        {
            switch (type)
            {
                case DistrictType.Downtown:
                case DistrictType.Commercial:
                    return BlockType.Commercial;
                case DistrictType.Residential:
                case DistrictType.Suburbs:
                case DistrictType.Waterfront:
                    return BlockType.Residential;
                case DistrictType.Industrial:
                    return BlockType.Industrial;
                case DistrictType.Park:
                    return BlockType.Park;
                default:
                    return BlockType.Residential;
            }
        }

        /// <summary>
        /// Get district untuk block index
        /// </summary>
        public District GetDistrictForBlock(int blockIndex)
        {
            if (blockToDistrict.ContainsKey(blockIndex))
                return blockToDistrict[blockIndex];
            return null;
        }

        /// <summary>
        /// Visualisasi district di Scene view (untuk debugging)
        /// </summary>
        public void DrawGizmos()
        {
            foreach (var d in districts)
            {
                Gizmos.color = new Color(d.districtColor.r, d.districtColor.g, d.districtColor.b, 0.3f);
                Gizmos.DrawSphere(d.center, d.radius);

                Gizmos.color = d.districtColor;
                Gizmos.DrawWireSphere(d.center, d.radius);
            }
        }
    }
