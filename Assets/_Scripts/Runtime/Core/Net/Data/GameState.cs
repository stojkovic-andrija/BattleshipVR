namespace BattleshipsVR.Net.Data
{
    /// <summary>Authoritative match states driven by the server</summary>
    public enum GameState
    {
        BOOT = 0,
        LOBBY = 1,
        PRE_PLACEMENT = 2,
        PLACEMENT = 3,
        CONFIRM = 4,
        BATTLE = 5,
        REVEAL = 6,
        END = 7
    }
}
