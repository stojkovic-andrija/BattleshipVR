using UnityEngine;
using UnityEngine.UI;
using FishNet.Managing;
using FishNet.Transporting;
using TMPro;
using System.Net;
using Cysharp.Threading.Tasks;
using System.Threading;
using BattleshipsVR.Core;
using BattleshipsVR.Bootstrap;

namespace BattleshipsVR.Dev.UI
{
    /// <summary>TMP lobby with one Join button that accepts either code or IP and robust status & validation</summary>
    public sealed class DevLobbyPanel : MonoBehaviour
    {
        public event System.Action OnClickedStart;

        private enum JoinMode
        {
            None,
            Code,
            Ip
        }

        private static class UiText
        {
            public const string Idle = "Idle";
            public const string Hosting = "Hosting…";
            public const string ServerWaiting = "Server started, waiting for players";
            public const string JoiningCode = "Joining by code…";
            public const string JoiningIp = "Joining by IP…";
            public const string Connecting = "Connecting…";
            public const string Connected = "Connected";
            public const string Disconnected = "Disconnected";
            public const string InvalidCode = "Invalid code";
            public const string InvalidIp = "Invalid IP";
            public const string InvalidPort = "Invalid port";
            public const string Timeout = "Could not connect (timeout)";
            public const string Starting = "Starting game…";
        }

        // Serialized inspector fields
        [Header("References")]
        [SerializeField] private NetworkEntry _entry;
        [SerializeField] private NetworkManager _net;

        [Header("UI (TMP)")]
        [SerializeField] private TMP_Text _status;
        [SerializeField] private TMP_Text _codeLabel;
        [SerializeField] private TMP_Text _players;
        [SerializeField] private TMP_InputField _codeInput;
        [SerializeField] private TMP_InputField _ipInput;
        [SerializeField] private TMP_InputField _portInput;
        [SerializeField] private Button _hostBtn;
        [SerializeField] private Button _joinBtn;     // single Join for code or IP
        [SerializeField] private Button _startBtn;
        [SerializeField] private Button _copyBtn;     // optional convenience

        // Private vars
        private CancellationTokenSource _connectCts;

        // Unity event methods
        private void Awake()
        {
            if (_entry == null) _entry = FindFirstObjectByType<NetworkEntry>();
            if (_net == null) _net = FindFirstObjectByType<NetworkManager>();

            _hostBtn.onClick.AddListener(OnClickHost);
            _joinBtn.onClick.AddListener(OnClickJoin);
            _startBtn.onClick.AddListener(OnClickStart);
            if (_copyBtn != null) _copyBtn.onClick.AddListener(CopyCode);

            _entry.OnHostSessionReady += OnHostReady;
            _entry.OnServerStarted += () => SetStatus(UiText.ServerWaiting);

            _net.ServerManager.OnRemoteConnectionState += (_, __) => RefreshPlayers();
            _net.ClientManager.OnClientConnectionState += OnClientState;

            if (_portInput != null && string.IsNullOrWhiteSpace(_portInput.text))
                _portInput.text = BattleshipsVR.Bootstrap.NetConstants.DEFAULT_PORT.ToString();

            if (_ipInput != null) _ipInput.text = ""; // blank so we never advertise 0.0.0.0 by mistake
            SetStatus(UiText.Idle);
            _players.text = "Players: 0/2 (waiting)";
            _startBtn.interactable = false;

            // live validation so Join enables only when usable
            if (_codeInput != null) _codeInput.onValueChanged.AddListener(_ => RefreshJoinState());
            if (_ipInput != null) _ipInput.onValueChanged.AddListener(_ => RefreshJoinState());
            if (_portInput != null) _portInput.onValueChanged.AddListener(_ => RefreshJoinState());
            RefreshJoinState();
        }

        private void OnDestroy()
        {
            if (_entry != null) _entry.OnHostSessionReady -= OnHostReady;
            _net.ClientManager.OnClientConnectionState -= OnClientState;
            _net.ServerManager.OnRemoteConnectionState -= (_, __) => RefreshPlayers();
            _connectCts?.Cancel();
            _connectCts?.Dispose();
        }

        // Public methods

