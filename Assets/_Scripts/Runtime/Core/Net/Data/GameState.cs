namespace BattleshipsVR.Net.Data
{
    /// <summary>Authoritative match states driven by the server</summary>
    public enum GameState
    {
        BOOT = 0,
        LOBBY = 1,
        PLACEMENT = 2,
        CONFIRM = 3,
        BATTLE = 4,
        REVEAL = 5,
        END = 6
    }
}
