using UnityEngine;
using UnityEngine.UI;
using FishNet.Managing;
using FishNet.Transporting;
using TMPro;
using System.Net;
using Cysharp.Threading.Tasks;
using System.Threading;
using Zenject;
using BattleshipsVR.Core;
using BattleshipsVR.Bootstrap;
using FishNet.Connection;
using BattleshipsVR.Net.Services;
using BattleshipsVR.Net.Data;

namespace BattleshipsVR.Dev.UI
{
    /// <summary>
    /// Lobby UI for hosting or joining a session. Reflects true network state
    /// and hides once the gameplay scene is loaded.
    /// </summary>
    public sealed class DevLobbyPanel : MonoBehaviour
    {
        private enum JoinMode
        {
            None,
            Code,
            Ip
        }

        private static class UiText
        {
            public const string Idle = "Idle — not connected";
            public const string Hosting = "Starting host...";
            public const string Waiting = "Waiting for another player...";
            public const string Ready = "Both players connected!";
            public const string JoiningCode = "Joining via session code...";
            public const string JoiningIp = "Joining via direct IP...";
            public const string Connecting = "Connecting to host...";
            public const string Connected = "Connected — waiting for host to start...";
            public const string Disconnected = "Disconnected";
            public const string InvalidCode = "Invalid session code";
            public const string InvalidIp = "Invalid IP address";
            public const string InvalidPort = "Invalid port";
            public const string Timeout = "Connection timeout — could not reach host";
            public const string Starting = "Loading battlefield...";
        }

        /// <summary>Raised when the Start button is clicked by the host.</summary>
        public event System.Action OnClickedStart;

        [Header("UI (TMP)")]
        [SerializeField, Tooltip("Status label reflecting network state.")]
        private TMP_Text _status;
        [SerializeField, Tooltip("Displays the generated session code when hosting.")]
        private TMP_Text _codeLabel;
        [SerializeField, Tooltip("Shows number of connected players.")]
        private TMP_Text _players;
        [SerializeField, Tooltip("Input field for session code joining.")]
        private TMP_InputField _codeInput;
        [SerializeField, Tooltip("Direct join IP address.")]
        private TMP_InputField _ipInput;
        [SerializeField, Tooltip("Direct join port.")]
        private TMP_InputField _portInput;
        [SerializeField, Tooltip("Button to host a session.")]
        private Button _hostBtn;
        [SerializeField, Tooltip("Button to join a session (code or IP).")]
        private Button _joinBtn;
        [SerializeField, Tooltip("Button to start gameplay once both players are present.")]
        private Button _startBtn;

        [SerializeField, Tooltip("Network entry component that performs host/join operations.")]
        private NetworkEntry _entry;
        [SerializeField, Tooltip("FishNet NetworkManager in the scene.")]
        private NetworkManager _net;

        private CancellationTokenSource _connectCts;
        private bool _isHost;

        /// <summary>Initializes UI callbacks and default field values.</summary>
        private void Awake()
        {
            _hostBtn.onClick.AddListener(OnClickHost);
            _joinBtn.onClick.AddListener(OnClickJoin);
            _startBtn.onClick.AddListener(OnClickStart);

            if (_portInput != null && string.IsNullOrWhiteSpace(_portInput.text))
                _portInput.text = NetConstants.DEFAULT_PORT.ToString();

            if (_ipInput != null)
                _ipInput.text = "127.0.0.1";

            if (_portInput != null)
                _portInput.text = "7770";

            SetStatus(UiText.Idle);
            UpdatePlayersLabel(0);
            _startBtn.interactable = false;

            if (_codeInput != null) _codeInput.onValueChanged.AddListener(_ => RefreshJoinState());
            if (_ipInput != null) _ipInput.onValueChanged.AddListener(_ => RefreshJoinState());
            if (_portInput != null) _portInput.onValueChanged.AddListener(_ => RefreshJoinState());
            RefreshJoinState();
        }

        /// <summary>Hooks network events once objects are initialized.</summary>
        private void Start()
        {
            _entry.OnHostSessionReady += OnHostReady;
            _entry.OnServerStarted += () => SetStatus(UiText.Waiting);

            _net.ServerManager.OnRemoteConnectionState += OnRemoteConnectionChanged;
            _net.ClientManager.OnClientConnectionState += OnClientState;
        }

        /// <summary>Unhooks events and cancels any pending connect timeout.</summary>
        private void OnDestroy()
        {
            if (_entry != null)
            {
                _entry.OnHostSessionReady -= OnHostReady;
                _entry.OnServerStarted -= () => SetStatus(UiText.Waiting);
            }

            if (_net != null)
            {
                _net.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionChanged;
                _net.ClientManager.OnClientConnectionState -= OnClientState;
            }

            _connectCts?.Cancel();
            _connectCts?.Dispose();
        }

        /// <summary>Hosts a session with provided IP and port.</summary>
        private void OnClickHost()
        {
            ushort port = ParsePort(out bool portOk);
            if (!portOk)
            {
                SetStatus(UiText.InvalidPort);
                return;
            }

            string bindIp = _ipInput != null ? _ipInput.text.Trim() : string.Empty;
            _entry.StartHost(bindIp, port);
            _isHost = true;

            SetStatus(UiText.Hosting);
            UpdatePlayersLabel(1);
        }

