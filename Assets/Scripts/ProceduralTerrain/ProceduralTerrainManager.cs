using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Terrain))]
public class ProceduralTerrainManager : MonoBehaviour
{
    [Header("Global Settings")]
    public int seed = 1337;
    public float terrainHeight = 200f; 
    public Vector2 offset;
    public Noise.NormalizeMode normalizeMode = Noise.NormalizeMode.Global;

    [Header("1. Continentalness (Daratan Dasar)")]
    public float baseScale = 250f;
    [Range(0, 1)] public float baseHeightContribution = 0.3f; 
    public AnimationCurve baseHeightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("1.5 Domain Warping (Distorsi Geografi Organik)")]
    [Tooltip("Membengkokkan koordinat noise agar benua dan gunung tidak terlihat asimetris dan kaku.")]
    public bool enableDomainWarping = true;
    public float warpScale = 100f;
    [Range(0, 100)] public float warpStrength = 40f;

    [Header("2. Erosion / Plains Mask")]
    public float maskScale = 150f;
    [Range(0, 1)] public float plainsThreshold = 0.45f; 
    [Range(0, 1)] public float maskBlend = 0.15f; 

    [Header("3. Mountain Details")]
    public float detailScale = 50f;
    public int detailOctaves = 5;
    [Range(0, 1)] public float persistence = 0.5f;
    public float lacunarity = 2f;
    [Range(0, 1)] public float mountainHeightContribution = 0.7f;
    public AnimationCurve mountainHeightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("4. Plateaus / Mesas (Daratan Tinggi Rata)")]
    public bool enablePlateaus = true;
    public float plateauScale = 120f;
    [Range(0, 1)] [Tooltip("Batas tinggi di mana gunung dipotong rata jadi Plateau")]
    public float plateauFlattenThreshold = 0.6f;

    [Header("5. River System (Sungai)")]
    public bool enableRivers = true;
    public float riverScale = 150f;
    [Range(0, 1)] public float riverThreshold = 0.04f; 
    [Range(0, 1)] public float riverDepth = 0.05f;

    [Header("6. Lake System (Danau)")]
    public bool enableLakes = true;
    public float lakeScale = 100f;
    [Range(0, 1)] public float lakeThreshold = 0.3f; 
    [Range(0, 1)] public float lakeDepth = 0.15f;

    [Header("7. Canyon / Ravine (Jurang Retakan Bumi)")]
    public bool enableCanyons = true;
    public float canyonScale = 80f;
    [Range(0, 1)] public float canyonThreshold = 0.015f; // Sangat tipis
    [Range(0, 1)] public float canyonDepth = 0.4f; // Sangat dalam

    [Header("8. Meteor Craters (Kawah Berapi/Meteor)")]
    public bool enableCraters = true;
    public float craterScale = 60f;
    [Range(0, 1)] public float craterThreshold = 0.1f; // Hanya kawah-kawah kecil yang terpilih
    [Range(0, 2)] public float craterRimHeight = 0.2f; // Bibir kawah naik
    [Range(0, 1)] public float craterDepth = 0.15f; // Dalam kawah

    [Header("Island Falloff")]
    public bool useFalloff = true;
    public float falloffIntensity = 1f;

    [Header("Biome / Texture Splatmap Settings")]
    public TerrainBiome[] biomes;

    [System.Serializable]
    public class TerrainBiome
    {
        public string name;
        [Range(0, 1)] public float startHeight;
        [Range(0, 90)] public float minSlope = 0f;
        [Range(0, 90)] public float maxSlope = 90f;
        public int terrainLayerIndex;
    }

    private Terrain terrain;
    private TerrainData terrainData;
    private float[,] falloffMap;

    public void GenerateTerrain()
    {
        terrain = GetComponent<Terrain>();
        terrainData = terrain.terrainData;

        int resolution = terrainData.heightmapResolution;

        if (useFalloff) falloffMap = FalloffGenerator.GenerateFalloffMap(resolution);

        float[,] baseMap = Noise.GenerateNoiseMap(resolution, resolution, seed, baseScale, 3, 0.5f, 2f, offset, normalizeMode);
        float[,] maskMap = Noise.GenerateNoiseMap(resolution, resolution, seed + 1, maskScale, 3, 0.5f, 2f, offset, normalizeMode);
        float[,] detailMap = Noise.GenerateNoiseMap(resolution, resolution, seed + 2, detailScale, detailOctaves, persistence, lacunarity, offset, normalizeMode);
        
        // Use warp map to bend coordinates
        float[,] warpMapX = enableDomainWarping ? Noise.GenerateNoiseMap(resolution, resolution, seed + 10, warpScale, 3, 0.5f, 2f, offset, normalizeMode) : null;
        float[,] warpMapY = enableDomainWarping ? Noise.GenerateNoiseMap(resolution, resolution, seed + 11, warpScale, 3, 0.5f, 2f, offset, normalizeMode) : null;

        float[,] plateauMap = enablePlateaus ? Noise.GenerateNoiseMap(resolution, resolution, seed + 9, plateauScale, 2, 0.5f, 2f, offset, normalizeMode) : null;
        float[,] riverMap = enableRivers ? Noise.GenerateNoiseMap(resolution, resolution, seed + 3, riverScale, 4, 0.5f, 2f, offset, normalizeMode) : null;
        float[,] lakeMap = enableLakes ? Noise.GenerateNoiseMap(resolution, resolution, seed + 4, lakeScale, 2, 0.5f, 2f, offset, normalizeMode) : null;
        float[,] canyonMap = enableCanyons ? Noise.GenerateNoiseMap(resolution, resolution, seed + 5, canyonScale, 3, 0.5f, 2f, offset, normalizeMode) : null;
        float[,] craterMap = enableCraters ? Noise.GenerateNoiseMap(resolution, resolution, seed + 6, craterScale, 1, 0.5f, 2f, offset, normalizeMode) : null;

        float[,] heights = new float[resolution, resolution];

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int warpX = x;
                int warpY = y;

                if (enableDomainWarping)
                {
                    warpX = Mathf.Clamp(Mathf.RoundToInt(x + (warpMapX[x, y] - 0.5f) * 2f * warpStrength), 0, resolution - 1);
                    warpY = Mathf.Clamp(Mathf.RoundToInt(y + (warpMapY[x, y] - 0.5f) * 2f * warpStrength), 0, resolution - 1);
                }

                float baseH = baseHeightCurve.Evaluate(baseMap[warpX, warpY]) * baseHeightContribution;
                float maskStrength = Mathf.InverseLerp(plainsThreshold, plainsThreshold + maskBlend, maskMap[warpX, warpY]);
                
                float rawDetailH = mountainHeightCurve.Evaluate(detailMap[warpX, warpY]);
                
                // 4. Plateaus (Mesas) - Potong rata puncaknya jika berada di zona plateau
                if (enablePlateaus)
                {
                    float isPlateauZone = plateauMap[warpX, warpY];
                    if (isPlateauZone > 0.5f && rawDetailH > plateauFlattenThreshold)
                    {
                        // Lerp smoothly flatten the top based on how deep they are in plateau zone
                        float flattenStrength = Mathf.InverseLerp(0.5f, 0.7f, isPlateauZone);
                        rawDetailH = Mathf.Lerp(rawDetailH, plateauFlattenThreshold, flattenStrength);
                    }
                }

                float detailH = rawDetailH * mountainHeightContribution * maskStrength;
                float finalHeight = baseH + detailH;

                // 8. Meteor Craters (Kawah Berapi/Meteor)
                if (enableCraters)
                {
                    float cVal = craterMap[warpX, warpY]; 
                    float craterDist = Mathf.Abs(cVal - 0.5f) * 2f; 
                    
                    if (craterDist < craterThreshold)
                    {
                        float distNorm = craterDist / craterThreshold; // 0 (tengah) ke 1 (pinggir)
                        
                        // Bentuk kawah: Parabola untuk gundukan pinggir, -Cos buat lubang.
                        float rim = Mathf.Sin(distNorm * Mathf.PI) * craterRimHeight; 
                        float pit = (1f - distNorm) * craterDepth; 
                        
                        finalHeight += rim;
                        finalHeight -= pit;
                    }
                }

                // 5. Mathematical Ridged Multifractal Rivers
                if (enableRivers)
                {
                    float riverValue = Mathf.Abs(riverMap[warpX, warpY] - 0.5f) * 2f; 
                    
                    if (riverValue < riverThreshold)
                    {
                        float riverBankLerp = riverValue / riverThreshold; // 0 di tengah, 1 di pinggir
                        float smoothBank = Mathf.SmoothStep(0f, 1f, riverBankLerp);
                        
                        // Menyesuaikan tinggi agar tidak membelah gunung menjadi tebing 90 derajat
                        float targetRiverBedHeight = baseH - riverDepth; 
                        
                        // Melembutkan impact carving jika terrainnya sangat tinggi (gunung) menggunakan Pow
                        float mountainBlend = Mathf.InverseLerp(0f, 0.4f, detailH); 
                        float widenRiverOnMountain = Mathf.Lerp(smoothBank, Mathf.Pow(smoothBank, 0.3f), mountainBlend);
                        
                        finalHeight = Mathf.Lerp(targetRiverBedHeight, finalHeight, widenRiverOnMountain);
                    }
                }

                // 7. Canyon / Ravines (Jurang Retakan Bumi)
                if (enableCanyons)
                {
                    float canyonValue = Mathf.Abs(canyonMap[warpX, warpY] - 0.5f) * 2f;
                    if (canyonValue < canyonThreshold)
                    {
                        float canyonLerp = canyonValue / canyonThreshold; // 0 di tengah jurang, 1 pinggir
                        float steepness = Mathf.Pow(canyonLerp, 0.2f); // V-shape sangat curam
                        
                        float targetRavineDepth = finalHeight - canyonDepth;
                        finalHeight = Mathf.Lerp(targetRavineDepth, finalHeight, steepness);
                    }
                }

                // 6. Lake / Pits (Danau)
                if (enableLakes)
                {
                    float lakeValue = lakeMap[warpX, warpY];
                    if (lakeValue < lakeThreshold)
                    {
                        float lakeBankLerp = lakeValue / lakeThreshold; 
                        float smoothBank = Mathf.SmoothStep(0f, 1f, lakeBankLerp);
                        
                        float carveAmount = Mathf.Lerp(lakeDepth, 0f, smoothBank);
                        finalHeight -= carveAmount * (1f - maskStrength); 
                    }
                }

                // Clamp agar tidak tembus map (Bawah laut 0)
                finalHeight = Mathf.Max(0f, finalHeight);

                // Apply falloff at the very end
                if (useFalloff)
                {
                    finalHeight = Mathf.Clamp01(finalHeight - falloffMap[x, y] * falloffIntensity);
                }

                heights[x, y] = finalHeight * (terrainHeight / terrainData.size.y);
            }
        }

        terrainData.SetHeights(0, 0, heights);
        ApplyTextures();
    }

    public void ApplyTextures()
    {
        if (biomes == null || biomes.Length == 0) return;
        if (terrainData.terrainLayers == null || terrainData.terrainLayers.Length == 0)
        {
            Debug.LogWarning("Please assign Terrain Layers to the Terrain component before applying textures.");
            return;
        }

        int alphaResolution = terrainData.alphamapResolution;
        float[,,] splatmapData = new float[alphaResolution, alphaResolution, terrainData.alphamapLayers];

        for (int y = 0; y < alphaResolution; y++)
        {
            for (int x = 0; x < alphaResolution; x++)
            {
                float normX = (float)x / alphaResolution;
                float normY = (float)y / alphaResolution;

                float height = terrainData.GetHeight(
                    Mathf.RoundToInt(normY * (terrainData.heightmapResolution - 1)), 
                    Mathf.RoundToInt(normX * (terrainData.heightmapResolution - 1))
                ) / terrainHeight;

                float steepness = terrainData.GetSteepness(normY, normX);

                float[] splatWeights = new float[terrainData.alphamapLayers];

                for (int i = 0; i < biomes.Length; i++)
                {
                    if (height >= biomes[i].startHeight && steepness >= biomes[i].minSlope && steepness <= biomes[i].maxSlope)
                    {
                        if (biomes[i].terrainLayerIndex < splatWeights.Length)
                        {
                            splatWeights[biomes[i].terrainLayerIndex] = 1f;
                        }
                    }
                }

                bool hasWeight = false;
                foreach (float w in splatWeights) if (w > 0) hasWeight = true;
                if (!hasWeight && splatWeights.Length > 0) splatWeights[0] = 1f;

                float sum = 0;
                for (int i = 0; i < splatWeights.Length; i++) sum += splatWeights[i];
                for (int i = 0; i < splatWeights.Length; i++)
                {
                    splatWeights[i] /= sum;
                    splatmapData[x, y, i] = splatWeights[i];
                }
            }
        }

        terrainData.SetAlphamaps(0, 0, splatmapData);
    }
}
