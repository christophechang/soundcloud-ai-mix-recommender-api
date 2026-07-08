namespace Changsta.Ai.Core.BusinessProcesses.MixLab
{
    /// <summary>
    /// Tunable knobs for the MixLab run/worker use cases. A follow-up ticket (A5) binds this to the
    /// <c>MixLab</c> configuration section and registers it in DI; this project only defines and
    /// consumes it. Injected as a concrete instance (this project does not reference
    /// <c>Microsoft.Extensions.Options</c>). See issue #130.
    /// </summary>
    public sealed class MixLabOptions
    {
        /// <summary>
        /// How long a claimed (running) run may go without completing before a subsequent claim
        /// treats it as stale and requeues it. Defaults to 45 minutes.
        /// </summary>
        public int ClaimLeaseMinutes { get; init; } = 45;
    }
}
