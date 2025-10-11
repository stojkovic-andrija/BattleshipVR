using FishNet.Managing;
using UnityEngine;
using BattleshipsVR.Core;

namespace BattleshipsVR.Bootstrap
{
    /// <summary>Direct IP and port transport for local or LAN testing</summary>
    [DisallowMultipleComponent]
    public sealed class DirectTransportSetup : MonoBehaviour, INetworkTransportSetup
    {
        [SerializeField, Tooltip("Default client address when no session code is used")]
        private string _address = NetConstants.DEFAULT_LOCAL_IP;

        [SerializeField, Tooltip("Default transport port")]
        private ushort _port = NetConstants.DEFAULT_PORT;

        private NetworkManager TransportCheck(NetworkManager manager)
        {
            if (manager == null || manager.TransportManager == null || manager.TransportManager.Transport == null)
            {
                AppLogger.Warn("DirectTransportSetup found no transport");
                return null;
            }
            return manager;
        }

        /// <summary>Configures default address and port</summary>
        public void Configure(NetworkManager manager)
        {
            if (TransportCheck(manager) == null) return;
            manager.TransportManager.Transport.SetClientAddress(_address);
            manager.TransportManager.Transport.SetPort(_port);
            AppLogger.Info($"Direct transport configured to {_address}:{_port}");
        }

        /// <summary>Overrides client connect target from code or CLI</summary>
        public void OverrideForJoin(string address, ushort port, NetworkManager manager)
        {
            if (TransportCheck(manager) == null) return;
            manager.TransportManager.Transport.SetClientAddress(address);
            manager.TransportManager.Transport.SetPort(port);
            AppLogger.Info($"Transport overridden for join to {address}:{port}");
        }

        /// <summary>Overrides server bind port for host or dedicated</summary>
        public void OverrideForHostBind(ushort port, NetworkManager manager)
        {
            if (TransportCheck(manager) == null) return;
            manager.TransportManager.Transport.SetPort(port);
            AppLogger.Info($"Transport host bind port set to {port}");
        }
    }
}
