using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tahap 5 dari procedural city generation: Tambahkan detail lingkungan.
/// UPGRADED: Sekarang support vehicles, traffic lights, dan street props.
    ///
    /// Fitur:
    /// - Street lights di kedua sisi jalan
    /// - Pohon di taman (Park district)
    /// - Vehicles (mobil) acak di jalan
    /// - Traffic lights di intersection utama
    /// - Props tambahan (bench, hydrant, dll)
    /// </summary>
    [System.Serializable]
    public class CityEnvironmentDetails : MonoBehaviour
    {
        private CityGenerator cityGenerator;
        private RoadNetwork roadNetwork;
        private DistrictManager districtManager;

        private Transform lightsParent;
        private Transform propsParent;
        private Transform vehiclesParent;

        public GameObject vehiclePrefab;

        public void Initialize(CityGenerator generator, RoadNetwork roads)
        {
            cityGenerator = generator;
            roadNetwork = roads;
        }

        public void Initialize(CityGenerator generator, RoadNetwork roads, DistrictManager districts)
        {
            cityGenerator = generator;
            roadNetwork = roads;
            districtManager = districts;
        }

        public void AddDetails()
        {
            ClearDetails();

            GameObject lightsGO = new GameObject("StreetLights");
            lightsGO.transform.SetParent(cityGenerator.transform);
            cityGenerator.RegisterSpawnedObject(lightsGO);
            lightsParent = lightsGO.transform;

            GameObject propsGO = new GameObject("EnvironmentProps");
            propsGO.transform.SetParent(cityGenerator.transform);
            cityGenerator.RegisterSpawnedObject(propsGO);
            propsParent = propsGO.transform;

            GameObject vehiclesGO = new GameObject("Vehicles");
            vehiclesGO.transform.SetParent(cityGenerator.transform);
            cityGenerator.RegisterSpawnedObject(vehiclesGO);
            vehiclesParent = vehiclesGO.transform;

            PlaceStreetLights();
            PlaceTreesInParks();
            PlaceVehicles();
            PlaceTrafficLights();

            Debug.Log("[CityEnvironmentDetails] Environment details added");
        }

        private void PlaceStreetLights()
        {
            // Spacing lebih besar (25m) supaya lampu tidak terlalu banyak/berantakan
            float spacing = 25f;
            float roadOffset = cityGenerator.roadWidth * 0.5f + 1f;
            // Minimum distance dari intersection supaya lampu tidak clustered di persimpangan
            float minDistFromIntersection = 12f;

            foreach (var road in roadNetwork.horizontalRoads)
            {
                PlaceLightsAlongRoad(road, Vector3.forward * roadOffset, spacing, minDistFromIntersection);
                PlaceLightsAlongRoad(road, Vector3.back * roadOffset, spacing, minDistFromIntersection);
            }

            foreach (var road in roadNetwork.verticalRoads)
            {
                PlaceLightsAlongRoad(road, Vector3.right * roadOffset, spacing, minDistFromIntersection);
                PlaceLightsAlongRoad(road, Vector3.left * roadOffset, spacing, minDistFromIntersection);
            }

            // SKIP lampu di curved roads - terlalu random dan susah align dengan tangent
            // Curved roads tetap punya jalan tapi tanpa lampu
        }

        private void PlaceLightsAlongRoad(RoadSegment road, Vector3 sideOffset, float spacing, float minDistFromIntersection)
        {
            float length = Vector3.Distance(road.start, road.end);
            int lightCount = Mathf.FloorToInt(length / spacing);

            for (int i = 0; i < lightCount; i++)
            {
                float t = (i + 0.5f) / lightCount;
                Vector3 pos = Vector3.Lerp(road.start, road.end, t) + sideOffset;

                // SKIP lampu yang terlalu dekat dengan intersection (mencegah cluster)
                if (IsNearIntersection(pos, minDistFromIntersection)) continue;

                GameObject light = CreateStreetLight(pos);
                if (light != null) light.transform.SetParent(lightsParent);
            }
        }

        /// <summary>
        /// Cek apakah posisi terlalu dekat dengan intersection manapun.
        /// </summary>
        private bool IsNearIntersection(Vector3 pos, float minDist)
        {
            if (roadNetwork.intersections == null) return false;
            foreach (var intersection in roadNetwork.intersections)
            {
                // Hanya cek di XZ plane (ignore Y)
                Vector2 posXZ = new Vector2(pos.x, pos.z);
                Vector2 intXZ = new Vector2(intersection.x, intersection.z);
                if (Vector2.Distance(posXZ, intXZ) < minDist) return true;
            }
            return false;
        }

        private GameObject CreateStreetLight(Vector3 position)
        {
            if (cityGenerator.streetLightPrefab != null)
            {
                GameObject light = Instantiate(cityGenerator.streetLightPrefab);
                light.transform.position = position;
                light.name = "StreetLight";
                return light;
            }

            GameObject lightGO = new GameObject("StreetLight");
            lightGO.transform.position = position;

            GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole";
            pole.transform.SetParent(lightGO.transform);
            pole.transform.localPosition = new Vector3(0, 2f, 0);
            pole.transform.localScale = new Vector3(0.1f, 2f, 0.1f);

            GameObject bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulb.name = "Bulb";
            bulb.transform.SetParent(lightGO.transform);
            bulb.transform.localPosition = new Vector3(0, 4f, 0);
            bulb.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);

            Material bulbMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            bulbMat.color = new Color(1f, 0.95f, 0.7f);
            bulb.GetComponent<Renderer>().sharedMaterial = bulbMat;

            Material poleMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            poleMat.color = new Color(0.3f, 0.3f, 0.3f);
            pole.GetComponent<Renderer>().sharedMaterial = poleMat;

            DestroyCollider(pole);
            DestroyCollider(bulb);

            return lightGO;
        }

        private void PlaceTreesInParks()
        {
            if (districtManager == null) return;

            float treeSpacing = 5f;
            float treeMargin = 2f;

            foreach (var block in roadNetwork.blocks)
            {
                District district = null;
                for (int i = 0; i < roadNetwork.blocks.Count; i++)
                {
                    if (roadNetwork.blocks[i] == block)
                    {
                        district = districtManager.GetDistrictForBlock(i);
                        break;
                    }
                }

                if (district == null || district.type != DistrictType.Park) continue;

                int treesX = Mathf.FloorToInt((block.size.x - treeMargin * 2) / treeSpacing);
                int treesZ = Mathf.FloorToInt((block.size.y - treeMargin * 2) / treeSpacing);

                for (int i = 0; i < treesX; i++)
                {
                    for (int j = 0; j < treesZ; j++)
                    {
                        if (Random.value > 0.6f) continue;

                        float x = block.center.x - block.size.x * 0.5f + treeMargin + i * treeSpacing + treeSpacing * 0.5f;
                        float z = block.center.z - block.size.y * 0.5f + treeMargin + j * treeSpacing + treeSpacing * 0.5f;

                        x += Random.Range(-1f, 1f);
                        z += Random.Range(-1f, 1f);

                        Vector3 pos = new Vector3(x, 0, z);
                        GameObject tree = CreateTree(pos);
                        if (tree != null) tree.transform.SetParent(propsParent);
                    }
                }
            }
        }

        private GameObject CreateTree(Vector3 position)
        {
            if (cityGenerator.treePrefab != null)
            {
                GameObject tree = Instantiate(cityGenerator.treePrefab);
                tree.transform.position = position;
                tree.name = "Tree";
                float scale = Random.Range(0.8f, 1.3f);
                tree.transform.localScale = Vector3.one * scale;
                tree.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                return tree;
            }

            GameObject treeGO = new GameObject("Tree");
            treeGO.transform.position = position;

            GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Trunk";
            trunk.transform.SetParent(treeGO.transform);
            trunk.transform.localPosition = new Vector3(0, 1.5f, 0);
            trunk.transform.localScale = new Vector3(0.3f, 1.5f, 0.3f);

            GameObject foliage1 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            foliage1.name = "Foliage1";
            foliage1.transform.SetParent(treeGO.transform);
            foliage1.transform.localPosition = new Vector3(0, 4f, 0);
            foliage1.transform.localScale = new Vector3(1.8f, 1.8f, 1.8f);

            GameObject foliage2 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            foliage2.name = "Foliage2";
            foliage2.transform.SetParent(treeGO.transform);
            foliage2.transform.localPosition = new Vector3(0, 5.2f, 0);
            foliage2.transform.localScale = new Vector3(1.4f, 1.4f, 1.4f);

            Material trunkMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            trunkMat.color = new Color(0.4f, 0.25f, 0.1f);
            trunk.GetComponent<Renderer>().sharedMaterial = trunkMat;

            Material foliageMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            foliageMat.color = new Color(0.2f, 0.5f, 0.15f);
            foliage1.GetComponent<Renderer>().sharedMaterial = foliageMat;
            foliage2.GetComponent<Renderer>().sharedMaterial = foliageMat;

            DestroyCollider(trunk);
            DestroyCollider(foliage1);
            DestroyCollider(foliage2);

            treeGO.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);

            return treeGO;
        }

        private void PlaceVehicles()
        {
            // Place beberapa vehicle di jalan horizontal
            foreach (var road in roadNetwork.horizontalRoads)
            {
                int vehicleCount = Random.Range(0, 3);
                for (int i = 0; i < vehicleCount; i++)
                {
                    float t = Random.Range(0.1f, 0.9f);
                    Vector3 pos = Vector3.Lerp(road.start, road.end, t);
                    GameObject vehicle = CreateVehicle(pos, true);
                    if (vehicle != null) vehicle.transform.SetParent(vehiclesParent);
                }
            }

            foreach (var road in roadNetwork.verticalRoads)
            {
                int vehicleCount = Random.Range(0, 3);
                for (int i = 0; i < vehicleCount; i++)
                {
                    float t = Random.Range(0.1f, 0.9f);
                    Vector3 pos = Vector3.Lerp(road.start, road.end, t);
                    GameObject vehicle = CreateVehicle(pos, false);
                    if (vehicle != null) vehicle.transform.SetParent(vehiclesParent);
                }
            }
        }

        private GameObject CreateVehicle(Vector3 position, bool horizontalRoad)
        {
            if (vehiclePrefab != null)
            {
                GameObject vehicle = Instantiate(vehiclePrefab);
                vehicle.transform.position = position;
                vehicle.transform.rotation = horizontalRoad
                    ? Quaternion.Euler(0, Random.Range(-15f, 15f) + (Random.value > 0.5f ? 0 : 180), 0)
                    : Quaternion.Euler(0, Random.Range(75f, 105f), 0);
                return vehicle;
            }

            // Fallback: simple car shape
            GameObject car = new GameObject("Vehicle");
            car.transform.position = position;

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(car.transform);
            body.transform.localPosition = new Vector3(0, 0.6f, 0);
            body.transform.localScale = new Vector3(2f, 1.2f, 4f);

            // Random warna mobil
            Material carMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Color[] carColors = {
                new Color(0.8f, 0.1f, 0.1f), // Red
                new Color(0.1f, 0.3f, 0.8f), // Blue
                new Color(0.9f, 0.9f, 0.9f), // White
                new Color(0.1f, 0.1f, 0.1f), // Black
                new Color(0.7f, 0.7f, 0.7f), // Gray
            };
            carMat.color = carColors[Random.Range(0, carColors.Length)];
            body.GetComponent<Renderer>().sharedMaterial = carMat;

            // Cabin
            GameObject cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cabin.name = "Cabin";
            cabin.transform.SetParent(car.transform);
            cabin.transform.localPosition = new Vector3(0, 1.4f, -0.3f);
            cabin.transform.localScale = new Vector3(1.8f, 0.8f, 2f);

            Material cabinMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            cabinMat.color = new Color(0.3f, 0.4f, 0.5f);
            cabin.GetComponent<Renderer>().sharedMaterial = cabinMat;

            DestroyCollider(body);
            DestroyCollider(cabin);

            car.transform.rotation = horizontalRoad
                ? Quaternion.Euler(0, Random.Range(-15f, 15f) + (Random.value > 0.5f ? 0 : 180), 0)
                : Quaternion.Euler(0, Random.Range(75f, 105f), 0);

            return car;
        }

        private void PlaceTrafficLights()
        {
            // Traffic light di intersection utama (bukan semua, biar tidak overwhelming)
            int maxTrafficLights = Mathf.Min(roadNetwork.intersections.Count / 4, 20);

            for (int i = 0; i < maxTrafficLights; i++)
            {
                int idx = Random.Range(0, roadNetwork.intersections.Count);
                Vector3 intersection = roadNetwork.intersections[idx];
                CreateTrafficLight(intersection);
            }
        }

        private void CreateTrafficLight(Vector3 position)
        {
            GameObject trafficLight = new GameObject("TrafficLight");
            trafficLight.transform.position = position + Vector3.up * 0.2f;

            GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.transform.SetParent(trafficLight.transform);
            pole.transform.localPosition = new Vector3(0, 2.5f, 0);
            pole.transform.localScale = new Vector3(0.15f, 2.5f, 0.15f);
            Material poleMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            poleMat.color = new Color(0.2f, 0.2f, 0.2f);
            pole.GetComponent<Renderer>().sharedMaterial = poleMat;
            DestroyCollider(pole);

            // 3 lampu (red, yellow, green)
            Color[] lightColors = { Color.red, Color.yellow, Color.green };
            for (int i = 0; i < 3; i++)
            {
                GameObject lightBulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                lightBulb.transform.SetParent(trafficLight.transform);
                lightBulb.transform.localPosition = new Vector3(0.4f, 4f - i * 0.6f, 0);
                lightBulb.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
                Material lightMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                lightMat.color = lightColors[i];
                lightBulb.GetComponent<Renderer>().sharedMaterial = lightMat;
                DestroyCollider(lightBulb);
            }

            trafficLight.transform.SetParent(propsParent);
        }

        private void DestroyCollider(GameObject obj)
        {
            Collider col = obj.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
        }

        public void ClearDetails()
        {
            if (lightsParent != null)
            {
                if (Application.isPlaying) Destroy(lightsParent.gameObject);
                else DestroyImmediate(lightsParent.gameObject);
                lightsParent = null;
            }
            if (propsParent != null)
            {
                if (Application.isPlaying) Destroy(propsParent.gameObject);
                else DestroyImmediate(propsParent.gameObject);
                propsParent = null;
            }
            if (vehiclesParent != null)
            {
                if (Application.isPlaying) Destroy(vehiclesParent.gameObject);
                else DestroyImmediate(vehiclesParent.gameObject);
                vehiclesParent = null;
            }
        }
    }
