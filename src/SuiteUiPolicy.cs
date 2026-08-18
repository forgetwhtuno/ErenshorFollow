using Lunaris;
using Lunaris.IPC;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ErenshorFollow
{
    internal static class SuiteUiPolicy
    {
        private const float StableReadySeconds = 1.0f;
        private const float HubProbeSeconds = 1.0f;
        private const string HubPresenceEndpoint = "forgetwhtuno.erenshor.suitehub.v1.describe";

        private static float _rawReadySince = -1f;
        private static int _readySceneHandle = int.MinValue;
        private static bool _canMoveLatched;
        private static bool _acquired;
        private static float _nextHubProbe;
        private static SuiteHubPresenceState _hubState;
        private static IAuraSubscriber<string> _hubPresence;

        internal static void InitializeHubPresence(LunarisPlugin owner)
        {
            _hubPresence = null;
            _hubState = new SuiteHubPresenceState(false, false, false);
            _nextHubProbe = 0f;
            if (owner == null) return;
            try { _hubPresence = owner.IPCAuraSubscriber<string>(HubPresenceEndpoint); }
            catch { _hubPresence = null; }
        }

        internal static bool IsGameplayReady()
        {
            if (!RawGameplayReady())
            {
                _rawReadySince = -1f;
                _readySceneHandle = int.MinValue;
                _canMoveLatched = false;
                _acquired = false;
                return false;
            }
            Scene scene = SceneManager.GetActiveScene();
            if (_readySceneHandle != scene.handle)
            {
                _readySceneHandle = scene.handle;
                _rawReadySince = Time.unscaledTime;
                _canMoveLatched = false;
                _acquired = false;
            }
            if (_rawReadySince < 0f) _rawReadySince = Time.unscaledTime;
            if (_acquired) return true;
            try { if (GameData.PlayerControl != null && GameData.PlayerControl.CanMove) _canMoveLatched = true; } catch { }
            if (!_canMoveLatched || Time.unscaledTime - _rawReadySince < StableReadySeconds) return false;
            _acquired = true;
            return true;
        }

        // A registered Hub IPC function is authoritative proof that Hub is loaded, independent of
        // whether this second's describe payload happened to be usable.
        internal static bool IsHubPresent()
        {
            ProbeHub();
            return _hubState.Present;
        }

        internal static bool IsHubAvailable()
        {
            ProbeHub();
            return _hubState.Usable;
        }

        internal static bool IsHubQuickCloseVerified()
        {
            ProbeHub();
            return _hubState.QuickCloseVerified;
        }

        private static void ProbeHub()
        {
            if (Time.unscaledTime < _nextHubProbe) return;
            _nextHubProbe = Time.unscaledTime + HubProbeSeconds;
            _hubState = new SuiteHubPresenceState(false, false, false);
            bool endpointPresent = false;
            try
            {
                endpointPresent = _hubPresence != null && _hubPresence.HasFunction;
                if (!endpointPresent) return;
                string payload = null;
                try { payload = _hubPresence.InvokeFunc(); } catch { }
                _hubState = SuiteHubPresencePolicy.FromEndpoint(true, payload);
            }
            catch
            {
                _hubState = endpointPresent
                    ? new SuiteHubPresenceState(true, false, false)
                    : new SuiteHubPresenceState(false, false, false);
            }
        }

        internal static void Reset()
        {
            _rawReadySince = -1f;
            _readySceneHandle = int.MinValue;
            _canMoveLatched = false;
            _acquired = false;
            _nextHubProbe = 0f;
            _hubState = new SuiteHubPresenceState(false, false, false);
            _hubPresence = null;
        }

        private static bool RawGameplayReady()
        {
            try
            {
                if (GameData.InCharSelect || GameData.Zoning) return false;
                if (GameData.PlayerControl == null || GameData.PlayerControl.Myself == null) return false;
                Character player = GameData.PlayerControl.Myself;
                if (player.MyStats == null || player.gameObject == null || !player.gameObject.activeInHierarchy) return false;
                Scene scene = SceneManager.GetActiveScene();
                if (!scene.IsValid() || !scene.isLoaded) return false;
                if (GameData.SimMngr == null || GameData.SimPlayerGrouping == null || GameData.GroupMembers == null) return false;
                return true;
            }
            catch { return false; }
        }
    }
}
