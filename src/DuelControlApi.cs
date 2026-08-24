using System;
using ForgottenRoads.StandaloneUi;

namespace ErenshorDuel
{
    public sealed class DuelCandidateInfo
    {
        public string Name { get; private set; }
        public string Scope { get; private set; }
        public float DistanceMeters { get; private set; }

        internal DuelCandidateInfo(string name, string scope, float distanceMeters)
        { Name = name ?? string.Empty; Scope = scope ?? string.Empty; DistanceMeters = distanceMeters; }
    }

    public sealed class DuelControlState
    {
        public bool Active;
        public bool CanStart;
        public string Status;
        public string[] EligibleNames;
        public DuelCandidateInfo[] EligibleCandidates;
    }

    public static class DuelControlApi
    {
        public const int ApiVersion = 1;
        public const string ModuleId = "duel";
        public static bool IsAvailable { get { return ErenshorDuelPlugin.Instance != null && ErenshorDuelPlugin.Instance.RuntimeHooksReady; } }
        public static bool HasDedicatedPanel { get { return true; } }
        public static bool IsPanelOpen { get { return StandaloneFallbackUi.IsOpen; } }
        public static DuelControlState GetBasicState()
        {
            ErenshorDuelPlugin plugin = ErenshorDuelPlugin.Instance;
            if (plugin == null || !plugin.RuntimeHooksReady)
                return new DuelControlState { Active = false, CanStart = false, Status = GetStatus(), EligibleNames = new string[0], EligibleCandidates = new DuelCandidateInfo[0] };
            DuelCandidateInfo[] candidates = DuelController.EligibleCandidates();
            string[] names = new string[candidates.Length];
            for (int i = 0; i < candidates.Length; i++) names[i] = candidates[i].Name;
            return new DuelControlState { Active = DuelController.Active, CanStart = DuelController.CanStartNewDuel, Status = DuelController.Status(), EligibleNames = names, EligibleCandidates = candidates };
        }
        // Names to surface in full before falling back to a bare count. This mod has no dedicated
        // Hub panel with per-Sim buttons (see StandaloneFallbackUi's fixed action list), so this
        // status line is the standalone fallback's only way to make eligible Sims discoverable.
        private const int MaxNamedEligible = 6;

        public static string GetStatus()
        {
            ErenshorDuelPlugin plugin = ErenshorDuelPlugin.Instance;
            if (plugin == null) return "Practice Duels unavailable";
            if (!plugin.RuntimeHooksReady) return "Compatibility unavailable" + (string.IsNullOrWhiteSpace(plugin.RuntimeHookFailure) ? string.Empty : " (" + plugin.RuntimeHookFailure + ")");
            if (DuelController.Active) return "Duel active";
            if (!DuelController.CanStartNewDuel) return "Cleanup finishing";
            DuelCandidateInfo[] candidates = DuelController.EligibleCandidates();
            int count = candidates == null ? 0 : candidates.Length;
            if (count == 0) return "Idle | 0 eligible local candidate(s)";
            return "Idle | " + count + " eligible: " + DescribeEligible(candidates);
        }

        private static string DescribeEligible(DuelCandidateInfo[] candidates)
        {
            int shown = Math.Min(candidates.Length, MaxNamedEligible);
            string[] labels = new string[shown];
            for (int i = 0; i < shown; i++) labels[i] = candidates[i].Scope + ": " + candidates[i].Name;
            string joined = string.Join(", ", labels);
            return candidates.Length > shown ? joined + ", +" + (candidates.Length - shown) + " more" : joined;
        }
        public static bool TryChallenge(string simName)
        {
            ErenshorDuelPlugin p = ErenshorDuelPlugin.Instance;
            return p != null && p.RequestControlChallenge(simName);
        }
        public static bool TryStop()
        {
            ErenshorDuelPlugin p = ErenshorDuelPlugin.Instance;
            if (p == null) return false;
            if (!DuelController.Active) return true;
            return p.RequestControlStop();
        }
        public static bool OpenPanel() { return StandaloneFallbackUi.Open(); }
        public static bool ClosePanel() { return StandaloneFallbackUi.Close(); }
    }
}
