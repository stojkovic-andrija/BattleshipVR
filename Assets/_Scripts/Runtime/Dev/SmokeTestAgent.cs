using UnityEngine;
using Zenject;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using BattleshipsVR.Config;
using BattleshipsVR.Core;
using BattleshipsVR.Net.Services;
using BattleshipsVR.Net.Data;

namespace BattleshipsVR.Dev
{
    /// <summary>Autoplaces a valid fleet and fires scanline shots at a fixed interval so we can verify the whole skeleton without input</summary>
    public sealed class SmokeTestAgent : MonoBehaviour
    {
        // Serialized inspector fields
        [SerializeField, Tooltip("Seconds between shot attempts per client")]
        private float _shotIntervalSeconds = 1.25f;
        [SerializeField, Tooltip("Enable auto placement when PLACEMENT begins")]
        private bool _autoPlace = true;
        [SerializeField, Tooltip("Enable auto fire loop after placement")]
        private bool _autoFire = true;

        // Public vars

        // Protected vars

        // Private vars
        [Inject] private GameSettingsSO _settings;
        [Inject] private GridCodec _codec;
        [Inject] private PlacementService _placement;
        [Inject] private TurnService _turns;
        [Inject(Optional = true)] private GameStateService _gameState;

        private bool _placementSent;
        private int _nextShotIndex;
        private BitBoard256 _firedMask;
        private bool _running;

        // Unity event methods
        private void OnEnable()
        {
            // optional in case you want to gate auto placement by state change later
            if (_gameState != null) _gameState.OnGameStateChanged += HandleStateChanged;
        }

        private void OnDisable()
        {
            if (_gameState != null) _gameState.OnGameStateChanged -= HandleStateChanged;
        }

        private void Start()
        {
            StartAsync().Forget();
        }

        // Public methods

        // Private methods
        private async UniTaskVoid StartAsync()
        {
            await UniTask.Delay(750); // let FishNet spawn scene objects

            if (_autoPlace)
                TrySendPlacement();

            if (_autoFire)
            {
                _running = true;
                FireLoopAsync().Forget();
            }
        }

        private void HandleStateChanged(Net.Data.GameState state)
        {
            if (state == Net.Data.GameState.PLACEMENT && _autoPlace)
                TrySendPlacement();
        }

        private void TrySendPlacement()
        {
            if (_placementSent || _settings == null || _settings.BoatTypes == null || _settings.BoatTypes.Length == 0)
                return;

            int grid = _settings.GridSize;
            bool[] used = new bool[grid * grid];
            var ships = new List<ShipPlacementData>(_settings.BoatTypes.Length);

            // simple search: try horizontal first rows then vertical fallback
            for (int i = 0; i < _settings.BoatTypes.Length; i++)
            {
                var bt = _settings.BoatTypes[i];
                if (bt == null || bt.Length <= 0) continue;

                if (!FindPlacement(bt.Length, grid, used, out byte rootCell, out bool vertical))
                {
                    AppLogger.Warn($"SmokeTest failed to place boat len {bt.Length}, shrinking layout");
                    // make a last-ditch place at 0,0 vertical inside grid to avoid stalling the test
                    rootCell = _codec.Pack(0, 0);
                    vertical = true;
                }

                ships.Add(new ShipPlacementData
                {
                    typeId = bt.TypeId,
                    rootCell = rootCell,
                    vertical = vertical
                });
            }

            var fleet = new FleetPlacementData { ships = ships.ToArray() };
            _placement.ClientSubmitPlacementServerRpc(fleet, default); // server validates, we keep it lean
            _placementSent = true;
            AppLogger.Info("SmokeTest sent placement");
        }

        private bool FindPlacement(int length, int grid, bool[] used, out byte rootCell, out bool vertical)
        {
            // scan rows for horizontal fit with 1 column spacing, then try vertical
            for (int y = 0; y < grid; y += 2)
            {
                for (int x = 0; x + length <= grid; x++)
                {
                    if (CanLay(x, y, length, grid, used, false))
                    {
                        Mark(x, y, length, grid, used, false);
                        rootCell = _codec.Pack(x, y);
                        vertical = false;
                        return true;
                    }
                }
            }

            for (int x = 0; x < grid; x += 2)
            {
                for (int y = 0; y + length <= grid; y++)
                {
                    if (CanLay(x, y, length, grid, used, true))
                    {
                        Mark(x, y, length, grid, used, true);
                        rootCell = _codec.Pack(x, y);
                        vertical = true;
                        return true;
                    }
                }
            }

            rootCell = _codec.Pack(0, 0);
            vertical = true;
            return false;
        }

        private bool CanLay(int x, int y, int length, int grid, bool[] used, bool vertical)
        {
            for (int i = 0; i < length; i++)
            {
                int cx = vertical ? x : x + i;
                int cy = vertical ? y + i : y;
                int idx = cy * grid + cx;
                if (used[idx]) return false;
            }
            return true;
        }

        private void Mark(int x, int y, int length, int grid, bool[] used, bool vertical)
        {
            for (int i = 0; i < length; i++)
            {
                int cx = vertical ? x : x + i;
                int cy = vertical ? y + i : y;
                int idx = cy * grid + cx;
                used[idx] = true;
            }
        }

        private async UniTaskVoid FireLoopAsync()
        {
            int grid = _settings.GridSize;
            int max = grid * grid;

            while (_running)
            {
                await UniTask.Delay((int)(_shotIntervalSeconds * 1000f));

                // try next cell in scan order, skip ones we've already fired
                for (int safety = 0; safety < max; safety++)
                {
                    int idx = _nextShotIndex % max;
                    _nextShotIndex++;

                    if (GetBit(_firedMask, idx)) continue;

                    SetBit(ref _firedMask, idx);

                    int x = idx % grid;
                    int y = idx / grid;
                    byte cell = _codec.Pack(x, y);

                    _turns.ClientRequestShotServerRpc(cell, default); // server will drop if it's not our turn
                    AppLogger.Info($"SmokeTest shot at {x},{y}");
                    break;
                }
            }
        }

        private static bool GetBit(BitBoard256 bb, int index)
        {
            if (index < 64) return (bb.x0 & (1UL << index)) != 0;
            if (index < 128) return (bb.x1 & (1UL << (index - 64))) != 0;
            if (index < 192) return (bb.x2 & (1UL << (index - 128))) != 0;
            return (bb.x3 & (1UL << (index - 192))) != 0;
        }

        private static void SetBit(ref BitBoard256 bb, int index)
        {
            if (index < 64) bb.x0 |= 1UL << index;
            else if (index < 128) bb.x1 |= 1UL << (index - 64);
            else if (index < 192) bb.x2 |= 1UL << (index - 128);
            else bb.x3 |= 1UL << (index - 192);
        }
    }
}
