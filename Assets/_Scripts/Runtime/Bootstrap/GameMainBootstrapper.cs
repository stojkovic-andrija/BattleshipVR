using UnityEngine;
using Zenject;
using FishNet.Managing;
using BattleshipsVR.Core;
using BattleshipsVR.Net.Services;

/// <summary>Hooks gameplay services to the NetworkManager after Game_Main loads</summary>
public sealed class GameMainBootstrapper : MonoBehaviour
{
    [Inject] private PlayerRegistryService _playerRegistry;
    [Inject] private NetworkManager _networkManager;

    private void Awake()
    {
        if (_networkManager == null)
        {
            AppLogger.Warn("GameMainBootstrapper could not find NetworkManager");
            return;
        }

        _playerRegistry.ServerHook(_networkManager);
        AppLogger.Info("GameMainBootstrapper hooked PlayerRegistry to NetworkManager");
    }
}
