using System;
using System.Reflection;

namespace ErenshorFollow
{
    // Optional reflection-only check. Erenshor COOP gives remote humans a SimPlayer component,
    // so SimPlayer alone is not enough to decide that an entity is safe to follow or lead.
    internal static class CoopCompatibility
    {
        private static Type _networkedPlayer;
        private static Type _legacyNetworkedPlayer;
        private static Type _networkedSim;
        private static volatile bool _resolved;

        // BepInEx load order is not guaranteed, so a resolve that ran before ErenshorCoop's
        // assembly loaded would otherwise cache "not installed" for the rest of the session.
        static CoopCompatibility()
        {
            try { AppDomain.CurrentDomain.AssemblyLoad += delegate { _resolved = false; }; }
            catch { }
        }

        internal static bool IsRemoteHuman(SimPlayer sim)
        {
            if (sim == null || sim.gameObject == null) return false;
            Resolve();
            try
            {
                // NetworkedSim is a Sim owned and driven by another COOP client; treating it as a
                // local follow/lead target would fight the network sync for its position and state.
                return (_networkedPlayer != null && sim.gameObject.GetComponent(_networkedPlayer) != null) ||
                       (_legacyNetworkedPlayer != null && sim.gameObject.GetComponent(_legacyNetworkedPlayer) != null) ||
                       (_networkedSim != null && sim.gameObject.GetComponent(_networkedSim) != null);
            }
            catch { return false; }
        }

        private static void Resolve()
        {
            if (_resolved) return;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (_networkedPlayer == null) _networkedPlayer = assembly.GetType("ErenshorCoop.NetworkedPlayer", false);
                if (_legacyNetworkedPlayer == null) _legacyNetworkedPlayer = assembly.GetType("ErenshorCoop.Client.NetworkedPlayer", false);
                if (_networkedSim == null) _networkedSim = assembly.GetType("ErenshorCoop.NetworkedSim", false);
            }
            _resolved = true;
        }
    }
}
