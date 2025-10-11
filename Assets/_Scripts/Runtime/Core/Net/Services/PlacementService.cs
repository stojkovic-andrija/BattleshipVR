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

        /// <summary>Client sends compact fleet intent, server reconstructs masks</summary>
        [ServerRpc(RequireOwnership = false)]
        public void ClientSubmitPlacementServerRpc(FleetPlacementData data, NetworkConnection caller)
        {
            if (!_validator.ServerTryBuildFleet(data, out var fleet))
                return;

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
                StartPlacementTimerAsync(_settings.PlacementSeconds).Forget();
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
            }
            catch (System.OperationCanceledException)
            {
                // timer cancelled by both ready
            }
        }

        [Server]
        private void TryCompletePlacement(bool force = false)
        {
            if (!_roster.HasBothPlayers && !force) return;

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
    }
}
