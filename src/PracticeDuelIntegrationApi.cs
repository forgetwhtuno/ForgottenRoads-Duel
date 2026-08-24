using System;

namespace ErenshorDuel
{
    // Reflection-friendly v1 result retained exactly for existing optional consumers.
    public sealed class DuelRequestResult
    {
        public string RequestId { get; private set; }
        public string Status { get; private set; }
        public string ReasonToken { get; private set; }

        internal DuelRequestResult(string requestId, string status, string reasonToken)
        {
            RequestId = Clean(requestId, 96);
            Status = Clean(status, 32);
            ReasonToken = Clean(reasonToken, 64);
        }

        internal static DuelRequestResult Queued(string requestId)
        { return new DuelRequestResult(requestId, "queued", string.Empty); }

        internal static DuelRequestResult Rejected(string reasonToken)
        { return new DuelRequestResult(string.Empty, "rejected", reasonToken); }

        private static string Clean(string value, int max)
        {
            string clean = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return clean.Length <= max ? clean : clean.Substring(0, max);
        }
    }

    // Existing v1 autonomous surface. Its method shape/semantics remain unchanged.
    public static class PracticeDuelIntegrationApi
    {
        public const int ContractVersion = 1;
        public static bool IsAvailable
        {
            get
            {
                ErenshorDuelPlugin plugin = ErenshorDuelPlugin.Instance;
                return plugin != null && plugin.RuntimeHooksReady;
            }
        }

        public static DuelRequestResult RequestAutonomousChallenge(string opponentName, string source)
        {
            ErenshorDuelPlugin plugin = ErenshorDuelPlugin.Instance;
            if (plugin == null || !plugin.RuntimeHooksReady) return DuelRequestResult.Rejected("runtime_unavailable");
            return plugin.RequestAutonomousChallenge(opponentName, source);
        }
    }

    // v2 request/result contract is additive and intentionally separate from v1. Callers provide a
    // stable RequestId/timestamp when they have one; Duel generates missing IDs and owns DuelId.
    public sealed class DuelAutonomousRequestResultV2
    {
        public string RequestId { get; private set; }
        public string DuelId { get; private set; }
        public string Status { get; private set; }
        public string ReasonToken { get; private set; }
        public string Source { get; private set; }
        public string Origin { get; private set; }
        public string ParticipantA { get; private set; }
        public string ParticipantB { get; private set; }
        public long RequestedUtcTicks { get; private set; }

        internal DuelAutonomousRequestResultV2(PendingAutonomousDuelRequest request, string status, string reasonToken)
        {
            RequestId = request == null ? string.Empty : Clean(request.RequestId, 96);
            DuelId = request == null ? string.Empty : Clean(request.DuelId, 96);
            Status = Clean(status, 32);
            ReasonToken = Clean(reasonToken, 64);
            Source = request == null ? string.Empty : Clean(request.Source, 64);
            Origin = "autonomous";
            ParticipantA = request == null ? string.Empty : Clean(request.ParticipantA, 80);
            ParticipantB = request == null ? string.Empty : Clean(request.ParticipantB, 80);
            RequestedUtcTicks = request == null ? 0L : request.RequestedUtcTicks;
        }

        private static string Clean(string value, int max)
        {
            string clean = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return clean.Length <= max ? clean : clean.Substring(0, max);
        }
    }

    public static class PracticeDuelIntegrationApiV2
    {
        public const int ContractVersion = 2;
        public static bool IsAvailable { get { return PracticeDuelIntegrationApi.IsAvailable; } }

        public static DuelAutonomousRequestResultV2 RequestPlayerChallenge(string opponentName, string source,
            string requestId, long requestedUtcTicks)
        {
            ErenshorDuelPlugin plugin = ErenshorDuelPlugin.Instance;
            if (plugin == null) return ErenshorDuelPlugin.RejectUnboundV2("player", opponentName, source, requestId, requestedUtcTicks, "runtime_unavailable");
            return plugin.RequestAutonomousPlayerChallengeV2(opponentName, source, requestId, requestedUtcTicks);
        }

        public static DuelAutonomousRequestResultV2 RequestSimSpar(string participantA, string participantB,
            string source, string requestId, long requestedUtcTicks)
        {
            ErenshorDuelPlugin plugin = ErenshorDuelPlugin.Instance;
            if (plugin == null) return ErenshorDuelPlugin.RejectUnboundV2(participantA, participantB, source, requestId, requestedUtcTicks, "runtime_unavailable");
            return plugin.RequestAutonomousSimSparV2(participantA, participantB, source, requestId, requestedUtcTicks);
        }
    }

    internal sealed class PendingAutonomousDuelRequest
    {
        internal string RequestId;
        internal string DuelId;
        internal string Source;
        internal string ParticipantA;
        internal string ParticipantB;
        internal long RequestedUtcTicks;
        internal bool SimVsSim;

        internal string OpponentName { get { return ParticipantB; } }

        internal DuelEventContext Context()
        {
            return new DuelEventContext(RequestId, DuelId, DuelRequestOrigin.Autonomous, Source,
                ParticipantA, ParticipantB, RequestedUtcTicks);
        }

        internal static PendingAutonomousDuelRequest Create(string participantA, string participantB,
            string source, string requestId, long requestedUtcTicks, bool simVsSim)
        {
            return new PendingAutonomousDuelRequest
            {
                RequestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId.Trim(),
                DuelId = Guid.NewGuid().ToString("N"),
                Source = (source ?? string.Empty).Trim(),
                ParticipantA = (participantA ?? string.Empty).Trim(),
                ParticipantB = (participantB ?? string.Empty).Trim(),
                RequestedUtcTicks = requestedUtcTicks > 0L ? requestedUtcTicks : DateTime.UtcNow.Ticks,
                SimVsSim = simVsSim
            };
        }
    }
}
