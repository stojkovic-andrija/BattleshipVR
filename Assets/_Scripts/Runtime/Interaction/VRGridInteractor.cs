using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Zenject;
using BattleshipsVR.Core;
using BattleshipsVR.Net.Data;
using BattleshipsVR.Net.Services;
using BattleshipsVR.Interaction.Abstractions;
using BattleshipsVR.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using BattleshipsVR.Audio;
using Cysharp.Threading.Tasks;

namespace BattleshipsVR.Interaction
{
    /// <summary>
    /// VR interactor for placement and battle: aims via Near/Far XR interactor, places ships with rotation,
    /// and locks/fires at opponent cells with mirrored indexing. Uses the new Input System.
    /// </summary>
    public sealed class VRGridInteractor : MonoBehaviour
    {
        [Header("XR")]
        [SerializeField, Tooltip("XR Near/Far interactor used for curved/ray aiming.")]
        private NearFarInteractor _nearFar;
        [SerializeField, Tooltip("Extra distance added when raycasting to the curve end.")]
        private float _rayBackoff = 0.02f;

        [Header("Input")]
        [SerializeField] private InputActionReference _selectAction;
        [SerializeField] private InputActionReference _cancelAction;
        [SerializeField] private InputActionReference _rotateCWAction;
        [SerializeField] private InputActionReference _rotateCCWAction;

        [Header("Battle Mapping")]
        [SerializeField] private LayerMask _placementGridMask;
        [SerializeField] private LayerMask _opponentGridMask;
        [SerializeField, Tooltip("BoardGridMapper implementing IGridRayResolver.")]
        private MonoBehaviour _opponentResolverBehaviour;
        [SerializeField, Tooltip("BoardGridMapper implementing IGridWorld.")]
        private MonoBehaviour _myGridWorldBehaviour;
        [SerializeField, Tooltip("BoardGridMapper implementing IGridWorld.")]
        private MonoBehaviour _opponentGridWorldBehaviour;

        [Header("Battle Visuals")]
        [SerializeField, Tooltip("TargetVisualizer implementing ITargetAimer for opponent grid.")]
        private MonoBehaviour _opponentTargetBehaviour;
        [SerializeField, Tooltip("TargetVisualizer implementing ITargetAimer for local/base grid.")]
        private MonoBehaviour _baseTargetBehaviour;
        [SerializeField] private GridOverlayMarks _myGridMarks;
        [SerializeField] private GridOverlayMarks _opponentGridMarks;

        [Header("Debug")]
        [SerializeField] private bool _verboseLogs = true;

        [Inject] private ClientPlacementPlanner _planner;
        [Inject] private GridCodec _codec;
        [Inject] private GameStateService _gameState;
        [Inject] private TurnService _turns;
        [Inject] private AudioManager _audioManager;

        private IGridRayResolver _opponentResolver;
        private IGridWorld _myGridWorld;
        private IGridWorld _opponentGridWorld;
        private ITargetAimer _opponentTarget;
        private ITargetAimer _baseTarget;

        private IGridDraggable _active;
        private bool _previewVertical;
        private bool _hasValidPreview;
        private bool _lastPreviewValidLogged;
        private int _previewRootX, _previewRootY;

        private bool _isPlacement;
        private bool _isBattle;
        private bool _canShoot;
        private bool _launchedThisTurn;
        private int _hoverCell = -1;
        private BitBoard256 _triedOpponentCells;

        private readonly List<int> _greens = new List<int>(16);
        private readonly List<int> _reds = new List<int>(16);
        private readonly float[] _greenFloats = new float[16];
        private readonly float[] _redFloats = new float[16];
        private MaterialPropertyBlock _mpb;

        private int _lastLoggedHoverCell = -2;
        private bool _loggedAwaitingOpponent;

        /// <summary>Validates assigned components implement required interfaces.</summary>
        private void OnValidate()
        {
            if (_opponentTargetBehaviour != null && !(_opponentTargetBehaviour is ITargetAimer))
                Debug.LogError("[VRGridInteractor] Opponent Target must implement ITargetAimer.", this);
            if (_baseTargetBehaviour != null && !(_baseTargetBehaviour is ITargetAimer))
                Debug.LogError("[VRGridInteractor] Base Target must implement ITargetAimer.", this);
            if (_myGridWorldBehaviour != null && !(_myGridWorldBehaviour is IGridWorld))
                Debug.LogError("[VRGridInteractor] My Grid World must implement IGridWorld.", this);
            if (_opponentGridWorldBehaviour != null && !(_opponentGridWorldBehaviour is IGridWorld))
                Debug.LogError("[VRGridInteractor] Opp Grid World must implement IGridWorld.", this);
            if (_opponentResolverBehaviour != null && !(_opponentResolverBehaviour is IGridRayResolver))
                Debug.LogError("[VRGridInteractor] Opponent Resolver must implement IGridRayResolver.", this);
        }

