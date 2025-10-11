using System.Net;
using UnityEngine;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Managing.Scened;
using BattleshipsVR.Core;
using BattleshipsVR.Session;
using static BattleshipsVR.Bootstrap.NetConstants;

namespace BattleshipsVR.Bootstrap
{
    /// <summary>Boots networking, exposes Host/Join API for the lobby UI, and lets UI manually start gameplay scene</summary>
    public sealed class NetworkEntry : MonoBehaviour
    {
        // C# events
        public event System.Action<string, ushort, string> OnHostSessionReady; // bindIp, port, joinCode
        public event System.Action OnServerStarted;

        // Serialized inspector fields
        [SerializeField, Tooltip("NetworkManager in this scene")]
        private NetworkManager _networkManager;
        [SerializeField, Tooltip("Transport setup strategy component")]
        private DirectTransportSetup _directTransport;
        [SerializeField, Tooltip("Gameplay scene to load after lobby Start")]
        private SceneId _gameScene = SceneId.GameMain;
        [SerializeField, Tooltip("If true, UI must call StartHost/StartClient")]
        private bool _manualStart = true;
        [SerializeField, Tooltip("If true, UI must call StartGameplay to load scene")]
        private bool _manualSceneStart = true;
        [SerializeField, Tooltip("Legacy fallback when no UI is present")]
        private bool _defaultToHostInEditor = true;

        // Public vars

        // Protected vars

        // Private vars
        private bool _serverLoadedScene;
        private bool _isHost;
        private bool _clientStarted;
        private bool _sceneLoadRequested;

        // Unity event methods
        private void Awake()
        {
            if (_networkManager == null)
            {
                AppLogger.Error("NetworkEntry has no NetworkManager assigned");
                return;
            }

            DontDestroyOnLoad(gameObject);
            DontDestroyOnLoad(_networkManager.gameObject);

            _networkManager.ServerManager.OnServerConnectionState += OnServerState;
            _networkManager.ClientManager.OnClientConnectionState += OnClientState;

            AppLogger.Info("NetworkEntry Awake");
        }

        private void Start()
        {
            if (_manualStart) return;

            string modeStr = StartupArgs.Get(ARG_MODE, _defaultToHostInEditor && Application.isEditor
                ? NetworkRunMode.Host.ToString() : NetworkRunMode.Client.ToString());

            string code = StartupArgs.Get(ARG_CODE, string.Empty);
            string hostIp = StartupArgs.Get(ARG_HOST_IP, string.Empty);
            ushort port = ushort.TryParse(StartupArgs.Get(ARG_PORT, DEFAULT_PORT.ToString()), out ushort p) ? p : DEFAULT_PORT;

            switch (ParseMode(modeStr))
            {
                case NetworkRunMode.Host:   StartHost(hostIp, port); break;
                case NetworkRunMode.Server: StartServer(hostIp, port); break;
                default:                    StartClientByCodeOrIp(code, hostIp, port); break;
            }
        }

        private void OnDestroy()
        {
            if (_networkManager == null) return;
            _networkManager.ServerManager.OnServerConnectionState -= OnServerState;
            _networkManager.ClientManager.OnClientConnectionState -= OnClientState;
        }

        // Public methods

        /// <summary>Start listen host and local client, compute a shareable code</summary>
        public void StartHost(string bindIpArg, ushort port)
        {
            _isHost = true;
            _directTransport?.OverrideForHostBind(port, _networkManager);

            string bindIp = string.IsNullOrWhiteSpace(bindIpArg) ? TryGetLocalIPv4() : bindIpArg;
            string code = SessionCodeCodec.Encode(bindIp, port);

            AppLogger.Info($"Starting Host on {bindIp}:{port}");
            AppLogger.Info($"Share session code: {code}");
            OnHostSessionReady?.Invoke(bindIp, port, code);

            _networkManager.ServerManager.StartConnection();
            _networkManager.ClientManager.StartConnection();
        }

        /// <summary>Start a dedicated server</summary>
        public void StartServer(string bindIpArg, ushort port)
        {
            _isHost = false;
            _directTransport?.OverrideForHostBind(port, _networkManager);

            string bindIp = string.IsNullOrWhiteSpace(bindIpArg) ? TryGetLocalIPv4() : bindIpArg;
            string code = SessionCodeCodec.Encode(bindIp, port);

            AppLogger.Info($"Starting Server on {bindIp}:{port}");
            AppLogger.Info($"Clients can join using code: {code}");

            _networkManager.ServerManager.StartConnection();
        }

