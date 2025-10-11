using System.Collections.Generic;
using BattleshipsVR.Config;
using BattleshipsVR.Core;
using BattleshipsVR.Net.Data;
using Zenject;

namespace BattleshipsVR.Net.Services
{
    /// <summary>Rebuilds per ship masks on server from compact placement intent</summary>
    public sealed class BoardValidator
    {
        [Inject] private GameSettingsSO _settings;
        [Inject] private GridCodec _codec;

        /// <summary>Validates bounds and overlap and returns authoritative fleet masks</summary>
        public bool ServerTryBuildFleet(FleetPlacementData placement, out BoardService.FleetState fleet)
        {
            fleet = new BoardService.FleetState();

            var types = new Dictionary<byte, BoatTypeSO>();
            var seen = new HashSet<byte>();
            foreach (var bt in _settings.BoatTypes)
                if (bt != null) types[bt.TypeId] = bt;

            if (placement.ships == null || placement.ships.Length != _settings.BoatTypes.Length)
                return false;

            BitBoard256 used = default;

            foreach (var sp in placement.ships)
            {
                if (!types.TryGetValue(sp.typeId, out BoatTypeSO def))
                    return false;

                if (!seen.Add(sp.typeId))
                    return false; // duplicate type

                BitBoard256 shipMask = default;

                _codec.Unpack(sp.rootCell, out int x, out int y);
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

            // OR all ships once for fast hit tests
            foreach (var s in fleet.ships)
            {
                fleet.combinedMask.x0 |= s.mask.x0;
                fleet.combinedMask.x1 |= s.mask.x1;
                fleet.combinedMask.x2 |= s.mask.x2;
                fleet.combinedMask.x3 |= s.mask.x3;
            }

            return true;
        }
    }
}
