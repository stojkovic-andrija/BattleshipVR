using UnityEngine;
using Zenject;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using BattleshipsVR.Config;
using BattleshipsVR.Core;
using BattleshipsVR.Net.Data;
using BattleshipsVR.Net.Services;

namespace BattleshipsVR.Dev
{
    /// <summary>Places fleet, waits for your turn, fires one shot, repeats until END</summary>
    public sealed class AutoPlayerDriver : MonoBehaviour
    {
        [SerializeField] private float _thinkSeconds = 0.35f;
        [SerializeField] private int _placementJitterMs = 120;

        [Inject] private GameSettingsSO _settings;
        [Inject] private GridCodec _codec;
        [Inject] private PlayerRegistryService _roster;
        [Inject] private PlacementService _placement;
        [Inject] private TurnService _turns;
        [Inject] private GameStateService _gameState;

        private bool _placed;
        private bool _ended;
        private BitBoard256 _fired;
        private int _scanIdx;

        // Unity event methods
        private void OnEnable()
        {
            _gameState.OnClientGameStateChanged += HandleState;
            _gameState.OnLocalTurnStarted += HandleLocalTurn;
        }

        private void OnDisable()
        {
            _gameState.OnClientGameStateChanged -= HandleState;
            _gameState.OnLocalTurnStarted -= HandleLocalTurn;
        }

        private void Start()
        {
            if (_roster.HasBothPlayers && !_placed)
                TryPlaceAsync().Forget();
        }


        private void HandleState(GameState s)
        {
            if (s == GameState.PLACEMENT && _roster.HasBothPlayers && !_placed)
                TryPlaceAsync().Forget();
            if (s == GameState.END) _ended = true;
        }

        private void HandleLocalTurn(int seconds)
        {
            if (_ended) return;
            FireOnTurnAsync(seconds).Forget();
        }

        private async UniTaskVoid TryPlaceAsync()
        {
            int jitter = Mathf.Max(0, _placementJitterMs);
            if (jitter > 0) await UniTask.Delay(Random.Range(0, jitter)); // one of us will finish first

            if (_placed) return;

            var boats = _settings.BoatTypes;
            if (boats == null || boats.Length == 0) return;

            int g = _settings.GridSize;
            bool[] used = new bool[g * g];
            var ships = new List<ShipPlacementData>(boats.Length);

            for (int i = 0; i < boats.Length; i++)
            {
                var bt = boats[i];
                if (bt == null) continue;

                if (!FindSlot(bt.Length, g, used, out int rx, out int ry, out bool vertical))
                { rx = 0; ry = 0; vertical = true; }

                ships.Add(new ShipPlacementData { typeId = bt.TypeId, rootCell = _codec.Pack(rx, ry), vertical = vertical });
            }

            _placement.ClientSubmitPlacementServerRpc(new FleetPlacementData { ships = ships.ToArray() }, default);
            _placed = true;
            AppLogger.Info("auto placed fleet");
        }

        private async UniTaskVoid FireOnTurnAsync(int seconds)
        {
            await UniTask.Delay((int)(_thinkSeconds * 1000f)); // tiny human-ish delay

            int grid = _settings.GridSize;
            int max = grid * grid;

            for (int safety = 0; safety < max; safety++)
            {
                int idx = _scanIdx % max;
                _scanIdx++;

                if (GetBit(_fired, idx)) continue;

                SetBit(ref _fired, idx);

                int x = idx % grid;
                int y = idx / grid;
                byte cell = _codec.Pack(x, y);

                _turns.ClientRequestShotServerRpc(cell, default);
                AppLogger.Info($"auto fired at {x},{y}");
                break;
            }
        }

        private bool FindSlot(int len, int grid, bool[] used, out int rx, out int ry, out bool vertical)
        {
            for (int y = 0; y < grid; y += 2)
                for (int x = 0; x + len <= grid; x++)
                    if (Fits(x, y, len, grid, used, false))
                    { Mark(x, y, len, grid, used, false); rx = x; ry = y; vertical = false; return true; }

            for (int x = 0; x < grid; x += 2)
                for (int y = 0; y + len <= grid; y++)
                    if (Fits(x, y, len, grid, used, true))
                    { Mark(x, y, len, grid, used, true); rx = x; ry = y; vertical = true; return true; }

            rx = 0; ry = 0; vertical = true;
            return false;
        }

        private bool Fits(int x, int y, int len, int grid, bool[] used, bool vertical)
        {
            for (int i = 0; i < len; i++)
            {
                int cx = vertical ? x : x + i;
                int cy = vertical ? y + i : y;
                if (cx >= grid || cy >= grid) return false;
                if (used[cy * grid + cx]) return false;
            }
            return true;
        }

        private void Mark(int x, int y, int len, int grid, bool[] used, bool vertical)
        {
            for (int i = 0; i < len; i++)
            {
                int cx = vertical ? x : x + i;
                int cy = vertical ? y + i : y;
                used[cy * grid + cx] = true;
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
            else if (index < 192) bb.x2 |= (1UL << (index - 128));
            else bb.x3 |= (1UL << (index - 192));
        }
    }
}
