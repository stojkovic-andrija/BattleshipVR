using FishNet;
using UnityEngine;
using UnityEngine.UI;

public class MultiplayerMenuView : View
{
    [SerializeField] private Button _hostButton;
    [SerializeField] private Button _connectButton;
    [SerializeField] private Button _exitButton;

    public override void Initialize()
    {
        _hostButton.onClick.AddListener(() =>
        {
            InstanceFinder.ServerManager.StartConnection();

            InstanceFinder.ClientManager.StartConnection();
        });

        _connectButton.onClick.AddListener(() =>
        {
            InstanceFinder.ClientManager.StartConnection();
        });

        _exitButton.onClick.AddListener(Application.Quit);

        base.Initialize();
    }
}
