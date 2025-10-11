using UnityEngine;

namespace BattleshipsVR.Config
{
    /// <summary>Single boat type definition used by the server during validation</summary>
    [CreateAssetMenu(menuName = "BattleshipsVR/BoatType", fileName = "BoatTypeSO")]
    public sealed class BoatTypeSO : ScriptableObject
    {
        [SerializeField] private byte _typeId = 0;
        [SerializeField] private string _displayName = "Battleship";
        [SerializeField] private int _length = 3;

        public byte TypeId => _typeId;
        public string DisplayName => _displayName;
        public int Length => _length;

        private void OnValidate()
        {
            _length = Mathf.Clamp(_length, 2, 16);
        }
    }
}