        // Private methods
        private void OnClickHost()
        {
            ushort port = ParsePort(out bool portOk);
            if (!portOk) { SetStatus(UiText.InvalidPort); return; }

            string bindIp = _ipInput != null ? _ipInput.text.Trim() : "";
            _entry.StartHost(bindIp, port);
            SetStatus(UiText.Hosting);
            RefreshPlayers();
        }

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

            // IP mode
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

        private void OnClickStart()
        {
            _entry.StartGameplay();
            SetStatus(UiText.Starting);
            OnClickedStart?.Invoke();
        }

        private void OnHostReady(string advertiseIp, ushort port, string code)
        {
            if (_codeLabel != null) _codeLabel.text = $"Code: {code}";
            SetStatus($"Host running on {advertiseIp}:{port}");
            RefreshPlayers();
        }

        private void CopyCode()
        {
            if (_codeLabel == null) return;
            string code = _codeLabel.text.Replace("Code:", "").Trim();
            if (!string.IsNullOrEmpty(code))
            {
                GUIUtility.systemCopyBuffer = code;
                AppLogger.Info("Session code copied");
            }
        }

        private void OnClientState(ClientConnectionStateArgs args)
        {
            switch (args.ConnectionState)
            {
                case LocalConnectionState.Starting: SetStatus(UiText.Connecting); break;
                case LocalConnectionState.Started:
                    SetStatus(UiText.Connected);
                    _connectCts?.Cancel();
                    break;
                case LocalConnectionState.Stopped:
                    SetStatus(UiText.Disconnected);
                    _connectCts?.Cancel();
                    break;
            }
            RefreshPlayers();
        }

        private void RefreshPlayers()
        {
            if (!_net.ServerManager.Started)
            {
                _players.text = "Players: 0/2 (waiting)";
                _startBtn.interactable = false;
                return;
            }

            int total = Mathf.Clamp(_net.ServerManager.Clients.Count, 0, 2);
            _players.text = $"Players: {total}/2 {(total >= 2 ? "(ready)" : "(waiting)")}";
            _startBtn.interactable = (total >= 2);
        }

        private void RefreshJoinState()
        {
            // enable when at least one valid source is present
            var mode = DetermineMode(out string code, out string ip, out ushort port);

            bool enable = false;
            if (mode == JoinMode.Code)
                enable = IsCodePossiblyValidFormat(code); // cheap precheck before real decode
            else if (mode == JoinMode.Ip)
                enable = IsIPv4(ip) && port != 0;

            _joinBtn.interactable = enable;
        }

        private JoinMode DetermineMode(out string code, out string ip, out ushort port)
        {
            code = _codeInput != null ? _codeInput.text.Trim() : "";
            ip = _ipInput != null ? _ipInput.text.Trim() : "";
            port = ParsePort(out _);

            // prefer code when both are present
            if (!string.IsNullOrEmpty(code)) return JoinMode.Code;
            if (!string.IsNullOrEmpty(ip)) return JoinMode.Ip;
            return JoinMode.None;
        }

        private bool IsCodeValid(string code)
        {
            // full decode check via codec
            return BattleshipsVR.Session.SessionCodeCodec.TryDecode(code, out _, out _);
        }

        private bool IsCodePossiblyValidFormat(string code)
        {
            // loose format check so the button can enable while typing without spam
            // alnum and length between 6 and 12 tends to be fine for our codec
            if (string.IsNullOrEmpty(code)) return false;
            if (code.Length < 6 || code.Length > 16) return false;
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                if (!char.IsLetterOrDigit(c)) return false;
            }
            return true;
        }

        private bool IsIPv4(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;
            if (ip == "0.0.0.0") return false;
            return IPAddress.TryParse(ip, out var addr) && addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
        }

        private ushort ParsePort(out bool ok)
        {
            ok = _portInput != null && ushort.TryParse(_portInput.text, out ushort p) && p > 0;
            return ok ? ushort.Parse(_portInput.text) : (ushort)0;
        }

        private void SetStatus(string s)
        {
            if (_status != null) _status.text = s;
        }

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
                // connection completed or stopped
            }
        }
    }
} 
