using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Server;
using FishNet.Transporting;

namespace BattleshipsVR.Net.Services
{
    public sealed class PlayerRegistryService
    {
        public event System.Action<NetworkConnection, NetworkConnection> OnRosterReady;
        public event System.Action<NetworkConnection> OnPlayerDisconnected;

        public bool HasBothPlayers => _playerA != null && _playerB != null && _ready;

        private NetworkConnection _playerA;
        private NetworkConnection _playerB;
        private bool _ready;

        /// <summary>Iterates currently connected players (0–2).</summary>
        public IEnumerable<NetworkConnection> AllConnections
        {
            get
            {
                if (_playerA != null) yield return _playerA;
                if (_playerB != null) yield return _playerB;
            }
        }

        public void ServerHook(NetworkManager networkManager)
        {
            networkManager.ServerManager.OnRemoteConnectionState += HandleRemoteState;
        }

        public NetworkConnection GetOpponent(NetworkConnection c)
        {
            if (c == _playerA) return _playerB;
            if (c == _playerB) return _playerA;
            return null;
        }

        private void HandleRemoteState(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == RemoteConnectionState.Started)
            {
                if (_playerA == null) _playerA = conn;
                else if (_playerB == null) _playerB = conn;
                TryMarkReady();
            }
            else if (args.ConnectionState == RemoteConnectionState.Stopped)
            {
                if (conn == _playerA) _playerA = null;
                if (conn == _playerB) _playerB = null;
                _ready = false;
                OnPlayerDisconnected?.Invoke(conn);
            }
        }

        private void TryMarkReady()
        {
            if (_ready) return;
            if (_playerA != null && _playerB != null)
            {
                _ready = true;
                OnRosterReady?.Invoke(_playerA, _playerB);
            }
        }
    }
}
