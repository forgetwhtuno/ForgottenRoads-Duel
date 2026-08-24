using System;
using System.Collections.Generic;

namespace ErenshorDuel
{
    internal sealed class DuelEventContext
    {
        internal readonly string RequestId;
        internal readonly string DuelId;
        internal readonly string Origin;
        internal readonly string Source;
        internal readonly string ParticipantA;
        internal readonly string ParticipantB;
        internal readonly long RequestedUtcTicks;

        internal DuelEventContext(string requestId, DuelRequestOrigin origin, string source)
            : this(requestId, Guid.NewGuid().ToString("N"), origin, source,
                origin == DuelRequestOrigin.ExplicitPlayer ? "player" : string.Empty, string.Empty, DateTime.UtcNow.Ticks)
        { }

        internal DuelEventContext(string requestId, string duelId, DuelRequestOrigin origin, string source,
            string participantA, string participantB, long requestedUtcTicks)
        {
            RequestId = Clean(requestId, 96);
            DuelId = string.IsNullOrWhiteSpace(duelId) ? Guid.NewGuid().ToString("N") : Clean(duelId, 96);
            Origin = origin == DuelRequestOrigin.Autonomous ? "autonomous" : "explicit_player";
            string cleanSource = Clean(source, 64);
            Source = cleanSource.Length == 0 ? (origin == DuelRequestOrigin.Autonomous ? "autonomous" : "player") : cleanSource;
            ParticipantA = Clean(participantA, 80);
            ParticipantB = Clean(participantB, 80);
            RequestedUtcTicks = requestedUtcTicks > 0L ? requestedUtcTicks : DateTime.UtcNow.Ticks;
        }

        internal static DuelEventContext ForOrigin(DuelRequestOrigin origin)
        { return new DuelEventContext(string.Empty, origin, string.Empty); }

        internal DuelEventContext WithParticipants(string participantA, string participantB)
        {
            return new DuelEventContext(RequestId, DuelId,
                Origin == "autonomous" ? DuelRequestOrigin.Autonomous : DuelRequestOrigin.ExplicitPlayer,
                Source, participantA, participantB, RequestedUtcTicks);
        }

        private static string Clean(string value, int max)
        {
            string clean = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return clean.Length <= max ? clean : clean.Substring(0, max);
        }
    }

    public sealed class DuelSemanticEvent
    {
        public string Type { get; private set; }
        public string OpponentName { get; private set; }
        public string OpponentScope { get; private set; }
        public string Decision { get; private set; }
        public string Outcome { get; private set; }
        public string Winner { get; private set; }
        public string Yielded { get; private set; }
        public string ReasonToken { get; private set; }
        public string Reason { get; private set; }
        public string RequestId { get; private set; }
        public string DuelId { get; private set; }
        public string Origin { get; private set; }
        public string Source { get; private set; }
        public string SourceSystem { get; private set; }
        public string ParticipantA { get; private set; }
        public string ParticipantB { get; private set; }
        public long RequestedUtcTicks { get; private set; }

        internal DuelSemanticEvent(string type, string opponentName, string opponentScope,
            string decision, string outcome, string winner, string yielded,
            string reasonToken, string reason, DuelEventContext context)
        {
            DuelEventContext c = context ?? DuelEventContext.ForOrigin(DuelRequestOrigin.ExplicitPlayer);
            Type = Clean(type); OpponentName = Clean(opponentName); OpponentScope = Clean(opponentScope);
            Decision = Clean(decision); Outcome = Clean(outcome); Winner = Clean(winner); Yielded = Clean(yielded);
            ReasonToken = Clean(reasonToken); Reason = Clean(reason);
            RequestId = Clean(c.RequestId); DuelId = Clean(c.DuelId); Origin = Clean(c.Origin); Source = Clean(c.Source);
            SourceSystem = "practice_duel";
            ParticipantA = Clean(c.ParticipantA); ParticipantB = Clean(c.ParticipantB);
            RequestedUtcTicks = c.RequestedUtcTicks;
        }

