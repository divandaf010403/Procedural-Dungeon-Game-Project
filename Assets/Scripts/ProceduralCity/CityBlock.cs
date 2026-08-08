using UnityEngine;

/// <summary>
/// Tipe blok kota — menentukan fungsi area.
/// </summary>
public enum BlockType
{
    Residential,
    Commercial,
    Industrial,
    Park
}

/// <summary>
/// Representasi satu blok kota: area persegi yang dibatasi 4 jalan.
/// Dibuat oleh RoadNetwork.GenerateRoads() / RebuildBlocks().
/// </summary>
[System.Serializable]
public class CityBlock
{
    public Vector3   center;           // Posisi tengah blok (world space)
    public Vector2   size;             // Ukuran blok (X dan Z)
    public BlockType blockType = BlockType.Residential;
    public int       buildingCount = 0;

    public bool CanFitBuilding(float minSize) =>
        size.x >= minSize && size.y >= minSize;

    public Vector3 GetRandomBuildingPosition(float margin)
    {
        float halfX = (size.x - margin * 2) * 0.5f;
        float halfZ = (size.y - margin * 2) * 0.5f;
        return new Vector3(
            center.x + Random.Range(-halfX, halfX),
            center.y,
            center.z + Random.Range(-halfZ, halfZ));
    }
}
