using System.Collections.Generic;
using BattleshipsVR.Config;
using BattleshipsVR.Core;
using BattleshipsVR.Net.Data;
using BattleshipsVR.Net.Services;
using Zenject;

public sealed class BoardValidator
{
    [Inject] private GameSettingsSO _settings;
    [Inject] private GridCodec _codec;

    public bool ServerTryBuildFleet(FleetPlacementData placement, out BoardService.FleetState fleet)
    {
        // original full-validate remains (omitted here for brevity)
        return ServerTryBuildFleetFromPartial(placement, out fleet, out var missing) && missing.Count == 0;
    }

    /// <summary>Builds fleet from provided ships, returns which typeIds are missing.</summary>
    public bool ServerTryBuildFleetFromPartial(FleetPlacementData placement, out BoardService.FleetState fleet, out List<byte> missingTypeIds)
    {
        fleet = new BoardService.FleetState();
        missingTypeIds = new List<byte>();

        var types = new Dictionary<byte, BoatTypeSO>();
        var seen = new HashSet<byte>();
        foreach (var bt in _settings.BoatTypes)
            if (bt != null) types[bt.TypeId] = bt;

        BitBoard256 used = default;

        if (placement.ships != null)
        {
            foreach (var sp in placement.ships)
            {
                if (!types.TryGetValue(sp.typeId, out BoatTypeSO def))
                    return false;

                if (!seen.Add(sp.typeId))
                    return false; // duplicate type

                _codec.Unpack(sp.rootCell, out int x, out int y);

                BitBoard256 shipMask = default;
                for (int i = 0; i < def.Length; i++)
                {
                    int cx = sp.vertical ? x : x + i;
                    int cy = sp.vertical ? y + i : y;

                    if (cx < 0 || cy < 0 || cx >= _codec.GridSize || cy >= _codec.GridSize)
                        return false;

                    int idx = cy * _codec.GridSize + cx;
                    if (used.Get(idx))
                        return false;

                    shipMask.Set(idx);
                    used.Set(idx);
                }

                fleet.ships.Add(new BoardService.ShipState { typeId = sp.typeId, mask = shipMask, sunk = false });
            }
        }

        // compute missing types
        foreach (var bt in _settings.BoatTypes)
        {
            if (bt == null) continue;
            if (!seen.Contains(bt.TypeId)) missingTypeIds.Add(bt.TypeId);
        }

        // combined mask
        foreach (var s in fleet.ships)
        {
            fleet.combinedMask.x0 |= s.mask.x0;
            fleet.combinedMask.x1 |= s.mask.x1;
            fleet.combinedMask.x2 |= s.mask.x2;
            fleet.combinedMask.x3 |= s.mask.x3;
        }

        return true;
    }

    /// <summary>Randomly fills the missing boats without overlap.</summary>
    public void ServerFillRandomShips(ref BoardService.FleetState fleet, List<byte> missingTypeIds, int seed = 0)
    {
        var types = new Dictionary<byte, BoatTypeSO>();
        foreach (var bt in _settings.BoatTypes)
            if (bt != null) types[bt.TypeId] = bt;

        var rng = (seed == 0) ? new System.Random() : new System.Random(seed);

        foreach (byte tid in missingTypeIds)
        {
            if (!types.TryGetValue(tid, out BoatTypeSO def)) continue;

            bool placed = false;
            for (int attempt = 0; attempt < 512 && !placed; attempt++)
            {
                bool vertical = rng.Next(0, 2) == 0;
                int maxX = vertical ? _codec.GridSize - 1 : _codec.GridSize - def.Length;
                int maxY = vertical ? _codec.GridSize - def.Length : _codec.GridSize - 1;
                int x = rng.Next(0, maxX + 1);
                int y = rng.Next(0, maxY + 1);

                BitBoard256 shipMask = default;
                bool overlap = false;

                for (int i = 0; i < def.Length; i++)
                {
                    int cx = vertical ? x : x + i;
                    int cy = vertical ? y + i : y;
                    int idx = cy * _codec.GridSize + cx;

                    if ((fleet.combinedMask.Get(idx)))
                    {
                        overlap = true;
                        break;
                    }
                    shipMask.Set(idx);
                }

                if (!overlap)
                {
                    // add to fleet and OR into combined
                    fleet.ships.Add(new BoardService.ShipState { typeId = tid, mask = shipMask, sunk = false });

                    fleet.combinedMask.Or(in shipMask);
                    placed = true;
                }
            }
        }
    }
}
