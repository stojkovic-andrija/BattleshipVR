using System.Collections.Generic;
using FishNet.Connection;
using BattleshipsVR.Core;
using UnityEngine;

namespace BattleshipsVR.Net.Services
{
    /// <summary>Authoritative board/fleet state: placements, hit tracking, and sink checks.</summary>
    public sealed class BoardService
    {
        public enum ShipOrientation : byte
        {
            Horizontal,
            Vertical
        }

        public sealed class ShipState
        {
            public byte typeId;
            public BitBoard256 mask;
            public bool sunk;
            public ShipOrientation orientation;
            public Vector3 center; // Grid center for visualizer placement
        }

        public sealed class FleetState
        {
            public readonly List<ShipState> ships = new();
            public BitBoard256 combinedMask;
        }

        /// <summary>Raised when a player commits their fleet (server-side).</summary>
        public event System.Action<NetworkConnection> OnFleetCommitted;
        /// <summary>Raised when a ship is sunk; provides defender id, type, orientation, and grid center.</summary>
        public event System.Action<int, byte, ShipOrientation, Vector3> OnShipSunk;

        private readonly Dictionary<NetworkConnection, FleetState> _fleets = new();
        private readonly Dictionary<NetworkConnection, BitBoard256> _hitsByDefender = new();

        /// <summary>Notifies listeners that a ship has sunk for a given defender.</summary>
        public void ClientRaiseShipSunk(int defenderClientId, byte typeId, ShipOrientation orientation, Vector3 center)
        {
            OnShipSunk?.Invoke(defenderClientId, typeId, orientation, center);
        }

        /// <summary>Registers a player's fleet and derives per-ship orientation and centers.</summary>
        public void ServerSetFleet(NetworkConnection player, FleetState fleet)
        {
            for (int i = 0; i < fleet.ships.Count; i++)
                ExtractShipOrientationAndCenter(fleet.ships[i]);

            _fleets[player] = fleet;
            _hitsByDefender[player] = default;
            OnFleetCommitted?.Invoke(player);
        }

        /// <summary>Resolves a shot against defender; returns hit/sink data and sunk ship info if any.</summary>
        public bool ServerTryResolveShot(NetworkConnection attacker, NetworkConnection defender, int cellIndex,
            out bool isHit, out bool isSunk, out byte sunkTypeId,
            out ShipOrientation sunkOrientation, out Vector3 sunkCenter)
        {
            isHit = false; isSunk = false; sunkTypeId = 255; sunkOrientation = ShipOrientation.Horizontal; sunkCenter = default;
            if (!_fleets.TryGetValue(defender, out FleetState defFleet)) return false;

            isHit = defFleet.combinedMask.Get(cellIndex);
            if (!_hitsByDefender.TryGetValue(defender, out BitBoard256 hits)) hits = default;

            if (isHit)
            {
                if (cellIndex < 64) hits.x0 |= 1UL << cellIndex;
                else if (cellIndex < 128) hits.x1 |= 1UL << (cellIndex - 64);
                else if (cellIndex < 192) hits.x2 |= 1UL << (cellIndex - 128);
                else hits.x3 |= 1UL << (cellIndex - 192);

                _hitsByDefender[defender] = hits;

                for (int i = 0; i < defFleet.ships.Count; i++)
                {
                    ShipState s = defFleet.ships[i];
                    if (!s.mask.Get(cellIndex)) continue;

                    if (!s.sunk && IsSubset(s.mask, hits))
                    {
                        s.sunk = true;
                        isSunk = true;
                        sunkTypeId = s.typeId;
                        sunkOrientation = s.orientation;
                        sunkCenter = s.center;
                    }
                    break;
                }
            }

            return true;
        }

        /// <summary>Gets the defender's full fleet mask for reveal effects.</summary>
        public BitBoard256 ServerGetReveal(NetworkConnection player)
        {
            return _fleets.TryGetValue(player, out FleetState f) ? f.combinedMask : default;
        }

        /// <summary>Checks if all ships in the defender's fleet are sunk.</summary>
        public bool ServerIsFleetDefeated(NetworkConnection player)
        {
            if (!_fleets.TryGetValue(player, out FleetState f))
                return false;

            for (int i = 0; i < f.ships.Count; i++)
                if (!f.ships[i].sunk)
                    return false;

            return true;
        }

        /// <summary>DEBUG: Forces sink of a specific ship type for a defender (marks hits and sunk).</summary>
        public void ServerForceSinkShip(NetworkConnection defender, byte typeId)
        {
            if (!_fleets.TryGetValue(defender, out FleetState f))
                return;

            if (!_hitsByDefender.TryGetValue(defender, out BitBoard256 hits))
                hits = default;

            for (int i = 0; i < f.ships.Count; i++)
            {
                ShipState s = f.ships[i];
                if (s.typeId != typeId)
                    continue;

                hits.x0 |= s.mask.x0;
                hits.x1 |= s.mask.x1;
                hits.x2 |= s.mask.x2;
                hits.x3 |= s.mask.x3;

                s.sunk = true;
                break;
            }

            _hitsByDefender[defender] = hits;
        }

        /// <summary>DEBUG: Forces sink of all ships for a defender.</summary>
        public void ServerForceSinkAll(NetworkConnection defender)
        {
            if (!_fleets.TryGetValue(defender, out FleetState f))
                return;

            _hitsByDefender[defender] = f.combinedMask;

            for (int i = 0; i < f.ships.Count; i++)
                f.ships[i].sunk = true;
        }

        private bool IsSubset(BitBoard256 a, BitBoard256 b)
        {
            if ((a.x0 & ~b.x0) != 0) return false;
            if ((a.x1 & ~b.x1) != 0) return false;
            if ((a.x2 & ~b.x2) != 0) return false;
            if ((a.x3 & ~b.x3) != 0) return false;
            return true;
        }

        /// <summary>Derives ship orientation and grid center from its mask.</summary>
        private void ExtractShipOrientationAndCenter(ShipState ship)
        {
            const int GRID_SIZE = 10;
            List<Vector2Int> cells = new List<Vector2Int>();

            for (int i = 0; i < 100; i++)
            {
                if (ship.mask.Get(i))
                {
                    int x = i % GRID_SIZE;
                    int y = i / GRID_SIZE;
                    cells.Add(new Vector2Int(x, y));
                }
            }

            if (cells.Count == 0)
            {
                ship.orientation = ShipOrientation.Horizontal;
                ship.center = Vector3.zero;
                return;
            }

            bool horizontal = true;
            for (int i = 1; i < cells.Count; i++)
            {
                if (cells[i].x != cells[0].x)
                {
                    horizontal = true;
                    break;
                }
                if (cells[i].y != cells[0].y)
                {
                    horizontal = false;
                    break;
                }
            }
            ship.orientation = horizontal ? ShipOrientation.Horizontal : ShipOrientation.Vertical;

            float minX = cells[0].x;
            float maxX = cells[0].x;
            float minY = cells[0].y;
            float maxY = cells[0].y;

            for (int i = 1; i < cells.Count; i++)
            {
                if (cells[i].x < minX) minX = cells[i].x;
                if (cells[i].x > maxX) maxX = cells[i].x;
                if (cells[i].y < minY) minY = cells[i].y;
                if (cells[i].y > maxY) maxY = cells[i].y;
            }

            float centerX = (minX + maxX) * 0.5f;
            float centerY = (minY + maxY) * 0.5f;

            ship.center = new Vector3(centerX, 0f, centerY);
        }
    }
}
