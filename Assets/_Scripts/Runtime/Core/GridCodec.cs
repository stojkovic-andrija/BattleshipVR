using UnityEngine;

namespace BattleshipsVR.Core
{
    /// <summary>1 byte per cell using 4+4 bits for x and y</summary>
    public sealed class GridCodec
    {
        public int MaxCells => _gridSize * _gridSize;
        public int GridSize => _gridSize;
        private readonly int _gridSize;

        public GridCodec(int gridSize)
        {
            _gridSize = Mathf.Clamp(gridSize, 1, 16);
        }

        public byte Pack(int x, int y)
        {
            x = Mathf.Clamp(x, 0, _gridSize - 1);
            y = Mathf.Clamp(y, 0, _gridSize - 1);
            return (byte)(((x & 0x0F) << 4) | (y & 0x0F));
        }

        public void Unpack(byte cell, out int x, out int y)
        {
            x = (cell >> 4) & 0x0F;
            y = cell & 0x0F;
        }

        public int ToIndex(byte cell)
        {
            Unpack(cell, out int x, out int y);
            return y * _gridSize + x;
        }
    }
}
