using System;
using System.Reflection;

namespace ErenshorFollow
{
    // Optional, reflection-only sink for verified expedition lifecycle facts.
    //
    // Expeditions emit facts; a companion mod decides whether they matter socially. This bridge has no
    // gameplay side effects, never invokes an LLM, and silently does nothing when Deep Sims is absent.
    internal static class ExpeditionIntegrationBridge
    {
        private static Type _pluginType;
        private static MethodInfo _structured;
        private static MethodInfo _observed;
        private static FieldInfo _instanceField;
        private static volatile bool _resolved;

        static ExpeditionIntegrationBridge()
        {
            // BepInEx load order is not guaranteed, so a resolve that ran first must not cache "absent".
            try { AppDomain.CurrentDomain.AssemblyLoad += delegate { _resolved = false; }; }
            catch { }
        }

        internal static void Emit(string eventType, ExpeditionSession session)
        {
            if (session == null || string.IsNullOrWhiteSpace(eventType)) return;
            try { Deliver(eventType, session); }
            catch (Exception ex)
            {
                try { if (ErenshorFollowPlugin.Instance != null) ErenshorFollowPlugin.Instance.LogDebug("Expedition bridge skipped " + eventType + ": " + ex.Message); }
                catch { }
            }
        }

        private static void Deliver(string eventType, ExpeditionSession session)
        {
            Resolve();
            object instance = _instanceField == null ? null : _instanceField.GetValue(null);
            if (instance == null) return;

            string leader = Safe(session.LeaderName);
            string origin = Safe(session.OriginZone);
            string destination = Safe(session.DestinationName);
            string current = Safe(session.CurrentZone);
            string objective = session.Objective == ExpeditionObjective.Return ? "return" : "outbound";
            string reasonCode = ReasonCode(session);

            // Preferred: a structured, primitive-only call so the social layer receives the leader role
            // directly instead of inferring it from prose.
            if (_structured != null)
            {
                _structured.Invoke(instance, new object[]
                {
                    eventType, leader, origin, destination, current, objective, session.CombatInterruptions, reasonCode
                });
                return;
            }

            // Compatibility fallback: the same generic observed-event path Practice Duels uses. Prose
            // cannot prove who led whom, so nothing emitted this way is ever marked as a durable memory.
            if (_observed == null) return;
            _observed.Invoke(instance, new object[]
            {
                eventType, Describe(eventType, leader, destination, current), Importance(eventType), false, BaseChance(eventType)
            });
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            _pluginType = null;
            _structured = null;
            _observed = null;
            _instanceField = null;
            const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try { type = assembly.GetType("ErenshorDeepSims.DeepSimsPlugin", false); }
                catch { continue; }
                if (type == null) continue;
                _pluginType = type;
                _instanceField = type.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                _structured = type.GetMethod("NotifyExpeditionEvent", Any, null,
                    new Type[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(int), typeof(string) }, null);
                _observed = type.GetMethod("NotifyObservedGameEvent", Any, null,
                    new Type[] { typeof(string), typeof(string), typeof(int), typeof(bool), typeof(double) }, null);
                return;
            }
        }

        private static string ReasonCode(ExpeditionSession session)
        {
            if (session.FailureReason != ExpeditionFailureReason.None) return session.FailureReason.ToString();
            if (session.PauseReason != ExpeditionPauseReason.None) return session.PauseReason.ToString();
            return "None";
        }

        private static string Describe(string eventType, string leader, string destination, string current)
        {
            switch (eventType)
            {
                case "expedition_arrived": return leader + " led the group to " + destination + ".";
                case "expedition_zone_entered": return "The group entered " + current + " while travelling to " + destination + ".";
                case "expedition_failed": return "The expedition to " + destination + " ended early.";
                case "expedition_cancelled": return "The expedition to " + destination + " was called off.";
                default: return leader + " is leading the group to " + destination + " (" + eventType + ").";
            }
        }

        // Arrival is the only event worth a social candidate. Everything else is low-value context that
        // should normally stay silent, so it is scored far below the director's speaking bar.
        private static int Importance(string eventType)
        {
            return eventType == "expedition_arrived" ? 55 : 10;
        }

        private static double BaseChance(string eventType)
        {
            return eventType == "expedition_arrived" ? 0.35 : 0.0;
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
