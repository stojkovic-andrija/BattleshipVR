using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Zenject;
using BattleshipsVR.Net.Services;
using FishNet;
using BattleshipsVR.Net.Data;
using FishNet.Connection;
using FishNet.Utility.Extension;
using BattleshipsVR.Audio;

/// <summary>
/// Displays win/lose result after a match ends and allows restart or quit.
/// </summary>
public sealed class EndResultUI : MonoBehaviour
{
    // Serialized inspector variables
    [Header("References")]
    [SerializeField, Tooltip("Root CanvasGroup for showing/hiding the result UI.")]
    private CanvasGroup _group;
    [SerializeField, Tooltip("Text element showing WIN/LOSE.")]
    private TMP_Text _resultText;
    [SerializeField, Tooltip("Button to request a match restart.")]
    private Button _restartButton;
    [SerializeField, Tooltip("Button to quit the application or stop play mode in Editor.")]
    private Button _quitButton;

    [Header("Visuals")]
    [SerializeField, Tooltip("Text displayed when the local player wins.")]
    private string _winText = "YOU WIN";
    [SerializeField, Tooltip("Text displayed when the local player loses.")]
    private string _loseText = "YOU LOSE";

    private GameStateService _gameState;
    private PlayerRegistryService _roster;
    private AudioManager _audioManager;

    /// <summary>Initializes UI state and wires button callbacks.</summary>
    private void Awake()
    {
        _group.alpha = 0f;
        _group.interactable = false;
        _group.blocksRaycasts = false;

        _restartButton.onClick.AddListener(OnRestartPressed);
        _quitButton.onClick.AddListener(OnQuitPressed);
    }

    /// <summary>Subscribes to game state changes.</summary>
    private void OnEnable()
    {
        if (_gameState != null)
            _gameState.OnClientGameStateChanged += HandleGameStateChanged;
    }

    /// <summary>Unsubscribes from game state changes.</summary>
    private void OnDisable()
    {
        if (_gameState != null)
            _gameState.OnClientGameStateChanged -= HandleGameStateChanged;
    }

    /// <summary>Receives dependencies via DI.</summary>
    [Inject]
    private void Construct(GameStateService gameState, PlayerRegistryService roster, AudioManager audioManager)
    {
        _gameState = gameState;
        _roster = roster;
        _audioManager = audioManager;
    }

    /// <summary>Shows/hides the panel based on state; computes local result on END.</summary>
    private void HandleGameStateChanged(GameState state)
    {
        if (state == GameState.END)
        {
            bool didWin = DetermineLocalVictory();
            ShowResult(didWin);
        }
        else
        {
            Hide();
        }
    }

    /// <summary>Determines whether the local client won based on the defeated client id.</summary>
    private bool DetermineLocalVictory()
    {
        if (_gameState == null)
            return false;

        var myConn = InstanceFinder.ClientManager?.Connection;
        if (myConn == null)
            return false;

        int localId = myConn.ClientId;
        int defeatedId = _gameState.LastDefeatedClientId;

        if (defeatedId < 0)
            return false;

        return localId != defeatedId;
    }

    /// <summary>Shows the result UI and plays the corresponding audio cue.</summary>
    private void ShowResult(bool didWin)
    {
        if (didWin)
        {
            _resultText.text = _winText;
            _audioManager.PlayWithSpecificPitch(AudioManager.AudioType.Victory, 1f);
        }
        else
        {
            _resultText.text = _loseText;
            _audioManager.PlayWithSpecificPitch(AudioManager.AudioType.Failure, 1f);
        }
        _group.alpha = 1f;
        _group.interactable = true;
        _group.blocksRaycasts = true;

        PreserveXR_Origin();
    }

    /// <summary>Hides the result UI.</summary>
    private void Hide()
    {
        _group.alpha = 0f;
        _group.interactable = false;
        _group.blocksRaycasts = false;
    }

    /// <summary>Sends a restart request to the server.</summary>
    private void OnRestartPressed()
    {
        if (_gameState != null)
        {
            _group.interactable = false;
            _gameState.ClientRequestRestart();
        }
    }

    /// <summary>Detaches and preserves the XR origin object across scene loads.</summary>
    private void PreserveXR_Origin()
    {
        GameObject XR = FindAnyObjectByType<XRMultiSceneObjectDDOL>().gameObject;
        XR.transform.parent = null;
        DontDestroyOnLoad(XR);
    }

    /// <summary>Quits the application (or stops play mode in the Editor).</summary>
    private void OnQuitPressed()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
