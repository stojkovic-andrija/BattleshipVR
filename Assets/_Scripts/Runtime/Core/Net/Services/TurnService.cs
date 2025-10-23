using FishNet.Object;
using FishNet.Connection;
using Zenject;
using BattleshipsVR.Config;
using BattleshipsVR.Core;
using UnityEngine;

namespace BattleshipsVR.Net.Services
{
    /// <summary>Server-authoritative turn resolver: validates shots, announces results, and drives turn advances.</summary>
    public sealed class TurnService : NetworkBehaviour
    {
        public event System.Action<byte, bool, bool, byte> OnShotResolved;
        public event System.Action<byte, bool, bool, byte, bool> OnClientShotResult;
        public event System.Action<BitBoard256, BitBoard256> OnClientRevealDual;

        [Tooltip("DEBUG: typeId used by the context menu action to sink a single ship.")]
        [SerializeField] private byte _debugTypeId = 0;

        [Inject] private GameSettingsSO _settings;
        [Inject] private GridCodec _codec;
        [Inject] private BoardService _boardService;
        [Inject] private GameStateService _gameStateService;
        [Inject] private PlayerRegistryService _roster;

        private readonly System.Collections.Generic.Dictionary<NetworkConnection, BitBoard256> _triedByPlayer = new();
        private readonly System.Collections.Generic.Dictionary<NetworkConnection, int> _turnShotCount = new();
        private readonly System.Collections.Generic.Dictionary<NetworkConnection, float> _lastShotTime = new();
        private const float MIN_SECONDS_BETWEEN_SHOTS = 0.05f;

        /// <summary>Client requests a shot at packedCell (server validates turn, bounds, duplicates, and resolves).</summary>
        [ServerRpc(RequireOwnership = false)]
        public void ClientRequestShotServerRpc(byte packedCell, NetworkConnection caller = null)
        {
            if (!_gameStateService.ServerIsCurrentShooter(caller))
                return;

            // Basic anti-spam: throttle by time
            float now = Time.unscaledTime;
            if (_lastShotTime.TryGetValue(caller, out float last) && now - last < MIN_SECONDS_BETWEEN_SHOTS)
                return;
            _lastShotTime[caller] = now;

            // One shot per turn
            if (_turnShotCount.TryGetValue(caller, out int count) && count >= 1)
                return;

            int shooterIdx = _codec.ToIndex(packedCell);
            if (shooterIdx < 0 || shooterIdx >= _codec.MaxCells)
                return;

            // Prevent duplicate tries within the match
            if (!_triedByPlayer.TryGetValue(caller, out var tried)) tried = default;
            if (tried.Get(shooterIdx)) return;
            tried.Set(shooterIdx);
            _triedByPlayer[caller] = tried;
            _turnShotCount[caller] = 1;

            NetworkConnection defender = _roster.GetOpponent(caller);
            if (defender == null)
                return;

            // Transform attacker grid index into defender grid index by mirroring across board center
            int gx = shooterIdx % _codec.GridSize;
            int gy = shooterIdx / _codec.GridSize;
            int fx = (_codec.GridSize - 1) - gx;
            int fy = (_codec.GridSize - 1) - gy;
            int defenderIdx = fy * _codec.GridSize + fx;

            if (_boardService.ServerTryResolveShot(caller, defender, defenderIdx, out bool isHit, out bool isSunk, out byte sunkTypeId,
                out BoardService.ShipOrientation sunkOri, out Vector3 sunkCenter))
            {
                // Per-client, minimal payload result
                NotifyShotResultToShooterTargetRpc(caller, packedCell, isHit, isSunk, sunkTypeId);
                NotifyShotResultToDefenderTargetRpc(defender, packedCell, isHit, isSunk, sunkTypeId);

                OnShotResolved?.Invoke(packedCell, isHit, isSunk, sunkTypeId);

                if (isSunk)
                    NotifyShipSunkObserversRpc(defender.ClientId, sunkTypeId, (byte)sunkOri, sunkCenter.x, sunkCenter.z);

                // End condition: reveal both fleets, then transition to REVEAL/END
                if (_boardService.ServerIsFleetDefeated(defender))
                {
                    _gameStateService.ServerRegisterDefeat(defender);

                    var attackerReveal = _boardService.ServerGetReveal(caller);
                    var defenderReveal = _boardService.ServerGetReveal(defender);

                    SendRevealDualTargetRpc(caller, attackerReveal, defenderReveal);
                    SendRevealDualTargetRpc(defender, attackerReveal, defenderReveal);

                    _gameStateService.ServerHandleBattleConcluded();
                    return;
                }

                // Advance turn to opponent after a valid resolution
                _turnShotCount[caller] = 0;
                _gameStateService.ServerAdvanceTurn(defender);
            }
        }

