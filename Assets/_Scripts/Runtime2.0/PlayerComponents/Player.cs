using BattleshipsVR.Net.Services;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Zenject;

public sealed class Player : NetworkBehaviour
{
    public readonly SyncVar<string> Username = new();

    [ServerRpc] private void SetUsername(string value) => Username.Value = value;

    public readonly SyncVar<bool> IsReady = new();
    private PlayerRegistryService _playerRegistryService;

    [Inject(Optional = true)]
    private void Construct(PlayerRegistryService prs)
    {
        // This will run if Zenject injects before spawn
        _playerRegistryService = prs;
    }

    [ServerRpc] private void SetIsReady(bool value) => IsReady.Value = value;

    public override void OnStartServer()
    {
        base.OnStartServer();
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (!IsOwner) return;
    }

    public void LateInject(PlayerRegistryService prs)
    {
        _playerRegistryService = prs;
    }
}
