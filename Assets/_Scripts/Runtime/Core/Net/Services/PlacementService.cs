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
    /// <summary>Collects placements, validates on server, picks who shoots first</summary>
    public sealed class PlacementService : NetworkBehaviour
    {
        public event System.Action<NetworkConnection> OnPlacementCommitted;
        public event System.Action<int> OnPlacementTimerStarted; // seconds
        public event System.Action OnPlacementTimerEnded;

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

        /// <summary>Client sends compact fleet intent, server reconstructs masks (full set).</summary>
        [ServerRpc(RequireOwnership = false)]
        public void ClientSubmitPlacementServerRpc(FleetPlacementData data, NetworkConnection caller = null)
        {
            if (!_validator.ServerTryBuildFleet(data, out var fleet))
                return;

            ServerCommitFleet(caller, ref fleet);
        }

        /// <summary>Client sends partial; server fills the rest randomly.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void ClientSubmitPartialPlacementServerRpc(FleetPlacementData data, NetworkConnection caller = null)
        {
            if (!_validator.ServerTryBuildFleetFromPartial(data, out var fleet, out var missing))
                return;

            _validator.ServerFillRandomShips(ref fleet, missing);
            ServerCommitFleet(caller, ref fleet);
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
                // timer cancelled by both ready
                NotifyPlacementTimerEndedObserversRpc();
            }
        }

        [Server]
        private void TryCompletePlacement(bool force = false)
        {
            if (!_roster.HasBothPlayers && !force) return;

            // For any uncommitted players, create a full random fleet so the game can start
            foreach (var conn in _roster.AllConnections)
            {
                if (conn == null) continue;
                if (_hasCommitted.TryGetValue(conn, out bool committed) && committed) continue;

                // Build empty fleet state and fill all boats randomly.
                var empty = new BoardService.FleetState();
                var missing = new List<byte>();
                foreach (var bt in _settings.BoatTypes)
                    if (bt != null) missing.Add(bt.TypeId);

                _validator.ServerFillRandomShips(ref empty, missing);
                ServerCommitFleet(conn, ref empty);
            }

            // pick order by commit timestamp when both are ready
            List<(NetworkConnection conn, long tick)> ready = new();
            foreach (var kvp in _hasCommitted)
                if (kvp.Value) ready.Add((kvp.Key, _commitTicks.TryGetValue(kvp.Key, out var t) ? t : long.MaxValue));

            if (ready.Count < 2 && !force) return;

            ready.Sort((a, b) => a.tick.CompareTo(b.tick));
            NetworkConnection first = ready.Count > 0 ? ready[0].conn : null;

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
