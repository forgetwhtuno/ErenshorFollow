using System;
using System.Globalization;

namespace ErenshorFollow
{
    internal struct SuiteHubPresenceState
    {
        // Present  : a Forgotten Roads Hub exists at all (descriptor parsed, or the live IPC
        //            endpoint is registered even though this second's payload was unusable).
        // Usable   : Hub is Ready and advertises an available retained UI.
        // QuickCloseVerified : Hub additionally advertises a verified central quick-close binding.
        internal readonly bool Present;
        internal readonly bool Usable;
        internal readonly bool QuickCloseVerified;

        internal SuiteHubPresenceState(bool present, bool usable, bool quickCloseVerified)
        {
            Present = present;
            Usable = usable;
            QuickCloseVerified = quickCloseVerified;
        }
    }

    // Pure parser for the shared Hub presence descriptor. Missing, malformed, duplicate, or
    // unsupported fields fail closed. Quick-close is stronger than ordinary Hub usability: both
    // quickCloseContract=1 and quickClose=1 must be present on a usable retained Hub.
    internal static class SuiteHubPresencePolicy
    {
        // Runtime endpoint presence is stronger evidence that Hub exists than a transient malformed
        // or throwing payload. Never reinterpret a live Hub function as "standalone" and start a
        // competing Escape poll merely because its describe call failed this second.
        internal static SuiteHubPresenceState FromEndpoint(bool endpointPresent, string payload)
        {
            if (!endpointPresent) return Bad();
            SuiteHubPresenceState parsed = Parse(payload);
            return parsed.Present ? parsed : new SuiteHubPresenceState(true, false, false);
        }

        internal static SuiteHubPresenceState Parse(string payload)
        {
            if (string.IsNullOrEmpty(payload) || payload.Length > 2048)
                return Bad();

            string protocol = null, module = null, status = null, uiAvailable = null;
            string quickCloseContract = null, quickClose = null;
            string[] fields = payload.Split('&');
            for (int i = 0; i < fields.Length; i++)
            {
                int equals = fields[i].IndexOf('=');
                if (equals <= 0) return Bad();
                string key = fields[i].Substring(0, equals);
                string value = fields[i].Substring(equals + 1);
                if (key == "protocol") { if (protocol != null) return Bad(); protocol = value; }
                else if (key == "module") { if (module != null) return Bad(); module = value; }
                else if (key == "status") { if (status != null) return Bad(); status = value; }
                else if (key == "uiAvailable") { if (uiAvailable != null) return Bad(); uiAvailable = value; }
                else if (key == "quickCloseContract") { if (quickCloseContract != null) return Bad(); quickCloseContract = value; }
                else if (key == "quickClose") { if (quickClose != null) return Bad(); quickClose = value; }
            }

            bool present = string.Equals(protocol, "1", StringComparison.Ordinal)
                && string.Equals(module, "suitehub", StringComparison.Ordinal)
                && status != null;
            bool ready = present && string.Equals(status, "Ready", StringComparison.Ordinal);
            bool usable = ready
                && string.Equals(uiAvailable, "true", StringComparison.OrdinalIgnoreCase);
            bool verified = ready
                && string.Equals(quickCloseContract, "1", StringComparison.Ordinal)
                && string.Equals(quickClose, "1", StringComparison.Ordinal);
            return new SuiteHubPresenceState(present, usable, verified);
        }

        private static SuiteHubPresenceState Bad()
        {
            return new SuiteHubPresenceState(false, false, false);
        }
    }

    internal static class SuiteUiStatePolicy
    {
        internal static string Build(string moduleId, bool open, int sortOrder, double activated)
        {
            if (string.IsNullOrEmpty(moduleId)) return string.Empty;
            if (sortOrder < -10000) sortOrder = -10000;
            if (sortOrder > 10000) sortOrder = 10000;
            if (double.IsNaN(activated) || double.IsInfinity(activated) || activated < 0d) activated = 0d;
            return "protocol=1&module=" + moduleId
                + "&open=" + (open ? "true" : "false")
                + "&closeable=true&sortOrder=" + sortOrder.ToString(CultureInfo.InvariantCulture)
                + "&activated=" + activated.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }


    internal enum SuiteEscapeAuthority
    {
        // No Hub in the load order: this module may own a local Escape fallback for its own UI.
        StandaloneFallback,
        // Hub exists but central quick-close is not verified, or this module never registered its
        // provider. The module must NOT poll Escape, otherwise several Forgotten Roads modules
        // compete for the same key. The player uses each window's explicit X/close control.
        ExplicitCloseControls,
        // Hub verified central quick-close AND this module registered its provider: Hub owns it.
        HubVerified
    }