        /// <summary>Resolves near/far interactor and caches interface components.</summary>
        private void Awake()
        {
            AutoFindNearFarIfNeeded();

            _opponentResolver = _opponentResolverBehaviour as IGridRayResolver;
            _myGridWorld = _myGridWorldBehaviour as IGridWorld;
            _opponentGridWorld = _opponentGridWorldBehaviour as IGridWorld;
            _opponentTarget = _opponentTargetBehaviour as ITargetAimer;
            _baseTarget = _baseTargetBehaviour as ITargetAimer;

            _mpb = new MaterialPropertyBlock();
            if (_planner != null && _planner.GridRenderer != null)
            {
                _planner.GridRenderer.GetPropertyBlock(_mpb);
                _mpb.SetVector("_GridScale", new Vector4(_planner.GridSize, _planner.GridSize, 0f, 0f));
                _planner.GridRenderer.SetPropertyBlock(_mpb);
            }
        }

        /// <summary>Enables input and subscribes to state/turn events.</summary>
        private void OnEnable()
        {
            _selectAction?.action?.Enable();
            _cancelAction?.action?.Enable();
            _rotateCWAction?.action?.Enable();
            _rotateCCWAction?.action?.Enable();

            _gameState.OnClientGameStateChanged += HandleStateChanged;
            _gameState.OnLocalTurnStarted += HandleLocalTurnStarted;
            _gameState.OnClientTurnBroadcast += HandleTurnBroadcast;
            _turns.OnClientShotResult += HandleShotResult;
        }

        /// <summary>Disables input and unsubscribes from events.</summary>
        private void OnDisable()
        {
            _selectAction?.action?.Disable();
            _cancelAction?.action?.Disable();
            _rotateCWAction?.action?.Disable();
            _rotateCCWAction?.action?.Disable();

            _gameState.OnClientGameStateChanged -= HandleStateChanged;
            _gameState.OnLocalTurnStarted -= HandleLocalTurnStarted;
            _gameState.OnClientTurnBroadcast -= HandleTurnBroadcast;
            _turns.OnClientShotResult -= HandleShotResult;
        }

        /// <summary>Routes per-frame to placement or battle logic.</summary>
        private void Update()
        {
            if (_isPlacement) TickPlacement();
            else if (_isBattle) TickBattle();
        }

        /// <summary>Sets the layer mask used for placement raycasts.</summary>
        public void SetPlacementMask(LayerMask mask)
        {
            _placementGridMask = mask;
        }

        /// <summary>Placement loop: preview, rotate, commit/unplace under XR aim.</summary>
        private void TickPlacement()
        {
            if (_nearFar == null || _planner == null) return;

            if (_active != null)
            {
                if (!TryGetAimRay(out var ray)) return;

                if (_planner.TryGetGridCellFromRay(ray, _placementGridMask, out int gx, out int gy, out _))
                {
                    (int rx, int ry) = ComputeCenteredRoot(gx, gy, _active.Length, _previewVertical, _planner.GridSize);
                    _previewRootX = rx;
                    _previewRootY = ry;

                    Vector3 segCenter = _planner.GetSegmentCenterWorld(_previewRootX, _previewRootY, _active.Length, _previewVertical, GetActiveY());
                    bool wasValid = _hasValidPreview;
                    _hasValidPreview = _active.PreviewAt(_previewRootX, _previewRootY, _previewVertical, segCenter);

                    _planner.BuildPreviewIndexLists(_previewRootX, _previewRootY, _active.Length, _previewVertical, _greens, _reds);
                    PushOverlayArrays(_planner.GridRenderer, _greens, _reds);
                }

                if (RotateCWPressed())
                {
                    _previewVertical = !_previewVertical;
                    if (_active is BoatDraggable b1) b1.RotateQuarter(+1);
                    _audioManager.PlayWithRandomPitch(AudioManager.AudioType.RotateTick, .7f, 1.3f);
                }
                if (RotateCCWPressed())
                {
                    _previewVertical = !_previewVertical;
                    if (_active is BoatDraggable b2) b2.RotateQuarter(-1);
                    _audioManager.PlayWithRandomPitch(AudioManager.AudioType.RotateTick, .7f, 1.3f);
                }

                if (SelectReleased())
                {
                    Vector3 segCenter = _planner.GetSegmentCenterWorld(_previewRootX, _previewRootY, _active.Length, _previewVertical, GetActiveY());
                    if (_hasValidPreview)
                    {
                        _active.CommitAt(_previewRootX, _previewRootY, _previewVertical, segCenter);
                        _audioManager.PlayRandom(.8f, 1.2f, AudioManager.AudioType.BoatPlace, AudioManager.AudioType.BoatPlacement2);
                    }
                    else
                    {
                        _active.Unplace();
                        _audioManager.PlayWithSpecificPitch(AudioManager.AudioType.PlacementError, 1f);
                    }

                    _active = null;
                    ClearOverlayArrays(_planner.GridRenderer);
                    _lastPreviewValidLogged = false;
                }
            }
            else
            {
                if (SelectPressed())
                {
                    if (TryPickUnderAim())
                    {
                        Log("Picked draggable");
                        _audioManager.PlayWithSpecificPitch(AudioManager.AudioType.StoneFall, 2f);
                    }
                    else
                    {
                        Log("Pick failed");
                    }
                }
            }
        }

