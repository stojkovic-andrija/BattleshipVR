using FishNet.Object;
using FishNet.Connection;
using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;
using Zenject;
using BattleshipsVR.Config;
using BattleshipsVR.Net.Data;

namespace BattleshipsVR.Net.Services
{
    /// <summary>Authoritative state machine and per turn timer without tick spam</summary>
    public sealed class GameStateService : NetworkBehaviour
    {
        public event System.Action<GameState> OnGameStateChanged;           // server side
        public event System.Action<NetworkConnection, int> OnTurnStarted;   // server side
        public event System.Action<GameState> OnClientGameStateChanged;     // client side
        public event System.Action<int> OnLocalTurnStarted;                 // client side


        [Inject] private GameSettingsSO _settings;
        [Inject] private PlayerRegistryService _roster;

        private GameState _state = GameState.BOOT;
        private NetworkConnection _currentShooter;
        private CancellationTokenSource _turnCts;

        public override void OnStartServer()
        {
            _roster.OnRosterReady += HandleRosterReady;
            _state = GameState.LOBBY;
            OnGameStateChanged?.Invoke(_state);
            NotifyStateChangedObserversRpc(_state);
        }

        public override void OnStopServer()
        {
            _roster.OnRosterReady -= HandleRosterReady;
            ServerCancelTurnTimer();
        }

        /// <summary>Enters placement phase</summary>
        [Server]
        public void ServerBeginPlacement()
        {
            _state = GameState.PLACEMENT;
            OnGameStateChanged?.Invoke(_state);
            NotifyStateChangedObserversRpc(_state);
        }

        /// <summary>Optional confirm step if you want a short lock in</summary>
        [Server]
        public void ServerEnterConfirm()
        {
            _state = GameState.CONFIRM;
            OnGameStateChanged?.Invoke(_state);
            NotifyStateChangedObserversRpc(_state);
        }

        /// <summary>Starts battle and first turn</summary>
        [Server]
        public void ServerBeginBattle(NetworkConnection firstShooter)
        {
            _state = GameState.BATTLE;
            _currentShooter = firstShooter;
            OnGameStateChanged?.Invoke(_state);
            NotifyStateChangedObserversRpc(_state);
            ServerStartTurn(_currentShooter);
        }

        /// <summary>Returns true if caller is allowed to shoot now</summary>
        [Server]
        public bool ServerIsCurrentShooter(NetworkConnection c)
        {
            return c == _currentShooter;
        }

        /// <summary>Advances to next shooter and restarts timer</summary>
        [Server]
        public void ServerAdvanceTurn(NetworkConnection nextShooter)
        {
            ServerCancelTurnTimer();
            _currentShooter = nextShooter;
            ServerStartTurn(_currentShooter);
        }

        /// <summary>Enters reveal phase</summary>
        [Server]
        public void ServerEnterReveal()
        {
            _state = GameState.REVEAL;
            OnGameStateChanged?.Invoke(_state);
            NotifyStateChangedObserversRpc(_state);
        }

        /// <summary>Final end state after reveal</summary>
        [Server]
        public void ServerEnterEnd()
        {
            _state = GameState.END;
            OnGameStateChanged?.Invoke(_state);
            NotifyStateChangedObserversRpc(_state);
        }

        [Server]
        private void HandleRosterReady(NetworkConnection a, NetworkConnection b)
        {
            ServerBeginPlacement();
        }

        [Server]
        private void ServerStartTurn(NetworkConnection shooter)
        {
            int seconds = Mathf.Max(5, _settings.TurnSeconds);
            OnTurnStarted?.Invoke(shooter, seconds);
            NotifyTurnStartTargetRpc(shooter, seconds); // only shooter gets this
            _turnCts = new CancellationTokenSource();
            ServerTurnTimeoutAsync(shooter, seconds, _turnCts.Token).Forget();
        }

        [Server]
        private void ServerCancelTurnTimer()
        {
            _turnCts?.Cancel();
            _turnCts?.Dispose();
            _turnCts = null;
        }

        [Server]
        private async UniTaskVoid ServerTurnTimeoutAsync(NetworkConnection shooter, int seconds, CancellationToken ct)
        {
            try
            {
                await UniTask.Delay(seconds * 1000, cancellationToken: ct);
                NetworkConnection next = _roster.GetOpponent(shooter);
                ServerAdvanceTurn(next);
            }
            catch (System.OperationCanceledException)
            {
                // timer was canceled by a valid shot
            }
        }

        [ObserversRpc(BufferLast = true)]
        private void NotifyStateChangedObserversRpc(GameState state)
        {
            OnClientGameStateChanged?.Invoke(state);
        }

        [TargetRpc]
        private void NotifyTurnStartTargetRpc(NetworkConnection target, int seconds)
        {
            OnLocalTurnStarted?.Invoke(seconds);
        }
    }
}
