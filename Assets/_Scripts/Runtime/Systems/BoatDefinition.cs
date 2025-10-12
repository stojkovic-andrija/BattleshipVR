using UnityEngine;
using BattleshipsVR.Config;

public sealed class BoatDefinition : MonoBehaviour
{
    [SerializeField] private BoatTypeSO _boatType;

    public BoatTypeSO BoatType => _boatType;
    public byte TypeId => _boatType != null ? _boatType.TypeId : (byte)0;
    public int Length => _boatType != null ? _boatType.Length : 0;
}
