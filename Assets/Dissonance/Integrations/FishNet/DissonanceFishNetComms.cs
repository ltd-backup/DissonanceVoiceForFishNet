using System.Runtime.CompilerServices;
using Dissonance.Integrations.FishNet.Broadcasts;
using Dissonance.Integrations.FishNet.Utils;
using Dissonance.Networking;
using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;

namespace Dissonance.Integrations.FishNet
{
    // Comms implementation for Dissonance Voice. Used Unit, due to FishNet is connected while using Dissonance Voice
    public sealed class DissonanceFishNetComms : BaseCommsNetwork<DissonanceFishNetServer, DissonanceFishNetClient, DissonanceFishNetConnection, Unit, Unit>
    {
        public static DissonanceFishNetComms Instance { get; private set; }

        public DissonanceComms Comms { get; private set; }
        internal NetworkManager NetworkManager { get; private set; }

        private NetworkMode _currentNetworkMode = NetworkMode.None;
        private bool _subscribed;


        private void Awake()
        {
            // Check if there is no double comms instance
            if (Instance != null)
            {
                LoggingHelper.Logger.Error("Only one DissonanceFishNetComms instance is allowed to exist at the time! This one will not work.");
                return;
            }

            // Initialize this comms instance & log
            NetworkManager = InstanceFinder.NetworkManager;
            Comms = GetComponent<DissonanceComms>();
            Instance = this;
            ManageNetworkEvents(true);
            LoggingHelper.Logger.Info("FishNet comms initialized successfully!");
        }

        private void OnEnable()
        {
            ManageNetworkEvents(true);
        }

        protected override void OnDisable()
        {
            ManageNetworkEvents(false);

            base.OnDisable();
        }

        protected override void Initialize()
	{
            base.Initialize();
	    
            // Register no broadcast handler so errors can be captured easier
            NetworkManager.ServerManager.RegisterBroadcast<DissonanceFishNetBroadcast>(NullBroadcastReceivedHandler);
            NetworkManager.ClientManager.RegisterBroadcast<DissonanceFishNetBroadcast>(NullBroadcastReceivedHandler);
            
            // Now, start Dissonance Voice, depending on current FishNet state
            // If not offline (FN is running), then it's gonna adjust to current FishNet's mode
            // Otherwise, in Awake() Network event will detect when FishNet will go online & adjust running mode
            AdjustDissonanceRunningMode();
        }

	protected override DissonanceFishNetServer CreateServer(Unit connectionParameters)
	{
            LoggingHelper.Logger.Trace("Creating FishNet server...");
            return new DissonanceFishNetServer(this);
        }

	protected override DissonanceFishNetClient CreateClient(Unit connectionParameters)
        {
            LoggingHelper.Logger.Trace("Creating FishNet client...");
	    return new DissonanceFishNetClient(this);
        }

        // Helper method that subscribes or unsubscribed 
        private void ManageNetworkEvents(bool subscribe)
        {
            if (subscribe && !_subscribed)
            {
                NetworkManager.ServerManager.OnServerConnectionState += ServerManagerOnOnServerConnectionState;
                NetworkManager.ClientManager.OnClientConnectionState += ClientManagerOnOnClientConnectionState;
                NetworkManager.ClientManager.OnAuthenticated += ClientManagerOnAuthenticated;

                _subscribed = true;
            }
            else if(!subscribe && _subscribed)
            {
                NetworkManager.ServerManager.OnServerConnectionState -= ServerManagerOnOnServerConnectionState;
                NetworkManager.ClientManager.OnClientConnectionState -= ClientManagerOnOnClientConnectionState;
                NetworkManager.ClientManager.OnAuthenticated -= ClientManagerOnAuthenticated;

                _subscribed = false;
            }
        }

        #region Dissonance network peer status callbacks

        private void ClientManagerOnOnClientConnectionState(ClientConnectionStateArgs obj)
        {
            OnFishNetStateDirty(obj.ConnectionState);
        }

        private void ServerManagerOnOnServerConnectionState(ServerConnectionStateArgs obj)
        {
            OnFishNetStateDirty(obj.ConnectionState);
        }
        private void ClientManagerOnAuthenticated()
        {
            // Since Dissonance has been modified to not start before the client is authenticated,
            // Adjust Dissonance running mode again in this client authentication callback.

            // Ignore if is Server or Host, as it has already started.
            if (NetworkManager.ServerManager.Started == false)
                AdjustDissonanceRunningMode();
        }

        private void OnFishNetStateDirty(LocalConnectionState state)
        {
            if (state != LocalConnectionState.Started && state != LocalConnectionState.Stopped) return;
            AdjustDissonanceRunningMode();
        }

        #endregion


        #region Running Dissonance

        // NOTE: Dissonance can change it's running mode AT RUNTIME!
        // So when callbacks detect a change, I can just call this method to adjust it
        private void AdjustDissonanceRunningMode()
        {
            // Now, start Dissonance Voice, depending on current FishNet state
            // This is true for server & host mode
            if (NetworkManager.ServerManager.Started)
            {
                // Run host or dedicated server
                bool isHostStarted = NetworkManager.ServerManager.Started && NetworkManager.ClientManager.Started;
                
                if (isHostStarted)
                {
                    if (_currentNetworkMode == NetworkMode.Host) return;
                    
                    _currentNetworkMode = NetworkMode.Host;
                    RunAsHost(Unit.None, Unit.None);
                }

                // If is server (upper condition) and isn't a host, then it's a server
                else
                {
                    if (_currentNetworkMode == NetworkMode.DedicatedServer) return;
                    
                    _currentNetworkMode = NetworkMode.DedicatedServer;
                    RunAsDedicatedServer(Unit.None);
                }
                
                // Log changes
                LoggingHelper.RunningAs(_currentNetworkMode);
            }
            
            // If client only & dirty, start dissonance
            else if(NetworkManager.ClientManager.Started)
            {
                // Do not start Dissonance before the client is authenticated.
                // Otherwise, Dissonance will call Broadcast before authentication,
                // And the server will kick the client.
                if (NetworkManager.ClientManager.Connection.IsAuthenticated == false) return;

                if (_currentNetworkMode == NetworkMode.Client) return;

                _currentNetworkMode = NetworkMode.Client;
                RunAsClient(Unit.None);
                LoggingHelper.RunningAs(NetworkMode.Client);
            }

            // If offline & dirty, stop dissonance
            else if (NetworkManager.IsOffline)
            {
                if (_currentNetworkMode == NetworkMode.None) return;
                bool isClientOnly = _currentNetworkMode == NetworkMode.Client;
                
                _currentNetworkMode = NetworkMode.None;
                Stop();
                
                if (isClientOnly) LoggingHelper.StoppingAs(NetworkMode.Client);
                else LoggingHelper.StoppingAs(NetworkManager.IsHostStarted ? NetworkMode.Host : NetworkMode.DedicatedServer);
            }
        }

        #endregion
        
        
        #region Debugging

        internal static void NullBroadcastReceivedHandler(NetworkConnection source, DissonanceFishNetBroadcast broadcast, Channel channel) => NullBroadcastLogger(broadcast);
        internal static void NullBroadcastReceivedHandler(DissonanceFishNetBroadcast broadcast, Channel channel) => NullBroadcastLogger(broadcast);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void NullBroadcastLogger(DissonanceFishNetBroadcast broadcast)
        {
            LoggingHelper.Logger.Debug("Dissonance comms instance has not been initialized! Disregarding incoming packet.");
            broadcast.ReleaseBuffer();
        }

        #endregion
    }
}
