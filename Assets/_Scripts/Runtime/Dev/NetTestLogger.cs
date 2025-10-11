using UnityEngine;
using Zenject;
using BattleshipsVR.Config;
using BattleshipsVR.Core;
using BattleshipsVR.Net.Services;
using BattleshipsVR.Net.Data;

namespace BattleshipsVR.Dev
{
    /// <summary>Logs phase changes, local turns, and shot results with boat names</summary>
    public sealed class NetTestLogger : MonoBehaviour
    {
        [Inject] private GameSettingsSO _settings;
        [Inject] private GameStateService _gameState;
        [Inject] private TurnService _turns;
        [Inject] private PlayerRegistryService _roster;

        private void OnEnable()
        {
            _gameState.OnClientGameStateChanged += HandleStateChanged;
            _gameState.OnLocalTurnStarted += HandleLocalTurn;
            _turns.OnClientShotResult += HandleShot;
            if (_roster.HasBothPlayers) AppLogger.Info("logger ready, both players present");
            else AppLogger.Info("logger ready, waiting for both players");
        }

        private void OnDisable()
        {
            _gameState.OnClientGameStateChanged -= HandleStateChanged;
            _gameState.OnLocalTurnStarted -= HandleLocalTurn;
            _turns.OnClientShotResult -= HandleShot;
        }

        private void HandleStateChanged(GameState state)
        {
            AppLogger.Info($"phase → {state}");
        }

        private void HandleLocalTurn(int seconds)
        {
            AppLogger.Info($"your turn for {seconds} sec");
        }

        private void HandleShot(byte cell, bool hit, bool sunk, byte typeId, bool byLocal)
        {
            string who = byLocal ? "you" : "opponent";
            string boat = TryName(typeId);
            if (sunk && boat != null) AppLogger.Info($"shot by {who}: {(hit ? "hit" : "miss")}, sunk {boat}");
            else AppLogger.Info($"shot by {who}: {(hit ? "hit" : "miss")}");
        }

        private string TryName(byte typeId)
        {
            var boats = _settings.BoatTypes;
            if (boats == null) return null;
            for (int i = 0; i < boats.Length; i++)
                if (boats[i] != null && boats[i].TypeId == typeId)
                    return boats[i].DisplayName;
            return null;
        }
    }
}
