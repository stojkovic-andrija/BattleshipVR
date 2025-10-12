// GridMath.cs
using UnityEngine;

/// <summary>Lightweight helpers for world↔grid conversions and clamping.</summary>
public static class GridMath
{
    /// <summary>Convert world to grid indices, clamped to [0, gridSize-1].</summary>
    public static void WorldToGridXZ(Vector3 world, Transform gridOrigin, int gridSize, float cellSize, out int gx, out int gy)
    {
        Vector3 local = world - gridOrigin.position;
        float x = local.x / cellSize;
        float y = local.z / cellSize;
        gx = Mathf.FloorToInt(x);
        gy = Mathf.FloorToInt(y);
        gx = Mathf.Clamp(gx, 0, gridSize - 1);
        gy = Mathf.Clamp(gy, 0, gridSize - 1);
    }

    /// <summary>Center of a grid cell in world space (.5f on X/Z), preserving Y.</summary>
    public static Vector3 GridToWorldCenter(int gx, int gy, Transform gridOrigin, float cellSize, float keepY)
    {
        float cx = (gx + 0.5f) * cellSize;
        float cz = (gy + 0.5f) * cellSize;
        return new Vector3(gridOrigin.position.x + cx, keepY, gridOrigin.position.z + cz);
    }

    /// <summary>Clamp a root so a segment of <paramref name="length"/> fits within bounds.</summary>
    public static void ClampRootForLength(ref int gx, ref int gy, int length, int gridSize, bool vertical)
    {
        if (vertical) gy = Mathf.Clamp(gy, 0, gridSize - length);
        else gx = Mathf.Clamp(gx, 0, gridSize - length);
    }
}
