using System.Collections.Generic;

namespace Changsta.Ai.Interface.Api.ViewModels
{
    public sealed class RadioStationVm
    {
        required public string Id { get; init; }

        required public string Slug { get; init; }

        required public string Strapline { get; init; }

        required public string Name { get; init; }

        required public string Frequency { get; init; }

        required public string Description { get; init; }

        public bool IsDefault { get; init; }

        public IReadOnlyList<string> Genres { get; init; } = System.Array.Empty<string>();

        required public RadioSlotVm CurrentSlot { get; init; }

        public IReadOnlyList<RadioSlotVm> TodaySlots { get; init; } = System.Array.Empty<RadioSlotVm>();
    }
}
