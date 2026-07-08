using System;
using Changsta.Ai.Core.Domain.MixLab;

namespace Changsta.Ai.Core.Contracts.MixLab
{
    /// <summary>
    /// The snake_case wire strings for <see cref="MixLabFeedbackVerdict"/>
    /// (<c>played</c>, <c>played_modified</c>, <c>rejected</c>, <c>unused</c> — see
    /// docs/architecture/mixlab-anywhere.md §5.3), duplicated from the private switch inside
    /// <c>Changsta.Ai.Infrastructure.Services.Azure.MixLab.MixLabFeedbackVerdictJsonConverter</c>.
    /// That converter is <see langword="internal"/> to the Infrastructure.Services.Azure assembly
    /// (visible only to its own <c>InternalsVisibleTo</c> friend, Tests.Unit) so it cannot be
    /// referenced here to parse request bodies or render the feedback endpoints' JSON responses.
    /// Both call sites must stay in sync with that converter's switch if the wire values ever change.
    /// </summary>
    public static class MixLabFeedbackVerdictWireValues
    {
        public const string Played = "played";

        public const string PlayedModified = "played_modified";

        public const string Rejected = "rejected";

        public const string Unused = "unused";

        public static bool TryParse(string value, out MixLabFeedbackVerdict verdict)
        {
            switch (value)
            {
                case Played:
                    verdict = MixLabFeedbackVerdict.Played;
                    return true;
                case PlayedModified:
                    verdict = MixLabFeedbackVerdict.PlayedModified;
                    return true;
                case Rejected:
                    verdict = MixLabFeedbackVerdict.Rejected;
                    return true;
                case Unused:
                    verdict = MixLabFeedbackVerdict.Unused;
                    return true;
                default:
                    verdict = default;
                    return false;
            }
        }

        public static string ToWireValue(MixLabFeedbackVerdict verdict)
        {
            return verdict switch
            {
                MixLabFeedbackVerdict.Played => Played,
                MixLabFeedbackVerdict.PlayedModified => PlayedModified,
                MixLabFeedbackVerdict.Rejected => Rejected,
                MixLabFeedbackVerdict.Unused => Unused,
                _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Unknown MixLab feedback verdict."),
            };
        }
    }
}
