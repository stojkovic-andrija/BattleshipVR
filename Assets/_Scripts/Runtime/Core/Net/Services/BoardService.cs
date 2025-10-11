using System.Collections.Generic;
using FishNet.Connection;
using BattleshipsVR.Core;

namespace BattleshipsVR.Net.Services
{
    /// <summary>Server truth for fleets, per ship masks, and defeat checks</summary>
    public sealed class BoardService
    {
        public event System.Action<NetworkConnection> OnFleetCommitted;

        public sealed class ShipState
        {
            public byte typeId;
            public BitBoard256 mask;
            public bool sunk;
        }

        public sealed class FleetState
        {
            public readonly System.Collections.Generic.List<ShipState> ships = new();
            public BitBoard256 combinedMask; // OR of all ship masks for quick hit checks
        }

        private readonly Dictionary<NetworkConnection, FleetState> _fleets = new();
        private readonly Dictionary<NetworkConnection, BitBoard256> _shotsByAttacker = new();

        /// <summary>Registers the validated fleet for a player</summary>
        public void ServerSetFleet(NetworkConnection player, FleetState fleet)
        {
            _fleets[player] = fleet;
            OnFleetCommitted?.Invoke(player);
        }

        /// <summary>Applies a shot and reports hit and sunk info</summary>
        public bool ServerTryResolveShot(NetworkConnection attacker, NetworkConnection defender, int cellIndex, out bool isHit, out bool isSunk, out byte sunkTypeId)
        {
            isHit = false; isSunk = false; sunkTypeId = 255;

            if (!_fleets.TryGetValue(defender, out FleetState defFleet))
                return false;

            isHit = defFleet.combinedMask.Get(cellIndex);

            if (!_shotsByAttacker.TryGetValue(attacker, out BitBoard256 shots))
                shots = default;

            // write bit for this shot
            if (cellIndex < 64) shots.x0 |= 1UL << cellIndex;
            else if (cellIndex < 128) shots.x1 |= 1UL << (cellIndex - 64);
            else if (cellIndex < 192) shots.x2 |= 1UL << (cellIndex - 128);
            else shots.x3 |= 1UL << (cellIndex - 192);

            _shotsByAttacker[attacker] = shots;

            if (isHit)
            {
                // find which ship contained the cell and check if fully covered by shots
                for (int i = 0; i < defFleet.ships.Count; i++)
                {
                    ShipState s = defFleet.ships[i];
                    if (!s.mask.Get(cellIndex)) continue;
                    if (!s.sunk && IsSubset(s.mask, shots))
                    {
                        s.sunk = true;
                        isSunk = true;
                        sunkTypeId = s.typeId;
                    }
                    break;
                }
            }

            return true;
        }

        /// <summary>Returns combined mask for reveal visuals</summary>
        public BitBoard256 ServerGetReveal(NetworkConnection player)
        {
            return _fleets.TryGetValue(player, out FleetState f) ? f.combinedMask : default;
        }

        /// <summary>True when all ships are sunk</summary>
        public bool ServerIsFleetDefeated(NetworkConnection player)
        {
            if (!_fleets.TryGetValue(player, out FleetState f)) return false;
            for (int i = 0; i < f.ships.Count; i++)
                if (!f.ships[i].sunk) return false;
            return true;
        }

        private bool IsSubset(BitBoard256 a, BitBoard256 b)
        {
            if ((a.x0 & ~b.x0) != 0) return false;
            if ((a.x1 & ~b.x1) != 0) return false;
            if ((a.x2 & ~b.x2) != 0) return false;
            if ((a.x3 & ~b.x3) != 0) return false;
            return true;
        }
    }
}
