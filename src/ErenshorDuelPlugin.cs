using System;
using Lunaris;
using Lunaris.Config;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using ForgottenRoads.StandaloneUi;

namespace ErenshorDuel
{
    [LunarisPlugin(PluginGuid, PluginVersion, "forgetwhtuno",
        "Friendly, non-lethal simulated sparring between the player and a local Sim, or between two local Sims while the player watches, using virtualized health inside a bounded duel.")]
    [LunarisPermission(LunarisPermission.Reflection | LunarisPermission.Harmony)]
    public sealed class ErenshorDuelPlugin : LunarisPlugin
    {
        internal const string PluginGuid = "forgetwhtuno.erenshor.practice-duels";
        internal const string PluginVersion = "0.5.1";
        internal static ErenshorDuelPlugin Instance;
        private Harmony _harmony;
        private DuelSettings _settings;
        internal static bool VerboseDiagnostics { get; private set; }
        internal static bool AmbientSparringEnabled { get; private set; }
        internal static int AmbientMinimumMinutes { get; private set; }
        internal static int AmbientMaximumMinutes { get; private set; }
        internal static int AmbientPerSimCooldownMinutes { get; private set; }
        internal static int AmbientOpportunityPercent { get; private set; }
        private bool _runtimeHooksReady;
        private string _runtimeHookFailure = string.Empty;
        private static bool _sceneHooksInstalled;
        private string _pendingControlChallenge;
        private PendingAutonomousDuelRequest _pendingAutonomousChallenge;
        private bool _pendingControlStop;
        private DuelSuiteAuraProvider _auraProvider;

        // Launcher/control-surface readiness must match the rest of the Forgotten Roads suite.
        // Merely having PlayerControl/Myself is too early: those persistent objects can exist while
        // the destination scene and Sim systems are still initializing during character entry.
        private const float UiStableReadySeconds = 1.0f;
        private static float _uiRawReadySince = -1f;
        private static int _uiReadySceneHandle = int.MinValue;
        private static bool _uiCanMoveLatched;
        private static bool _uiReadyAcquired;

        private void Awake()
        {
            Instance = this;
            _settings = new DuelSettings();
            Config.Register(ref _settings);
            VerboseDiagnostics = _settings.DiagnosticsVerbose;
            AmbientSparringEnabled = _settings.AmbientSparringEnabled;
            AmbientMinimumMinutes = Math.Max(10, Math.Min(240, _settings.AmbientMinimumMinutes));
            AmbientMaximumMinutes = Math.Max(AmbientMinimumMinutes, Math.Min(360, _settings.AmbientMaximumMinutes));
            AmbientPerSimCooldownMinutes = Math.Max(20, Math.Min(720, _settings.AmbientPerSimCooldownMinutes));
            AmbientOpportunityPercent = Math.Max(1, Math.Min(100, _settings.AmbientOpportunityPercent));
            _harmony = new Harmony(PluginGuid);
            try
            {
                _harmony.PatchAll();
                _runtimeHooksReady = true;
                _runtimeHookFailure = string.Empty;
            }
            catch (Exception ex)
            {
                _runtimeHooksReady = false;
                _runtimeHookFailure = ex.GetType().Name;
                try { _harmony.UnpatchSelf(); } catch { }
                Logging.LogError("Practice Duels runtime hooks unavailable (" + _runtimeHookFailure + "). Duel gameplay is disabled, but the standalone status UI will remain available.");
            }
            DeepSimsCompatibility.Initialize();
            CoopCompatibility.Refresh();
            if (_runtimeHooksReady && !_sceneHooksInstalled)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
                _sceneHooksInstalled = true;
            }

            // Optional Suite Hub transport adapter. Never assumed present; registration failure
            // must never block normal standalone duel commands.
            try
            {
                _auraProvider = new DuelSuiteAuraProvider();
                _auraProvider.Register(this);
            }
            catch (Exception ex) { Logging.LogError("Duel Suite Aura provider setup failed: " + ex); }

