using System;

namespace Changsta.Ai.Tests.Unit.MixLab
{
    /// <summary>
    /// Minimal <see cref="TimeProvider"/> fake for deterministic claim-lease tests; this repo has
    /// no existing time abstraction and no reference to a testing-clock package, so this hand-rolls
    /// just the override needed. See issue #128.
    /// </summary>
    internal sealed class FakeTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
