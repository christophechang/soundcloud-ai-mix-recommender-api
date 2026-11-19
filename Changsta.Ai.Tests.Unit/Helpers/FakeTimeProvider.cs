using System;

namespace Changsta.Ai.Tests.Unit.Helpers
{
    /// <summary>
    /// Minimal <see cref="TimeProvider"/> fake for deterministic clock-dependent tests; the repo has no
    /// reference to a testing-clock package, so this hand-rolls just the override needed.
    /// </summary>
    internal sealed class FakeTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
