using System;

namespace BattleshipsVR.Core
{
    [Serializable]
    public struct BitBoard256
    {
        public ulong x0;
        public ulong x1;
        public ulong x2;
        public ulong x3;

        public bool Get(int index)
        {
            if (index < 64) return (x0 & (1UL << index)) != 0;
            if (index < 128) return (x1 & (1UL << (index - 64))) != 0;
            if (index < 192) return (x2 & (1UL << (index - 128))) != 0;
            return (x3 & (1UL << (index - 192))) != 0;
        }

        public void Set(int index)
        {
            if (index < 64) x0 |= 1UL << index;
            else if (index < 128) x1 |= 1UL << (index - 64);
            else if (index < 192) x2 |= 1UL << (index - 128);
            else x3 |= 1UL << (index - 192);
        }

        public void Clear(int index)
        {
            if (index < 64) x0 &= ~(1UL << index);
            else if (index < 128) x1 &= ~(1UL << (index - 64));
            else if (index < 192) x2 &= ~(1UL << (index - 128));
            else x3 &= ~(1UL << (index - 192));
        }

        /// <summary>Bitwise OR with another 256-bit mask.</summary>
        public void Or(in BitBoard256 other)
        {
            x0 |= other.x0;
            x1 |= other.x1;
            x2 |= other.x2;
            x3 |= other.x3;
        }
    }
}
