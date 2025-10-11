using FishNet.Managing;
using UnityEngine;
using Zenject;
using BattleshipsVR.Core;
using BattleshipsVR.Net.Services;

namespace BattleshipsVR.Bootstrap
{
    /// <summary>Wires server roster events to NetworkManager on scene load</summary>
    public sealed class ServerBootstrapper : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;

        [Inject] private PlayerRegistryService _playerRegistry;

        private void Awake()
        {
            if (_networkManager == null)
            {
                AppLogger.Warn("ServerBootstrapper has no NetworkManager assigned");
                return;
            }

            _playerRegistry.ServerHook(_networkManager);
            AppLogger.Info("ServerBootstrapper hooked PlayerRegistry to NetworkManager");
        }
    }
}