    internal static class FollowQuickClosePolicy
    {
        internal static SuiteEscapeAuthority Resolve(bool hubPresent, bool verifiedHubQuickClose, bool providerRegistered)
        {
            if (!hubPresent) return SuiteEscapeAuthority.StandaloneFallback;
            if (verifiedHubQuickClose && providerRegistered) return SuiteEscapeAuthority.HubVerified;
            return SuiteEscapeAuthority.ExplicitCloseControls;
        }

        internal static bool ShouldHandleEscapeLocally(bool ownedUiOpen, bool hubPresent,
            bool verifiedHubQuickClose, bool providerRegistered)
        {
            return ownedUiOpen
                && Resolve(hubPresent, verifiedHubQuickClose, providerRegistered) == SuiteEscapeAuthority.StandaloneFallback;
        }
    }

    internal static class FollowStartTransitionPolicy
    {
        // Restarting or switching an already-active movement owner must first return the current
        // PlayerControl movement/animation state to neutral before the new target is bound.
        internal static bool ShouldReleaseMovementBeforeStart(bool currentlyActive)
        {
            return currentlyActive;
        }

        // Expedition owns its own explicit pause/resume/cancel controls. The generic stop action is
        // useful only for an ordinary Follow/Lead leg and should not appear as a redundant no-op.
        internal static bool ShouldOfferGenericStop(bool expeditionActive, bool followActive, bool leadActive)
        {
            return !expeditionActive && (followActive || leadActive);
        }
    }

    internal enum FollowActorEligibility
    {
        Eligible,
        MissingOrDead,
        RemoteAuthority,
        LeftParty
    }

    // Pure admission rule shared by command selection and long-running direct-follow validation.
    // Leader/Expedition movement has its own established ownership lifecycle and is not widened by
    // this policy.
    internal static class FollowActorEligibilityPolicy
    {
        internal static FollowActorEligibility Evaluate(bool usable, bool remoteAuthority, bool inCurrentParty)
        {
            if (!usable) return FollowActorEligibility.MissingOrDead;
            if (remoteAuthority) return FollowActorEligibility.RemoteAuthority;
            if (!inCurrentParty) return FollowActorEligibility.LeftParty;
            return FollowActorEligibility.Eligible;
        }
    }

    internal static class FollowUiPositionPolicy
    {
        internal const float Unset = -1f;

        internal static float InterpretStoredAxis(float stored)
        {
            if (!IsFinite(stored) || stored < 0f || stored > 1f) return Unset;
            return stored;
        }

        internal static float ResolveAxis(float stored, float defaultNormalized, float extent, float size)
        {
            float normalized = InterpretStoredAxis(stored);
            if (normalized < 0f) normalized = Clamp(defaultNormalized, 0f, 1f);
            if (!IsFinite(extent) || extent <= 0f) return 0f;
            if (!IsFinite(size) || size < 0f) size = 0f;
            float max = Math.Max(0f, extent - size);
            return Clamp(normalized * extent, 0f, max);
        }

        internal static float NormalizeAxis(float pixels, float extent)
        {
            if (!IsFinite(pixels) || !IsFinite(extent) || extent <= 0f) return 0f;
            return Clamp(pixels / extent, 0f, 1f);
        }

        private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static float Clamp(float value, float min, float max)
        {
            if (!IsFinite(value)) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }

    internal enum FollowUiSurfaceKind
    {
        None,
        ExpeditionStatus,
        SimActions,
        ExpeditionSetup
    }

    internal struct FollowUiSurfaceCandidate
    {
        internal readonly FollowUiSurfaceKind Kind;
        internal readonly bool Open;
        internal readonly int SortOrder;
        internal readonly double Activated;

        internal FollowUiSurfaceCandidate(FollowUiSurfaceKind kind, bool open, int sortOrder, double activated)
        {
            Kind = kind;
            Open = open;
            SortOrder = sortOrder;
            Activated = activated;
        }
    }

    internal static class FollowUiSurfacePolicy
    {
        internal static FollowUiSurfaceCandidate SelectTopmost(params FollowUiSurfaceCandidate[] candidates)
        {
            FollowUiSurfaceCandidate best = new FollowUiSurfaceCandidate(FollowUiSurfaceKind.None, false, 0, 0d);
            if (candidates == null) return best;
            for (int i = 0; i < candidates.Length; i++)
            {
                FollowUiSurfaceCandidate candidate = candidates[i];
                if (!candidate.Open) continue;
                if (!best.Open || candidate.SortOrder > best.SortOrder ||
                    (candidate.SortOrder == best.SortOrder && candidate.Activated > best.Activated) ||
                    (candidate.SortOrder == best.SortOrder && Math.Abs(candidate.Activated - best.Activated) < 0.0001d &&
                     (int)candidate.Kind > (int)best.Kind))
                    best = candidate;
            }
            return best;
        }
    }

}
