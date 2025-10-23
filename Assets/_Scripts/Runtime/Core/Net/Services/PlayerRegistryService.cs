using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using BattleshipsVR.Core;

namespace BattleshipsVR.Net.Services
{
    /// <summary>
    /// Tracks two players, readiness, and assigns per-client Side A/B.
    /// Clients request their side after spawn; sides are also broadcast (buffered) as redundancy.
    /// </summary>
    public sealed class PlayerRegistryService : NetworkBehaviour
    {
        /// <summary>Fired when both players are known and marked ready.</summary>
        public event System.Action<NetworkConnection, NetworkConnection> OnRosterReady;
        /// <summary>Fired when a player disconnects; roster is reset.</summary>
        public event System.Action<NetworkConnection> OnPlayerDisconnected;
        /// <summary>Client-side: fired when the local client's side (A/B) is resolved.</summary>
        public event System.Action<bool> OnClientLocalSideResolved;

        /// <summary>True if both players are present and have called ready.</summary>
        public bool HasBothPlayers => _playerA != null && _playerB != null && _playerAReady && _playerBReady;

        /// <summary>Local cached side flag (true = A, false = B, null = unresolved).</summary>
        public bool? LocalIsSideA { get; private set; }

        /// <summary>Enumerates currently attached player connections (A then B if present).</summary>
        public IEnumerable<NetworkConnection> AllConnections
        {
            get
            {
                if (_playerA != null) yield return _playerA;
                if (_playerB != null) yield return _playerB;
            }
        }

        private NetworkConnection _playerA;
        private NetworkConnection _playerB;
        private bool _playerAReady;
        private bool _playerBReady;
        private bool _rosterFired;
        private bool _sidesAnnounced;

        // Clients that asked for their side before both connections were known.
        private readonly HashSet<int> _awaitingSideRequestClientIds = new HashSet<int>();

        public override void OnStartServer()
        {
            base.OnStartServer();

            AttachExistingConnections();

            NetworkManager nm = InstanceFinder.NetworkManager;
            if (nm != null)
                ServerHook(nm);

            AnnounceSidesIfReadyAsync().Forget();
            Debug.Log("[PRS] Server started.");
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            LateInjectPlayers();
            RequestMySideServerRpc();
        }

        /// <summary>Server: subscribe to connection state changes.</summary>
        public void ServerHook(NetworkManager networkManager)
        {
            networkManager.ServerManager.OnRemoteConnectionState += HandleRemoteState;
        }

        /// <summary>Server: marks a connection (or host) as ready and tries to fire roster ready.</summary>
        [Server]
        public void ServerMarkReady(NetworkConnection conn)
        {
            if (conn == null)
            {
                if (IsServerStarted) _playerAReady = true;
            }
            else if (conn == _playerA) _playerAReady = true;
            else if (conn == _playerB) _playerBReady = true;

            TryFireRosterReady();
        }

        /// <summary>Client API: signals ready to the server.</summary>
        public void ClientRequestReady()
        {
            if (!IsClientInitialized || !IsSpawned)
            {
                Debug.LogWarning("[PRS] ClientRequestReady before init/spawn.");
                return;
            }
            ClientSendReadyServerRpc();
        }

        /// <summary>Returns the opponent of the given connection or null if not found.</summary>
        public NetworkConnection GetOpponent(NetworkConnection c)
        {
            if (c == _playerA) return _playerB;
            if (c == _playerB) return _playerA;
            return null;
        }

        [ServerRpc(RequireOwnership = false)]
        private void ClientSendReadyServerRpc(NetworkConnection caller = null)
        {
            ServerMarkReady(caller);
        }

        private void LateInjectPlayers()
        {
            var players = FindObjectsByType<Player>(FindObjectsSortMode.InstanceID).ToList();
            foreach (var p in players)
                p.LateInject(this);
        }

        [Server]
        private void AttachExistingConnections()
        {
            NetworkManager nm = InstanceFinder.NetworkManager;
            if (nm == null) return;

            IReadOnlyDictionary<int, NetworkConnection> clients = nm.ServerManager.Clients;
            foreach (var conn in clients.Values)
            {
                if (_playerA == null) _playerA = conn;
                else if (_playerB == null) _playerB = conn;
            }
        }

        private void HandleRemoteState(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == RemoteConnectionState.Started)
            {
                if (_playerA == null) _playerA = conn;
                else if (_playerB == null) _playerB = conn;

                AnnounceSidesIfReadyAsync().Forget();
            }
            else if (args.ConnectionState == RemoteConnectionState.Stopped)
            {
                if (conn == _playerA) _playerA = null;
                if (conn == _playerB) _playerB = null;

                _playerAReady = false;
                _playerBReady = false;
                _rosterFired = false;
                _sidesAnnounced = false;
                _awaitingSideRequestClientIds.Clear();

                OnPlayerDisconnected?.Invoke(conn);
            }
        }

        private void TryFireRosterReady()
        {
            if (_rosterFired) return;
            if (_playerAReady && _playerBReady)
            {
                _rosterFired = true;
                OnRosterReady?.Invoke(_playerA, _playerB);
            }
        }

        [Server]
        private async UniTaskVoid AnnounceSidesIfReadyAsync()
        {
            if (_playerA == null || _playerB == null) return;

            await UniTask.NextFrame();

            if (!_sidesAnnounced)
            {
                NotifySidesObserversRpc(_playerA.ClientId, _playerB.ClientId);
                _sidesAnnounced = true;
            }

            if (_awaitingSideRequestClientIds.Count > 0)
            {
                foreach (var id in _awaitingSideRequestClientIds.ToArray())
                {
                    NetworkConnection c = InstanceFinder.ServerManager.Clients.TryGetValue(id, out var nc) ? nc : null;
                    if (c == null) continue;
                    bool isA = c == _playerA;
                    bool isB = c == _playerB;
                    if (isA || isB)
                        SetLocalSideTargetRpc(c, isA);
                }
                _awaitingSideRequestClientIds.Clear();
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestMySideServerRpc(NetworkConnection caller = null)
        {
            if (caller == null) return;

            if (_playerA != null && _playerB != null)
            {
                bool isA = caller == _playerA;
                bool isB = caller == _playerB;
                if (isA || isB)
                {
                    SetLocalSideTargetRpc(caller, isA);
                    return;
                }
            }

            _awaitingSideRequestClientIds.Add(caller.ClientId);
        }

        [ObserversRpc(BufferLast = true)]
        private void NotifySidesObserversRpc(int playerAId, int playerBId)
        {
            if (!IsClientInitialized) return;

            var myConn = InstanceFinder.ClientManager?.Connection;
            if (myConn == null) return;

            bool isA = myConn.ClientId == playerAId;
            bool isB = myConn.ClientId == playerBId;

            if (!isA && !isB) return;

            LocalIsSideA = isA;
            OnClientLocalSideResolved?.Invoke(isA);
        }

        [TargetRpc]
        private void SetLocalSideTargetRpc(NetworkConnection target, bool isSideA)
        {
            LocalIsSideA = isSideA;
            OnClientLocalSideResolved?.Invoke(isSideA);
            Debug.Log(isSideA ? "[PRS] Local side = A (TargetRpc)" : "[PRS] Local side = B (TargetRpc)");
        }
    }
}
