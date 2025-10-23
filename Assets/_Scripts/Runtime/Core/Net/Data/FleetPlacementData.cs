using System;

namespace BattleshipsVR.Net.Data
{
    /// <summary>Compact per ship placement intent from client to server</summary>
    [Serializable]
    public struct ShipPlacementData
    {
        public byte typeId;    
        public byte rootCell;  // packed nibble cell
        public bool vertical;  // true vertical, false horizontal
    }

    /// <summary>Full fleet placement for one player</summary>
    [Serializable]
    public struct FleetPlacementData
    {
        public ShipPlacementData[] ships;
    }
}
