using Zenject;
using UnityEngine;
using FishNet.Managing;
using BattleshipsVR.Config;
using BattleshipsVR.Core;
using BattleshipsVR.Net.Services;

public sealed class GameplayInstaller : MonoInstaller
{
    [SerializeField, Tooltip("Authoritative game settings asset")]
    private GameSettingsSO _settings;

    /// <summary>Binds config, helpers, Boot's NetworkManager, and gameplay services.</summary>
    public override void InstallBindings()
    {
        // Config and helpers
        Container.Bind<GameSettingsSO>().FromInstance(_settings).AsSingle().IfNotBound();
        Container.Bind<GridCodec>().FromMethod(_ => new GridCodec(_settings.GridSize)).AsSingle().IfNotBound();

        // Resolve the NetworkManager living in Boot (already loaded)
        Container.Bind<NetworkManager>().FromMethod(_ =>
            Object.FindFirstObjectByType<NetworkManager>()).AsSingle().IfNotBound();

        // Core gameplay services (pure classes)
        Container.Bind<PlayerRegistryService>().AsSingle().IfNotBound();
        Container.Bind<BoardService>().AsSingle().IfNotBound();
        Container.Bind<BoardValidator>().AsSingle().IfNotBound();

        // Scene network behaviours (must exist in this scene hierarchy)
        Container.Bind<GameStateService>().FromComponentInHierarchy().AsSingle().IfNotBound();
        Container.Bind<PlacementService>().FromComponentInHierarchy().AsSingle().IfNotBound();
        Container.Bind<TurnService>().FromComponentInHierarchy().AsSingle().IfNotBound();

        // Client-side placement components that have [Inject] fields
        Container.Bind<ClientPlacementPlanner>().FromComponentInHierarchy().AsSingle().IfNotBound();
        Container.Bind<DesktopGridInteractor>().FromComponentInHierarchy().AsSingle().IfNotBound();
        Container.Bind<PlacementSubmitter>().FromComponentInHierarchy().AsSingle().IfNotBound();
    }
}
