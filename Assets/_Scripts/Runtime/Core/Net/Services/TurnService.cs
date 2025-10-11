using FishNet.Object;
using FishNet.Connection;
using Zenject;
using BattleshipsVR.Config;
using BattleshipsVR.Core;

namespace BattleshipsVR.Net.Services
{
    /// <summary>Resolves one byte shot intents and notifies both players</summary>
    public sealed class TurnService : NetworkBehaviour
    {
        public event System.Action<byte, bool, bool, byte> OnShotResolved; // server only
        public event System.Action<byte, bool, bool, byte, bool> OnClientShotResult; // cell, hit, sunk, sunkTypeId, isLocalShooter
        public event System.Action<BitBoard256> OnClientReveal;

        [Inject] private GameSettingsSO _settings;
        [Inject] private GridCodec _codec;
        [Inject] private BoardService _boardService;
        [Inject] private GameStateService _gameStateService;
        [Inject] private PlayerRegistryService _roster;

        private readonly System.Collections.Generic.Dictionary<NetworkConnection, BitBoard256> _triedByPlayer = new();
        private readonly System.Collections.Generic.Dictionary<NetworkConnection, int> _turnShotCount = new();
        private readonly System.Collections.Generic.Dictionary<NetworkConnection, float> _lastShotTime = new();
        private const float MIN_SECONDS_BETWEEN_SHOTS = 0.05f;

        /// <summary>Client requests to fire at packed cell, validated server side</summary>
        [ServerRpc(RequireOwnership = false)]
        public void ClientRequestShotServerRpc(byte packedCell, NetworkConnection caller)
        {
            if (!_gameStateService.ServerIsCurrentShooter(caller))
                return;

            float now = UnityEngine.Time.unscaledTime;
            if (_lastShotTime.TryGetValue(caller, out float last) && now - last < MIN_SECONDS_BETWEEN_SHOTS)
                return;
            _lastShotTime[caller] = now;

            if (_turnShotCount.TryGetValue(caller, out int count) && count >= 1)
                return;

            int cellIndex = _codec.ToIndex(packedCell);
            if (cellIndex < 0 || cellIndex >= _codec.MaxCells)
                return;

            if (!_triedByPlayer.TryGetValue(caller, out var tried)) tried = default;
            if (tried.Get(cellIndex)) return; // duplicate cell guard
            tried.Set(cellIndex);
            _triedByPlayer[caller] = tried;

            _turnShotCount[caller] = 1;

            NetworkConnection defender = _roster.GetOpponent(caller);
            if (defender == null)
                return;

            if (_boardService.ServerTryResolveShot(caller, defender, cellIndex, out bool isHit, out bool isSunk, out byte sunkTypeId))
            {
                NotifyShotResultToShooterTargetRpc(caller, packedCell, isHit, isSunk, sunkTypeId);
                NotifyShotResultToDefenderTargetRpc(defender, packedCell, isHit, isSunk, sunkTypeId);

                OnShotResolved?.Invoke(packedCell, isHit, isSunk, sunkTypeId);

                if (_boardService.ServerIsFleetDefeated(defender))
                {
                    var defReveal = _boardService.ServerGetReveal(defender);
                    SendRevealTargetRpc(caller, defReveal);
                    SendRevealTargetRpc(defender, defReveal);

                    _gameStateService.ServerEnterReveal();
                    _gameStateService.ServerEnterEnd();
                    return;
                }

                _turnShotCount[caller] = 0;
                _gameStateService.ServerAdvanceTurn(defender);
            }
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
        private void SendRevealTargetRpc(NetworkConnection target, BitBoard256 defenderShips)
        {
            OnClientReveal?.Invoke(defenderShips);
        }
    }
}
