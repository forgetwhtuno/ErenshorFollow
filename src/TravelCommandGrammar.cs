using System;

namespace ErenshorFollow
{
    // Pure deterministic grammar for party-chat travel requests. Gameplay ownership stays in Follow;
    // this helper only decides whether a message is the direct command form Follow accepts.
    internal static class TravelCommandGrammar
    {
        private static readonly string[] LeadPhrases =
        {
            " lead us to ",
            " lead me to ",
            " take us to ",
            " take me to ",
            " lead the way to ",
            " lead the group to ",
            " show us the way to ",
            " guide us to "
        };

        internal static bool TryParseLeadRequest(string message, out string leader, out string destination)
        {
            leader = null;
            destination = null;
            if (string.IsNullOrWhiteSpace(message)) return false;

            string lower = message.ToLowerInvariant();
            int phraseAt = -1;
            string phrase = null;
            for (int i = 0; i < LeadPhrases.Length; i++)
            {
                string candidate = LeadPhrases[i];
                int found = lower.IndexOf(candidate, StringComparison.Ordinal);
                if (found > 0 && (phraseAt < 0 || found < phraseAt))
                {
                    phraseAt = found;
                    phrase = candidate;
                }
            }
            if (phraseAt <= 0 || phrase == null) return false;

            string directLeader = message.Substring(0, phraseAt).Trim().TrimEnd(',', ':');
            if (!IsDirectLeaderAddress(directLeader)) return false;

            string directDestination = message.Substring(phraseAt + phrase.Length)
                .Trim().TrimEnd('.', '!', '?').Trim();
            if (directDestination.Length == 0) return false;

            leader = directLeader;
            destination = directDestination;
            return true;
        }

        private static bool IsDirectLeaderAddress(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOf('?') >= 0) return false;

            string trimmed = value.Trim();
            int split = trimmed.IndexOfAny(new[] { ' ', '\t' });
            string first = (split < 0 ? trimmed : trimmed.Substring(0, split)).Trim().ToLowerInvariant();

            // These introduce a question/opinion rather than directly addressing the leader. Keep the
            // backstop deliberately narrower than ordinary conversation: Follow owns only direct orders.
            switch (first)
            {
                case "can":
                case "could":
                case "would":
                case "do":
                case "does":
                case "did":
                case "who":
                case "what":
                case "when":
                case "where":
                case "why":
                case "how":
                case "i":
                case "we":
                case "you":
                case "they":
                case "should":
                case "maybe":
                case "think":
                    return false;
                default:
                    return true;
            }
        }
    }
}
