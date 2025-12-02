using System;
using System.Collections;
using Dissonance.Integrations.FishNet.Utils;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Dissonance.Integrations.FishNet
{
    // A Player object wrapper for Dissonance Voice
    public class DissonanceFishNetPlayer : NetworkBehaviour, IDissonancePlayer
    {
        [Tooltip("This transform will be used in positional voice processing. If unset, then GameObject's transform will be used.")]
        [SerializeField] private Transform trackingTransform;

        // SyncVar ensures that all observers know player ID, even late joiners
        private readonly SyncVar<string> _syncedPlayerName = new(string.Empty, settings: new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.Observers));

        // Captured DissonanceComms instance
        public DissonanceComms Comms { get; private set; }

        public string _cachedPlayerId;
        public string PlayerId => _cachedPlayerId ??= _syncedPlayerName.Value;
        public Vector3 Position => trackingTransform.position;
        public Quaternion Rotation => trackingTransform.rotation;
        public NetworkPlayerType Type => IsOwner ? NetworkPlayerType.Local : NetworkPlayerType.Remote;

        public bool IsTracking { get; private set; }

        private Coroutine _trackingCoroutine;

        private void Awake()
        {
            if (trackingTransform == null) trackingTransform = transform;
        }


        public override void OnStartNetwork()
        {
            base.OnStartNetwork();

            Comms = DissonanceFishNetComms.Instance.Comms;
            _syncedPlayerName.OnChange += OnSyncedPlayerNameUpdated;
        }
        private void OnEnable()
        {
            //ManageTrackingState(true);
        }

        private void OnDisable()
        {
            if (IsTracking)
                ManageTrackingStateCoroutine(false);
        }

        // Called by FishNet when object is spawned on client with authority
        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            Debug.Log($"DissonanceFishNetPlayer.OnOwnershipClient: prevOwner={prevOwner?.ClientId}, newOwner={Owner?.ClientId}, isOwner={IsOwner}");
            base.OnOwnershipClient(prevOwner);

            if (prevOwner == null || !IsOwner) return;

            DissonanceFishNetComms fishNetComms = DissonanceFishNetComms.Instance;
            if (fishNetComms == null)
            {
                LoggingHelper.Logger.Error("Could not find any DissonanceFishNetComms instance! This DissonancePlayer instance will not work!");
                return;
            }

            // Configure Player name
            fishNetComms.Comms.LocalPlayerNameChanged += SetPlayerName;
            if (fishNetComms.Comms.LocalPlayerName == null)
            {
                string playerGuid = Guid.NewGuid().ToString();
                Debug.Log($"No player name set in DissonanceFishNetComms, using random GUID: {playerGuid}");
                fishNetComms.Comms.LocalPlayerName = playerGuid;
            }
            else
            {
                SetPlayerName(fishNetComms.Comms.LocalPlayerName);
            }
        }

        private void SetPlayerName(string playerName)
        {


            if (IsOwner) ServerRpcSetPlayerName(playerName);
        }

        [ServerRpc]
        private void ServerRpcSetPlayerName(string playerName)
        {
            if (string.IsNullOrEmpty(playerName) || playerName.Equals(_syncedPlayerName.Value, StringComparison.Ordinal))
                return;
            _syncedPlayerName.Value = playerName;
        }

        private void OnSyncedPlayerNameUpdated(string _, string updatedName, bool __)
        {
            if (string.IsNullOrEmpty(updatedName))
            {
                Debug.LogWarning("[DissonanceFishNetPlayer] Received empty player name update, ignoring.");
                return;
            }

            if (updatedName.Equals(PlayerId, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning("[DissonanceFishNetPlayer] Player name unchanged, ignoring.");
                return;
            }

            if (IsTracking) ManageTrackingState(false);


            _cachedPlayerId = updatedName;
            ManageTrackingState(true);


        }


        private void ManageTrackingState(bool track)
        {
            // Stop any existing coroutine
            if (_trackingCoroutine != null)
            {
                StopCoroutine(_trackingCoroutine);
                _trackingCoroutine = null;
            }

            // Start new coroutine to manage tracking state
            _trackingCoroutine = StartCoroutine(ManageTrackingStateCoroutine(track));
        }


        private IEnumerator ManageTrackingStateCoroutine(bool track)
        {

            Debug.Log("[DissonanceFishNetPlayer] ManageTrackingState called. track: " + track + ", IsTracking: " + IsTracking + ", IsOwner: " + IsOwner + "PlayerId: " + PlayerId);

            // Check if you should change tracking state
            if (IsTracking == track || string.IsNullOrEmpty(PlayerId)) yield break;

            if (track)
            {
                // If we are starting tracking, wait until the Comms instance exists AND is initialized
                while (DissonanceFishNetComms.Instance == null || !DissonanceFishNetComms.Instance.IsInitialized)
                {
                    Debug.Log("[DissonanceFishNetPlayer] Waiting for DissonanceFishNetComms to initialize...");
                    yield return null;
                }
            }
            else
            {
                // If we are stopping tracking, wait until the Comms instance exists (optional, but safer)
                while (DissonanceFishNetComms.Instance == null)
                {
                    Debug.Log("[DissonanceFishNetPlayer] Waiting for DissonanceFishNetComms instance...");
                    yield return null;
                }
            }
            DissonanceComms comms = DissonanceFishNetComms.Instance.Comms;

            if (track)
            {
                Debug.Log("<color=green>[DissonanceFishNetPlayer]</color> Tracking player position for PlayerId: " + PlayerId);
                comms.TrackPlayerPosition(this);
            }
            else
            {
                Debug.Log("<color=green>[DissonanceFishNetPlayer]</color> Stopping tracking player position for PlayerId: " + PlayerId);
                comms.StopTracking(this);
            }

            IsTracking = track;
            Debug.Log("<color=green>[DissonanceFishNetPlayer]</color> " + (track ? "Started" : "Stopped") + " tracking player position for PlayerId: " + PlayerId);

        }
    }
}
