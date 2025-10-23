using UnityEngine;
using Unity.Cinemachine;
using Zenject;
using Cysharp.Threading.Tasks;
using System.Threading;
using BattleshipsVR.Net.Services;
using BattleshipsVR.Net.Data;

/// <summary>
/// Directs a Cinemachine camera to focus on base or opponent based on turn flow,
/// with short observation delays after shots and automatic resets during reveal/end.
/// </summary>
public sealed class TurnCameraDirector : MonoBehaviour
{
    private enum DelayMode { None, Shooter, Defender }

    [Header("Cinemachine")]
    [SerializeField] private CinemachineCamera _cmCamera;
    [SerializeField] private Transform _baseFocus;
    [SerializeField] private Transform _opponentFocus;

    [Header("UX")]
    [SerializeField] private float _observeSeconds = 2f;
    [Inject] private GameStateService _gameState;
    [Inject] private TurnService _turns;

    private CinemachineFollow _follow;
    private CancellationTokenSource _delayCts;
    private DelayMode _delayMode = DelayMode.None;
    private bool _pendingShooterFocus;

    /// <summary>Initializes camera references and required components.</summary>
    private void Awake()
    {
        if (_cmCamera == null) _cmCamera = GetComponent<CinemachineCamera>();
        _follow = _cmCamera != null ? _cmCamera.GetComponent<CinemachineFollow>() : null;
        if (_follow == null) Debug.LogError("[TurnCameraDirector] Missing CinemachineFollow.");
    }

    /// <summary>Subscribes to turn, shot, and state events.</summary>
    private void OnEnable()
    {
        _gameState.OnClientTurnBroadcast += HandleTurnBroadcast;
        _turns.OnClientShotResult += HandleShotResult;
        _gameState.OnClientGameStateChanged += HandleStateChanged;
    }

    /// <summary>Unsubscribes from events and cancels any pending delays.</summary>
    private void OnDisable()
    {
        _gameState.OnClientTurnBroadcast -= HandleTurnBroadcast;
        _turns.OnClientShotResult -= HandleShotResult;
        _gameState.OnClientGameStateChanged -= HandleStateChanged;
        CancelDelay();
    }

    /// <summary>Immediately focuses the base anchor.</summary>
    public void SnapToBase()
    {
        FocusBase();
    }

    /// <summary>Resets camera focus and pending delays when entering REVEAL/END.</summary>
    private void HandleStateChanged(GameState state)
    {
        if (state == GameState.REVEAL || state == GameState.END)
        {
            CancelDelay();
            _delayMode = DelayMode.None;
            _pendingShooterFocus = false;
            FocusBase();
        }
    }

    /// <summary>Handles turn broadcast and moves focus based on whether the local client is the shooter.</summary>
    private void HandleTurnBroadcast(bool isLocalShooter, int seconds)
    {
        if (isLocalShooter)
        {
            if (_delayMode == DelayMode.Defender && _delayCts != null)
            {
                _pendingShooterFocus = true;
                return;
            }
            FocusOpponent();
            return;
        }

        if (_delayMode != DelayMode.Shooter)
            FocusBase();
    }

    /// <summary>Starts a short observation delay after each shot and then returns focus.</summary>
    private void HandleShotResult(byte packedCell, bool hit, bool sunk, byte sunkTypeId, bool isLocalShooter)
    {
        if (isLocalShooter)
            StartObservationDelay(DelayMode.Shooter, _observeSeconds).Forget();
        else
            StartObservationDelay(DelayMode.Defender, _observeSeconds).Forget();
    }

    /// <summary>Sets camera follow to the base focus.</summary>
    private void FocusBase()
    {
        if (_cmCamera == null || _follow == null || _baseFocus == null) return;
        _cmCamera.Follow = _baseFocus;
    }

    /// <summary>Sets camera follow to the opponent focus.</summary>
    private void FocusOpponent()
    {
        if (_cmCamera == null || _follow == null || _opponentFocus == null) return;
        _cmCamera.Follow = _opponentFocus;
    }

    /// <summary>Runs a timed observation focusing on shooter/defender, then restores the appropriate focus.</summary>
    private async UniTaskVoid StartObservationDelay(DelayMode mode, float seconds)
    {
        CancelDelay();
        _delayMode = mode;
        _pendingShooterFocus = false;

        if (mode == DelayMode.Shooter) FocusOpponent();
        else FocusBase();

        _delayCts = new CancellationTokenSource();
        try
        {
            await UniTask.Delay((int)(seconds * 1000f), cancellationToken: _delayCts.Token);
        }
        catch (System.OperationCanceledException) { }

        if (!_delayCts?.IsCancellationRequested ?? false)
        {
            if (mode == DelayMode.Shooter)
                FocusBase();
            else if (_pendingShooterFocus)
                FocusOpponent();
            else
                FocusBase();
        }

        _delayMode = DelayMode.None;
        CancelDelay();
    }

    /// <summary>Cancels and disposes the active observation delay, if any.</summary>
    private void CancelDelay()
    {
        if (_delayCts == null) return;
        _delayCts.Cancel();
        _delayCts.Dispose();
        _delayCts = null;
    }
}
