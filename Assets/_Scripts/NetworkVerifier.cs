using FishNet;
using FishNet.Managing;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class NetworkVerifier : MonoBehaviour
{
    private NetworkManager _manager;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Awake()
    {
        _manager = InstanceFinder.NetworkManager;
    }

    // Update is called once per frame
    private void Start()
    {
        _manager.ServerManager.StartConnection();

        _manager.ClientManager.StartConnection("127.0.0.1");

        Debug.Log("Fishnet host started, Press P to test RPC");
    }

    private void Update()
    {
        if (Keyboard.current.pKey.wasPressedThisFrame)
        {
            Debug.Log($"Fishnet server active = {_manager.ServerManager.Started}, "
            + $"Client active = {_manager.ClientManager.Started}");
        }
    }
}
