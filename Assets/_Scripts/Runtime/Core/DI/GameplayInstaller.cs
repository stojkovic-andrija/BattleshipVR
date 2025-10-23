using Zenject;
using UnityEngine;
using BattleshipsVR.Net.Services;
using BattleshipsVR.Core;
using BattleshipsVR.Config;
using BattleshipsVR.Interaction;
using BattleshipsVR.Visuals;

public sealed class GameplayInstaller : MonoInstaller
{
    /// <summary>
    /// Binds gameplay-scene-specific systems and components.
    /// </summary>

    [SerializeField] private GameSettingsSO _settings;
    [SerializeField] private GameStateService _gameStateService;
    [SerializeField] private PlayerRegistryService _playerRegistryService;
    [SerializeField] private PlacementService _placementService;
    [SerializeField] private TurnService _turnService;

    public override void InstallBindings()
    {
        Container.Bind<GameSettingsSO>()
            .FromInstance(_settings)
            .AsSingle();

        Container.Bind<GridCodec>()
            .FromMethod(_ => new GridCodec(_settings.GridSize))
            .AsSingle();

        Container.Bind<BoardService>()
            .AsSingle()
            .IfNotBound();

        Container.Bind<BoardValidator>()
            .AsSingle()
            .IfNotBound();

        Container.Bind<GameStateService>()
            .FromInstance(_gameStateService)
            .AsSingle()
            .IfNotBound();

        Container.Bind<PlayerRegistryService>()
            .FromInstance(_playerRegistryService)
            .AsSingle()
            .IfNotBound();

        Container.Bind<PlacementService>()
            .FromInstance(_placementService)
            .AsSingle()
            .IfNotBound();

        Container.Bind<TurnService>()
            .FromInstance(_turnService)
            .AsSingle()
            .IfNotBound();

        Container.Bind<ClientPlacementPlanner>()
            .FromComponentInHierarchy()
            .AsSingle()
            .IfNotBound();

        Container.Bind<DesktopGridInteractor>()
            .FromComponentInHierarchy()
            .AsSingle()
            .IfNotBound();

        Container.Bind<PlacementSubmitter>()
            .FromComponentInHierarchy()
            .AsSingle()
            .IfNotBound();

        Container.BindInterfacesTo<BoardGridMapper>()
            .FromComponentsInHierarchy()
            .AsCached();     // IGridRayResolver, IGridWorld

        Container.BindInterfacesTo<TargetVisualizer>()
            .FromComponentsInHierarchy()
            .AsCached();     // ITargetAimer

        Container.Bind<GridOverlayMarks>()
            .FromComponentsInHierarchy()
            .AsCached();
    }
}
