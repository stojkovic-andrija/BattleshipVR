using System.Collections.Generic;
using UnityEngine;
using Zenject;
using BattleshipsVR.Config;
using BattleshipsVR.Core;
using BattleshipsVR.Net.Data;

public sealed class ClientPlacementPlanner : MonoBehaviour
{
    public event System.Action OnLocalFleetChanged;
    public event System.Action<bool> OnLocalValidityChanged;

    [SerializeField] private Renderer _gridRenderer;
    [SerializeField] private MeshFilter _gridMesh;
    [SerializeField] private LayerMask _gridLayer;
    [SerializeField] private List<BoatDraggable> _boats = new List<BoatDraggable>();

    public Transform GridTransform => _gridRenderer != null ? _gridRenderer.transform : transform;
    public Renderer GridRenderer => _gridRenderer;
    public LayerMask GridLayer => _gridLayer;
    public int GridSize => _gridSize;

    [Inject] private GridCodec _codec;
    [Inject] private GameSettingsSO _settings;

    private bool[] _occupied;
    private int _gridSize = 10;
    private Bounds _localBounds;
    private MaterialPropertyBlock _mpb;

    private void Awake()
    {
        _gridSize = _codec.GridSize;
        _occupied = new bool[_gridSize * _gridSize];

        _localBounds = (_gridMesh != null && _gridMesh.sharedMesh != null)
            ? _gridMesh.sharedMesh.bounds
            : new Bounds(Vector3.zero, new Vector3(1f, 0f, 1f));

        if (_gridRenderer != null)
        {
            _mpb = new MaterialPropertyBlock();
            _gridRenderer.GetPropertyBlock(_mpb);
            _mpb.SetVector("_GridScale", new Vector4(_gridSize, _gridSize, 0f, 0f));
            _mpb.SetVector("_BoardMin",  new Vector4(_localBounds.min.x, 0f, _localBounds.min.z, 0f));
            _mpb.SetVector("_BoardSize", new Vector4(_localBounds.size.x, 0f, _localBounds.size.z, 0f));
            _gridRenderer.SetPropertyBlock(_mpb);
        }
    }

    public void SetOccupiedRect(int rootX, int rootY, int length, bool vertical, bool occupied)
    {
        for (int i = 0; i < length; i++)
        {
            int cx = vertical ? rootX : rootX + i;
            int cy = vertical ? rootY + i : rootY;
            int idx = cy * _gridSize + cx;
            _occupied[idx] = occupied;
        }
        OnLocalFleetChanged?.Invoke();
    }

    public bool CanPlaceAt(int rootX, int rootY, int length, bool vertical)
    {
        if (vertical && (rootY + length) > _gridSize) return false;
        if (!vertical && (rootX + length) > _gridSize) return false;

        for (int i = 0; i < length; i++)
        {
            int cx = vertical ? rootX : rootX + i;
            int cy = vertical ? rootY + i : rootY;
            int idx = cy * _gridSize + cx;
            if (_occupied[idx]) return false;
        }
        return true;
    }

    public bool TryBuildFleetData(out FleetPlacementData data)
    {
        data = default;
        if (_boats.Count != _settings.BoatTypes.Length) return false;

        var ships = new List<ShipPlacementData>(_boats.Count);
        for (int i = 0; i < _boats.Count; i++)
        {
            BoatDraggable b = _boats[i];
            if (!b.IsPlaced) return false;
            byte packed = _codec.Pack((byte)b.RootX, (byte)b.RootY);
            ships.Add(new ShipPlacementData { typeId = b.TypeId, rootCell = packed, vertical = b.Vertical });
        }

        data = new FleetPlacementData { ships = ships.ToArray() };
        return true;
    }

    /// <summary>Serialize only the ships that are currently placed.</summary>
    public bool TryBuildPartialFleetData(out FleetPlacementData data)
    {
        data = default;
        var ships = new List<ShipPlacementData>(_boats.Count);
        for (int i = 0; i < _boats.Count; i++)
        {
            BoatDraggable b = _boats[i];
            if (!b.IsPlaced) continue;
            byte packed = _codec.Pack((byte)b.RootX, (byte)b.RootY);
            ships.Add(new ShipPlacementData { typeId = b.TypeId, rootCell = packed, vertical = b.Vertical });
        }

        data = new FleetPlacementData { ships = ships.ToArray() };
        return ships.Count > 0;
    }

    public bool TryGetGridCellFromRay(Ray ray, out int gx, out int gy, out Vector3 hitPointWS)
    {
        gx = 0; gy = 0; hitPointWS = default;
        if (!Physics.Raycast(ray, out var hit, 1000f, _gridLayer, QueryTriggerInteraction.Ignore))
            return false;

        hitPointWS = hit.point;

        Vector3 local = GridTransform.InverseTransformPoint(hit.point);
        float u = Mathf.InverseLerp(_localBounds.min.x, _localBounds.max.x, local.x);
        float v = Mathf.InverseLerp(_localBounds.min.z, _localBounds.max.z, local.z);

        gx = Mathf.Clamp(Mathf.FloorToInt(u * _gridSize), 0, _gridSize - 1);
        gy = Mathf.Clamp(Mathf.FloorToInt(v * _gridSize), 0, _gridSize - 1);
        return true;
    }

    public Vector3 GetSegmentCenterWorld(int rootX, int rootY, int length, bool vertical, float keepY)
    {
        float centerU = vertical ? (rootX + 0.5f) : (rootX + (length * 0.5f));
        float centerV = vertical ? (rootY + (length * 0.5f)) : (rootY + 0.5f);
        float u = centerU / _gridSize;
        float v = centerV / _gridSize;

        float lx = Mathf.Lerp(_localBounds.min.x, _localBounds.max.x, u);
        float lz = Mathf.Lerp(_localBounds.min.z, _localBounds.max.z, v);

        Vector3 world = GridTransform.TransformPoint(new Vector3(lx, 0f, lz));
        world.y = keepY;
        return world;
    }

    public void BuildPreviewIndexLists(int rootX, int rootY, int length, bool vertical, List<int> greenOut, List<int> redOut)
    {
        greenOut.Clear();
        redOut.Clear();
        int Nx = _gridSize;

        for (int i = 0; i < length; i++)
        {
            int cx = vertical ? rootX : rootX + i;
            int cy = vertical ? rootY + i : rootY;

            if (cx < 0 || cy < 0 || cx >= _gridSize || cy >= _gridSize)
            {
                int ccx = Mathf.Clamp(cx, 0, _gridSize - 1);
                int ccy = Mathf.Clamp(cy, 0, _gridSize - 1);
                int oobIdx = ccy * Nx + ccx;
                if (!redOut.Contains(oobIdx)) redOut.Add(oobIdx);
                continue;
            }

            int idx = cy * Nx + cx;
            if (!greenOut.Contains(idx)) greenOut.Add(idx);
            if (_occupied[idx] && !redOut.Contains(idx)) redOut.Add(idx);
        }
    }

    public void NotifyPreviewValidity(bool valid) => OnLocalValidityChanged?.Invoke(valid);
}
