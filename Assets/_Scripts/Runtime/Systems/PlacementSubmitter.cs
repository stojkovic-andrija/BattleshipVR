// PlacementSubmitter.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Zenject;
using BattleshipsVR.Net.Services;
using BattleshipsVR.Net.Data;

/// <summary>
/// Serializes local fleet and submits to the server when Space is pressed. Disables
/// optional behaviours upon successful submit to prevent further edits.
/// </summary>
public sealed class PlacementSubmitter : MonoBehaviour
{
    public event System.Action OnSubmitAttempt;
    public event System.Action OnSubmitSuccess;
    public event System.Action OnSubmitFailed;

    [SerializeField, Tooltip("Behaviours disabled after a successful submit (e.g., interactor).")]
    private List<Behaviour> _toDisableOnSubmit = new List<Behaviour>();

    [Inject] private ClientPlacementPlanner _planner;
    [Inject] private PlacementService _placementService;

    private InputActionMap _map;
    private InputAction _confirm;

    private void Awake()
    {
        _map = new InputActionMap("PlacementConfirm");
        _confirm = _map.AddAction("Confirm", InputActionType.Button, "<Keyboard>/space");
        _confirm.performed += OnConfirmPerformed;
        _map.Enable();
    }

    private void OnDestroy()
    {
        _confirm.performed -= OnConfirmPerformed;
        _map.Disable();
    }

    private void OnConfirmPerformed(InputAction.CallbackContext ctx)
    {
        OnSubmitAttempt?.Invoke();

        if (!_planner.TryBuildFleetData(out var data))
        {
            OnSubmitFailed?.Invoke();
            return;
        }

        // Server authoritative; FishNet injects caller connection on the server.
        _placementService.ClientSubmitPlacementServerRpc(data);

        for (int i = 0; i < _toDisableOnSubmit.Count; i++)
        {
            if (_toDisableOnSubmit[i] != null)
                _toDisableOnSubmit[i].enabled = false;
        }

        OnSubmitSuccess?.Invoke();
    }
}
