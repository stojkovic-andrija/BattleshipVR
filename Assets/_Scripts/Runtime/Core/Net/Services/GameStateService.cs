using FishNet.Object;
using FishNet.Connection;
using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;
using Zenject;
using BattleshipsVR.Config;
using FishNet;
using BattleshipsVR.Net.Data;
using FishNet.Managing.Scened;
using UnityEngine.SceneManagement;
using BattleshipsVR.Bootstrap;
using BattleshipsVR.Core;
using static BattleshipsVR.Bootstrap.NetConstants;
using GameKit.Dependencies.Utilities.Types;

namespace BattleshipsVR.Net.Services
{
    /// <summary>
    /// Authoritative game state controller with per-turn timer and synchronized scene reload.
    /// </summary>
    public sealed class GameStateService : NetworkBehaviour
    {
        public event System.Action<GameState> OnGameStateChanged;
        public event System.Action<NetworkConnection, int> OnTurnStarted;
        public event System.Action<GameState> OnClientGameStateChanged;
        public event System.Action<int> OnLocalTurnStarted;
        public event System.Action<bool, int> OnClientTurnBroadcast;

        public int LastDefeatedClientId => _lastDefeatedClientId;
        public NetworkConnection LastDefeatedConnection => _lastDefeatedConnection;


        [Inject] private GameSettingsSO _settings;
        [Inject] private PlayerRegistryService _roster;

        private GameState _state = GameState.BOOT;
        private NetworkConnection _currentShooter;
        private CancellationTokenSource _turnCts;
        private SceneId _gameScene = SceneId.GameMain;

        private int _lastDefeatedClientId = -1;
        private NetworkConnection _lastDefeatedConnection;

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

        /// <summary>Transition to PRE_PLACEMENT.</summary>
        [Server]
        public void ServerBeginPreplacement()
        {
            _state = GameState.PRE_PLACEMENT;
            OnGameStateChanged?.Invoke(_state);
            NotifyStateChangedObserversRpc(_state);
        }

        /// <summary>Transition to PLACEMENT.</summary>
        [Server]
        public void ServerBeginPlacement()
        {
            _state = GameState.PLACEMENT;
            OnGameStateChanged?.Invoke(_state);
            NotifyStateChangedObserversRpc(_state);
        }

        /// <summary>Transition to CONFIRM.</summary>
        [Server]
        public void ServerEnterConfirm()
        {
            _state = GameState.CONFIRM;
            OnGameStateChanged?.Invoke(_state);
            NotifyStateChangedObserversRpc(_state);
        }

        /// <summary>Transition to BATTLE and start first shooter's turn.</summary>
        [Server]
        public void ServerBeginBattle(NetworkConnection firstShooter)
        {
            _state = GameState.BATTLE;
            _currentShooter = firstShooter;

            OnGameStateChanged?.Invoke(_state);
            NotifyStateChangedObserversRpc(_state);
            ServerStartTurn(_currentShooter);
        }

        /// <summary>Checks if the given connection is the current shooter.</summary>
        [Server]
        public bool ServerIsCurrentShooter(NetworkConnection connection)
        {
            return connection == _currentShooter;
        }

        /// <summary>Advance turn to the specified next shooter.</summary>
        [Server]
        public void ServerAdvanceTurn(NetworkConnection nextShooter)
        {
            ServerCancelTurnTimer();
            _currentShooter = nextShooter;
            ServerStartTurn(_currentShooter);
        }

        /// <summary>Called by TurnService when a fleet is defeated; enters REVEAL and schedules END.</summary>
        [Server]
        public void ServerHandleBattleConcluded()
        {
            ServerCancelTurnTimer();

            _state = GameState.REVEAL;
            OnGameStateChanged?.Invoke(_state);
            NotifyStateChangedObserversRpc(_state);

            int revealSeconds = _settings.RevealSeconds > 0 ? _settings.RevealSeconds : 3;
            ServerAdvanceToEndAfterDelayAsync(revealSeconds).Forget();
        }

        /// <summary>Transition to END.</summary>
        [Server]
        public void ServerEnterEnd()
        {
            _state = GameState.END;
            OnGameStateChanged?.Invoke(_state);
            NotifyStateChangedObserversRpc(_state);
        }

        /// <summary>Registers the last defeated player for UI/summary screens.</summary>
        [Server]
        public void ServerRegisterDefeat(NetworkConnection defeated)
        {
            _lastDefeatedConnection = defeated;
            _lastDefeatedClientId = defeated != null ? defeated.ClientId : -1;
            NotifyDefeatObserversRpc(_lastDefeatedClientId);
        }

        /// <summary>Client helper for restart button in EndResultUI.</summary>
        [Client]
        public void ClientRequestRestart()
        {
            ClientRequestRestartServerRpc();
        }

        [ObserversRpc(BufferLast = true)]
        private void NotifyDefeatObserversRpc(int defeatedClientId)
        {
            _lastDefeatedClientId = defeatedClientId;
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
            NotifyTurnStartTargetRpc(shooter, seconds);

            int shooterId = shooter != null ? shooter.ClientId : -1;
            NotifyTurnStartedObserversRpc(shooterId, seconds);

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
                if (_state == GameState.BATTLE)
                    ServerAdvanceTurn(next);
            }
            catch (System.OperationCanceledException)
            {
                // Turn timer cancelled by a valid shot or match end.
            }
        }

        [Server]
        private async UniTaskVoid ServerAdvanceToEndAfterDelayAsync(int seconds)
        {
            await UniTask.Delay(seconds * 1000);
            if (_state == GameState.REVEAL)
                ServerEnterEnd();
        }

        /// <summary>Server reloads the current gameplay scene globally for all clients.</summary>
        [Server]
        private void ServerRestartMatch()
        {
            ServerCancelTurnTimer();

            string sceneName = ResolveSceneName(_gameScene);

            SceneLoadData sld = new SceneLoadData("EmptyScene");
            sld.ReplaceScenes = ReplaceOption.All;
            NetworkManager.SceneManager.LoadGlobalScenes(sld);

            sld = new SceneLoadData(sceneName)
            {
                ReplaceScenes = ReplaceOption.All
            };

            InstanceFinder.SceneManager.LoadGlobalScenes(sld);
        }

        /// <summary>Client asks the server to restart the match.</summary>
        [ServerRpc(RequireOwnership = false)]
        private void ClientRequestRestartServerRpc(NetworkConnection caller = null)
        {
            if (_state != GameState.END)
                return;

            AppLogger.Info($"[GameStateService] Restart requested by client {caller.ClientId}");
            ServerRestartMatch();
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

        [ObserversRpc(BufferLast = false)]
        private void NotifyTurnStartedObserversRpc(int shooterClientId, int seconds)
        {
            var myConn = InstanceFinder.ClientManager?.Connection;
            bool isLocal = myConn != null && myConn.ClientId == shooterClientId;
            OnClientTurnBroadcast?.Invoke(isLocal, seconds);
        }
    }
}
