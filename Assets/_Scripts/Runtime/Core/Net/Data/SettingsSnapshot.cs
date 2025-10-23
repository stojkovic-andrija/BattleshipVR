namespace BattleshipsVR.Net.Data
{
    /// <summary>Tiny server snapshot for client UI parity without trusting it (Obsolete)</summary>
    public struct SettingsSnapshot
    {
        public byte gridSize;
        public BoatEntry[] boats;

        public struct BoatEntry
        {
            public byte typeId;
            public byte length;
        }
    }
}
