using UnityEngine;

namespace BattleshipsVR.Bootstrap
{
    /// <summary>Central enums and constants for networking args and defaults</summary>
    public static class NetConstants
    {
        public enum NetworkRunMode { Host = 0, Client = 1, Server = 2 }
        public enum SceneId { GameMain = 0 }

        public const string ARG_MODE = "-mode=";
        public const string ARG_CODE = "-code=";
        public const string ARG_HOST_IP = "-hostip=";
        public const string ARG_PORT = "-port=";
        public const string ARG_SCENE = "-scene=";

        public const ushort DEFAULT_PORT = 7770;
        public const string DEFAULT_LOCAL_IP = "127.0.0.1";

        /// <summary>Maps scene id to asset name for load calls</summary>
        public static string ResolveSceneName(SceneId id)
        {
            switch (id)
            {
                case SceneId.GameMain: return "GameMainScene";
                default: return "GameMainScene";
            }
        }
    }
}