        public string ToObservedGameEventDescription()
        {
            List<string> fields = new List<string>();
            Add(fields, "type", Type); Add(fields, "opponent", OpponentName); Add(fields, "scope", OpponentScope);
            Add(fields, "participant_a", ParticipantA); Add(fields, "participant_b", ParticipantB);
            Add(fields, "decision", Decision); Add(fields, "outcome", Outcome); Add(fields, "winner", Winner);
            Add(fields, "yielded", Yielded); Add(fields, "reason_token", ReasonToken); Add(fields, "reason", Reason);
            Add(fields, "request_id", RequestId); Add(fields, "duel_id", DuelId); Add(fields, "origin", Origin); Add(fields, "source", Source); Add(fields, "source_system", SourceSystem);
            if (RequestedUtcTicks > 0L) Add(fields, "requested_utc_ticks", RequestedUtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return string.Join("; ", fields.ToArray());
        }

        private static void Add(List<string> fields, string key, string value)
        { if (!string.IsNullOrWhiteSpace(value)) fields.Add(key + "=" + value); }

        private static string Clean(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string clean = value.Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',').Replace('=', ':').Trim();
            return clean.Length <= 160 ? clean : clean.Substring(0, 160);
        }
    }

    public static class PracticeDuelEvents
    {
        // v4 is additive over v3: DuelId, participant A/B, requested timestamp, requested lifecycle,
        // and first-class Sim-v-Sim lifecycle events. Existing v2/v3 consumers can ignore them.
        public const int ContractVersion = 4;
        public static event Action<DuelSemanticEvent> SemanticEvent;

        internal static void Publish(DuelSemanticEvent value)
        {
            Action<DuelSemanticEvent> handlers = SemanticEvent;
            if (handlers == null || value == null) return;
            foreach (Delegate raw in handlers.GetInvocationList())
                try { ((Action<DuelSemanticEvent>)raw)(value); } catch { }
        }
    }

    internal static class DuelEventFactory
    {
        private static string Scope(bool partySim) { return partySim ? "party" : "nearby"; }

        internal static DuelSemanticEvent Requested(DuelEventContext context)
        { return New("duel_requested", context == null ? string.Empty : context.ParticipantB, "request", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, context); }

        internal static DuelSemanticEvent Challenge(string opponent, bool partySim)
        { return Challenge(opponent, partySim, DuelEventContext.ForOrigin(DuelRequestOrigin.ExplicitPlayer)); }
        internal static DuelSemanticEvent Challenge(string opponent, bool partySim, DuelEventContext context)
        { return New("duel_challenge", opponent, Scope(partySim), string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, context); }

        internal static DuelSemanticEvent Accepted(string opponent, bool partySim)
        { return Accepted(opponent, partySim, DuelEventContext.ForOrigin(DuelRequestOrigin.ExplicitPlayer)); }
        internal static DuelSemanticEvent Accepted(string opponent, bool partySim, DuelEventContext context)
        { return New("duel_accepted", opponent, Scope(partySim), "accept", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, context); }

        internal static DuelSemanticEvent Declined(string opponent, bool partySim, string decision)
        { return Declined(opponent, partySim, decision, DuelEventContext.ForOrigin(DuelRequestOrigin.ExplicitPlayer)); }
        internal static DuelSemanticEvent Declined(string opponent, bool partySim, string decision, DuelEventContext context)
        { return New("duel_declined", opponent, Scope(partySim), decision, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, context); }

        internal static DuelSemanticEvent Started(string opponent, bool partySim)
        { return Started(opponent, partySim, DuelEventContext.ForOrigin(DuelRequestOrigin.ExplicitPlayer)); }
        internal static DuelSemanticEvent Started(string opponent, bool partySim, DuelEventContext context)
        { return New("duel_started", opponent, Scope(partySim), string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, context); }

        internal static DuelSemanticEvent Completed(string opponent, bool partySim, string outcome, string winner, string yielded)
        { return Completed(opponent, partySim, outcome, winner, yielded, DuelEventContext.ForOrigin(DuelRequestOrigin.ExplicitPlayer)); }
        internal static DuelSemanticEvent Completed(string opponent, bool partySim, string outcome, string winner, string yielded, DuelEventContext context)
        { return New("duel_completed", opponent, Scope(partySim), string.Empty, outcome, winner, yielded, string.Empty, string.Empty, context); }

        internal static DuelSemanticEvent Cancelled(string opponent, bool partySim, string reasonToken, string reason)
        { return Cancelled(opponent, partySim, reasonToken, reason, DuelEventContext.ForOrigin(DuelRequestOrigin.ExplicitPlayer)); }
        internal static DuelSemanticEvent Cancelled(string opponent, bool partySim, string reasonToken, string reason, DuelEventContext context)
        { return New("duel_cancelled", opponent, Scope(partySim), string.Empty, string.Empty, string.Empty, string.Empty, reasonToken, reason, context); }

