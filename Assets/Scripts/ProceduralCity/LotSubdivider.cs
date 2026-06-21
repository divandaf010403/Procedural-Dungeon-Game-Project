using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Algoritma untuk membagi CityBlock menjadi Lot-Lot kecil.
/// Ini adalah ADVANCED UPGRADE dari sistem basic yang hanya pakai blok.
    ///
    /// Algoritma:
    /// 1. Tentukan jumlah lot per blok (dari district.lotSubdivisionMin/Max)
    /// 2. Pilih orientation (landscape vs portrait)
    /// 3. Recursively subdivide block (binary split)
    /// 4. Assign lot shape based on posisi (corner = L-shape, dll)
    /// 5. Tentukan road access untuk tiap lot
    ///
    /// Dengan lot subdivision, layout jadi jauh lebih varied dan natural.
    /// </summary>
    public static class LotSubdivider
    {
        /// <summary>
        /// Subdivide sebuah blok menjadi lot-lot kecil berdasarkan district rules.
        /// </summary>
        public static List<Lot> SubdivideBlock(CityBlock block, District district)
        {
            List<Lot> lots = new List<Lot>();

            // Tentukan jumlah lot per dimension (misal 2x2, 3x3, dst)
            int targetLotCount = Random.Range(
                district.lotSubdivisionMin * district.lotSubdivisionMin,
                district.lotSubdivisionMax * district.lotSubdivisionMax + 1
            );

            // Pilih subdivision pattern (lebih natural jika varied)
            SubdivisionPattern pattern = ChoosePattern(block, district, targetLotCount);

            // Apply pattern
            switch (pattern)
            {
                case SubdivisionPattern.Grid:
                    lots = SubdivideGrid(block, district, pattern);
                    break;
                case SubdivisionPattern.Strip:
                    lots = SubdivideStrip(block, district);
                    break;
                case SubdivisionPattern.BinarySplit:
                    lots = SubdivideBinarySplit(block, district);
                    break;
                default:
                    lots = SubdivideGrid(block, district, pattern);
                    break;
            }

            // Assign lot shape berdasarkan posisi
            AssignLotShapes(lots, block);

            // Tentukan road access
            AssignRoadAccess(lots, block);

            return lots;
        }

        /// <summary>
        /// Pilih pattern subdivision berdasarkan district dan random
        /// </summary>
        private static SubdivisionPattern ChoosePattern(CityBlock block, District district, int targetCount)
        {
            float r = Random.value;

            // Industrial dan Large Commercial biasanya pakai strip
            if (district.type == DistrictType.Industrial && r < 0.7f)
                return SubdivisionPattern.Strip;

            // Suburbs dan Waterfront lebih suka binary split untuk variasi
            if ((district.type == DistrictType.Suburbs || district.type == DistrictType.Waterfront) && r < 0.5f)
                return SubdivisionPattern.BinarySplit;

            // Default: grid
            return SubdivisionPattern.Grid;
        }

        /// <summary>
        /// Grid subdivision: bagi blok jadi grid NxM
        /// </summary>
        private static List<Lot> SubdivideGrid(CityBlock block, District district, SubdivisionPattern pattern)
        {
            List<Lot> lots = new List<Lot>();

            // Tentukan orientasi (lebih banyak lot ke arah yang lebih panjang)
            bool landscape = block.size.x >= block.size.y;

            int n, m;
            if (landscape)
            {
                n = Random.Range(district.lotSubdivisionMin, district.lotSubdivisionMax + 1);
                m = Mathf.Max(1, Random.Range(district.lotSubdivisionMin - 1, district.lotSubdivisionMax));
            }
            else
            {
                m = Random.Range(district.lotSubdivisionMin, district.lotSubdivisionMax + 1);
                n = Mathf.Max(1, Random.Range(district.lotSubdivisionMin - 1, district.lotSubdivisionMax));
            }

            float lotW = block.size.x / n;
            float lotH = block.size.y / m;

            // Padding antar lot (gap untuk jalan kecil atau taman)
            float gap = Mathf.Min(lotW, lotH) * 0.08f;

            Vector3 origin = block.center - new Vector3(block.size.x * 0.5f, 0, block.size.y * 0.5f);

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < m; j++)
                {
                    Vector3 lotCenter = origin + new Vector3(
                        (i + 0.5f) * lotW,
                        0,
                        (j + 0.5f) * lotH
                    );

                    Lot lot = new Lot
                    {
                        center = lotCenter,
                        size = new Vector2(lotW - gap, lotH - gap),
                        shape = LotShape.Rectangle,
                        parentBlock = block
                    };

                    lots.Add(lot);
                }
            }

            return lots;
        }

        /// <summary>
        /// Strip subdivision: bagi blok jadi strip horizontal/vertikal
        /// Cocok untuk Industrial (warehouse panjang)
        /// </summary>
        private static List<Lot> SubdivideStrip(CityBlock block, District district)
        {
            List<Lot> lots = new List<Lot>();

            // Pilih orientasi strip
            bool horizontal = Random.value > 0.5f;
            int stripCount = Random.Range(2, 5);

            if (horizontal)
            {
                float stripH = block.size.y / stripCount;
                float gap = stripH * 0.05f;
                Vector3 origin = block.center - new Vector3(block.size.x * 0.5f, 0, block.size.y * 0.5f);

                for (int j = 0; j < stripCount; j++)
                {
                    Vector3 lotCenter = origin + new Vector3(0, 0, (j + 0.5f) * block.size.y / stripCount);
                    lots.Add(new Lot
                    {
                        center = lotCenter,
                        size = new Vector2(block.size.x * 0.95f, stripH - gap),
                        shape = LotShape.Wide,
                        parentBlock = block
                    });
                }
            }
            else
            {
                float stripW = block.size.x / stripCount;
                float gap = stripW * 0.05f;
                Vector3 origin = block.center - new Vector3(block.size.x * 0.5f, 0, block.size.y * 0.5f);

                for (int i = 0; i < stripCount; i++)
                {
                    Vector3 lotCenter = origin + new Vector3((i + 0.5f) * block.size.x / stripCount, 0, 0);
                    lots.Add(new Lot
                    {
                        center = lotCenter,
                        size = new Vector2(stripW - gap, block.size.y * 0.95f),
                        shape = LotShape.Narrow,
                        parentBlock = block
                    });
                }
            }

            return lots;
        }

        /// <summary>
        /// Binary subdivision: bagi blok jadi 2, lalu subdivide lagi recursively.
        /// Menghasilkan lot dengan ukuran yang varied (tidak seragam).
        /// </summary>
        private static List<Lot> SubdivideBinarySplit(CityBlock block, District district)
        {
            List<Lot> lots = new List<Lot>();
            BinarySplitRecursive(block, district, 2, lots);
            return lots;
        }

        private static void BinarySplitRecursive(CityBlock block, District district, int depth, List<Lot> result)
        {
            // Base case
            if (depth <= 0 || block.size.x < 8f || block.size.y < 8f)
            {
                result.Add(new Lot
                {
                    center = block.center,
                    size = block.size,
                    shape = LotShape.Rectangle,
                    parentBlock = block
                });
                return;
            }

            // Pilih axis split
            bool splitVertical = block.size.x > block.size.y ? Random.value > 0.3f : Random.value < 0.3f;

            float splitT = Random.Range(0.4f, 0.6f);

            if (splitVertical)
            {
                float splitX = block.size.x * splitT;
                CityBlock blockA = new CityBlock
                {
                    center = block.center + new Vector3(-block.size.x * 0.5f + splitX * 0.5f, 0, 0),
                    size = new Vector2(splitX, block.size.y),
                    blockType = block.blockType
                };
                CityBlock blockB = new CityBlock
                {
                    center = block.center + new Vector3(block.size.x * 0.5f - (block.size.x - splitX) * 0.5f, 0, 0),
                    size = new Vector2(block.size.x - splitX, block.size.y),
                    blockType = block.blockType
                };

                BinarySplitRecursive(blockA, district, depth - 1, result);
                BinarySplitRecursive(blockB, district, depth - 1, result);
            }
            else
            {
                float splitZ = block.size.y * splitT;
                CityBlock blockA = new CityBlock
                {
                    center = block.center + new Vector3(0, 0, -block.size.y * 0.5f + splitZ * 0.5f),
                    size = new Vector2(block.size.x, splitZ),
                    blockType = block.blockType
                };
                CityBlock blockB = new CityBlock
                {
                    center = block.center + new Vector3(0, 0, block.size.y * 0.5f - (block.size.y - splitZ) * 0.5f),
                    size = new Vector2(block.size.x, block.size.y - splitZ),
                    blockType = block.blockType
                };

                BinarySplitRecursive(blockA, district, depth - 1, result);
                BinarySplitRecursive(blockB, district, depth - 1, result);
            }
        }

        /// <summary>
        /// Assign lot shape berdasarkan posisi di blok.
        /// Corner lot = L-shape (lebih premium)
        /// Edge lot = Rectangle normal
        /// Center lot = bisa U-shape (dengan courtyard)
        /// </summary>
        private static void AssignLotShapes(List<Lot> lots, CityBlock block)
        {
            int n = lots.Count;
            if (n == 0) return;

            for (int i = 0; i < n; i++)
            {
                Lot lot = lots[i];

                // Detect corner: lot di pojok blok
                bool isCornerX = Mathf.Abs(lot.center.x - block.center.x) > block.size.x * 0.4f;
                bool isCornerZ = Mathf.Abs(lot.center.z - block.center.z) > block.size.y * 0.4f;
                bool isCorner = isCornerX && isCornerZ;

                if (isCorner && Random.value < 0.3f)
                {
                    lot.shape = LotShape.LShape;
                }
                else if (Random.value < 0.05f && lot.size.x > 12f && lot.size.y > 12f)
                {
                    lot.shape = LotShape.UShape;
                }
            }
        }

        /// <summary>
        /// Tentukan apakah lot punya akses jalan dan arah hadapnya.
        /// Lot di pinggir blok = punya road access.
        /// Lot di tengah = mungkin tidak (perlu alley atau di-skip).
        /// </summary>
        private static void AssignRoadAccess(List<Lot> lots, CityBlock block)
        {
            foreach (var lot in lots)
            {
                bool atEdgeX = Mathf.Abs(lot.center.x - block.center.x) > block.size.x * 0.35f;
                bool atEdgeZ = Mathf.Abs(lot.center.z - block.center.z) > block.size.y * 0.35f;

                lot.hasRoadAccess = atEdgeX || atEdgeZ;

                if (atEdgeX && !atEdgeZ)
                {
                    // Hadap ke arah X
                    lot.roadFacingDirection = lot.center.x > block.center.x ? Vector3.right : Vector3.left;
                }
                else if (atEdgeZ && !atEdgeX)
                {
                    // Hadap ke arah Z
                    lot.roadFacingDirection = lot.center.z > block.center.z ? Vector3.forward : Vector3.back;
                }
                else if (atEdgeX && atEdgeZ)
                {
                    // Corner - random salah satu
                    lot.roadFacingDirection = Random.value > 0.5f ? Vector3.right : Vector3.forward;
                }
            }
        }
    }

    public enum SubdivisionPattern
    {
        Grid,
        Strip,
        BinarySplit
    }
