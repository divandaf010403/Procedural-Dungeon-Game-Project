using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ADVANCED FEATURE: Generate sungai (river) yang mengalir melalui kota.
/// Sungai menambah keindahan layout kota dan membuat waterfront district.
    ///
    /// Algoritma:
    /// 1. Pilih entry & exit point sungai (2 sisi kota)
    /// 2. Buat path sungai dengan beberapa control points (Bezier curve)
    /// 3. Sample titik-titik di sepanjang path untuk buat mesh
    /// 4. Detect intersection dengan road → buat jembatan (bridge)
    ///
    /// Mesh sungai:
    /// - Plane di Y=0 dengan lebar tertentu
    /// - Material air (biru, semi-transparan)
    /// - Bisa dikombinasi dengan shader untuk efek air mengalir
    /// </summary>
    [System.Serializable]
    public class RiverGenerator
    {
        private CityGenerator cityGenerator;
        private RoadNetwork roadNetwork;

        // Generated objects
        private GameObject riverMeshObject;
        private List<GameObject> bridgeObjects = new List<GameObject>();

        // River path (list of points)
        public List<Vector3> riverPath = new List<Vector3>();

        // River config
        public float riverWidth = 20f;
        public int curveSegments = 30;

        public void Initialize(CityGenerator generator, RoadNetwork roads)
        {
            cityGenerator = generator;
            roadNetwork = roads;
        }

        /// <summary>
        /// Generate sungai + jembatan. Dipanggil setelah road network.
        /// </summary>
        public void GenerateRiver()
        {
            ClearRiver();

            // Tentukan entry & exit point sungai
            float halfSize = cityGenerator.citySize * 0.5f;
            int side = Random.Range(0, 4); // 0=North, 1=East, 2=South, 3=West
            int oppositeSide = (side + 2) % 4;

            Vector3 entry = GetSidePoint(side, halfSize);
            Vector3 exit = GetSidePoint(oppositeSide, halfSize);

            // Buat control points untuk Bezier curve
            List<Vector3> controlPoints = new List<Vector3> { entry };

            // Tambahkan beberapa titik tengah dengan random offset
            int segments = Random.Range(2, 4);
            Vector3 origin = cityGenerator.transform.position;

            for (int i = 1; i < segments; i++)
            {
                float t = (float)i / segments;
                Vector3 linearPoint = Vector3.Lerp(entry, exit, t);

                // Tambahkan random offset (max 30% city size)
                float offsetAmount = cityGenerator.citySize * 0.3f;
                linearPoint += new Vector3(
                    Random.Range(-offsetAmount, offsetAmount),
                    0,
                    Random.Range(-offsetAmount, offsetAmount)
                );

                controlPoints.Add(linearPoint);
            }
            controlPoints.Add(exit);

            // Generate river path dengan Bezier interpolation
            riverPath = GenerateBezierPath(controlPoints, curveSegments);

            // Build mesh sungai
            BuildRiverMesh();

            // Bridge generation disabled - user tidak butuh bridge
            // BuildBridges();

            Debug.Log($"[RiverGenerator] Generated river with {riverPath.Count} points, {bridgeObjects.Count} bridges");
        }

        /// <summary>
        /// Get titik di salah satu sisi kota
        /// </summary>
        private Vector3 GetSidePoint(int side, float halfSize)
        {
            Vector3 origin = cityGenerator.transform.position;
            float t = Random.Range(-0.8f, 0.8f); // Variasi posisi di sepanjang sisi

            switch (side)
            {
                case 0: // North (+Z)
                    return origin + new Vector3(t * halfSize, 0, halfSize);
                case 1: // East (+X)
                    return origin + new Vector3(halfSize, 0, t * halfSize);
                case 2: // South (-Z)
                    return origin + new Vector3(t * halfSize, 0, -halfSize);
                case 3: // West (-X)
                    return origin + new Vector3(-halfSize, 0, t * halfSize);
                default:
                    return origin;
            }
        }

        /// <summary>
        /// Generate smooth path dari control points menggunakan Catmull-Rom spline.
        /// Lebih natural dari Bezier untuk水流 (water flow).
        /// </summary>
        private List<Vector3> GenerateBezierPath(List<Vector3> controlPoints, int segmentsPerSegment)
        {
            List<Vector3> path = new List<Vector3>();

            for (int i = 0; i < controlPoints.Count - 1; i++)
            {
                Vector3 p0 = i == 0 ? controlPoints[i] : controlPoints[i - 1];
                Vector3 p1 = controlPoints[i];
                Vector3 p2 = controlPoints[i + 1];
                Vector3 p3 = i + 2 < controlPoints.Count ? controlPoints[i + 2] : controlPoints[i + 1];

                for (int j = 0; j < segmentsPerSegment; j++)
                {
                    float t = j / (float)segmentsPerSegment;
                    path.Add(CatmullRom(p0, p1, p2, p3, t));
                }
            }
            path.Add(controlPoints[controlPoints.Count - 1]);

            return path;
        }

        /// <summary>
        /// Catmull-Rom spline interpolation
        /// </summary>
        private Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            return 0.5f * (
                (2 * p1) +
                (-p0 + p2) * t +
                (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 +
                (-p0 + 3 * p1 - 3 * p2 + p3) * t3
            );
        }

        /// <summary>
        /// Build mesh sungai: ribbon sepanjang path
        /// </summary>
        private void BuildRiverMesh()
        {
            if (riverPath.Count < 2) return;

            riverMeshObject = new GameObject("RiverMesh");
            riverMeshObject.transform.SetParent(cityGenerator.transform);
            cityGenerator.RegisterSpawnedObject(riverMeshObject);

            MeshFilter mf = riverMeshObject.AddComponent<MeshFilter>();
            MeshRenderer mr = riverMeshObject.AddComponent<MeshRenderer>();

            // Material air
            Material waterMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            waterMat.color = new Color(0.2f, 0.4f, 0.7f, 0.85f);
            waterMat.SetFloat("_Surface", 1); // Transparent
            waterMat.SetFloat("_Blend", 0);
            waterMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            waterMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            waterMat.SetFloat("_ZWrite", 0);
            waterMat.DisableKeyword("_ALPHATEST_ON");
            waterMat.EnableKeyword("_ALPHABLEND_ON");
            waterMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            waterMat.renderQueue = 3000;
            mr.sharedMaterial = waterMat;

            // Build ribbon mesh
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uvs = new List<Vector2>();

            float halfWidth = riverWidth * 0.5f;
            float totalLength = 0f;

            for (int i = 0; i < riverPath.Count; i++)
            {
                Vector3 current = riverPath[i];

                // Hitung direction (ke next point, atau previous kalau di akhir)
                Vector3 dir;
                if (i < riverPath.Count - 1)
                    dir = (riverPath[i + 1] - current).normalized;
                else
                    dir = (current - riverPath[i - 1]).normalized;

                // Perpendicular (di sumbu XZ plane)
                Vector3 perp = new Vector3(-dir.z, 0, dir.x) * halfWidth;

                vertices.Add(current - perp);
                vertices.Add(current + perp);

                if (i > 0)
                {
                    totalLength += Vector3.Distance(riverPath[i - 1], current);
                }

                uvs.Add(new Vector2(0, totalLength * 0.1f));
                uvs.Add(new Vector2(1, totalLength * 0.1f));

                // Triangle (connect dengan previous)
                if (i > 0)
                {
                    int v0 = (i - 1) * 2;
                    int v1 = v0 + 1;
                    int v2 = i * 2;
                    int v3 = v2 + 1;

                    triangles.Add(v0);
                    triangles.Add(v2);
                    triangles.Add(v1);

                    triangles.Add(v1);
                    triangles.Add(v2);
                    triangles.Add(v3);
                }
            }

            Mesh mesh = new Mesh();
            mesh.name = "River";
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            mf.mesh = mesh;

            // Sedikit di bawah ground (Y = -0.1) supaya tidak z-fighting
            riverMeshObject.transform.position = new Vector3(0, -0.1f, 0);
        }

        /// <summary>
        /// Detect intersection sungai dengan road, buat jembatan di titik tersebut.
        /// </summary>
        private void BuildBridges()
        {
            // Cek setiap road segment apakah berpotongan dengan river path
            foreach (var hRoad in roadNetwork.horizontalRoads)
            {
                Vector3 intersection;
                if (FindIntersectionWithPath(hRoad, out intersection))
                {
                    CreateBridge(intersection, hRoad.start, hRoad.end, hRoad.width);
                }
            }
            foreach (var vRoad in roadNetwork.verticalRoads)
            {
                Vector3 intersection;
                if (FindIntersectionWithPath(vRoad, out intersection))
                {
                    CreateBridge(intersection, vRoad.start, vRoad.end, vRoad.width);
                }
            }
        }

        /// <summary>
        /// Cek apakah road segment berpotongan dengan river path
        /// </summary>
        private bool FindIntersectionWithPath(RoadSegment road, out Vector3 intersection)
        {
            for (int i = 0; i < riverPath.Count - 1; i++)
            {
                Vector3 riverA = riverPath[i];
                Vector3 riverB = riverPath[i + 1];

                Vector3 point;
                if (LineLineIntersection(road.start, road.end, riverA, riverB, out point))
                {
                    intersection = point;
                    return true;
                }
            }

            intersection = Vector3.zero;
            return false;
        }

        /// <summary>
        /// 2D line-line intersection (di XZ plane)
        /// </summary>
        private bool LineLineIntersection(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4, out Vector3 intersection)
        {
            // Project ke 2D (XZ plane)
            Vector2 a1 = new Vector2(p1.x, p1.z);
            Vector2 a2 = new Vector2(p2.x, p2.z);
            Vector2 b1 = new Vector2(p3.x, p3.z);
            Vector2 b2 = new Vector2(p4.x, p4.z);

            float d = (a1.x - a2.x) * (b1.y - b2.y) - (a1.y - a2.y) * (b1.x - b2.x);

            if (Mathf.Abs(d) < 0.0001f)
            {
                intersection = Vector3.zero;
                return false;
            }

            float t = ((a1.x - b1.x) * (b1.y - b2.y) - (a1.y - b1.y) * (b1.x - b2.x)) / d;

            if (t < 0 || t > 1)
            {
                intersection = Vector3.zero;
                return false;
            }

            intersection = new Vector3(
                p1.x + t * (p2.x - p1.x),
                p1.y,
                p1.z + t * (p2.z - p1.z)
            );

            return true;
        }

        /// <summary>
        /// Buat jembatan (bridge) di titik tertentu.
        /// BRIDGE SEHARUSNYA DI LEVEL JALAN (Y=0), bukan terbang.
        /// Bridge adalah jalan yang menyeberangi air - jadi flat dengan road.
        /// Yang membedakan bridge dari road biasa: railing di kedua sisi dan tiang di bawah.
        /// </summary>
        private void CreateBridge(Vector3 position, Vector3 roadStart, Vector3 roadEnd, float roadWidth)
        {
            Vector3 dir = (roadEnd - roadStart).normalized;

            // Bridge top di Y=0 (sama dengan jalan)
            // Bridge bawah di Y=-0.5 (sedikit lebih dalam dari road, menyentuh dasar sungai)
            float bridgeTopY = 0f;
            float bridgeBottomY = -0.5f;
            float bridgeThickness = bridgeTopY - bridgeBottomY;

            // 1. Bridge deck - jalan yang menyeberangi air
            GameObject bridge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bridge.name = "Bridge";
            bridge.transform.SetParent(cityGenerator.transform);
            // Center di tengah-tengah antara top dan bottom
            bridge.transform.position = position + Vector3.up * ((bridgeTopY + bridgeBottomY) * 0.5f);

            float length = riverWidth + 4f;

            if (Mathf.Abs(dir.x) > Mathf.Abs(dir.z))
            {
                bridge.transform.localScale = new Vector3(length, bridgeThickness, roadWidth + 0.5f);
            }
            else
            {
                bridge.transform.localScale = new Vector3(roadWidth + 0.5f, bridgeThickness, length);
            }

            Material bridgeMat = CityGenerator.CreateMaterial("Bridge", new Color(0.35f, 0.35f, 0.37f));
            bridge.GetComponent<Renderer>().sharedMaterial = bridgeMat;
            DestroyCollider(bridge);

            // 2. Tiang pendek di pojok (hanya estetika, bukan pendukung struktural karena bridge flat)
            // Tiangnya sedikit naik dari air untuk menunjukkan "ini bridge"
            CreateBridgePillars(position, dir, roadWidth, bridgeTopY, bridgeBottomY);

            // 3. Railing di kedua sisi bridge
            CreateRailing(bridge.transform, dir);

            bridgeObjects.Add(bridge);
        }

        /// <summary>
        /// Buat 4 tiang kecil di pojok bridge (hanya estetika, bridge flat jadi tiang tidak perlu tinggi).
        /// Tiang hanya di atas air untuk menunjukkan "ini bridge, ada railing".
        /// </summary>
        private void CreateBridgePillars(Vector3 position, Vector3 dir, float roadWidth, float bridgeTopY, float bridgeBottomY)
        {
            Vector3 perp = new Vector3(-dir.z, 0, dir.x);
            float length = riverWidth + 4f;

            // 4 posisi tiang kecil di pojok bridge, di atas air (Y=0 ke atas)
            Vector3[] pillarOffsets = new Vector3[]
            {
                perp * (roadWidth * 0.5f + 0.5f) + dir * (length * 0.5f - 0.5f),
                -perp * (roadWidth * 0.5f + 0.5f) + dir * (length * 0.5f - 0.5f),
                perp * (roadWidth * 0.5f + 0.5f) - dir * (length * 0.5f - 0.5f),
                -perp * (roadWidth * 0.5f + 0.5f) - dir * (length * 0.5f - 0.5f),
            };

            foreach (var offset in pillarOffsets)
            {
                Vector3 pos = position + offset;
                // Tiang pendek dari Y=0 ke Y=1.2 (sebagai tambahan visual railing)
                GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pillar.name = "BridgeCorner";
                pillar.transform.SetParent(cityGenerator.transform);
                pillar.transform.position = pos + Vector3.up * 0.6f;
                pillar.transform.localScale = new Vector3(0.4f, 1.2f, 0.4f);

                Material pillarMat = CityGenerator.CreateMaterial("BridgeCorner", new Color(0.5f, 0.5f, 0.52f));
                pillar.GetComponent<Renderer>().sharedMaterial = pillarMat;
                DestroyCollider(pillar);

                bridgeObjects.Add(pillar);
            }
        }

        /// <summary>
        /// Railing bridge - tiang pendek di kedua sisi bridge untuk keamanan visual.
        /// </summary>
        private void CreateRailing(Transform parent, Vector3 roadDir)
        {
            Vector3 perp = new Vector3(-roadDir.z, 0, roadDir.x);
            float railingHeight = 0.8f;

            for (int side = -1; side <= 1; side += 2)
            {
                GameObject railing = GameObject.CreatePrimitive(PrimitiveType.Cube);
                railing.name = "BridgeRailing";
                railing.transform.SetParent(parent);

                // Railing di atas deck (parent), offset dari center
                float deckWidth = parent.localScale.z;
                railing.transform.localPosition = perp * side * (deckWidth * 0.5f + 0.15f) + Vector3.up * (railingHeight * 0.5f + 0.05f);

                Vector3 scale;
                if (Mathf.Abs(roadDir.x) > Mathf.Abs(roadDir.z))
                    scale = new Vector3(parent.localScale.x, railingHeight, 0.15f);
                else
                    scale = new Vector3(0.15f, railingHeight, parent.localScale.z);
                railing.transform.localScale = scale;

                Material mat = CityGenerator.CreateMaterial("Railing", new Color(0.6f, 0.6f, 0.6f));
                railing.GetComponent<Renderer>().sharedMaterial = mat;
                DestroyCollider(railing);
            }
        }

        // Duplicate CreateRailing removed - using the elevated bridge version above

        private void DestroyCollider(GameObject obj)
        {
            Collider col = obj.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Object.Destroy(col);
                else Object.DestroyImmediate(col);
            }
        }

        public void ClearRiver()
        {
            riverPath.Clear();

            if (riverMeshObject != null)
            {
                if (Application.isPlaying) Object.Destroy(riverMeshObject);
                else Object.DestroyImmediate(riverMeshObject);
                riverMeshObject = null;
            }

            foreach (var b in bridgeObjects)
            {
                if (b != null)
                {
                    if (Application.isPlaying) Object.Destroy(b);
                    else Object.DestroyImmediate(b);
                }
            }
            bridgeObjects.Clear();
        }
    }
