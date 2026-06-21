using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ADVANCED: Generate procedural building shapes yang varied.
    /// Tidak hanya box standar, tapi juga L-shape, T-shape, U-shape, dll.
    ///
    /// Cara kerja:
    /// 1. Berdasarkan LotShape, generate mesh yang sesuai
    /// 2. Tambah detail (windows, doors) sebagai child meshes atau texture
    /// 3. Pakai material dengan warna yang sesuai district
    ///
    /// Ini membuat kota jauh lebih varied dibanding cube seragam.
    /// </summary>
    public static class ProceduralBuildingShape
    {
        /// <summary>
        /// Buat GameObject bangunan dengan shape sesuai lot
        /// </summary>
        public static GameObject CreateBuilding(
            Lot lot,
            District district,
            Vector3 position,
            Vector2 footprint,
            float height,
            Material material,
            CityGenerator cityGenerator)
        {
            GameObject building;

            switch (lot.shape)
            {
                case LotShape.LShape:
                    building = CreateLShape(position, footprint, height, material);
                    break;
                case LotShape.TShape:
                    building = CreateTShape(position, footprint, height, material);
                    break;
                case LotShape.UShape:
                    building = CreateUShape(position, footprint, height, material);
                    break;
                case LotShape.Wide:
                    building = CreateWideBuilding(position, footprint, height, material);
                    break;
                case LotShape.Narrow:
                    building = CreateNarrowBuilding(position, footprint, height, material);
                    break;
                default:
                    building = CreateRectangleBuilding(position, footprint, height, material);
                    break;
            }

            // Add detail (windows, roof)
            AddBuildingDetails(building, district, footprint, height);

            // Random rotasi Y
            float rotY = Random.Range(0f, 360f);
            building.transform.rotation = Quaternion.Euler(0, rotY, 0);

            return building;
        }

        private static GameObject CreateRectangleBuilding(Vector3 pos, Vector2 footprint, float height, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Building_Rect";
            go.transform.position = pos + Vector3.up * (height * 0.5f);
            go.transform.localScale = new Vector3(footprint.x, height, footprint.y);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            DestroyCollider(go);
            return go;
        }

        private static GameObject CreateLShape(Vector3 pos, Vector2 footprint, float height, Material mat)
        {
            // L-shape: 2 boxes membentuk huruf L
            GameObject parent = new GameObject("Building_LShape");
            parent.transform.position = pos;

            // Box 1 (main body)
            GameObject box1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box1.name = "Body";
            box1.transform.SetParent(parent.transform);
            float bodyW = footprint.x * 0.65f;
            float bodyD = footprint.y;
            box1.transform.localScale = new Vector3(bodyW, height, bodyD);
            box1.transform.localPosition = new Vector3(-footprint.x * 0.175f, height * 0.5f, 0);
            box1.GetComponent<Renderer>().sharedMaterial = mat;
            DestroyCollider(box1);

            // Box 2 (perpendicular wing)
            GameObject box2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box2.name = "Wing";
            box2.transform.SetParent(parent.transform);
            float wingW = footprint.x * 0.35f;
            float wingD = footprint.y * 0.55f;
            box2.transform.localScale = new Vector3(wingW, height, wingD);
            box2.transform.localPosition = new Vector3(footprint.x * 0.175f, height * 0.5f, -footprint.y * 0.225f);
            box2.GetComponent<Renderer>().sharedMaterial = mat;
            DestroyCollider(box2);

            return parent;
        }

        private static GameObject CreateTShape(Vector3 pos, Vector2 footprint, float height, Material mat)
        {
            // T-shape: 2 boxes membentuk huruf T
            GameObject parent = new GameObject("Building_TShape");
            parent.transform.position = pos;

            // Vertical stem (atas ke bawah)
            GameObject stem = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stem.name = "Stem";
            stem.transform.SetParent(parent.transform);
            float stemW = footprint.x * 0.35f;
            stem.transform.localScale = new Vector3(stemW, height, footprint.y);
            stem.transform.localPosition = new Vector3(0, height * 0.5f, 0);
            stem.GetComponent<Renderer>().sharedMaterial = mat;
            DestroyCollider(stem);

            // Horizontal top (atas T)
            GameObject top = GameObject.CreatePrimitive(PrimitiveType.Cube);
            top.name = "Top";
            top.transform.SetParent(parent.transform);
            float topH = height * 0.4f;
            top.transform.localScale = new Vector3(footprint.x, topH, footprint.y * 0.3f);
            top.transform.localPosition = new Vector3(0, height - topH * 0.5f, footprint.y * 0.35f);
            top.GetComponent<Renderer>().sharedMaterial = mat;
            DestroyCollider(top);

            return parent;
        }

        private static GameObject CreateUShape(Vector3 pos, Vector2 footprint, float height, Material mat)
        {
            // U-shape: 3 boxes membentuk huruf U (untuk courtyard)
            GameObject parent = new GameObject("Building_UShape");
            parent.transform.position = pos;

            float wingW = footprint.x * 0.3f;
            float wallDepth = footprint.y * 0.3f;

            // Left wing
            GameObject left = GameObject.CreatePrimitive(PrimitiveType.Cube);
            left.name = "LeftWing";
            left.transform.SetParent(parent.transform);
            left.transform.localScale = new Vector3(wingW, height, footprint.y);
            left.transform.localPosition = new Vector3(-footprint.x * 0.35f, height * 0.5f, 0);
            left.GetComponent<Renderer>().sharedMaterial = mat;
            DestroyCollider(left);

            // Right wing
            GameObject right = GameObject.CreatePrimitive(PrimitiveType.Cube);
            right.name = "RightWing";
            right.transform.SetParent(parent.transform);
            right.transform.localScale = new Vector3(wingW, height, footprint.y);
            right.transform.localPosition = new Vector3(footprint.x * 0.35f, height * 0.5f, 0);
            right.GetComponent<Renderer>().sharedMaterial = mat;
            DestroyCollider(right);

            // Back wall (menghubungkan kedua wing)
            GameObject back = GameObject.CreatePrimitive(PrimitiveType.Cube);
            back.name = "BackWall";
            back.transform.SetParent(parent.transform);
            back.transform.localScale = new Vector3(footprint.x * 0.7f, height, wallDepth);
            back.transform.localPosition = new Vector3(0, height * 0.5f, -footprint.y * 0.35f);
            back.GetComponent<Renderer>().sharedMaterial = mat;
            DestroyCollider(back);

            return parent;
        }

        private static GameObject CreateWideBuilding(Vector3 pos, Vector2 footprint, float height, Material mat)
        {
            // Wide: box lebar tapi pendek, kadang dengan section tambahan
            GameObject parent = new GameObject("Building_Wide");
            parent.transform.position = pos;

            GameObject main = GameObject.CreatePrimitive(PrimitiveType.Cube);
            main.name = "Main";
            main.transform.SetParent(parent.transform);
            main.transform.localScale = new Vector3(footprint.x, height * 0.7f, footprint.y);
            main.transform.localPosition = new Vector3(0, height * 0.35f, 0);
            main.GetComponent<Renderer>().sharedMaterial = mat;
            DestroyCollider(main);

            // Optional: atap tinggi kecil (atap gudang/warehouse)
            if (Random.value > 0.5f)
            {
                GameObject roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
                roof.name = "RoofBlock";
                roof.transform.SetParent(parent.transform);
                float roofW = footprint.x * 0.7f;
                float roofD = footprint.y * 0.6f;
                float roofH = height * 0.3f;
                roof.transform.localScale = new Vector3(roofW, roofH, roofD);
                roof.transform.localPosition = new Vector3(0, height * 0.7f + roofH * 0.5f, 0);
                roof.GetComponent<Renderer>().sharedMaterial = mat;
                DestroyCollider(roof);
            }

            return parent;
        }

        private static GameObject CreateNarrowBuilding(Vector3 pos, Vector2 footprint, float height, Material mat)
        {
            // Narrow: box kecil tapi tinggi (seperti townhouse)
            GameObject parent = new GameObject("Building_Narrow");
            parent.transform.position = pos;

            GameObject main = GameObject.CreatePrimitive(PrimitiveType.Cube);
            main.name = "Main";
            main.transform.SetParent(parent.transform);
            main.transform.localScale = new Vector3(footprint.x, height, footprint.y);
            main.transform.localPosition = new Vector3(0, height * 0.5f, 0);
            main.GetComponent<Renderer>().sharedMaterial = mat;
            DestroyCollider(main);

            // Tambahkan atap segitiga (atap rumah)
            if (height > 8f && Random.value > 0.5f)
            {
                GameObject roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
                roof.name = "Roof";
                roof.transform.SetParent(parent.transform);
                float roofH = height * 0.25f;
                roof.transform.localScale = new Vector3(footprint.x * 1.1f, roofH, footprint.y * 1.1f);
                roof.transform.localPosition = new Vector3(0, height + roofH * 0.5f, 0);
                Material roofMat = new Material(mat);
                roofMat.color = new Color(0.4f, 0.2f, 0.15f); // Dark red/brown
                roof.GetComponent<Renderer>().sharedMaterial = roofMat;
                DestroyCollider(roof);
            }

            return parent;
        }

        /// <summary>
        /// Add detail tambahan ke bangunan: windows pattern, dll
        /// </summary>
        private static void AddBuildingDetails(GameObject building, District district, Vector2 footprint, float height)
        {
            // Hanya tambah detail untuk bangunan yang cukup besar
            if (height < 5f || footprint.x < 4f) return;

            // Tambahkan roof element kecil untuk Industrial
            if (district.type == DistrictType.Industrial && Random.value > 0.6f)
            {
                GameObject chimney = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                chimney.name = "Chimney";
                chimney.transform.SetParent(building.transform);
                chimney.transform.localPosition = new Vector3(
                    Random.Range(-footprint.x * 0.3f, footprint.x * 0.3f),
                    height + 2f,
                    Random.Range(-footprint.y * 0.3f, footprint.y * 0.3f)
                );
                chimney.transform.localScale = new Vector3(0.5f, 2f, 0.5f);
                Material chimneyMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                chimneyMat.color = new Color(0.3f, 0.3f, 0.3f);
                chimney.GetComponent<Renderer>().sharedMaterial = chimneyMat;
                DestroyCollider(chimney);
            }

            // Untuk Downtown, tambahkan antena di atas
            if (district.type == DistrictType.Downtown && Random.value > 0.7f)
            {
                GameObject antenna = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                antenna.name = "Antenna";
                antenna.transform.SetParent(building.transform);
                antenna.transform.localPosition = new Vector3(0, height + 1f, 0);
                antenna.transform.localScale = new Vector3(0.1f, 2f, 0.1f);
                Material antennaMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                antennaMat.color = new Color(0.2f, 0.2f, 0.2f);
                antenna.GetComponent<Renderer>().sharedMaterial = antennaMat;
                DestroyCollider(antenna);
            }
        }

        private static void DestroyCollider(GameObject obj)
        {
            Collider col = obj.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Object.Destroy(col);
                else Object.DestroyImmediate(col);
            }
        }
    }
