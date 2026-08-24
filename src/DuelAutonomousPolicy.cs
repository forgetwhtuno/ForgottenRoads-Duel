using System;

namespace ErenshorDuel
{
    internal struct AmbientSparSafetyInput
    {
        internal bool FeatureEnabled;
        internal bool WorldReady;
        internal bool DuelIdle;
        internal bool PvpInactive;
        internal bool CampClear;
        internal bool RealCombatClear;
        internal bool GlobalCooldownElapsed;
        internal int CandidateCount;
    }

    internal struct AmbientSparWillingnessInput
    {
        internal string StableKey;
        internal string OpportunityKey;
        internal bool HasHealth;
        internal int CurrentHealth;
        internal int MaximumHealth;
        internal bool HasLevel;
        internal int Level;
        internal int OtherLevel;
        internal bool PerSimCooldownElapsed;
    }

    // Pure policy for optional autonomous Sim-v-Sim sparring. Runtime code supplies only verified
    // local facts; unknown health/level/identity never turns into invented willingness.
    internal static class DuelAutonomousPolicy
    {
        internal const int MinimumAmbientHealthPercent = 60;
        internal const int MaximumAmbientLevelGap = 6;
        internal const int DefaultWillingnessPercent = 35;

        internal static bool CanConsider(AmbientSparSafetyInput input)
        {
            return input.FeatureEnabled && input.WorldReady && input.DuelIdle && input.PvpInactive &&
                   input.CampClear && input.RealCombatClear && input.GlobalCooldownElapsed &&
                   input.CandidateCount >= 2;
        }

        internal static bool IsWilling(AmbientSparWillingnessInput input, int willingnessPercent)
        {
            if (string.IsNullOrWhiteSpace(input.StableKey) || string.IsNullOrWhiteSpace(input.OpportunityKey)) return false;
            if (!input.PerSimCooldownElapsed || !input.HasHealth || input.MaximumHealth <= 0 || !input.HasLevel) return false;
            int healthPercent = Math.Max(0, Math.Min(100,
                (int)Math.Round(input.CurrentHealth * 100.0 / input.MaximumHealth)));
            if (healthPercent < MinimumAmbientHealthPercent) return false;
            if (Math.Abs(input.Level - input.OtherLevel) > MaximumAmbientLevelGap) return false;
            int threshold = Math.Max(0, Math.Min(100, willingnessPercent));
            return DeterministicPercent(input.OpportunityKey + "|" + input.StableKey) < threshold;
        }

        internal static int DeterministicPercent(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                string text = value ?? string.Empty;
                for (int i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= 16777619u;
                }
                return (int)(hash % 100u);
            }
        }

        internal static float DeterministicDelaySeconds(float minimumMinutes, float maximumMinutes, string opportunityKey)
        {
            float min = Math.Max(1f, minimumMinutes);
            float max = Math.Max(min, maximumMinutes);
            int roll = DeterministicPercent(opportunityKey ?? string.Empty);
            return (min + ((max - min) * roll / 99f)) * 60f;
        }

        internal static string RunSelfTests()
        {
            AmbientSparSafetyInput safety = new AmbientSparSafetyInput
            {
                FeatureEnabled = true, WorldReady = true, DuelIdle = true, PvpInactive = true,
                CampClear = true, RealCombatClear = true, GlobalCooldownElapsed = true, CandidateCount = 2
            };
            if (!CanConsider(safety)) return "FAIL ambient: safe opportunity rejected";
            safety.PvpInactive = false;
            if (CanConsider(safety)) return "FAIL ambient: active PvP admitted";
            safety.PvpInactive = true; safety.RealCombatClear = false;
            if (CanConsider(safety)) return "FAIL ambient: real combat admitted";
            safety.RealCombatClear = true; safety.CandidateCount = 1;
            if (CanConsider(safety)) return "FAIL ambient: fewer than two candidates admitted";

            AmbientSparWillingnessInput willing = new AmbientSparWillingnessInput
            {
                StableKey = "sim:alpha", OpportunityKey = "zone:7", HasHealth = true,
                CurrentHealth = 90, MaximumHealth = 100, HasLevel = true, Level = 20, OtherLevel = 22,
                PerSimCooldownElapsed = true
            };
            bool first = IsWilling(willing, 100);
            bool second = IsWilling(willing, 100);
            if (!first || first != second) return "FAIL ambient: deterministic willingness";
            willing.CurrentHealth = 20;
            if (IsWilling(willing, 100)) return "FAIL ambient: low health must decline";
            willing.CurrentHealth = 90; willing.OtherLevel = 40;
            if (IsWilling(willing, 100)) return "FAIL ambient: level gap must decline";
            willing.OtherLevel = 22; willing.PerSimCooldownElapsed = false;
            if (IsWilling(willing, 100)) return "FAIL ambient: per-Sim cooldown must decline";
            willing.PerSimCooldownElapsed = true; willing.HasLevel = false;
            if (IsWilling(willing, 100)) return "FAIL ambient: unknown level must not invent willingness";
            willing.HasLevel = true; willing.StableKey = string.Empty;
            if (IsWilling(willing, 100)) return "FAIL ambient: unknown identity must not invent willingness";

            float delay = DeterministicDelaySeconds(20f, 35f, "ambient:one");
            if (delay < 1200f || delay > 2100f) return "FAIL ambient: cadence bounds";
            return "PASS ambient";
        }
    }
}
