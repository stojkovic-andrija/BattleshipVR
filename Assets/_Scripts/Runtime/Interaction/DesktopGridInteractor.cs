using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Zenject;

public sealed class DesktopGridInteractor : MonoBehaviour
{
    [SerializeField] private Camera _camera;
    [Inject] private ClientPlacementPlanner _planner;

    private InputActionMap _map;
    private InputAction _point, _click, _rotateCW, _rotateCCW;

    private IGridDraggable _active;
    private bool _previewVertical;
    private bool _hasValidPreview;
    private int _previewRootX, _previewRootY;

    private readonly List<int> _greens = new List<int>(16);
    private readonly List<int> _reds = new List<int>(16);
    private readonly float[] _greenFloats = new float[16];
    private readonly float[] _redFloats = new float[16];
    private MaterialPropertyBlock _mpb;

    private void Awake()
    {
        _map = new InputActionMap("Placement");
        _point     = _map.AddAction("Point",       InputActionType.Value,  "<Mouse>/position");
        _click     = _map.AddAction("Click",       InputActionType.Button, "<Mouse>/leftButton");
        _rotateCW  = _map.AddAction("RotateCW",    InputActionType.Button, "<Keyboard>/e");
        _rotateCCW = _map.AddAction("RotateCCW",   InputActionType.Button, "<Keyboard>/q");

        _click.performed += OnClickPerformed;
        _click.canceled  += OnClickCanceled;
        _rotateCW.performed += OnRotateCW;
        _rotateCCW.performed += OnRotateCCW;

        _map.Enable();
        _mpb = new MaterialPropertyBlock();

        var r = _planner.GridRenderer;
        r.GetPropertyBlock(_mpb);
        _mpb.SetVector("_GridScale", new Vector4(_planner.GridSize, _planner.GridSize, 0f, 0f));
        r.SetPropertyBlock(_mpb);
    }

    private void OnDestroy()
    {
        _click.performed -= OnClickPerformed;
        _click.canceled  -= OnClickCanceled;
        _rotateCW.performed -= OnRotateCW;
        _rotateCCW.performed -= OnRotateCCW;
        _map.Disable();
    }

    private void Update()
    {
        if (_active != null)
        {
            Ray ray = _camera.ScreenPointToRay(_point.ReadValue<Vector2>());
            if (_planner.TryGetGridCellFromRay(ray, out int gx, out int gy, out _))
            {
                (int rx, int ry) = ComputeCenteredRoot(gx, gy, _active.Length, _previewVertical, _planner.GridSize);
                _previewRootX = rx;
                _previewRootY = ry;

                Vector3 segCenter = _planner.GetSegmentCenterWorld(_previewRootX, _previewRootY, _active.Length, _previewVertical, GetActiveY());
                _hasValidPreview = _active.PreviewAt(_previewRootX, _previewRootY, _previewVertical, segCenter);

                _planner.BuildPreviewIndexLists(_previewRootX, _previewRootY, _active.Length, _previewVertical, _greens, _reds);
                PushOverlayArrays(_planner.GridRenderer, _greens, _reds);
            }
        }
        else
        {
            if (Mouse.current.leftButton.wasPressedThisFrame)
                TryPickUnderCursor();
            ClearOverlayArrays(_planner.GridRenderer);
        }
    }

    private void OnClickPerformed(InputAction.CallbackContext ctx)
    {
        if (_active == null)
            TryPickUnderCursor();
    }

    private void OnClickCanceled(InputAction.CallbackContext ctx)
    {
        if (_active == null) return;

        Vector3 segCenter = _planner.GetSegmentCenterWorld(_previewRootX, _previewRootY, _active.Length, _previewVertical, GetActiveY());

        if (_hasValidPreview)
            _active.CommitAt(_previewRootX, _previewRootY, _previewVertical, segCenter);
        else
            _active.Unplace();

        _active = null;
        ClearOverlayArrays(_planner.GridRenderer);
    }

    private void OnRotateCW(InputAction.CallbackContext ctx)
    {
        if (_active == null) return;
        _previewVertical = !_previewVertical;
        if (_active is BoatDraggable b) b.RotateQuarter(+1);
    }

    private void OnRotateCCW(InputAction.CallbackContext ctx)
    {
        if (_active == null) return;
        _previewVertical = !_previewVertical;
        if (_active is BoatDraggable b) b.RotateQuarter(-1);
    }

    private bool TryPickUnderCursor()
    {
        Ray ray = _camera.ScreenPointToRay(_point.ReadValue<Vector2>());
        if (Physics.Raycast(ray, out var hit, 1000f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.TryGetComponent<IGridDraggable>(out var drag))
            {
                _active = drag;

                if (_active is BoatDraggable b)
                {
                    b.SyncVerticalFromTransform();       // read actual yaw relative to grid
                    _previewVertical = b.Vertical;       // use that for highlight immediately
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

    private float GetActiveY()
    {
        if (_active is BoatDraggable b) return b.transform.position.y;
        return _planner.GridTransform.position.y;
    }

    private static (int rx, int ry) ComputeCenteredRoot(int hitX, int hitY, int length, bool vertical, int gridSize)
    {
        int offset = (length % 2 == 0) ? (length / 2 - 1) : (length / 2);
        int rx = vertical ? hitX : (hitX - offset);
        int ry = vertical ? (hitY - offset) : hitY;
        GridMath.ClampRootForLength(ref rx, ref ry, length, gridSize, vertical);
        return (rx, ry);
    }

    private void PushOverlayArrays(Renderer target, List<int> greens, List<int> reds)
    {
        if (target == null) return;

        int gCount = Mathf.Min(greens.Count, 16);
        int rCount = Mathf.Min(reds.Count, 16);

        for (int i = 0; i < gCount; i++) _greenFloats[i] = greens[i];
        for (int i = gCount; i < 16; i++) _greenFloats[i] = 0f;
        for (int i = 0; i < rCount; i++) _redFloats[i] = reds[i];
        for (int i = rCount; i < 16; i++) _redFloats[i] = 0f;

        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        target.GetPropertyBlock(_mpb);
        _mpb.SetInt("_GreenCount", gCount);
        _mpb.SetInt("_RedCount", rCount);
        _mpb.SetFloatArray("_GreenIdx", _greenFloats);
        _mpb.SetFloatArray("_RedIdx", _redFloats);
        target.SetPropertyBlock(_mpb);
    }

    private void ClearOverlayArrays(Renderer target)
    {
        if (target == null) return;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        target.GetPropertyBlock(_mpb);
        _mpb.SetInt("_GreenCount", 0);
        _mpb.SetInt("_RedCount", 0);
        target.SetPropertyBlock(_mpb);
    }
}
