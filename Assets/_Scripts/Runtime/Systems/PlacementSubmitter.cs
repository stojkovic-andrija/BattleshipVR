using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Zenject;
using BattleshipsVR.Net.Services;
using BattleshipsVR.Net.Data;
using BattleshipsVR.Core;

/// <summary>
/// Submits local fleet placement to the server and locks local placement UI on ACK.
/// Supports full or partial submission with optional server auto-fill for missing ships.
/// </summary>
public sealed class PlacementSubmitter : MonoBehaviour
{
    public event System.Action OnSubmitAttempt;
    public event System.Action OnSubmitSuccess;
    public event System.Action OnSubmitFailed;

    [Header("UI")]
    [SerializeField, Tooltip("Button used to confirm fleet placement.")]
    private Button _confirmButton;

    [Header("Behaviours disabled after server ACK (local lock).")]
    [SerializeField]
    private List<Behaviour> _toDisableOnLock = new();

    [Header("Placement Settings")]
    [SerializeField, Tooltip("Allow server to fill any missing ships.")]
    private bool _allowFillMissing = true;

    [Inject] private ClientPlacementPlanner _planner;
    [Inject] private PlacementService _placementService;
    [Inject] private GameStateService _gameState;

    private bool _awaitingAck;

    private void Awake()
    {
        if (_confirmButton != null)
            _confirmButton.onClick.AddListener(HandleConfirmButtonPressed);
    }

    private void OnEnable()
    {
        _placementService.OnClientLocalLocked += HandleLocalLocked;
    }

    private void OnDisable()
    {
        _placementService.OnClientLocalLocked -= HandleLocalLocked;
    }

    private void OnDestroy()
    {
        if (_confirmButton != null)
            _confirmButton.onClick.RemoveListener(HandleConfirmButtonPressed);
    }

    /// <summary>Builds the placement payload (full or partial) and sends the ready RPC.</summary>
    private void HandleConfirmButtonPressed()
    {
        if (_awaitingAck)
            return;

        OnSubmitAttempt?.Invoke();

        FleetPlacementData payload = default;
        bool allowFill = _allowFillMissing;

        if (_planner.TryBuildFleetData(out var full))
        {
            payload = full;
            allowFill = false;
        }
        else if (_allowFillMissing)
        {
            if (_planner.TryBuildPartialFleetData(out var partial))
                payload = partial;
            else
                payload = new FleetPlacementData { ships = System.Array.Empty<ShipPlacementData>() };

            allowFill = true;
        }
        else
        {
            OnSubmitFailed?.Invoke();
            return;
        }

        _awaitingAck = true;
        _placementService.ClientReadyPlacementServerRpc(payload, allowFill);
    }

    /// <summary>Disables local placement behaviours after server confirms lock.</summary>
    private void HandleLocalLocked()
    {
        _awaitingAck = false;

        for (int i = 0; i < _toDisableOnLock.Count; i++)
        {
            if (_toDisableOnLock[i] != null)
                _toDisableOnLock[i].enabled = false;
        }

        OnSubmitSuccess?.Invoke();
    }
}
