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

    /// <summary>Binds config, helpers, Boot's NetworkManager, and gameplay services</summary>
    public override void InstallBindings()
    {
        // settings and helpers
        Container.Bind<GameSettingsSO>().FromInstance(_settings).AsSingle();
        Container.Bind<GridCodec>().FromMethod(_ => new GridCodec(_settings.GridSize)).AsSingle();

        // resolve the NetworkManager that lives in Boot
        Container.Bind<NetworkManager>().FromMethod(_ =>
            Object.FindFirstObjectByType<NetworkManager>()).AsSingle();

        // gameplay services
        Container.Bind<PlayerRegistryService>().AsSingle();
        Container.Bind<BoardService>().AsSingle();
        Container.Bind<BoardValidator>().AsSingle();

        // network behaviours are in this scene
        Container.Bind<GameStateService>().FromComponentInHierarchy().AsSingle();
        Container.Bind<PlacementService>().FromComponentInHierarchy().AsSingle();
        Container.Bind<TurnService>().FromComponentInHierarchy().AsSingle();
    }
}
