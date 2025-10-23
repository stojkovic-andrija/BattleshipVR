using UnityEngine;
using Zenject;
using TMPro;
using BattleshipsVR.Net.Services;
using BattleshipsVR.Net.Data;
using BattleshipsVR.Core;
using BattleshipsVR.Audio;

/// <summary>
/// Displays a countdown for placement and per-turn timers, with optional auto-submit on placement timeout.
/// Emits formatted tick strings and a finished event.
/// </summary>
public sealed class PlacementCountdown : MonoBehaviour
{
    private enum Mode { None = 0, Placement = 1, BattleTurn = 2 }

    /// <summary>Raised every frame while running with the formatted time (MM:SS:CC).</summary>
    public event System.Action<string> OnTickFormatted;
    /// <summary>Raised once when the countdown reaches zero.</summary>
    public event System.Action OnFinished;

    [SerializeField, Tooltip("If true, submits whatever was placed when the placement timer ends.")]
    private bool _autoSubmitOnFinish = true;
    [SerializeField, Tooltip("TMP text element to display the formatted countdown.")]
    private TMP_Text _countdownTimerText;

    [Inject] private PlacementService _placementService;
    [Inject] private ClientPlacementPlanner _planner;
    [Inject] private GameStateService _gameStateService;
    [Inject] private TurnService _turnService;
    [Inject] private AudioManager _audioManager;

    private float _timeLeft;
    private bool _running;
    private Mode _mode = Mode.None;

    /// <summary>Subscribes to placement and battle flow events and clears the label.</summary>
    private void OnEnable()
    {
        _placementService.OnPlacementTimerStarted += HandlePlacementStart;
        _placementService.OnPlacementTimerEnded += HandlePlacementEnd;

        _gameStateService.OnLocalTurnStarted += HandleTurnStart;
        _gameStateService.OnClientGameStateChanged += HandleStateChanged;
        _turnService.OnClientShotResult += HandleClientShotResult;

        SetText(string.Empty);
    }

    /// <summary>Unsubscribes from all events.</summary>
    private void OnDisable()
    {
        _placementService.OnPlacementTimerStarted -= HandlePlacementStart;
        _placementService.OnPlacementTimerEnded -= HandlePlacementEnd;

        _gameStateService.OnLocalTurnStarted -= HandleTurnStart;
        _gameStateService.OnClientGameStateChanged -= HandleStateChanged;
        _turnService.OnClientShotResult -= HandleClientShotResult;
    }

    /// <summary>Drives countdown updates and completion behavior.</summary>
    private void Update()
    {
        if (!_running) return;

        _timeLeft -= Time.deltaTime;
        if (_timeLeft < 0f) _timeLeft = 0f;

        string f = Format(_timeLeft);
        OnTickFormatted?.Invoke(f);
        SetText(f);

        if (_timeLeft <= 0f)
        {
            _running = false;
            OnFinished?.Invoke();

            if (_mode == Mode.Placement) TryAutoSubmitPartial();

            StopAndClear();
        }
    }

    /// <summary>Begins placement countdown and plays an audio cue.</summary>
    private void HandlePlacementStart(int seconds)
    {
        _mode = Mode.Placement;
        Begin(seconds);
        _audioManager.PlayWithSpecificPitch(AudioManager.AudioType.Victory2, 1);
    }

    /// <summary>Ends placement countdown and plays an audio cue.</summary>
    private void HandlePlacementEnd()
    {
        StopAndClear();
        _mode = Mode.None;
        _audioManager.PlayWithSpecificPitch(AudioManager.AudioType.TimerEnd, 1);
    }

    /// <summary>Begins battle turn countdown and plays an audio cue.</summary>
    private void HandleTurnStart(int seconds)
    {
        _mode = Mode.BattleTurn;
        Begin(seconds);
        _audioManager.PlayWithSpecificPitch(AudioManager.AudioType.TimerEnd, 1);
    }

    /// <summary>Stops the timer when leaving placement/battle states.</summary>
    private void HandleStateChanged(GameState state)
    {
        if (state != GameState.PLACEMENT && state != GameState.BATTLE)
        {
            StopAndClear();
            _mode = Mode.None;
        }
    }

    /// <summary>Stops turn countdown after the local player's shot resolves.</summary>
    private void HandleClientShotResult(byte cell, bool isHit, bool isSunk, byte sunkTypeId, bool isLocalShooter)
    {
        if (isLocalShooter && _mode == Mode.BattleTurn)
        {
            StopAndClear();
            _mode = Mode.None;
        }
    }

    /// <summary>Initializes timer state and emits the first formatted tick.</summary>
    private void Begin(int seconds)
    {
        _timeLeft = Mathf.Max(0.0f, seconds);
        _running = true;

        string f = Format(_timeLeft);
        OnTickFormatted?.Invoke(f);
        SetText(f);
    }

    /// <summary>Stops the timer and clears the label.</summary>
    private void StopAndClear()
    {
        _running = false;
        _timeLeft = 0f;

        string f = Format(0f);
        OnTickFormatted?.Invoke(f);
        SetText(string.Empty);
    }

    /// <summary>Optionally submits a partial placement to the server on timeout.</summary>
    private void TryAutoSubmitPartial()
    {
        if (!_autoSubmitOnFinish) return;

        if (_planner.TryBuildPartialFleetData(out FleetPlacementData partial))
            _placementService.ClientSubmitPartialPlacementServerRpc(partial);
        else
            _placementService.ClientSubmitPartialPlacementServerRpc(new FleetPlacementData { ships = System.Array.Empty<ShipPlacementData>() });
    }

    /// <summary>Formats time in seconds to MM:SS:CC (centiseconds).</summary>
    private static string Format(float t)
    {
        int totalCs = Mathf.RoundToInt(t * 100f);
        int minutes = totalCs / 6000;
        int seconds = (totalCs / 100) % 60;
        int centis = totalCs % 100;
        return $"{minutes:00}:{seconds:00}:{centis:00}";
    }

    /// <summary>Safely sets the TMP label text.</summary>
    private void SetText(string s)
    {
        if (_countdownTimerText == null) return;
        _countdownTimerText.text = s;
    }
}
