using UnityEngine;
using Zenject;
using TMPro;
using BattleshipsVR.Net.Services;
using BattleshipsVR.Core;
using BattleshipsVR.Net.Data;

namespace BattleshipsVR.UI
{
    /// <summary>Displays whose turn it is during battle, reacting to game state and local turn events.</summary>
    public sealed class TurnIndicatorText : MonoBehaviour
    {
        [SerializeField, Tooltip("Label that displays the turn text.")]
        private TMP_Text _label;
        [SerializeField, Tooltip("Text shown when it's the local player's turn.")]
        private string _yourTurnText = "Your Turn";
        [SerializeField, Tooltip("Text shown when it's the opponent's turn.")]
        private string _opponentTurnText = "Opponent Turn";

        [Inject] private GameStateService _gameStateService;
        [Inject] private TurnService _turnService;

        private bool _isBattle;
        private bool _isMyTurn;

        /// <summary>Subscribes to game and turn events and clears label.</summary>
        private void OnEnable()
        {
            _gameStateService.OnClientGameStateChanged += HandleStateChanged;
            _gameStateService.OnLocalTurnStarted += HandleLocalTurnStarted;
            _turnService.OnClientShotResult += HandleShotResult;

            SetText(string.Empty);
        }

        /// <summary>Unsubscribes from events.</summary>
        private void OnDisable()
        {
            _gameStateService.OnClientGameStateChanged -= HandleStateChanged;
            _gameStateService.OnLocalTurnStarted -= HandleLocalTurnStarted;
            _turnService.OnClientShotResult -= HandleShotResult;
        }

        /// <summary>Tracks entry/exit of battle state and shows default opponent text when applicable.</summary>
        private void HandleStateChanged(GameState state)
        {
            _isBattle = (state == GameState.BATTLE);
            if (!_isBattle)
            {
                _isMyTurn = false;
                SetText(string.Empty);
                return;
            }

            if (!_isMyTurn) SetText(_opponentTurnText);
        }

        /// <summary>Called when the local client's turn starts.</summary>
        private void HandleLocalTurnStarted(int seconds)
        {
            _isMyTurn = true;
            SetText(_yourTurnText);
        }

        /// <summary>Switches to opponent text after the local shot is taken.</summary>
        private void HandleShotResult(byte packedCell, bool isHit, bool isSunk, byte sunkTypeId, bool isLocalShooter)
        {
            if (!_isBattle) return;
            if (isLocalShooter)
            {
                _isMyTurn = false;
                SetText(_opponentTurnText);
            }
        }

        /// <summary>Safely updates the TMP label.</summary>
        private void SetText(string s)
        {
            if (_label == null) return;
            _label.text = s;
        }
    }
}