        /// <summary>Attempts to pick a draggable under the XR aim.</summary>
        private bool TryPickUnderAim()
        {
            if (!TryGetAimRay(out var ray)) return false;
            if (Physics.Raycast(ray, out var hit, 1000f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.TryGetComponent<IGridDraggable>(out var drag))
                {
                    _active = drag;
                    if (_active is BoatDraggable b)
                    {
                        b.SyncVerticalFromTransform();
                        _previewVertical = b.Vertical;
                    }
                    else
                    {
                        _previewVertical = false;
                    }
                    _active.BeginDrag();
                    return true;
                }
            }
            return false;
        }

        /// <summary>Y height for preview/commit depending on active draggable.</summary>
        private float GetActiveY()
        {
            return _active is BoatDraggable b ? b.transform.position.y : _planner.GridTransform.position.y;
        }

        /// <summary>Centers a ship around the hit cell, clamped to grid.</summary>
        private static (int rx, int ry) ComputeCenteredRoot(int hitX, int hitY, int length, bool vertical, int gridSize)
        {
            int offset = (length % 2 == 0) ? (length / 2 - 1) : (length / 2);
            int rx = vertical ? hitX : (hitX - offset);
            int ry = vertical ? (hitY - offset) : hitY;
            GridMath.ClampRootForLength(ref rx, ref ry, length, gridSize, vertical);
            return (rx, ry);
        }

        /// <summary>Pushes preview indices to the grid material property block.</summary>
        private void PushOverlayArrays(Renderer target, List<int> greens, List<int> reds)
        {
            if (target == null) return;
            int gCount = Mathf.Min(greens.Count, 16);
            int rCount = Mathf.Min(reds.Count, 16);
            for (int i = 0; i < gCount; i++) _greenFloats[i] = greens[i];
            for (int i = gCount; i < 16; i++) _greenFloats[i] = 0f;
            for (int i = 0; i < rCount; i++) _redFloats[i] = reds[i];
            for (int i = rCount; i < 16; i++) _redFloats[i] = 0f;

            _mpb ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(_mpb);
            _mpb.SetInt("_GreenCount", gCount);
            _mpb.SetInt("_RedCount", rCount);
            _mpb.SetFloatArray("_GreenIdx", _greenFloats);
            _mpb.SetFloatArray("_RedIdx", _redFloats);
            target.SetPropertyBlock(_mpb);
        }

        /// <summary>Clears preview arrays from the grid material.</summary>
        private void ClearOverlayArrays(Renderer target)
        {
            if (target == null) return;
            _mpb ??= new MaterialPropertyBlock();
            target.GetPropertyBlock(_mpb);
            _mpb.SetInt("_GreenCount", 0);
            target.SetPropertyBlock(_mpb);
        }

