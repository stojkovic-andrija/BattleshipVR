using UnityEngine;

namespace BattleshipsVR.Config
{
    /// <summary>Authoritative game config for board size, fleet and timers</summary>
    [CreateAssetMenu(menuName = "BattleshipsVR/GameSettings", fileName = "GameSettingsSO")]
    public sealed class GameSettingsSO : ScriptableObject
    {
        public int GridSize => _gridSize;
        public BoatTypeSO[] BoatTypes => _boatTypes;
        public int PlacementSeconds => _placementSeconds;
        public int ConfirmSeconds => _confirmSeconds;
        public int TurnSeconds => _turnSeconds;
        public int RevealSeconds => _revealSeconds;
        public bool AutoConfirmWhenBothReady => _autoConfirmWhenBothReady;
        public bool FirstFinisherGetsFirstTurn => _firstFinisherGetsFirstTurn;
        
        [Header("Board")]
        [SerializeField] private int _gridSize = 10;
        [SerializeField] private BoatTypeSO[] _boatTypes;

        [Header("Phase Timers (seconds)")]
        [SerializeField] private int _placementSeconds = 60;
        [SerializeField] private int _confirmSeconds = 5;
        [SerializeField] private int _turnSeconds = 25;
        [SerializeField] private int _revealSeconds = 3;

        [Header("Flow Flags")]
        [SerializeField] private bool _autoConfirmWhenBothReady = true;
        [SerializeField] private bool _firstFinisherGetsFirstTurn = true;

        private void OnValidate()
        {
            _gridSize = Mathf.Clamp(_gridSize, 5, 16);
            _placementSeconds = Mathf.Clamp(_placementSeconds, 0, 999);
            _confirmSeconds = Mathf.Clamp(_confirmSeconds, 0, 999);
            _turnSeconds = Mathf.Clamp(_turnSeconds, 5, 999);
            _revealSeconds = Mathf.Clamp(_revealSeconds, 0, 999);
            if (_boatTypes == null) _boatTypes = new BoatTypeSO[0];
        }
    }
}