        /// <summary>Joins a session using code or direct IP:port based on inputs.</summary>
        private void OnClickJoin()
        {
            var mode = DetermineMode(out string code, out string ip, out ushort port);
            if (mode == JoinMode.None)
            {
                _joinBtn.interactable = false;
                return;
            }

            if (mode == JoinMode.Code)
            {
                if (!IsCodeValid(code))
                {
                    SetStatus(UiText.InvalidCode);
                    return;
                }
                SetStatus(UiText.JoiningCode);
                StartClientWithTimeout(() => _entry.StartClientWithCode(code)).Forget();
                return;
            }

            if (!IsIPv4(ip))
            {
                SetStatus(UiText.InvalidIp);
                return;
            }
            if (port == 0)
            {
                SetStatus(UiText.InvalidPort);
                return;
            }

            SetStatus(UiText.JoiningIp);
            StartClientWithTimeout(() => _entry.StartClientDirect(ip, port)).Forget();
        }

        /// <summary>Starts the gameplay scene when hosting and both players are present.</summary>
        private void OnClickStart()
        {
            ZenjectInjector.Inject(this);

            if (!_isHost) return;
            _entry.StartGameplay();
            SetStatus(UiText.Starting);
            OnClickedStart?.Invoke();
        }

        /// <summary>Updates the UI when hosting details are ready.</summary>
        private void OnHostReady(string advertiseIp, ushort port, string code)
        {
            if (_codeLabel != null)
                _codeLabel.text = $"Code: {code}";

            SetStatus($"Hosting on {advertiseIp}:{port}");
            UpdatePlayersLabel(1);
        }

        /// <summary>Reflects client connection state transitions.</summary>
        private void OnClientState(ClientConnectionStateArgs args)
        {
            switch (args.ConnectionState)
            {
                case LocalConnectionState.Starting:
                    SetStatus(UiText.Connecting);
                    break;
                case LocalConnectionState.Started:
                    SetStatus(UiText.Connected);
                    _connectCts?.Cancel();
                    UpdatePlayersLabel(1);
                    break;
                case LocalConnectionState.Stopped:
                    SetStatus(UiText.Disconnected);
                    _connectCts?.Cancel();
                    break;
            }
        }

        /// <summary>Tracks remote connections on the server and enables Start when two are present.</summary>
        private void OnRemoteConnectionChanged(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (!_net.ServerManager.Started)
                return;

            int connected = Mathf.Clamp(_net.ServerManager.Clients.Count, 0, 2);
            UpdatePlayersLabel(connected);

            if (connected < 2)
                SetStatus(UiText.Waiting);
            else
                SetStatus(UiText.Ready);

            _startBtn.interactable = connected >= 2;
        }

        /// <summary>Updates the players counter label.</summary>
        private void UpdatePlayersLabel(int connected)
        {
            _players.text = $"Players connected: {connected}/2";
        }

        /// <summary>Enables/disables Join button based on current inputs.</summary>
        private void RefreshJoinState()
        {
            var mode = DetermineMode(out string code, out string ip, out ushort port);

            bool enable = false;
            if (mode == JoinMode.Code)
                enable = IsCodePossiblyValidFormat(code);
            else if (mode == JoinMode.Ip)
                enable = IsIPv4(ip) && port != 0;

            _joinBtn.interactable = enable;
        }

        /// <summary>Determines whether joining by code or IP is intended from inputs.</summary>
        private JoinMode DetermineMode(out string code, out string ip, out ushort port)
        {
            code = _codeInput != null ? _codeInput.text.Trim() : string.Empty;
            ip = _ipInput != null ? _ipInput.text.Trim() : string.Empty;
            port = ParsePort(out _);

            if (!string.IsNullOrEmpty(code)) return JoinMode.Code;
            if (!string.IsNullOrEmpty(ip)) return JoinMode.Ip;
            return JoinMode.None;
        }

        /// <summary>Validates the session code against the codec.</summary>
        private bool IsCodeValid(string code)
        {
            return BattleshipsVR.Session.SessionCodeCodec.TryDecode(code, out _, out _);
        }

        /// <summary>Basic format validation for a potential session code.</summary>
        private bool IsCodePossiblyValidFormat(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            if (code.Length < 6 || code.Length > 16) return false;
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                if (!char.IsLetterOrDigit(c)) return false;
            }
            return true;
        }

        /// <summary>Checks whether a string is a valid IPv4 address (non-wildcard).</summary>
        private bool IsIPv4(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;
            if (ip == "0.0.0.0") return false;
            return IPAddress.TryParse(ip, out var addr) &&
                   addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
        }

        /// <summary>Parses the port input into a ushort; returns 0 on failure.</summary>
        private ushort ParsePort(out bool ok)
        {
            ushort p = 0;
            ok = _portInput != null && ushort.TryParse(_portInput.text, out p) && p > 0;
            return ok ? p : (ushort)0;
        }

        /// <summary>Sets the status label text.</summary>
        private void SetStatus(string s)
        {
            if (_status != null)
                _status.text = s;
        }

        /// <summary>Starts a client attempt and flags timeout if not connected within a window.</summary>
        private async UniTaskVoid StartClientWithTimeout(System.Action startFn)
        {
            _connectCts?.Cancel();
            _connectCts?.Dispose();
            _connectCts = new CancellationTokenSource();

            startFn.Invoke();

            try
            {
                await UniTask.Delay(7000, cancellationToken: _connectCts.Token);
                if (_net.ClientManager.Connection == null || !_net.ClientManager.Started)
                    SetStatus(UiText.Timeout);
            }
            catch (System.OperationCanceledException)
            {
                // Completed or stopped
            }
        }

        /// <summary>Hides the lobby once the gameplay scene has been loaded. (Obsolete)</summary>
        private void HandleGameplaySceneLoaded()
        {
            gameObject.SetActive(false);
            SetStatus(UiText.Starting);
            AppLogger.Info("Lobby hidden after gameplay scene load.");
        }
    }
}