        internal static DuelSemanticEvent SpectatorChallenge(DuelEventContext context)
        { return New("duel_challenge", context == null ? string.Empty : context.ParticipantB, "sim_vs_sim", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, context); }
        internal static DuelSemanticEvent SpectatorAccepted(DuelEventContext context)
        { return New("duel_accepted", context == null ? string.Empty : context.ParticipantB, "sim_vs_sim", "accept", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, context); }
        internal static DuelSemanticEvent SpectatorDeclined(DuelEventContext context, string decision)
        { return New("duel_declined", context == null ? string.Empty : context.ParticipantB, "sim_vs_sim", decision, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, context); }
        internal static DuelSemanticEvent SpectatorStarted(DuelEventContext context)
        { return New("duel_started", context == null ? string.Empty : context.ParticipantB, "sim_vs_sim", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, context); }
        internal static DuelSemanticEvent SpectatorCompleted(DuelEventContext context, string outcome, string winner, string yielded)
        { return New("duel_completed", context == null ? string.Empty : context.ParticipantB, "sim_vs_sim", string.Empty, outcome, winner, yielded, string.Empty, string.Empty, context); }
        internal static DuelSemanticEvent SpectatorCancelled(DuelEventContext context, string reasonToken, string reason)
        { return New("duel_cancelled", context == null ? string.Empty : context.ParticipantB, "sim_vs_sim", string.Empty, string.Empty, string.Empty, string.Empty, reasonToken, reason, context); }

        internal static DuelSemanticEvent RequestRejected(string opponent, string reasonToken, string reason, DuelEventContext context)
        { return New("duel_request_rejected", opponent, "request", string.Empty, string.Empty, string.Empty, string.Empty, reasonToken, reason, context); }

        internal static string CancellationToken(string source, string reason)
        {
            string s = (source ?? string.Empty).Trim().ToLowerInvariant();
            string r = (reason ?? string.Empty).Trim().ToLowerInvariant();
            if (s.IndexOf("shutdown", StringComparison.Ordinal) >= 0 || r.IndexOf("plugin is shutting down", StringComparison.Ordinal) >= 0 || r.IndexOf("application is shutting down", StringComparison.Ordinal) >= 0) return "plugin_shutdown";
            if (s.IndexOf("attackingplayer", StringComparison.Ordinal) >= 0 || r.IndexOf("outside actor", StringComparison.Ordinal) >= 0 || r.IndexOf("outside attacker", StringComparison.Ordinal) >= 0 || r.IndexOf("outside hostile", StringComparison.Ordinal) >= 0 || r.IndexOf("real combat", StringComparison.Ordinal) >= 0 || r.IndexOf("party combat", StringComparison.Ordinal) >= 0) return "hostile_interruption";
            if (s.IndexOf("scene", StringComparison.Ordinal) >= 0 || s.IndexOf("zone", StringComparison.Ordinal) >= 0 || r.IndexOf("zone", StringComparison.Ordinal) >= 0) return "zone_change";
            if (s.IndexOf("camp", StringComparison.Ordinal) >= 0 || r.IndexOf("camp mode", StringComparison.Ordinal) >= 0) return "camp";
            if (s.IndexOf("distance", StringComparison.Ordinal) >= 0 || r.IndexOf("too far", StringComparison.Ordinal) >= 0) return "distance";
            if (s.IndexOf("partyscope", StringComparison.Ordinal) >= 0 || r.IndexOf("party membership changed", StringComparison.Ordinal) >= 0) return "party_scope_changed";
            if (s.IndexOf("participant", StringComparison.Ordinal) >= 0 || r.IndexOf("no longer available", StringComparison.Ordinal) >= 0) return "participant_unavailable";
            if (s.IndexOf("exception", StringComparison.Ordinal) >= 0 || s.IndexOf("npcprocguard", StringComparison.Ordinal) >= 0 || r.IndexOf("internal error", StringComparison.Ordinal) >= 0 || r.IndexOf("could not start safely", StringComparison.Ordinal) >= 0) return "internal_error";
            if (r.IndexOf("practice duel stopped", StringComparison.Ordinal) >= 0 || r.IndexOf("duel stopped", StringComparison.Ordinal) >= 0) return "manual_stop";
            return "other";
        }

        private static DuelSemanticEvent New(string type, string opponent, string scope,
            string decision, string outcome, string winner, string yielded, string reasonToken, string reason,
            DuelEventContext context)
        {
            return new DuelSemanticEvent(type, opponent, scope, decision, outcome, winner, yielded,
                reasonToken, reason, context ?? DuelEventContext.ForOrigin(DuelRequestOrigin.ExplicitPlayer));
        }

