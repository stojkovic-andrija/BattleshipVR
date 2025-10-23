using UnityEngine;
using Zenject;
using Cysharp.Threading.Tasks;
using BattleshipsVR.Net.Services;
using FishNet.Connection;
using FishNet.Managing;
using System.Threading;
using BattleshipsVR.Interaction; // BoardGridMapper

namespace BattleshipsVR.Visuals
{
    /// <summary>
    /// Reveals sunk enemy ships by raising their prefabs at the correct world position.
    /// Uses <see cref="BoardGridMapper"/> if present for precise board mapping; otherwise falls back to a centered grid.
    /// </summary>
    public sealed class EnemyShipVisualizer : MonoBehaviour
    {
        [Header("Ship References")]
        [SerializeField, Tooltip("Prefabs owning BoatDefinition components. Indexed arbitrarily; matched by TypeId at runtime.")]
        private GameObject[] _ships = new GameObject[5];

        [Header("Animation Settings")]
        [SerializeField, Tooltip("Vertical rise distance once revealed.")]
        private float _riseHeight = 0.8f;
        [SerializeField, Tooltip("Total time for the reveal rise animation.")]
        private float _riseDuration = 1.2f;
        [SerializeField, Tooltip("Easing for the rise animation.")]
        private AnimationCurve _riseCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Grid Conversion (Fallback)")]
        [SerializeField, Tooltip("Optional board origin if mapper is missing; treated as center of the board.")]
        private Transform _gridOrigin;
        [SerializeField, Tooltip("World-space tile size for fallback conversion.")]
        private float _tileSize = 0.25f;
        [SerializeField, Tooltip("Board dimension (NxN) in fallback mode.")]
        private int _fallbackGridSize = 10;

        [Header("Grid Mapper (Preferred)")]
        [SerializeField, Tooltip("Preferred board mapper for accurate cell-to-world conversion.")]
        private BoardGridMapper _enemyGridMapper;

        [Inject] private BoardService _boardService;
        [Inject] private NetworkManager _networkManager;

        private NetworkConnection _localConnection;
        private CancellationTokenSource _revealCts;

        private void Awake()
        {
            for (int i = 0; i < _ships.Length; i++)
            {
                if (_ships[i] != null)
                    _ships[i].SetActive(false);
            }
        }

        private void OnEnable()
        {
            _boardService.OnShipSunk += HandleShipSunk;
        }

        private void OnDisable()
        {
            _boardService.OnShipSunk -= HandleShipSunk;
            _revealCts?.Cancel();
            _revealCts?.Dispose();
        }

        private void Start()
        {
            if (_networkManager != null)
                _localConnection = _networkManager.ClientManager.Connection;
        }

        /// <summary>
        /// Handles sink events for any ship; reveals only if the local player is the attacker (i.e., defender is not local).
        /// </summary>
        private void HandleShipSunk(int defenderClientId, byte typeId, BoardService.ShipOrientation orientation, Vector3 gridCenter)
        {
            int localId = _networkManager.ClientManager.Connection.ClientId;
            if (defenderClientId == localId)
                return;

            Vector3 worldPos = GridToWorldCenteredFlipped(gridCenter);
            Quaternion worldRot = orientation == BoardService.ShipOrientation.Horizontal
                ? Quaternion.Euler(0f, 90f, 0f)
                : Quaternion.identity;

            _revealCts?.Cancel();
            _revealCts?.Dispose();
            _revealCts = new CancellationTokenSource();
            RevealShipByIdAsync(typeId, worldPos, worldRot, _revealCts.Token).Forget();
        }

        /// <summary>
        /// Converts logical grid center to world using fallback centered grid, with X/Z flipped (attacker perspective).
        /// </summary>
        private Vector3 GridToWorldCenteredFlipped(Vector3 center)
        {
            if (_enemyGridMapper != null)
                return GridToWorldViaMapperCentered(_enemyGridMapper, center);

            float half = (_fallbackGridSize - 1) * 0.5f;
            Vector3 boardCenter = _gridOrigin != null ? _gridOrigin.position : Vector3.zero;
            float offX = -(center.x - half);
            float offZ = -(center.z - half);
            return boardCenter + new Vector3(offX * _tileSize, 0f, offZ * _tileSize);
        }

