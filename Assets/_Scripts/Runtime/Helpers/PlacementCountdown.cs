using UnityEngine;
using Zenject;
using BattleshipsVR.Net.Services;
using BattleshipsVR.Net.Data;
using TMPro;
using UnityEditor;

/// <summary>Local countdown for placement phase; submits partial on finish.</summary>
public sealed class PlacementCountdown : MonoBehaviour
{
    public event System.Action<string> OnTickFormatted;
    public event System.Action OnFinished;

    [SerializeField, Tooltip("Optional auto-submit on finish.")]
    private bool _autoSubmitOnFinish = true;
    [SerializeField]
    private TMP_Text _countdownTimerText; 

    [Inject] private PlacementService _placementService;
    [Inject] private ClientPlacementPlanner _planner;

    private float _timeLeft;
    private bool _running;

    private void OnEnable()
    {
        _placementService.OnPlacementTimerStarted += HandleStart;
        _placementService.OnPlacementTimerEnded += HandleEnd;
    }

    private void OnDisable()
    {
        _placementService.OnPlacementTimerStarted -= HandleStart;
        _placementService.OnPlacementTimerEnded -= HandleEnd;
    }

    private void Start()
    {
        // Temporary debug timer start
        HandleStart(60);
    }

    private void Update()
    {
        if (!_running) return;

        _timeLeft -= Time.deltaTime;
        if (_timeLeft < 0f) _timeLeft = 0f;

        OnTickFormatted?.Invoke(Format(_timeLeft));
        _countdownTimerText.text = Format(_timeLeft);

        if (_timeLeft <= 0f)
        {
            _running = false;
            OnFinished?.Invoke();

            if (_autoSubmitOnFinish)
            {
                if (_planner.TryBuildPartialFleetData(out FleetPlacementData partial))
                    _placementService.ClientSubmitPartialPlacementServerRpc(partial);
                else
                    _placementService.ClientSubmitPartialPlacementServerRpc(new FleetPlacementData { ships = System.Array.Empty<ShipPlacementData>() });
            }
        }
    }

    private void HandleStart(int seconds)
    {
        _timeLeft = Mathf.Max(0.0f, seconds);
        _running = true;
        OnTickFormatted?.Invoke(Format(_timeLeft));
    }

    private void HandleEnd()
    {
        // Server said it's over—ensure we stop locally even if clock drifted.
        _timeLeft = 0f;
        _running = false;
        OnTickFormatted?.Invoke(Format(0f));
        OnFinished?.Invoke();
    }

    /// <summary>mm:ss:ms (e.g., 00:37:42)</summary>
    private static string Format(float t)
    {
        int totalMs = Mathf.RoundToInt(t * 100);
        int minutes = totalMs / 6000;
        int seconds = (totalMs / 100) % 60;
        int centis = totalMs % 100;
        return $"{minutes:00}:{seconds:00}:{centis:00}";
    }
}