        internal static string RunSelfTests()
        {
            DuelSemanticEvent challenge = Challenge("Dancer", false);
            if (challenge.Type != "duel_challenge" || challenge.OpponentScope != "nearby") return "FAIL events: challenge shape";
            DuelSemanticEvent accepted = Accepted("Dancer", true);
            if (accepted.Type != "duel_accepted" || accepted.Decision != "accept" || accepted.OpponentScope != "party") return "FAIL events: accept shape";
            DuelSemanticEvent declined = Declined("Dancer", false, "decline_recent_duel");
            if (declined.Type != "duel_declined" || declined.Decision != "decline_recent_duel") return "FAIL events: decline shape";

            DuelEventContext correlated = new DuelEventContext("req-17", "duel-44", DuelRequestOrigin.Autonomous, "nemesis",
                "player", "Dancer", 123456L);
            DuelSemanticEvent requested = Requested(correlated);
            if (requested.Type != "duel_requested" || requested.RequestId != "req-17" || requested.DuelId != "duel-44" ||
                requested.ParticipantA != "player" || requested.ParticipantB != "Dancer" || requested.RequestedUtcTicks != 123456L)
                return "FAIL events: requested correlation shape";
            DuelSemanticEvent started = Started("Dancer", false, correlated);
            if (started.Type != "duel_started" || started.RequestId != "req-17" || started.Origin != "autonomous" || started.Source != "nemesis") return "FAIL events: correlated start shape";
            DuelSemanticEvent completed = Completed("Dancer", false, "yield", "player", "opponent", correlated);
            if (completed.Type != "duel_completed" || completed.Winner != "player" || completed.Yielded != "opponent" || completed.RequestId != "req-17") return "FAIL events: completion shape";
            DuelSemanticEvent rejected = RequestRejected("Dancer", "real_combat", "busy in world combat", correlated);
            if (rejected.Type != "duel_request_rejected" || rejected.ReasonToken != "real_combat" || rejected.RequestId != "req-17") return "FAIL events: request rejection shape";
            DuelSemanticEvent cancelled = Cancelled("Dancer", true, "distance", "Duelists moved too far apart.", correlated);
            if (cancelled.Type != "duel_cancelled" || cancelled.ReasonToken != "distance" || cancelled.RequestId != "req-17") return "FAIL events: cancel shape";

            DuelEventContext spar = new DuelEventContext("req-spar", "duel-spar", DuelRequestOrigin.Autonomous, "ambient_spar",
                "Ariadne", "Dancer", 99L);
            DuelSemanticEvent sparStarted = SpectatorStarted(spar);
            DuelSemanticEvent sparDone = SpectatorCompleted(spar, "yield", "Ariadne", "Dancer");
            if (sparStarted.OpponentScope != "sim_vs_sim" || sparStarted.ParticipantA != "Ariadne" || sparStarted.ParticipantB != "Dancer" ||
                sparDone.Winner != "Ariadne" || sparDone.Yielded != "Dancer") return "FAIL events: Sim-v-Sim factual shape";

            if (CancellationToken("Tick.AttackingPlayer", "verified outside attacker entered party combat") != "hostile_interruption" ||
                CancellationToken("Tick.Camp", "camp mode is active") != "camp" ||
                CancellationToken("Tick.PartyScope", "party membership changed") != "party_scope_changed" ||
                CancellationToken("Shutdown.Plugin", "Practice Duel plugin is shutting down.") != "plugin_shutdown" ||
                CancellationToken("Stop.Fallback", "Practice duel stopped.") != "manual_stop") return "FAIL events: cancellation tokens";

            string description = completed.ToObservedGameEventDescription();
            if (description.IndexOf("type=duel_completed", StringComparison.Ordinal) < 0 ||
                description.IndexOf("outcome=yield", StringComparison.Ordinal) < 0 ||
                description.IndexOf("winner=player", StringComparison.Ordinal) < 0 ||
                description.IndexOf("request_id=req-17", StringComparison.Ordinal) < 0 ||
                description.IndexOf("duel_id=duel-44", StringComparison.Ordinal) < 0 ||
                description.IndexOf("participant_b=Dancer", StringComparison.Ordinal) < 0 ||
                description.IndexOf("source=nemesis", StringComparison.Ordinal) < 0 ||
                description.IndexOf("source_system=practice_duel", StringComparison.Ordinal) < 0) return "FAIL events: generic fallback shape";
            return "PASS events";
        }
    }
}
