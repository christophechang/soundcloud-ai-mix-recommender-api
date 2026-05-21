using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.Dtos
{
    public sealed class RadioStationScheduleDto
    {
        required public string Id { get; init; }

        required public string Name { get; init; }

        required public string Frequency { get; init; }

        required public string Description { get; init; }

        public bool IsDefault { get; init; }

        required public RadioHourSlotDto CurrentSlot { get; init; }

        public IReadOnlyList<RadioHourSlotDto> TodaySlots { get; init; } = Array.Empty<RadioHourSlotDto>();
    }
}
