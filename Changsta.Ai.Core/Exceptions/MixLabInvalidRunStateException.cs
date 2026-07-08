using System;

namespace Changsta.Ai.Core.Exceptions
{
    /// <summary>
    /// Thrown when a MixLab run state transition is requested that is not valid from the run's
    /// current status — e.g. completing a run that was never claimed, or updating feedback for a
    /// concept that does not exist on the run. See docs/architecture/mixlab-anywhere.md §4 row 11
    /// and issue #128.
    /// </summary>
    public sealed class MixLabInvalidRunStateException : Exception
    {
        public MixLabInvalidRunStateException(string runId, string message)
            : base(message)
        {
            RunId = runId;
        }

        public string RunId { get; }
    }
}
