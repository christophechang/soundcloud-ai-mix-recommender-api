namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// Outcome of <see cref="IDeleteMixLabRunUseCase.DeleteAsync"/>, mapped by the controller to a
    /// transport status code.
    /// </summary>
    public sealed class DeleteMixLabRunResult
    {
        /// <summary>The mutually-exclusive results of a delete attempt.</summary>
        public enum DeleteOutcome
        {
            /// <summary>The run (and its history entry) was removed (→ 204).</summary>
            Deleted,

            /// <summary>No run exists with the supplied id (→ 404).</summary>
            NotFound,

            /// <summary>The run is still active (queued or running) and may not be deleted (→ 409).</summary>
            Active,

            /// <summary>
            /// Another run is running under a live claim, so no run may be deleted right now
            /// (→ 409). See <see cref="IDeleteMixLabRunUseCase"/>.
            /// </summary>
            EngineBusy,
        }

        required public DeleteOutcome Outcome { get; init; }
    }
}
