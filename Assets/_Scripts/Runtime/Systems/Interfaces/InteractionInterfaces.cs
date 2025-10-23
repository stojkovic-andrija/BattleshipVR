using UnityEngine;

namespace BattleshipsVR.Interaction.Abstractions
{
    public interface ITargetAimer
    {
        bool IsLocked { get; }
        int LockedCellIndex { get; }
        void ShowHover(Vector3 worldPos, int cellIndex);
        void Lock();
        void Cancel();
        void Launch();
        void ApplyResult(bool isHit);
        void PlayIncoming(Vector3 impactWorldPos, bool isHit);
        void HideAll();
    }

    public interface IGridRayResolver
    {
        bool TryResolve(RaycastHit hit, out int cellIndex, out Vector3 cellCenterWorld);
    }

    public interface IGridWorld
    {
        Vector3 GetCellCenterWorld(int cellIndex);
        int Nx { get; }
        int Ny { get; }
    }

    public interface IPlacementGridInteractorDriver
    {
        void EnablePlacement();
        void TickPlacement();
        void DisablePlacement();
    }
}
