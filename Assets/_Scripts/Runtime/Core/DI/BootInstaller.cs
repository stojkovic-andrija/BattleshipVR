using Zenject;
using UnityEngine;
using FishNet.Managing;
using BattleshipsVR.Config;
using BattleshipsVR.Net.Services;
using BattleshipsVR.Bootstrap;
using BattleshipsVR.Audio;

public sealed class BootInstaller : MonoInstaller
{

    [SerializeField] private NetworkManager _networkManager;
    [SerializeField] private NetworkEntry _networkEntry;
    [SerializeField] private AudioManager _audioManager;

    /// <summary>Networking core already present in scene, for global use only</summary>
    public override void InstallBindings()
    {
        _networkManager = FindFirstObjectByType<NetworkManager>();
        _networkEntry = FindFirstObjectByType<NetworkEntry>();
        _audioManager = FindFirstObjectByType<AudioManager>();

        Container.Bind<NetworkManager>().FromInstance(_networkManager).AsSingle();
        Container.Bind<NetworkEntry>().FromInstance(_networkEntry).AsSingle();
        Container.Bind<AudioManager>().FromInstance(_audioManager).AsSingle();
    }
}
