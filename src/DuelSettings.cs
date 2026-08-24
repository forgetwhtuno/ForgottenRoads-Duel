using Lunaris.Config;

namespace ErenshorDuel
{
    internal sealed class DuelSettings
    {
        [Config("Verbose", "Diagnostics",
            "Enable forensic per-hit/per-spell Practice Duel logging. Off by default; lifecycle transitions and real errors remain visible.")]
        public bool DiagnosticsVerbose = false;

        [Config("Enabled", "AmbientSparring",
            "Allow very rare autonomous Sim-v-Sim practice spars among safe local Sims. Off by default; Practice Duel remains fully manual unless enabled.")]
        public bool AmbientSparringEnabled = false;

        [Config("MinimumMinutes", "AmbientSparring",
            "Minimum minutes between ambient spar opportunities, clamped to 10-240.")]
        public int AmbientMinimumMinutes = 20;

        [Config("MaximumMinutes", "AmbientSparring",
            "Maximum minutes between ambient spar opportunities, clamped to the minimum through 360.")]
        public int AmbientMaximumMinutes = 35;

        [Config("PerSimCooldownMinutes", "AmbientSparring",
            "Minimum minutes before the same Sim may join another autonomous ambient spar, clamped to 20-720.")]
        public int AmbientPerSimCooldownMinutes = 60;

        [Config("OpportunityPercent", "AmbientSparring",
            "Deterministic admission percentage at each already-rare opportunity. Silence is normal. Clamped to 1-100.")]
        public int AmbientOpportunityPercent = 20;
    }
}