        /// <summary>Battle loop: hover/lock/cancel/launch and send shot RPC.</summary>
        private void TickBattle()
        {
            if (!_canShoot)
            {
                if (!_loggedAwaitingOpponent)
                {
                    Log("Awaiting opponent");
                    _loggedAwaitingOpponent = true;
                }
                return;
            }
            if (_opponentTarget == null || _opponentGridWorld == null || _opponentResolver == null) return;

            if (!_opponentTarget.IsLocked)
            {
                if (TryGetMaskedCurveHit(_opponentGridMask, out var ray, out var hit, out _))
                {
                    if (_opponentResolver.TryResolve(hit, out int cell, out Vector3 center))
                    {
                        _hoverCell = cell;
                        if (_hoverCell != _lastLoggedHoverCell)
                        {
                            Log($"Hover cell={_hoverCell}");
                            _audioManager.PlayWithRandomPitch(AudioManager.AudioType.HoverTick, .8f, 1.2f);
                            _lastLoggedHoverCell = _hoverCell;
                        }
                        _opponentTarget.ShowHover(center, _hoverCell);

                        if (SelectPressed())
                        {
                            if (_triedOpponentCells.Get(cell)) { Log($"Cell {_hoverCell} already tried"); return; }
                            _opponentTarget.Lock();
                            _audioManager.PlayWithSpecificPitch(AudioManager.AudioType.MissileLockIn, 1f);
                            Log($"Locked cell={_hoverCell}");
                        }
                    }
                }
                else
                {
                    _hoverCell = -1;
                }
            }
            else
            {
                if (CancelPressed())
                {
                    _opponentTarget.Cancel();
                    _audioManager.PlayWithSpecificPitch(AudioManager.AudioType.RotateTick, 1f);
                    Log("Lock canceled");
                    return;
                }

                if (SelectPressed() && !_launchedThisTurn)
                {
                    int cell = _opponentTarget.LockedCellIndex >= 0 ? _opponentTarget.LockedCellIndex : _hoverCell;
                    if (cell < 0) return;
                    if (_triedOpponentCells.Get(cell)) return;

                    _launchedThisTurn = true;
                    _canShoot = false;
                    _triedOpponentCells.Set(cell);

                    _opponentTarget.Launch();

                    int gx = cell % _codec.GridSize;
                    int gy = cell / _codec.GridSize;
                    _turns.ClientRequestShotServerRpc(_codec.Pack(gx, gy));
                    _audioManager.PlayWithRandomPitch(AudioManager.AudioType.MissileFall, .9f, 1.5f);
                }
            }
        }

        /// <summary>Updates local battle flags in response to server broadcast.</summary>
        private void HandleTurnBroadcast(bool isLocalShooter, int seconds)
        {
            _isBattle = true;
            _canShoot = isLocalShooter;
            _launchedThisTurn = false;
            _loggedAwaitingOpponent = !isLocalShooter;
        }

        /// <summary>Marks local turn start (redundant to broadcast) and resets flags.</summary>
        private void HandleLocalTurnStarted(int seconds)
        {
            _isBattle = true;
            _canShoot = true;
            _launchedThisTurn = false;
            _loggedAwaitingOpponent = false;
        }

        /// <summary>Transitions between placement/battle and resets visuals/state as needed.</summary>
        private void HandleStateChanged(GameState state)
        {
            bool wasPlacement = _isPlacement;
            bool wasBattle = _isBattle;

            _isPlacement = state == GameState.PLACEMENT || state == GameState.PRE_PLACEMENT || state == GameState.CONFIRM;
            _isBattle = state == GameState.BATTLE;

            if (!_isPlacement && wasPlacement)
            {
                if (_active != null) { _active.Unplace(); _active = null; }
                ClearOverlayArrays(_planner != null ? _planner.GridRenderer : null);
                _lastPreviewValidLogged = false;
            }

            if (!_isBattle)
            {
                _canShoot = false;
                _launchedThisTurn = false;
                _opponentTarget?.HideAll();
                _baseTarget?.HideAll();
                _lastLoggedHoverCell = -2;
                _loggedAwaitingOpponent = false;
            }

            if (state == GameState.REVEAL || state == GameState.END)
            {
                _triedOpponentCells = default;
                _opponentTarget?.HideAll();
                _baseTarget?.HideAll();
            }
        }

