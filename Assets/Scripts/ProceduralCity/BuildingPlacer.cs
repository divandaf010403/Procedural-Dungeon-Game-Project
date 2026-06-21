using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tahap 3 dari procedural city generation: Menempatkan bangunan otomatis.
/// UPGRADED: Sekarang pakai District + Lot system.
    ///
    /// Workflow baru:
    /// 1. Untuk tiap block → subdivide jadi lots (LotSubdivider)
    /// 2. Untuk tiap lot → tentukan building params dari district rules
    /// 3. Place bangunan dengan ProceduralBuildingShape (L/T/U/etc)
    /// 4. Skyline variation: tinggi bangunan berdasarkan jarak ke pusat kota
    ///
    /// District menentukan:
    /// - buildingHeightRange (min/max tinggi)
    /// - buildingFootprintRange (min/max ukuran)
    /// - buildingDensity (0-1)
    /// - lotSubdivisionMin/Max (jumlah lot per blok)
    /// </summary>
    [System.Serializable]
    public class BuildingPlacer : MonoBehaviour
    {
        private CityGenerator cityGenerator;
        private RoadNetwork roadNetwork;
        private DistrictManager districtManager;

        private Transform buildingParent;
        private GameObject[] cachedBuildingPrefabs;

        public void Initialize(CityGenerator generator, RoadNetwork roads, DistrictManager districts)
        {
            cityGenerator = generator;
            roadNetwork = roads;
            districtManager = districts;
        }

        /// <summary>
        /// Backward compatible: bisa dipanggil tanpa DistrictManager
        /// </summary>
        public void Initialize(CityGenerator generator, RoadNetwork roads)
        {
            Initialize(generator, roads, null);
        }

        public void PlaceBuildings()
        {
            ClearBuildings();

            GameObject parent = new GameObject("Buildings");
            parent.transform.SetParent(cityGenerator.transform);
            cityGenerator.RegisterSpawnedObject(parent);
            buildingParent = parent.transform;

            // Setup prefab cache
            SetupPrefabCache();

            // Untuk setiap block:
            // 1. Subdivide jadi lots
            // 2. Place bangunan di tiap lot
            int buildingCount = 0;
            for (int blockIdx = 0; blockIdx < roadNetwork.blocks.Count; blockIdx++)
            {
                CityBlock block = roadNetwork.blocks[blockIdx];

                // Get district dari manager (atau fallback)
                District district = null;
                if (districtManager != null)
                {
                    district = districtManager.GetDistrictForBlock(blockIdx);
                    block.district = district;
                }

                if (district == null)
                {
                    // Fallback: buat default district
                    district = District.CreatePreset(DistrictType.Residential, block.center, 50f);
                    block.district = district;
                }

                // Skip Park district
                if (district.type == DistrictType.Park) continue;

                // Subdivide block jadi lots
                block.SubdivideIntoLots();

                // Place bangunan di tiap lot
                foreach (var lot in block.lots)
                {
                    if (lot.hasRoadAccess || Random.value < 0.3f) // Kadang isi juga lot interior
                    {
                        PlaceBuildingInLot(lot, district);
                        buildingCount++;
                    }
                }

                if (district != null) district.buildingCount += block.lots.Count;
            }

            Debug.Log($"[BuildingPlacer] Placed {buildingCount} buildings in {roadNetwork.blocks.Count} blocks");
        }

        private void SetupPrefabCache()
        {
            if (cityGenerator.buildingPrefabs != null && cityGenerator.buildingPrefabs.Length > 0)
            {
                cachedBuildingPrefabs = cityGenerator.buildingPrefabs;
            }
            else
            {
                cachedBuildingPrefabs = GenerateFallbackPrefabs();
            }
        }

        private GameObject[] GenerateFallbackPrefabs()
        {
            List<GameObject> fallbacks = new List<GameObject>();

            Vector3[] sizes = new Vector3[]
            {
                new Vector3(8, 12, 8),
                new Vector3(10, 18, 10),
                new Vector3(6, 8, 12),
                new Vector3(12, 25, 12),
                new Vector3(8, 10, 8),
                new Vector3(15, 30, 15),
            };

            Color[] colors = new Color[]
            {
                new Color(0.7f, 0.7f, 0.75f),
                new Color(0.85f, 0.8f, 0.7f),
                new Color(0.6f, 0.65f, 0.7f),
                new Color(0.75f, 0.75f, 0.7f),
                new Color(0.8f, 0.7f, 0.6f),
            };

            for (int i = 0; i < sizes.Length; i++)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"FallbackBuilding_{i}";
                cube.transform.localScale = sizes[i];

                Renderer rend = cube.GetComponent<Renderer>();
                Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = colors[i % colors.Length];
                rend.sharedMaterial = mat;

                Collider col = cube.GetComponent<Collider>();
                if (col != null)
                {
                    if (Application.isPlaying) Destroy(col);
                    else DestroyImmediate(col);
                }

                cube.SetActive(false);
                cube.hideFlags = HideFlags.HideAndDontSave;
                fallbacks.Add(cube);
            }

            return fallbacks.ToArray();
        }

        /// <summary>
        /// Place bangunan dalam 1 lot dengan district rules + skyline variation.
        /// </summary>
        private void PlaceBuildingInLot(Lot lot, District district)
        {
            // Cek density - apakah place bangunan di lot ini?
            if (Random.value > district.buildingDensity && district.maxBuildingsPerLot <= 1) return;

            // SKYLINE VARIATION: tinggi bangunan berdasarkan jarak ke pusat kota
            float distToCenter = Vector3.Distance(lot.center, cityGenerator.transform.position);
            float maxDist = cityGenerator.citySize * 0.5f;
            float distNormalized = Mathf.Clamp01(distToCenter / maxDist);

            // Distance factor: 1.0 di pusat, 0.0 di pinggir
            float centerFactor = 1f - distNormalized;

            // Tinggi random dalam range district, weighted ke center
            float baseHeight = Random.Range(district.buildingHeightRange.x, district.buildingHeightRange.y);
            // Boost tinggi di pusat kota (skyline effect)
            float skylineMultiplier = 1f + centerFactor * 0.5f;
            float finalHeight = baseHeight * skylineMultiplier;

            // Footprint dari district rules, scaled dengan lot size
            float footprintScale = Mathf.Min(lot.size.x, lot.size.y) / 8f;
            float baseFootprintX = Random.Range(district.buildingFootprintRange.x, district.buildingFootprintRange.y);
            float baseFootprintY = Random.Range(district.buildingFootprintRange.x, district.buildingFootprintRange.y);

            // Pastikan tidak melebihi lot size
            Vector2 footprint = new Vector2(
                Mathf.Min(baseFootprintX * footprintScale * 0.8f, lot.size.x * 0.85f),
                Mathf.Min(baseFootprintY * footprintScale * 0.8f, lot.size.y * 0.85f)
            );

            if (footprint.x < 2f || footprint.y < 2f) return;

            // Material dengan warna sesuai district
            Material buildingMat = GetBuildingMaterial(district);

            // Generate bangunan dengan shape sesuai lot
            GameObject building = ProceduralBuildingShape.CreateBuilding(
                lot, district, lot.center, footprint, finalHeight, buildingMat, cityGenerator);

            building.name = $"Building_{buildingParent.childCount}";
            building.transform.SetParent(buildingParent);

            // Random rotasi Y (supaya variasi)
            // (Sudah di-set di ProceduralBuildingShape, tapi bisa di-override)

            lot.buildingCount++;
        }

        /// <summary>
        /// Get material bangunan berdasarkan district type.
        /// Bisa di-customize dengan prefab user.
        /// </summary>
        private Material GetBuildingMaterial(District district)
        {
            // Jika user punya prefab, pakai material dari prefab
            if (cachedBuildingPrefabs != null && cachedBuildingPrefabs.Length > 0)
            {
                GameObject prefab = cachedBuildingPrefabs[Random.Range(0, cachedBuildingPrefabs.Length)];
                Renderer rend = prefab.GetComponentInChildren<Renderer>();
                if (rend != null && rend.sharedMaterial != null)
                {
                    return rend.sharedMaterial;
                }
            }

            // Generate material sesuai district (dengan fallback shader)
            Material mat = CityGenerator.CreateMaterial("Building", Color.gray);

            switch (district.type)
            {
                case DistrictType.Downtown:
                    // Glass & steel towers - abu-abu kebiruan
                    mat.color = new Color(0.5f + Random.Range(-0.1f, 0.1f),
                                          0.55f + Random.Range(-0.1f, 0.1f),
                                          0.65f + Random.Range(-0.05f, 0.05f));
                    break;

                case DistrictType.Commercial:
                    mat.color = new Color(0.7f + Random.Range(-0.1f, 0.1f),
                                          0.65f + Random.Range(-0.1f, 0.1f),
                                          0.5f + Random.Range(-0.1f, 0.1f));
                    break;

                case DistrictType.Residential:
                    mat.color = new Color(0.75f + Random.Range(-0.1f, 0.1f),
                                          0.7f + Random.Range(-0.1f, 0.1f),
                                          0.6f + Random.Range(-0.1f, 0.1f));
                    break;

                case DistrictType.Industrial:
                    mat.color = new Color(0.45f + Random.Range(-0.1f, 0.1f),
                                          0.4f + Random.Range(-0.1f, 0.1f),
                                          0.35f + Random.Range(-0.1f, 0.1f));
                    break;

                case DistrictType.Suburbs:
                    mat.color = new Color(0.8f + Random.Range(-0.1f, 0.1f),
                                          0.75f + Random.Range(-0.1f, 0.1f),
                                          0.65f + Random.Range(-0.1f, 0.1f));
                    break;

                case DistrictType.Waterfront:
                    mat.color = new Color(0.6f + Random.Range(-0.1f, 0.1f),
                                          0.7f + Random.Range(-0.1f, 0.1f),
                                          0.75f + Random.Range(-0.1f, 0.1f));
                    break;

                default:
                    mat.color = Color.gray;
                    break;
            }

            return mat;
        }

        public void ClearBuildings()
        {
            if (buildingParent != null)
            {
                if (Application.isPlaying) Destroy(buildingParent.gameObject);
                else DestroyImmediate(buildingParent.gameObject);
                buildingParent = null;
            }
        }
    }
