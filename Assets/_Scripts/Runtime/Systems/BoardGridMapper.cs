using UnityEngine;
using BattleshipsVR.Interaction.Abstractions;

namespace BattleshipsVR.Interaction
{
    /// <summary>
    /// Resolves ray hits to grid cells in this object's local space and exposes world-space cell centers.
    /// Uses local mesh bounds (XZ) for placement even when not centered at world origin.
    /// </summary>
    public sealed class BoardGridMapper : MonoBehaviour, IGridRayResolver, IGridWorld
    {
        [Header("Grid")]
        [SerializeField, Tooltip("Grid size in cells (X by Y).")]
        private Vector2Int _grid = new Vector2Int(10, 10);

        [Header("Local Bounds (auto from MeshFilter if available)")]
        [SerializeField, Tooltip("If true, reads local bounds from MeshFilter.sharedMesh.bounds.")]
        private bool _useMeshBounds = true;
        [SerializeField, Tooltip("Optional MeshFilter to read local bounds from.")]
        private MeshFilter _meshFilter;
        [SerializeField, Tooltip("Local-space minimum corner (X, Z) of the grid area.")]
        private Vector2 _localMin = new Vector2(-0.5f, -0.5f);
        [SerializeField, Tooltip("Local-space size (X, Z) of the grid area.")]
        private Vector2 _localSize = new Vector2(1f, 1f);

        [Header("Visual Y")]
        [SerializeField, Tooltip("World-space Y used for returned cell centers.")]
        private float _targetWorldY = 0f;

        /// <summary>Grid width (X cells), clamped to at least 1.</summary>
        public int Nx => Mathf.Max(1, _grid.x);
        /// <summary>Grid height (Y cells), clamped to at least 1.</summary>
        public int Ny => Mathf.Max(1, _grid.y);

        /// <summary>Initializes bounds from mesh if configured.</summary>
        private void Awake()
        {
            SyncBoundsFromMesh();
        }

        /// <summary>Keeps serialized bounds consistent when edited in the inspector.</summary>
        private void OnValidate()
        {
            SyncBoundsFromMesh();
        }

        /// <summary>
        /// Converts a raycast hit on this object into a grid cell index and its center in world space.
        /// </summary>
        public bool TryResolve(RaycastHit hit, out int cellIndex, out Vector3 cellCenterWorld)
        {
            Vector3 local = transform.InverseTransformPoint(hit.point);
            if (!TryLocalToCell(local, out int cx, out int cy))
            {
                cellIndex = -1;
                cellCenterWorld = default;
                return false;
            }

            cellIndex = cy * Nx + cx;
            cellCenterWorld = GetCellCenterWorld(cellIndex);
            return true;
        }

        /// <summary>
        /// Returns the world-space center for a given linear cell index (row-major: x + y*Nx).
        /// </summary>
        public Vector3 GetCellCenterWorld(int cellIndex)
        {
            int cx = Mathf.Clamp(cellIndex % Nx, 0, Nx - 1);
            int cy = Mathf.Clamp(cellIndex / Nx, 0, Ny - 1);

            float sx = _localSize.x / Mathf.Max(1, Nx);
            float sz = _localSize.y / Mathf.Max(1, Ny);

            float lx = _localMin.x + (cx + 0.5f) * sx;
            float lz = _localMin.y + (cy + 0.5f) * sz;

            Vector3 world = transform.TransformPoint(new Vector3(lx, 0f, lz));
            world.y = _targetWorldY;
            return world;
        }

        /// <summary>Reads local-space XZ bounds from MeshFilter if enabled and available.</summary>
        private void SyncBoundsFromMesh()
        {
            if (!_useMeshBounds)
                return;

            if (_meshFilter == null)
                _meshFilter = GetComponent<MeshFilter>();

            if (_meshFilter != null && _meshFilter.sharedMesh != null)
            {
                Bounds b = _meshFilter.sharedMesh.bounds; // local-space bounds
                _localMin = new Vector2(b.min.x, b.min.z);
                _localSize = new Vector2(b.size.x, b.size.z);
            }
        }

        /// <summary>Maps a local-space point to (cx, cy) within [_localMin, _localMin + _localSize].</summary>
        private bool TryLocalToCell(Vector3 local, out int cx, out int cy)
        {
            float u = (local.x - _localMin.x) / Mathf.Max(1e-6f, _localSize.x);
            float v = (local.z - _localMin.y) / Mathf.Max(1e-6f, _localSize.y);

            if (u < 0f || v < 0f || u >= 1f || v >= 1f)
            {
                cx = cy = -1;
                return false;
            }

            cx = Mathf.Clamp(Mathf.FloorToInt(u * Nx), 0, Nx - 1);
            cy = Mathf.Clamp(Mathf.FloorToInt(v * Ny), 0, Ny - 1);
            return true;
        }
    }
}
