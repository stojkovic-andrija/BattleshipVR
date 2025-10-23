using UnityEngine;
using UnityEngine.UI;
using Zenject;
using BattleshipsVR.Net.Services;

/// <summary>
/// Marks the local player as ready and disables the ready button thereafter.
/// </summary>
public sealed class ReadyButton : MonoBehaviour
{
    [SerializeField, Tooltip("Button that marks this player as ready")]
    private Button _readyButton;

    [Inject] private PlayerRegistryService _registry;

    /// <summary>Wires the click handler.</summary>
    private void Awake()
    {
        _readyButton.onClick.AddListener(OnClickReady);
    }

    /// <summary>Signals ready to the registry and disables the button.</summary>
    private void OnClickReady()
    {
        _registry.ClientRequestReady();
        _readyButton.interactable = false;
        _readyButton.gameObject.SetActive(false);
    }
}
