using System.Collections.Generic;
using BattleshipsVR.Config;
using BattleshipsVR.Core;
using BattleshipsVR.Net.Data;
using BattleshipsVR.Net.Services;
using Zenject;

/// <summary>
/// Validates player fleet placements and constructs <see cref="BoardService.FleetState"/> instances.
/// Also supports partially specified fleets and randomized filling of missing ships.
/// </summary>
public sealed class BoardValidator
{
    [Inject] private GameSettingsSO _settings;
    [Inject] private GridCodec _codec;

    /// <summary>
    /// Validates and builds a complete fleet from the given placement.
    /// </summary>
    /// <param name="placement">Incoming placement data.</param>
    /// <param name="fleet">Resulting built fleet when validation succeeds.</param>
    /// <returns>True if the fleet is valid and complete; otherwise false.</returns>
    public bool ServerTryBuildFleet(FleetPlacementData placement, out BoardService.FleetState fleet)
    {
        return ServerTryBuildFleetFromPartial(placement, out fleet, out var missing) && missing.Count == 0;
    }

    /// <summary>
    /// Builds a fleet from provided ships only, allowing for missing types.
    /// </summary>
    /// <param name="placement">Incoming placement data (can be partial).</param>
    /// <param name="fleet">Partially built fleet result.</param>
    /// <param name="missingTypeIds">Type IDs that were not provided in <paramref name="placement"/>.</param>
    /// <returns>True if all provided ships are valid and non-overlapping; otherwise false.</returns>
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
                    return false;

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

        foreach (var bt in _settings.BoatTypes)
        {
            if (bt == null) continue;
            if (!seen.Contains(bt.TypeId)) missingTypeIds.Add(bt.TypeId);
        }

        foreach (var s in fleet.ships)
        {
            fleet.combinedMask.x0 |= s.mask.x0;
            fleet.combinedMask.x1 |= s.mask.x1;
            fleet.combinedMask.x2 |= s.mask.x2;
            fleet.combinedMask.x3 |= s.mask.x3;
        }

        return true;
    }

    /// <summary>
    /// Randomly fills any missing boats into the provided fleet without overlap.
    /// </summary>
    /// <param name="fleet">Fleet to add ships into.</param>
    /// <param name="missingTypeIds">Types to place.</param>
    /// <param name="seed">Optional RNG seed (0 = random).</param>
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

                    if (fleet.combinedMask.Get(idx))
                    {
                        overlap = true;
                        break;
                    }
                    shipMask.Set(idx);
                }

                if (!overlap)
                {
                    fleet.ships.Add(new BoardService.ShipState { typeId = tid, mask = shipMask, sunk = false });
                    fleet.combinedMask.Or(in shipMask);
                    placed = true;
                }
            }
        }
    }
}