            Logging.LogInfo("Practice Duels " + PluginVersion + " loaded. Use /eduel <SimName>, /eduel <Sim A> vs <Sim B>, /eduel nearby, /eduel status, /eduel diag, /eduel selftest, or /eduel stop.");
            StandaloneFallbackUi.Initialize(this, "duel", "PRACTICE DUEL",
                "Select a Sim for the full Sim Actions surface, or challenge the first eligible nearby Sim here.\n" +
                "Sim vs Sim: /eduel <Sim A> vs <Sim B>",
                StandaloneLauncherColumnPolicy.DefaultX(),
                StandaloneLauncherColumnPolicy.DefaultY(StandaloneLauncherColumnPolicy.SlotIndex),
                DuelControlApi.GetStatus,
                new FallbackAction("Challenge Nearby", ChallengeFirstEligible, delegate { return DuelControlApi.GetBasicState().CanStart && (DuelControlApi.GetBasicState().EligibleNames ?? new string[0]).Length > 0; }),
                new FallbackAction("Stop Duel", DuelControlApi.TryStop, delegate { return DuelControlApi.GetBasicState().Active; }));
            // Compact workspace tuning: the guide text alone is two lines, and status can grow to
            // list eligible Sim names, so this keeps more headroom than Follow's. Default panel
            // position sits in the shared right-side workspace below the launcher column - above
            // the combat/chat log, not overlapping it - instead of the old lower-center default.
            StandaloneFallbackUi.ConfigureWorkspaceDefaults(68f,
                StandaloneLauncherColumnPolicy.DefaultPanelRightNormalized(),
                StandaloneLauncherColumnPolicy.DefaultPanelTopNormalized(),
                StandaloneLauncherColumnPolicy.SlotIndex);
        }
        private static bool ChallengeFirstEligible()
        { string[] names = DuelControlApi.GetBasicState().EligibleNames ?? new string[0]; return names.Length > 0 && DuelControlApi.TryChallenge(names[0]); }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ResetDuelUiReadiness();
            // Queued actor-name requests must never survive a zone boundary. Autonomous callers get
            // an explicit correlated terminal rejection before their request is discarded.
            _pendingControlChallenge = null;
            RejectPendingAutonomous("zone_change", "Practice Duel request cancelled because the zone changed.");
            DuelController.HandleSceneTransition();
            DeepSimsCompatibility.Refresh();
            CoopCompatibility.Refresh();
        }

        private void OnSceneUnloaded(Scene scene)
        {
            ResetDuelUiReadiness();
            _pendingControlChallenge = null;
            RejectPendingAutonomous("zone_change", "Practice Duel request cancelled because the zone changed.");
            DuelController.HandleSceneTransition();
        }

        private void OnApplicationQuit()
        {
            // Real CurrentHP is mirrored to virtual health while fighting. Restore it before the
            // application can run ordinary disconnect/quit save paths.
            RejectPendingAutonomous("plugin_shutdown", "Practice Duel application is shutting down.");
            DuelController.StopForShutdown("Practice Duel application is shutting down.");
        }

        private void Update()
        {
            StandaloneFallbackUi.Tick(DuelUiReady());
            if (!_runtimeHooksReady)
            {
                _pendingControlChallenge = null;
                _pendingControlStop = false;
                return;
            }
            try { DuelSimActionsFallback.Tick(); } catch { }
            try
            {
                if (_pendingControlStop) { _pendingControlStop = false; DuelController.Stop("Practice duel stopped from Suite Hub."); }
                if (!string.IsNullOrWhiteSpace(_pendingControlChallenge))
                {
                    string requested = _pendingControlChallenge; _pendingControlChallenge = null;
                    bool ambiguous; SimPlayer sim = DuelController.FindSim(requested, out ambiguous);
                    if (!ambiguous && sim != null && DuelController.CanStartNewDuel) DuelController.Start(sim, DuelRequestOrigin.ExplicitPlayer);
                }
                if (_pendingAutonomousChallenge != null)
                {
                    PendingAutonomousDuelRequest request = _pendingAutonomousChallenge;
                    _pendingAutonomousChallenge = null;
                    ProcessPendingAutonomous(request);
                }
                DuelController.Tick();
            }
            catch (Exception ex)
            {
                Logging.LogError("Practice duel update failed: " + ex);
                DuelController.Cancel("Update.Exception", null, null, null,
                    "Duel cancelled after an internal error: " + ex.GetType().Name + ".");
            }
        }

        private void OnDestroy()
        {
            StandaloneFallbackUi.Dispose();
            try { DuelSimActionsFallback.Shutdown(); } catch { }
            _pendingControlChallenge = null; _pendingControlStop = false;
            RejectPendingAutonomous("plugin_shutdown", "Practice Duel plugin is shutting down.");
            DuelController.Shutdown();
            try { if (_harmony != null) _harmony.UnpatchSelf(); } catch { }
            if (_sceneHooksInstalled)
            {
                try { SceneManager.sceneLoaded -= OnSceneLoaded; } catch { }
                try { SceneManager.sceneUnloaded -= OnSceneUnloaded; } catch { }
                _sceneHooksInstalled = false;
            }
            try { if (_auraProvider != null) _auraProvider.Unregister(); } catch { }
            _auraProvider = null;
            DeepSimsCompatibility.Reset();
            CoopCompatibility.Reset();
            DuelFollowCompatibility.Reset();
            ResetDuelUiReadiness();
            VerboseDiagnostics = false;
            _settings = null;
            if (Instance == this) Instance = null;
        }

        // internal (not private): DuelSimActionsFallback shares the exact same stable-world gate as
        // the standalone launcher. PlayerControl/Myself alone is not sufficient during character
        // entry because those persistent objects can exist before the active zone/Sim systems are
        // actually usable. Match the suite's canonical readiness acquisition semantics: prove the
        // world graph, observe CanMove at least once, then remain stable for one second.
        internal static bool DuelUiReady()
        {
            if (!RawDuelUiReady())
            {
                ResetDuelUiReadiness();
                return false;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (_uiReadySceneHandle != scene.handle)
            {
                _uiReadySceneHandle = scene.handle;
                _uiRawReadySince = Time.unscaledTime;
                _uiCanMoveLatched = false;
                _uiReadyAcquired = false;
            }
            if (_uiRawReadySince < 0f) _uiRawReadySince = Time.unscaledTime;

            if (_uiReadyAcquired) return true;

            try
            {
                if (GameData.PlayerControl != null && GameData.PlayerControl.CanMove)
                    _uiCanMoveLatched = true;
            }
            catch { }

            if (!_uiCanMoveLatched || Time.unscaledTime - _uiRawReadySince < UiStableReadySeconds)
                return false;

            _uiReadyAcquired = true;
            return true;
        }

        private static bool RawDuelUiReady()
        {
            try
            {
                if (GameData.InCharSelect || GameData.Zoning) return false;
                if (GameData.PlayerControl == null || GameData.PlayerControl.Myself == null) return false;

                Character player = GameData.PlayerControl.Myself;
                if (player.MyStats == null || player.gameObject == null || !player.gameObject.activeInHierarchy)
                    return false;

                Scene scene = SceneManager.GetActiveScene();
                if (!scene.IsValid() || !scene.isLoaded) return false;

                if (GameData.SimMngr == null || GameData.SimPlayerGrouping == null || GameData.GroupMembers == null)
                    return false;

                return true;
            }
            catch { return false; }
        }

        private static void ResetDuelUiReadiness()
        {
            _uiRawReadySince = -1f;
            _uiReadySceneHandle = int.MinValue;
            _uiCanMoveLatched = false;
            _uiReadyAcquired = false;
        }

        internal void Chat(string message, string color)
        {
            try { UpdateSocialLog.LogAdd(message, color); }
            catch { try { UpdateSocialLog.LogAdd(message); } catch { } }
        }

        internal bool RuntimeHooksReady { get { return _runtimeHooksReady; } }
        internal string RuntimeHookFailure { get { return _runtimeHookFailure; } }

        internal bool RequestControlChallenge(string simName)
        {
            if (!_runtimeHooksReady || string.IsNullOrWhiteSpace(simName) || !DuelController.CanStartNewDuel) return false;
            // Explicit player intent wins over an autonomous request that has only been queued and has
            // not entered the Duel controller yet. The autonomous caller still receives a terminal fact.
            RejectPendingAutonomous("superseded", "Autonomous Practice Duel request was superseded by an explicit player challenge.");
            _pendingControlChallenge = simName.Trim(); _pendingControlStop = false; return true;
        }

        internal DuelRequestResult RequestAutonomousChallenge(string simName, string source)
        {
            // v1 semantics remain: immediate rejections have no RequestId and only a queued request
            // receives one. The richer v2 API below is used by new correlated callers.
            if (!_runtimeHooksReady) return DuelRequestResult.Rejected("runtime_unavailable");
            if (string.IsNullOrWhiteSpace(simName)) return DuelRequestResult.Rejected("invalid_opponent");
            if (string.IsNullOrWhiteSpace(source)) return DuelRequestResult.Rejected("invalid_source");
            if (!DuelController.CanStartNewDuel) return DuelRequestResult.Rejected("busy");
            if (!string.IsNullOrWhiteSpace(_pendingControlChallenge) || _pendingAutonomousChallenge != null)
                return DuelRequestResult.Rejected("request_queue_busy");

            PendingAutonomousDuelRequest request = PendingAutonomousDuelRequest.Create(
                "player", simName, source, string.Empty, DateTime.UtcNow.Ticks, false);
            DuelController.PublishAutonomousRequested(request);
            _pendingAutonomousChallenge = request;
            _pendingControlStop = false;
            return DuelRequestResult.Queued(request.RequestId);
        }

        internal DuelAutonomousRequestResultV2 RequestAutonomousPlayerChallengeV2(string simName, string source,
            string requestId, long requestedUtcTicks)
        {
            PendingAutonomousDuelRequest request = PendingAutonomousDuelRequest.Create(
                "player", simName, source, requestId, requestedUtcTicks, false);
            return QueueAutonomousV2(request);
        }

        internal DuelAutonomousRequestResultV2 RequestAutonomousSimSparV2(string firstName, string secondName,
            string source, string requestId, long requestedUtcTicks)
        {
            PendingAutonomousDuelRequest request = PendingAutonomousDuelRequest.Create(
                firstName, secondName, source, requestId, requestedUtcTicks, true);
            return QueueAutonomousV2(request);
        }

        private DuelAutonomousRequestResultV2 QueueAutonomousV2(PendingAutonomousDuelRequest request)
        {
            DuelController.PublishAutonomousRequested(request);
            string reason = ValidateAutonomousQueue(request);
            if (!string.IsNullOrEmpty(reason))
            {
                DuelController.RejectAutonomousRequest(request, reason, QueueReason(reason));
                return new DuelAutonomousRequestResultV2(request, "rejected", reason);
            }
            _pendingAutonomousChallenge = request;
            _pendingControlStop = false;
            return new DuelAutonomousRequestResultV2(request, "queued", string.Empty);
        }

        private string ValidateAutonomousQueue(PendingAutonomousDuelRequest request)
        {
            if (!_runtimeHooksReady) return "runtime_unavailable";
            if (request == null || string.IsNullOrWhiteSpace(request.Source)) return "invalid_source";
            if (string.IsNullOrWhiteSpace(request.ParticipantB)) return "invalid_opponent";
            if (request.SimVsSim && (string.IsNullOrWhiteSpace(request.ParticipantA) ||
                string.Equals(request.ParticipantA, request.ParticipantB, StringComparison.OrdinalIgnoreCase))) return "invalid_participants";
            if (!DuelController.CanStartNewDuel) return "busy";
            if (!string.IsNullOrWhiteSpace(_pendingControlChallenge) || _pendingAutonomousChallenge != null) return "request_queue_busy";
            if (DuelPvpCompatibility.HasConflict()) return "pvp_conflict";
            return string.Empty;
        }

        internal static DuelAutonomousRequestResultV2 RejectUnboundV2(string participantA, string participantB,
            string source, string requestId, long requestedUtcTicks, string reasonToken)
        {
            PendingAutonomousDuelRequest request = PendingAutonomousDuelRequest.Create(
                participantA, participantB, source, requestId, requestedUtcTicks,
                !string.Equals(participantA, "player", StringComparison.OrdinalIgnoreCase));
            PracticeDuelEvents.Publish(DuelEventFactory.Requested(request.Context()));
            PracticeDuelEvents.Publish(DuelEventFactory.RequestRejected(participantB, reasonToken, QueueReason(reasonToken), request.Context()));
            return new DuelAutonomousRequestResultV2(request, "rejected", reasonToken);
        }

        private void ProcessPendingAutonomous(PendingAutonomousDuelRequest request)
        {
            if (request == null) return;
            if (!DuelController.CanStartNewDuel)
            {
                DuelController.RejectAutonomousRequest(request, "busy", "Practice Duel is already active or cleaning up.");
                return;
            }
            if (DuelPvpCompatibility.HasConflict())
            {
                DuelController.RejectAutonomousRequest(request, "pvp_conflict", "Practice Duel will not start while PvP is active or pending.");
                return;
            }

            if (!request.SimVsSim)
            {
                bool ambiguous; SimPlayer sim = DuelController.FindSim(request.ParticipantB, out ambiguous);
                if (ambiguous) DuelController.RejectAutonomousRequest(request, "ambiguous_opponent", "More than one nearby Sim matches the requested name.");
                else if (sim == null) DuelController.RejectAutonomousRequest(request, "invalid_opponent", "No living local Sim matched the autonomous challenge request.");
                else DuelController.Start(sim, DuelRequestOrigin.Autonomous, request.Context());
                return;
            }

            bool firstAmbiguous, secondAmbiguous;
            SimPlayer first = DuelController.FindSim(request.ParticipantA, out firstAmbiguous);
            SimPlayer second = DuelController.FindSim(request.ParticipantB, out secondAmbiguous);
            if (firstAmbiguous || secondAmbiguous)
                DuelController.RejectAutonomousRequest(request, "ambiguous_participant", "More than one nearby Sim matches an autonomous spar participant.");
            else if (first == null || second == null || first == second)
                DuelController.RejectAutonomousRequest(request, "invalid_participants", "Both autonomous spar participants must be distinct living local Sims.");
            else
                DuelController.StartSpectator(first, second, DuelRequestOrigin.Autonomous, request.Context());
        }

        private static string QueueReason(string reasonToken)
        {
            switch ((reasonToken ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "runtime_unavailable": return "Practice Duel runtime hooks are unavailable.";
                case "invalid_source": return "Autonomous requests require a source.";
                case "invalid_opponent": return "Autonomous requests require a local opponent.";
                case "invalid_participants": return "Autonomous Sim-v-Sim requests require two distinct participants.";
                case "request_queue_busy": return "Another Practice Duel request is already queued.";
                case "pvp_conflict": return "Practice Duel will not start while PvP is active or pending.";
                case "busy": return "Practice Duel is already active or cleaning up.";
                default: return "Practice Duel rejected the autonomous request.";
            }
        }

        internal bool RequestControlStop()
        {
            if (!_runtimeHooksReady) return !DuelController.Active;
            RejectPendingAutonomous("manual_stop", "Autonomous Practice Duel request was stopped before evaluation.");
            _pendingControlStop = true; _pendingControlChallenge = null; return true;
        }

        private void RejectPendingAutonomous(string reasonToken, string reason)
        {
            PendingAutonomousDuelRequest request = _pendingAutonomousChallenge;
            _pendingAutonomousChallenge = null;
            if (request == null) return;
            DuelController.RejectAutonomousRequest(request, reasonToken, reason);
        }

        internal void Diagnostic(string message)
        {
            // Forensic duel diagnostics can fire once per hit/spell/effect. Keep that developer
            // observability available, but do not synchronously format/write it during normal play.
            if (!VerboseDiagnostics) return;
            Logging.LogDebug(message);
        }

        internal void LifecycleDiagnostic(string message)
        {
            // Low-frequency state transitions remain visible even with verbose diagnostics off, so a
            // live report can still prove Preparing/Countdown/Active/Cleaning/Idle and terminal cleanup.
            Logging.LogDebug(message);
        }

        internal bool Handle(TypeText typeText, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string command = raw.Trim();
            if (!command.StartsWith("/eduel", StringComparison.OrdinalIgnoreCase) ||
                (command.Length > 6 && !char.IsWhiteSpace(command[6]))) return false;
            string argument = command.Length == 6 ? string.Empty : command.Substring(6).Trim();
            try { typeText.typed.text = string.Empty; } catch { }

            if (argument.Equals("stop", StringComparison.OrdinalIgnoreCase) || argument.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                DuelController.Stop("Practice duel stopped.");
                return true;
            }
            if (argument.Length == 0)
            {
                Chat("[Practice Duel] Usage: /eduel <SimName>, /eduel <Sim A> vs <Sim B>, /eduel nearby, /eduel status, /eduel diag, /eduel selftest, or /eduel stop", "yellow");
                return true;
            }
            if (argument.Equals("selftest", StringComparison.OrdinalIgnoreCase))
            {
                string result = DuelSelfTests.RunAll();
                Chat("[Practice Duel] " + result, "lightblue");
                Diagnostic("[Practice Duel] selftest=" + result);
                return true;
            }
            if (argument.Equals("nearby", StringComparison.OrdinalIgnoreCase))
            {
                Chat(DuelController.NearbySummary(), "lightblue");
                return true;
            }
            if (argument.Equals("diag", StringComparison.OrdinalIgnoreCase))
            {
                Chat(DuelController.DiagSummary(), "lightblue");
                return true;
            }
            if (argument.Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                Chat(DuelController.Status(), "lightblue");
                return true;
            }
            if (argument.Equals("diag", StringComparison.OrdinalIgnoreCase) || argument.Equals("diagnostics", StringComparison.OrdinalIgnoreCase))
            {
                string result = DuelController.Diagnostics();
                Chat(result, "lightblue");
                Diagnostic(result);
                return true;
            }

            bool explicitWatch = argument.StartsWith("watch ", StringComparison.OrdinalIgnoreCase);
            string pairing = explicitWatch ? argument.Substring(6).Trim() : argument;
            int versus = pairing.IndexOf(" vs ", StringComparison.OrdinalIgnoreCase);
            if (explicitWatch || versus >= 0)
            {
                if (versus <= 0 || versus + 4 >= pairing.Length)
                {
                    Chat("[Practice Duel] Usage: /eduel <Sim A> vs <Sim B>", "yellow");
                    return true;
                }
                string firstName = pairing.Substring(0, versus).Trim();
                string secondName = pairing.Substring(versus + 4).Trim();
                bool firstAmbiguous;
                bool secondAmbiguous;
                SimPlayer first = DuelController.FindSim(firstName, out firstAmbiguous);
                SimPlayer second = DuelController.FindSim(secondName, out secondAmbiguous);
                if (first == null || second == null)
                {
                    Chat(firstAmbiguous || secondAmbiguous
                        ? "[Practice Duel] A Sim name is ambiguous. Type longer names."
                        : "[Practice Duel] Both Sims must be living, local, in this scene, and within 25m of you.", "yellow");
                    return true;
                }
                DuelController.StartSpectator(first, second, DuelRequestOrigin.ExplicitPlayer);
                return true;
            }

            bool ambiguous;
            SimPlayer sim = DuelController.FindSim(argument, out ambiguous);
            if (sim == null)
            {
                Chat(ambiguous
                    ? "[Practice Duel] More than one nearby Sim matches that name. Type a longer name."
                    : "[Practice Duel] No living same-scene local SimPlayer matched within 25m. Use /eduel nearby or /eduel diag for status.", "yellow");
                return true;
            }
            DuelController.Start(sim, DuelRequestOrigin.ExplicitPlayer);
            return true;
        }
    }

    [HarmonyPatch(typeof(TypeText), "CheckCommands")]
    internal static class DuelChatPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(TypeText __instance)
        {
            try
            {
                return ErenshorDuelPlugin.Instance == null || __instance == null || __instance.typed == null ||
                       !ErenshorDuelPlugin.Instance.Handle(__instance, __instance.typed.text);
            }
            catch { return true; }
        }
    }
}
