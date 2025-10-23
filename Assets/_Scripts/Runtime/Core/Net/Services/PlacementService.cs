using System.Collections.Generic;
using FishNet.Object;
using FishNet.Connection;
using Cysharp.Threading.Tasks;
using System.Diagnostics;
using System.Threading;
using Zenject;
using BattleshipsVR.Config;
using BattleshipsVR.Net.Data;

namespace BattleshipsVR.Net.Services
{
    /// <summary>Collects placements, validates on server, and selects the first shooter.</summary>
    public sealed class PlacementService : NetworkBehaviour
    {
        /// <summary>Raised when a player's placement is committed on the server.</summary>
        public event System.Action<NetworkConnection> OnPlacementCommitted;
        /// <summary>Raised when the placement timer starts; payload is remaining seconds.</summary>
        public event System.Action<int> OnPlacementTimerStarted;
        /// <summary>Raised when the placement timer ends.</summary>
        public event System.Action OnPlacementTimerEnded;
        /// <summary>Client-side notification that the local player is locked.</summary>
        public event System.Action OnClientLocalLocked;

        [Inject] private GameSettingsSO _settings;
        [Inject] private BoardService _boardService;
        [Inject] private BoardValidator _validator;
        [Inject] private GameStateService _gameState;
        [Inject] private PlayerRegistryService _roster;

        private readonly Dictionary<NetworkConnection, bool> _hasCommitted = new();
        private readonly Dictionary<NetworkConnection, long> _commitTicks = new();
        private CancellationTokenSource _placementCts;

        private void OnEnable()
        {
            _gameState.OnGameStateChanged += HandleStateChanged;
        }

        private void OnDisable()
        {
            _gameState.OnGameStateChanged -= HandleStateChanged;
        }

        /// <summary>Client sends compact fleet intent; server reconstructs and validates a full fleet.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void ClientSubmitPlacementServerRpc(FleetPlacementData data, NetworkConnection caller = null)
        {
            if (!_validator.ServerTryBuildFleet(data, out var fleet))
                return;

            ServerCommitFleet(caller, ref fleet);
        }

        /// <summary>Client sends partial fleet; server fills any missing ships randomly.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void ClientSubmitPartialPlacementServerRpc(FleetPlacementData data, NetworkConnection caller = null)
        {
            if (!_validator.ServerTryBuildFleetFromPartial(data, out var fleet, out var missing))
                return;

            _validator.ServerFillRandomShips(ref fleet, missing);
            ServerCommitFleet(caller, ref fleet);
        }

        /// <summary>Unified ready signal. If allowFillMissing is true, server completes missing ships randomly.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void ClientReadyPlacementServerRpc(FleetPlacementData data, bool allowFillMissing, NetworkConnection caller = null)
        {
            if (caller == null) return;

            if (allowFillMissing)
            {
                if (!_validator.ServerTryBuildFleetFromPartial(data, out var fleet, out var missing))
                    return;

                _validator.ServerFillRandomShips(ref fleet, missing);
                ServerCommitFleet(caller, ref fleet);
            }
            else
            {
                if (!_validator.ServerTryBuildFleet(data, out var fleet))
                    return;

                ServerCommitFleet(caller, ref fleet);
            }

            NotifyLocalLockedTargetRpc(caller);
        }

        [TargetRpc]
        private void NotifyLocalLockedTargetRpc(NetworkConnection target)
        {
            OnClientLocalLocked?.Invoke();
        }

        [Server]
        private void ServerCommitFleet(NetworkConnection caller, ref BoardService.FleetState fleet)
        {
            _boardService.ServerSetFleet(caller, fleet);

            if (!_hasCommitted.ContainsKey(caller))
            {
                _hasCommitted[caller] = true;
                _commitTicks[caller] = Stopwatch.GetTimestamp();
                OnPlacementCommitted?.Invoke(caller);
            }

            TryCompletePlacement();
        }

        [Server]
        private void HandleStateChanged(GameState state)
        {
            if (state == GameState.PLACEMENT)
            {
                NotifyPlacementTimerStartedObserversRpc(_settings.PlacementSeconds);
                StartPlacementTimerAsync(_settings.PlacementSeconds).Forget();
            }
        }

        [Server]
        private async UniTaskVoid StartPlacementTimerAsync(int seconds)
        {
            _placementCts?.Cancel();
            _placementCts?.Dispose();
            _placementCts = null;

            if (seconds <= 0) { TryCompletePlacement(force: true); return; }

            _placementCts = new CancellationTokenSource();
            try
            {
                await UniTask.Delay(seconds * 1000, cancellationToken: _placementCts.Token);
                TryCompletePlacement(force: true);
                NotifyPlacementTimerEndedObserversRpc();
            }
            catch (System.OperationCanceledException)
            {
                NotifyPlacementTimerEndedObserversRpc();
            }
        }

        [Server]
        private void TryCompletePlacement(bool force = false)
        {
            if (!_roster.HasBothPlayers)
                return;

            if (force)
            {
                foreach (var conn in _roster.AllConnections)
                {
                    if (conn == null) continue;
                    if (_hasCommitted.TryGetValue(conn, out bool committed) && committed)
                        continue;

                    var empty = new BoardService.FleetState();
                    var missing = new List<byte>();
                    foreach (var bt in _settings.BoatTypes)
                        if (bt != null)
                            missing.Add(bt.TypeId);

                    _validator.ServerFillRandomShips(ref empty, missing);
                    ServerCommitFleet(conn, ref empty);
                }
            }

            int committedCount = 0;
            foreach (var conn in _roster.AllConnections)
                if (conn != null && _hasCommitted.TryGetValue(conn, out bool c) && c)
                    committedCount++;

            if (committedCount < 2)
                return;

            NetworkConnection first = null;
            long bestTick = long.MaxValue;
            foreach (var conn in _roster.AllConnections)
            {
                if (conn == null) continue;
                if (_hasCommitted.TryGetValue(conn, out bool c) && c)
                {
                    long t = _commitTicks.TryGetValue(conn, out var tick) ? tick : long.MaxValue;
                    if (t < bestTick) { bestTick = t; first = conn; }
                }
            }

            _placementCts?.Cancel();
            _placementCts?.Dispose();
            _placementCts = null;

            _gameState.ServerBeginBattle(first);
        }

        [ObserversRpc(BufferLast = false)]
        private void NotifyPlacementTimerStartedObserversRpc(int seconds)
        {
            OnPlacementTimerStarted?.Invoke(seconds);
        }

        [ObserversRpc(BufferLast = false)]
        private void NotifyPlacementTimerEndedObserversRpc()
        {
            OnPlacementTimerEnded?.Invoke();
        }
    }
}
