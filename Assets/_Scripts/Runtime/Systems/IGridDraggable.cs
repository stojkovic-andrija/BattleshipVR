// IGridDraggable.cs
using UnityEngine;

/// <summary>
/// Minimal interaction interface for grid-placement objects. Desktop mouse or XR
/// controllers can drive these uniformly.
/// </summary>
public interface IGridDraggable
{
    /// <summary>Begin dragging this object. Should free any prior occupancy.</summary>
    void BeginDrag();

    /// <summary>Preview placement at a grid root with orientation. Returns true if fits locally.</summary>
    bool PreviewAt(int rootX, int rootY, bool vertical, Vector3 snappedWorld);

    /// <summary>Commit a valid placement at the provided root/orientation.</summary>
    void CommitAt(int rootX, int rootY, bool vertical, Vector3 snappedWorld);

    /// <summary>Cancel placement and free any previous occupancy.</summary>
    void Unplace();

    /// <summary>Rotate by a 90° quarter turn. direction: +1 = CW, -1 = CCW.</summary>
    void RotateQuarter(int direction);

    /// <summary>True when currently placed on the grid.</summary>
    bool IsPlaced { get; }

    /// <summary>Boat type id used in network payload.</summary>
    byte TypeId { get; }

    /// <summary>Length in cells.</summary>
    int Length { get; }
}
