using FishNet.Managing;

namespace BattleshipsVR.Bootstrap
{
    /// <summary>Abstraction to switch transports later without touching gameplay code</summary>
    public interface INetworkTransportSetup
    {
        /// <summary>Apply default transport settings for this build profile</summary>
        void Configure(NetworkManager manager);
    }
}