        /// <summary>Start a client using short join code</summary>
        public void StartClientWithCode(string code)
        {
            if (!string.IsNullOrWhiteSpace(code) && SessionCodeCodec.TryDecode(code, out string ip, out ushort decodedPort))
            {
                _directTransport?.OverrideForJoin(ip, decodedPort, _networkManager);
                AppLogger.Info($"Client decoded code to {ip}:{decodedPort}");
            }
            else
            {
                AppLogger.Warn("Join code invalid or empty");
            }

            AppLogger.Info("Starting Client");
            _networkManager.ClientManager.StartConnection();
        }

        /// <summary>Start a client using direct IP:port</summary>
        public void StartClientDirect(string ip, ushort port)
        {
            if (string.IsNullOrWhiteSpace(ip))
            {
                AppLogger.Warn("Host IP is empty");
                return;
            }

            _directTransport?.OverrideForJoin(ip, port, _networkManager);
            AppLogger.Info($"Client joining {ip}:{port}");
            _networkManager.ClientManager.StartConnection();
        }

        /// <summary>Lobby clicks this to load gameplay scene</summary>
        public void StartGameplay()
        {
            if (_serverLoadedScene)
                return;

            _sceneLoadRequested = true;

            if (_isHost && !_clientStarted)
            {
                AppLogger.Info("Start requested, waiting for local client to authenticate");
                return;
            }

            ServerLoadGameplayScene();
        }

        // Private methods
        private void OnServerState(ServerConnectionStateArgs args)
        {
            AppLogger.Info($"Server state changed to {args.ConnectionState}");

            if (args.ConnectionState == LocalConnectionState.Started)
                OnServerStarted?.Invoke();

            if (args.ConnectionState == LocalConnectionState.Started && !_serverLoadedScene)
            {
                if (_manualSceneStart)
                {
                    AppLogger.Info("Manual scene start enabled, waiting for Start button");
                    return;
                }

                if (_isHost && !_clientStarted)
                {
                    AppLogger.Info("Deferring gameplay scene load until local client is started");
                    return;
                }

                ServerLoadGameplayScene();
            }

            if (args.ConnectionState == LocalConnectionState.Stopped)
                AppLogger.Warn("Server stopped");
        }

        private void OnClientState(ClientConnectionStateArgs args)
        {
            AppLogger.Info($"Client state changed to {args.ConnectionState}");

            _clientStarted = args.ConnectionState == LocalConnectionState.Started;

            if (_clientStarted && _sceneLoadRequested && !_serverLoadedScene)
                ServerLoadGameplayScene();
        }

        private void ServerLoadGameplayScene()
        {
            string sceneName = ResolveSceneName(_gameScene);
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                AppLogger.Error($"Scene '{sceneName}' is not in Build Settings or cannot be loaded");
                return;
            }

            var sld = new SceneLoadData(new string[] { sceneName });
            _networkManager.SceneManager.LoadGlobalScenes(sld);
            _serverLoadedScene = true;
            AppLogger.Info($"Server loading gameplay scene '{sceneName}'");
        }

        private NetworkRunMode ParseMode(string modeStr)
        {
            return System.Enum.TryParse(modeStr, true, out NetworkRunMode parsed) ? parsed : NetworkRunMode.Client;
        }

        private string TryGetLocalIPv4()
        {
            try
            {
                string host = Dns.GetHostName();
                var entry = Dns.GetHostEntry(host);
                foreach (var ip in entry.AddressList)
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return ip.ToString();
            }
            catch { }
            return DEFAULT_LOCAL_IP;
        }

        private void StartClientByCodeOrIp(string code, string hostIp, ushort port)
        {
            if (!string.IsNullOrWhiteSpace(code) && SessionCodeCodec.TryDecode(code, out string ip, out ushort decodedPort))
            {
                _directTransport?.OverrideForJoin(ip, decodedPort, _networkManager);
                AppLogger.Info($"Client decoded session code to {ip}:{decodedPort}");
            }
            else if (!string.IsNullOrWhiteSpace(hostIp))
            {
                _directTransport?.OverrideForJoin(hostIp, port, _networkManager);
                AppLogger.Info($"Client joining direct {hostIp}:{port}");
            }
            else
            {
                AppLogger.Info("Client using default transport address and port");
            }

            AppLogger.Info("Starting Client");
            _networkManager.ClientManager.StartConnection();
        }
    }
}
