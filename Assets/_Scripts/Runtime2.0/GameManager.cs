using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

public sealed class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    public SyncList<Player> Players { get; } = new SyncList<Player>();

    public readonly SyncVar<bool> CanStart;
    [ServerRpc] private void SetCanStart(bool value) => CanStart.Value = value;

    public readonly SyncVar<bool> DidStart;
    [ServerRpc] private void SetDidStart(bool value) => DidStart.Value = value;

    private void Awake()
    {
        if (!Instance) Instance = this;
        else Destroy(gameObject);
    }

    private void Update()
    {
        if (!IsServerStarted) return;

        SetCanStart(Players.Count > 1);

        Debug.Log($"There are {Players.Count} players in the game.");
    }

    [Server]
    public void StartGame()
    {
        SetDidStart(true);
    }

    [Server]
    public void StopGame()
    {
        SetDidStart(false);
    }
}
