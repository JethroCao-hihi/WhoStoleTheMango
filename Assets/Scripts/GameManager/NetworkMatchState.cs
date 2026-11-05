using Fusion;
using UnityEngine;

namespace Assets.Scripts.GameManager
{
    public enum NetworkMatchPhase
    {
        NotStarted = 0,
        Preparing = 1,
        Playing = 2,
        Finished = 3,
        Aborted = 4
    }

    public enum NetworkMatchOutcome
    {
        None = 0,
        ThievesWin = 1,
        OrchardOwnerWins = 2
    }

    /// <summary>
    /// Authoritative network state and Fusion-tick timers for one match.
    /// This component deliberately does not start itself when its scene loads.
    /// </summary>
    public sealed class NetworkMatchState : NetworkBehaviour
    {
        public const float PreparingDurationSeconds = 20f;
        public const float PlayingDurationSeconds = 8f * 60f;

        /// <summary>
        /// Authoritative gameplay entry points use this fail-closed lookup when they are not
        /// called through NetworkPlayerController. Duplicate/missing match objects block mutation.
        /// </summary>
        public static bool IsRunnerInPlayingPhase(NetworkRunner runner)
        {
            if (runner == null || !runner.IsRunning)
                return false;

            NetworkMatchState found = null;
            NetworkMatchState[] states = UnityEngine.Object.FindObjectsByType<NetworkMatchState>(FindObjectsSortMode.None);
            foreach (NetworkMatchState candidate in states)
            {
                if (candidate == null || candidate.Runner != runner ||
                    candidate.Object == null || !candidate.Object.IsValid)
                    continue;

                if (found != null)
                    return false;

                found = candidate;
            }

            return found != null && found.Phase == NetworkMatchPhase.Playing;
        }

        [Networked]
        public NetworkMatchPhase Phase { get; private set; }

        [Networked]
        public NetworkMatchOutcome Outcome { get; private set; }

        [Networked]
        private NetworkBool GameplayReady { get; set; }

        [Networked]
        private TickTimer PreparingTimer { get; set; }

        [Networked]
        private TickTimer PlayingTimer { get; set; }

        public bool IsGameplayReady => GameplayReady;

        /// <summary>
        /// True when the 20-second preparation period has elapsed. This is informational;
        /// clients must not use it to change the phase.
        /// </summary>
        public bool IsPreparationComplete =>
            Phase == NetworkMatchPhase.Preparing && RunnerIsRunning && PreparingTimer.Expired(Runner);

        /// <summary>
        /// Indicates that preparation time elapsed but gameplay has not confirmed readiness yet.
        /// </summary>
        public bool IsWaitingForGameplayReady => IsPreparationComplete && !GameplayReady;

        /// <summary>
        /// Remaining preparation time derived from Fusion's replicated TickTimer and Runner time.
        /// Returns zero outside the Preparing phase or after the timer expires.
        /// </summary>
        public float PreparingTimeRemaining => GetRemainingSeconds(
            Phase == NetworkMatchPhase.Preparing ? PreparingTimer : default);

        /// <summary>
        /// Remaining official match time derived from Fusion's replicated TickTimer and Runner time.
        /// Returns zero outside the Playing phase or after the timer expires.
        /// </summary>
        public float PlayingTimeRemaining => GetRemainingSeconds(
            Phase == NetworkMatchPhase.Playing ? PlayingTimer : default);

        private bool RunnerIsRunning => Runner != null && Runner.IsRunning;

        /// <summary>
        /// Starts the 20-second preparation phase. Call only from State Authority after the
        /// match setup is ready to enter preparation; this method does not infer player roles.
        /// </summary>
        public bool TryBeginPreparing()
        {
            if (!HasStateAuthority || !RunnerIsRunning || Phase != NetworkMatchPhase.NotStarted ||
                Outcome != NetworkMatchOutcome.None)
                return false;

            GameplayReady = false;
            PlayingTimer = default;
            PreparingTimer = TickTimer.CreateFromSeconds(Runner, PreparingDurationSeconds);
            Phase = NetworkMatchPhase.Preparing;
            return true;
        }

        /// <summary>
        /// Records that an authoritative gameplay system has completed its preparation checks.
        /// It does not start the official timer. This signal is intentionally not an RPC.
        /// </summary>
        public bool ConfirmGameplayReady()
        {
            if (!HasStateAuthority || !RunnerIsRunning || Phase != NetworkMatchPhase.Preparing || GameplayReady)
                return false;

            GameplayReady = true;
            return true;
        }

        /// <summary>
        /// Starts official play only after both the preparation timer and gameplay readiness are complete.
        /// The 8-minute timer begins at this transition, not when GameScene loads or preparation begins.
        /// </summary>
        public bool TryStartPlaying()
        {
            if (!HasStateAuthority || !RunnerIsRunning || Phase != NetworkMatchPhase.Preparing ||
                !GameplayReady || !PreparingTimer.Expired(Runner))
                return false;

            PreparingTimer = default;
            PlayingTimer = TickTimer.CreateFromSeconds(Runner, PlayingDurationSeconds);
            Phase = NetworkMatchPhase.Playing;
            return true;
        }

        /// <summary>
        /// Finishes a playing match with an outcome already validated by authoritative gameplay logic.
        /// This is a local method, not an RPC: calling it on a Client cannot request a Host transition.
        /// </summary>
        public bool TryFinish(NetworkMatchOutcome outcome)
        {
            if (!HasStateAuthority || !RunnerIsRunning || Phase != NetworkMatchPhase.Playing ||
                Outcome != NetworkMatchOutcome.None ||
                (outcome != NetworkMatchOutcome.ThievesWin &&
                 outcome != NetworkMatchOutcome.OrchardOwnerWins))
                return false;

            FinishOnStateAuthority(outcome);
            return true;
        }

        /// <summary>
        /// Marks a start attempt as aborted before official play. This is terminal for this
        /// scene object; returning to Lobby unloads it and a later attempt gets a fresh object.
        /// </summary>
        public bool TryAbortBeforePlaying()
        {
            if (!HasStateAuthority || !RunnerIsRunning || Outcome != NetworkMatchOutcome.None ||
                (Phase != NetworkMatchPhase.NotStarted && Phase != NetworkMatchPhase.Preparing))
                return false;

            PreparingTimer = default;
            PlayingTimer = default;
            GameplayReady = false;
            Phase = NetworkMatchPhase.Aborted;
            return true;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || !RunnerIsRunning || Phase != NetworkMatchPhase.Playing ||
                !PlayingTimer.Expired(Runner))
                return;

            // The GDD specifies an OrchardOwner victory when official time expires without a Thief win.
            FinishOnStateAuthority(NetworkMatchOutcome.OrchardOwnerWins);
        }

        private float GetRemainingSeconds(TickTimer timer)
        {
            if (!RunnerIsRunning)
                return 0f;

            float? remaining = timer.RemainingTime(Runner);
            return remaining.HasValue && remaining.Value > 0f ? remaining.Value : 0f;
        }

        private void FinishOnStateAuthority(NetworkMatchOutcome outcome)
        {
            if (!HasStateAuthority || Phase != NetworkMatchPhase.Playing ||
                Outcome != NetworkMatchOutcome.None)
                return;

            Outcome = outcome;
            PreparingTimer = default;
            PlayingTimer = default;
            Phase = NetworkMatchPhase.Finished;
        }
    }
}