        /// <summary>Applies shot result feedback for shooter/defender and triggers audio.</summary>
        private void HandleShotResult(byte packedCell, bool isHit, bool isSunk, byte sunkTypeId, bool isLocalShooter)
        {
            int idx = _codec.ToIndex(packedCell);
            int gx = idx % _codec.GridSize;
            int gy = idx / _codec.GridSize;

            if (isLocalShooter)
            {
                _opponentTarget?.ApplyResult(isHit);
                if (_opponentGridMarks != null)
                {
                    if (isHit)
                    {
                        _opponentGridMarks.MarkHit(idx);
                        _audioManager.PlayRandom(.8f, 1.2f,
                            AudioManager.AudioType.Explosion,
                            AudioManager.AudioType.BigExplosion,
                            AudioManager.AudioType.HugeExplosion,
                            AudioManager.AudioType.LargeExplosion
                        );
                    }
                    else
                    {
                        _opponentGridMarks.MarkMiss(idx);
                        _audioManager.PlayRandom(.8f, 1.2f,
                            AudioManager.AudioType.BoatPlace,
                            AudioManager.AudioType.BoatPlacement2
                        );
                    }
                }
            }
            else
            {
                int fx = (_codec.GridSize - 1) - gx;
                int fy = (_codec.GridSize - 1) - gy;
                int fIdx = fy * _codec.GridSize + fx;

                if (_myGridWorld != null && _baseTarget != null)
                {
                    Vector3 impact = _myGridWorld.GetCellCenterWorld(fIdx);
                    _baseTarget.PlayIncoming(impact, isHit);
                }
                if (_myGridMarks != null)
                {
                    if (isHit)
                    {
                        _myGridMarks.MarkHit(fIdx);
                        _audioManager.PlayRandom(.8f, 1.2f,
                            AudioManager.AudioType.Explosion,
                            AudioManager.AudioType.BigExplosion,
                            AudioManager.AudioType.HugeExplosion,
                            AudioManager.AudioType.LargeExplosion
                        );
                    }
                    else
                    {
                        _myGridMarks.MarkMiss(fIdx);
                        _audioManager.PlayRandom(.8f, 1.2f,
                            AudioManager.AudioType.BoatPlace,
                            AudioManager.AudioType.BoatPlacement2
                        );
                    }
                }
            }
        }

        /// <summary>Attempts to find a right-hand NearFar interactor if not assigned.</summary>
        private void AutoFindNearFarIfNeeded()
        {
            if (_nearFar != null) return;
            var all = FindObjectsOfType<NearFarInteractor>(true);
            if (all.Length == 0) return;
            for (int i = 0; i < all.Length; i++)
                if (all[i].name.ToLower().Contains("right")) { _nearFar = all[i]; return; }
            _nearFar = all[0];
            ResetNearFarAsync(_nearFar, 0.15f).Forget();
        }

        /// <summary>Temporarily disables and re-enables the interactor to refresh curve state.</summary>
        private async UniTaskVoid ResetNearFarAsync(NearFarInteractor interactor, float delaySeconds = 0.1f)
        {
            if (interactor == null)
                return;

            interactor.enabled = false;
            await UniTask.Delay(System.TimeSpan.FromSeconds(delaySeconds));
            interactor.enabled = true;
        }

        /// <summary>Builds an aim ray from the Near/Far curve; falls back to forward when needed.</summary>
        private bool TryGetAimRay(out Ray ray)
        {
            ray = default;
            if (_nearFar == null) return false;

            var origin = _nearFar.curveOrigin.position;
            if (_nearFar.TryGetCurveEndPoint(out var endWS, false, false) == EndPointType.None)
            {
                ray = new Ray(origin, _nearFar.curveOrigin.forward);
                return true;
            }

            var dir = (endWS - origin);
            if (dir.sqrMagnitude < 1e-6f) dir = _nearFar.curveOrigin.forward;
            ray = new Ray(origin, dir.normalized);
            return true;
        }

        /// <summary>Raycasts along the Near/Far curve against a layer mask.</summary>
        private bool TryGetMaskedCurveHit(LayerMask mask, out Ray ray, out RaycastHit hit, out Vector3 endWS)
        {
            hit = default; endWS = default; ray = default;
            if (!TryGetAimRay(out ray)) return false;

            Vector3 origin = ray.origin;
            if (!_nearFar.TryGetCurveEndPoint(out endWS, false, false).Equals(EndPointType.None))
            {
                float dist = Mathf.Max(0.01f, Vector3.Distance(origin, endWS) + _rayBackoff);
                return Physics.Raycast(ray, out hit, dist, mask, QueryTriggerInteraction.Collide);
            }

            return Physics.Raycast(ray, out hit, 1000f, mask, QueryTriggerInteraction.Collide);
        }

        // Input wrappers
        private bool SelectPressed()  => _selectAction != null && _selectAction.action != null && _selectAction.action.WasPressedThisFrame();
        private bool SelectReleased() => _selectAction != null && _selectAction.action != null && _selectAction.action.WasReleasedThisFrame();
        private bool CancelPressed()  => _cancelAction != null && _cancelAction.action != null && _cancelAction.action.WasPressedThisFrame();
        private bool RotateCWPressed()=> _rotateCWAction != null && _rotateCWAction.action != null && _rotateCWAction.action.WasPressedThisFrame();
        private bool RotateCCWPressed()=> _rotateCCWAction != null && _rotateCCWAction.action != null && _rotateCCWAction.action.WasPressedThisFrame();

        private void Log(string m)
        {
            if (_verboseLogs) Debug.Log($"[VRGridInteractor] {m}", this);
        }
    }
}
