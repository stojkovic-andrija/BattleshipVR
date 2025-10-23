using System.Collections;
using UnityEngine;
using Zenject;

/// <summary>Boat placement behaviour for drag/rotate/commit with revert-on-invalid.</summary>
public sealed class BoatDraggable : MonoBehaviour, IGridDraggable
{
    public event System.Action<BoatDraggable> OnPlaced;
    public event System.Action<BoatDraggable> OnUnplaced;

    [SerializeField, Tooltip("Holds BoatTypeSO and identifiers for this prefab.")]
    private BoatDefinition _definition;

    [Header("Lift / Drop Animation")]
    [SerializeField] private float _liftHeight = 0.2f;
    [SerializeField] private float _liftDuration = 0.15f;
    [SerializeField] private float _dropDuration = 0.12f;
    [SerializeField] private AnimationCurve _liftCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private AnimationCurve _dropCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Magnetic Snap (XZ spring)")]
    [SerializeField, Tooltip("Oscillation frequency (Hz).")]
    private float _magnetFrequency = 8f;
    [SerializeField, Tooltip("Critical ~1.0, underdamped <1, overdamped >1.")]
    private float _magnetDamping = 0.65f;

    public bool IsPlaced => _isPlaced;
    public byte TypeId => _definition != null ? _definition.TypeId : (byte)0;
    public int Length => _definition != null ? _definition.Length : 0;
    public bool Vertical => _vertical; // true = along grid Z, false = along grid X
    public int RootX => _rootX;
    public int RootY => _rootY;

    [Inject] private ClientPlacementPlanner _planner;

    private bool _isPlaced;
    private bool _vertical;
    private bool _dragging;
    private int _rootX;
    private int _rootY;

    private float _restY;
    private Coroutine _yAnimCo;
    private Vector2 _targetXZ;
    private Vector2 _velXZ;

    private bool _hadPrevPlacement;
    private int _prevRootX, _prevRootY;
    private bool _prevVertical;
    private Vector3 _prevCenterWS;
    private Quaternion _prevYaw;

    private Vector3 _homePosWS;
    private Quaternion _homeYaw;

    private void Awake()
    {
        _restY = transform.position.y;
        _targetXZ = new Vector2(transform.position.x, transform.position.z);
        _homePosWS = transform.position;
        _homeYaw = transform.rotation;
    }

    private void Update()
    {
        if (!_dragging) return;

        float dt = Time.deltaTime;
        float w = Mathf.Max(0.01f, _magnetFrequency) * 2f * Mathf.PI;
        float z = Mathf.Clamp(_magnetDamping, 0f, 5f);

        Vector2 pos = new Vector2(transform.position.x, transform.position.z);
        Vector2 acc = -w * w * (pos - _targetXZ) - 2f * z * w * _velXZ;

        _velXZ += acc * dt;
        pos += _velXZ * dt;

        transform.position = new Vector3(pos.x, transform.position.y, pos.y);
    }

    /// <summary>Begins dragging: frees occupancy, snapshots previous placement, lifts model.</summary>
    public void BeginDrag()
    {
        _hadPrevPlacement = _isPlaced;
        if (_hadPrevPlacement)
        {
            _prevRootX = _rootX;
            _prevRootY = _rootY;
            _prevVertical = _vertical;
            _prevCenterWS = _planner.GetSegmentCenterWorld(_rootX, _rootY, Length, _vertical, transform.position.y);
            _prevYaw = transform.rotation;

            _planner.SetOccupiedRect(_rootX, _rootY, Length, _vertical, false);
            _isPlaced = false;
            OnUnplaced?.Invoke(this);
        }
        else
        {
            _prevCenterWS = _homePosWS;
            _prevYaw = _homeYaw;
        }

        SyncVerticalFromTransform();
        _dragging = true;
        StartLift();
    }

    /// <summary>Moves preview to a clamped root; reports validity and updates spring target.</summary>
    public bool PreviewAt(int rootX, int rootY, bool vertical, Vector3 segmentCenterWorld)
    {
        int gx = rootX;
        int gy = rootY;
        GridMath.ClampRootForLength(ref gx, ref gy, Length, _planner.GridSize, vertical);

        bool fits = _planner.CanPlaceAt(gx, gy, Length, vertical);

        _targetXZ = new Vector2(segmentCenterWorld.x, segmentCenterWorld.z);
        _planner.NotifyPreviewValidity(fits);
        return fits;
    }

    /// <summary>Commits placement at root and orientation; occupies grid and drops model.</summary>
    public void CommitAt(int rootX, int rootY, bool vertical, Vector3 segmentCenterWorld)
    {
        _vertical = vertical;
        _rootX = rootX;
        _rootY = rootY;
        _isPlaced = true;
        _dragging = false;

        transform.position = new Vector3(segmentCenterWorld.x, transform.position.y, segmentCenterWorld.z);

        _planner.SetOccupiedRect(_rootX, _rootY, Length, _vertical, true);
        StartDrop();
        OnPlaced?.Invoke(this);
    }

    /// <summary>Reverts to last valid placement or home pose on invalid drop.</summary>
    public void Unplace()
    {
        _dragging = false;

        if (_hadPrevPlacement)
        {
            _vertical = _prevVertical;
            _rootX = _prevRootX;
            _rootY = _prevRootY;
            _isPlaced = true;

            transform.SetPositionAndRotation(_prevCenterWS, _prevYaw);
            _planner.SetOccupiedRect(_rootX, _rootY, Length, _vertical, true);
            OnPlaced?.Invoke(this);
        }
        else
        {
            _isPlaced = false;
            transform.SetPositionAndRotation(_homePosWS, _homeYaw);
        }

        StartDrop();
    }

    /// <summary>Rotates model 90° and toggles logical orientation.</summary>
    public void RotateQuarter(int direction)
    {
        float angle = (direction >= 0) ? 90f : -90f;
        Vector3 axis = _planner.GridTransform.up;
        transform.rotation = Quaternion.AngleAxis(angle, axis) * transform.rotation;
        _vertical = !_vertical;
    }

    /// <summary>Aligns the logical Vertical flag to the model's current yaw vs grid axes.</summary>
    public void SyncVerticalFromTransform()
    {
        Vector3 f = _planner.GridTransform.InverseTransformDirection(transform.forward);
        f.y = 0f;
        _vertical = Mathf.Abs(f.z) >= Mathf.Abs(f.x);
    }

    private void StartLift()
    {
        if (_yAnimCo != null) StopCoroutine(_yAnimCo);
        _yAnimCo = StartCoroutine(AnimateY(transform.position.y, _restY + _liftHeight, _liftDuration, _liftCurve));
    }

    private void StartDrop()
    {
        if (_yAnimCo != null) StopCoroutine(_yAnimCo);
        _yAnimCo = StartCoroutine(AnimateY(transform.position.y, _restY, _dropDuration, _dropCurve));
    }

    private IEnumerator AnimateY(float fromY, float toY, float duration, AnimationCurve curve)
    {
        duration = Mathf.Max(0.0001f, duration);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float k = curve.Evaluate(Mathf.Clamp01(t));
            Vector3 p = transform.position;
            p.y = Mathf.LerpUnclamped(fromY, toY, k);
            transform.position = p;
            yield return null;
        }
        Vector3 final = transform.position;
        final.y = toY;
        transform.position = final;
        _yAnimCo = null;
    }
}