        [ObserversRpc(BufferLast = false)]
        private void NotifyShipSunkObserversRpc(int defenderClientId, byte typeId, byte orientation, float cx, float cz)
        {
            _boardService.ClientRaiseShipSunk(defenderClientId, typeId, (BoardService.ShipOrientation)orientation, new Vector3(cx, 0f, cz));
        }

        /// <summary>Sink opponent ship by typeId (trigger from client; server computes defender).</summary>
        [ServerRpc(RequireOwnership = false)]
        private void ClientForceSinkShipServerRpc(byte typeId, NetworkConnection caller = null)
        {
            NetworkConnection defender = _roster.GetOpponent(caller);
            if (defender == null)
                return;

            _boardService.ServerForceSinkShip(defender, typeId);

            if (_boardService.ServerIsFleetDefeated(defender))
            {
                _gameStateService.ServerRegisterDefeat(defender);

                var attackerReveal = _boardService.ServerGetReveal(caller);
                var defenderReveal = _boardService.ServerGetReveal(defender);

                SendRevealDualTargetRpc(caller, attackerReveal, defenderReveal);
                SendRevealDualTargetRpc(defender, attackerReveal, defenderReveal);

                _gameStateService.ServerHandleBattleConcluded();
            }
        }

        /// <summary>Sink all opponent ships (instant win).</summary>
        [ServerRpc(RequireOwnership = false)]
        private void ClientForceSinkAllServerRpc(NetworkConnection caller = null)
        {
            NetworkConnection defender = _roster.GetOpponent(caller);
            if (defender == null)
                return;

            _boardService.ServerForceSinkAll(defender);

            _gameStateService.ServerRegisterDefeat(defender);

            var attackerReveal = _boardService.ServerGetReveal(caller);
            var defenderReveal = _boardService.ServerGetReveal(defender);

            SendRevealDualTargetRpc(caller, attackerReveal, defenderReveal);
            SendRevealDualTargetRpc(defender, attackerReveal, defenderReveal);

            _gameStateService.ServerHandleBattleConcluded();
        }


        [ContextMenu("DEBUG/Sink Opponent: Ship By _debugTypeId")]
        private void DebugSinkOpponentShipByTypeId()
        {
            // In Host mode this routes through the server via RPC
            ClientForceSinkShipServerRpc(_debugTypeId);
        }

        [ContextMenu("DEBUG/Sink Opponent: ALL Ships (Instant Win)")]
        private void DebugSinkOpponentAll()
        {
            ClientForceSinkAllServerRpc();
        }

        [TargetRpc]
        private void NotifyShotResultToShooterTargetRpc(NetworkConnection target, byte packedCell, bool isHit, bool isSunk, byte sunkTypeId)
        {
            OnClientShotResult?.Invoke(packedCell, isHit, isSunk, sunkTypeId, true);
        }

        [TargetRpc]
        private void NotifyShotResultToDefenderTargetRpc(NetworkConnection target, byte packedCell, bool isHit, bool isSunk, byte sunkTypeId)
        {
            OnClientShotResult?.Invoke(packedCell, isHit, isSunk, sunkTypeId, false);
        }

        [TargetRpc]
        private void SendRevealDualTargetRpc(NetworkConnection target, BitBoard256 attackerFleet, BitBoard256 defenderFleet)
        {
            OnClientRevealDual?.Invoke(attackerFleet, defenderFleet);
        }
    }
}