        /// <summary>
        /// Converts logical grid center to world via mapper, preserving a centered origin and flipped attacker view.
        /// </summary>
        private Vector3 GridToWorldViaMapperCentered(BoardGridMapper m, Vector3 center)
        {
            int nx = m.Nx;
            int ny = m.Ny;
            float halfX = (nx - 1) * 0.5f;
            float halfZ = (ny - 1) * 0.5f;

            Vector3 p00 = m.GetCellCenterWorld(0);
            Vector3 p10 = m.GetCellCenterWorld(1);
            Vector3 p01 = m.GetCellCenterWorld(nx);

            Vector3 stepX = Vector3.ProjectOnPlane(p10 - p00, Vector3.up);
            Vector3 stepZ = Vector3.ProjectOnPlane(p01 - p00, Vector3.up);

            Vector3 boardCenter = GetBoardCenterWorld(m);

            float offX = -(center.x - halfX);
            float offZ = -(center.z - halfZ);

            return boardCenter + stepX * offX + stepZ * offZ;
        }

        /// <summary>
        /// Computes the geometric board center in world space, supporting even and odd dimensions.
        /// </summary>
        private Vector3 GetBoardCenterWorld(BoardGridMapper m)
        {
            int nx = m.Nx;
            int ny = m.Ny;

            if ((nx % 2) == 1 && (ny % 2) == 1)
            {
                int cx = nx / 2;
                int cz = ny / 2;
                return m.GetCellCenterWorld(cz * nx + cx);
            }

            int xL = (nx - 1) / 2;
            int xR = nx / 2;
            int zB = (ny - 1) / 2;
            int zT = ny / 2;

            Vector3 a = m.GetCellCenterWorld(zB * nx + xL);
            Vector3 b = m.GetCellCenterWorld(zB * nx + xR);
            Vector3 c = m.GetCellCenterWorld(zT * nx + xL);
            Vector3 d = m.GetCellCenterWorld(zT * nx + xR);
            return (a + b + c + d) * 0.25f;
        }

        /// <summary>
        /// Finds the prefab by TypeId and performs a timed rise animation at the given pose.
        /// </summary>
        private async UniTaskVoid RevealShipByIdAsync(byte typeId, Vector3 position, Quaternion rotation, CancellationToken token)
        {
            if (_ships == null || _ships.Length == 0)
                return;

            BoatDefinition targetBoat = null;
            GameObject targetObject = null;

            for (int i = 0; i < _ships.Length; i++)
            {
                GameObject shipObj = _ships[i];
                if (shipObj == null) continue;
                if (shipObj.TryGetComponent(out BoatDefinition boatDef) && boatDef.TypeId == typeId)
                {
                    targetBoat = boatDef;
                    targetObject = shipObj;
                    break;
                }
            }

            if (targetBoat == null || targetObject == null)
                return;

            Quaternion adjustedRotation = Quaternion.Euler(180f, rotation.eulerAngles.y, rotation.eulerAngles.z);

            targetObject.SetActive(true);
            targetObject.transform.SetPositionAndRotation(position, adjustedRotation);

            Vector3 startPos = new Vector3(position.x, position.y - 3f, position.z);
            Vector3 endPos = startPos + Vector3.up * _riseHeight;

            float time = 0f;
            while (time < _riseDuration)
            {
                if (token.IsCancellationRequested)
                    return;

                float t = time / _riseDuration;
                float curveY = _riseCurve.Evaluate(t);
                targetObject.transform.position = Vector3.LerpUnclamped(startPos, endPos, curveY);

                time += Time.deltaTime;
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            targetObject.transform.position = endPos;
        }
    }
}
