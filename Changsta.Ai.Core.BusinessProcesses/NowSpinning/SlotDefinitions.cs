using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.BusinessProcesses.NowSpinning
{
    internal static class SlotDefinitions
    {
        internal static readonly SlotKey[] SlotOrder = new[]
        {
            SlotKey.Dead,
            SlotKey.Comedown,
            SlotKey.Morning,
            SlotKey.Afternoon,
            SlotKey.EarlyEve,
            SlotKey.Primetime,
        };

        internal static readonly IReadOnlyDictionary<SlotKey, SlotConfig> Slots =
            new Dictionary<SlotKey, SlotConfig>
            {
                [SlotKey.Dead] = new SlotConfig(SlotKey.Dead, "dead of night", 172, -0.6, new[] { "peak", "high" }),
                [SlotKey.Comedown] = new SlotConfig(SlotKey.Comedown, "comedown", 110, 0.4, new[] { "chilled", "low-mid", "low" }),
                [SlotKey.Morning] = new SlotConfig(SlotKey.Morning, "morning", 122, 0.5, new[] { "low-mid", "mid", "chilled", "low" }),
                [SlotKey.Afternoon] = new SlotConfig(SlotKey.Afternoon, "afternoon", 125, 0.3, new[] { "mid", "journey" }),
                [SlotKey.EarlyEve] = new SlotConfig(SlotKey.EarlyEve, "evening", 128, 0.0, new[] { "mid", "mid-peak", "journey", "mid-high" }),
                [SlotKey.Primetime] = new SlotConfig(SlotKey.Primetime, "primetime", 138, -0.3, new[] { "peak", "high", "mid-peak", "mid-high" }),
            };

        internal static SlotKey ResolveSlot(int localHour) => localHour switch
        {
            < 4 => SlotKey.Dead,
            < 8 => SlotKey.Comedown,
            < 12 => SlotKey.Morning,
            < 17 => SlotKey.Afternoon,
            < 21 => SlotKey.EarlyEve,
            _ => SlotKey.Primetime,
        };

        internal static DayBucket ResolveDayBucket(DayOfWeek day) => day switch
        {
            DayOfWeek.Sunday => DayBucket.Sunday,
            DayOfWeek.Friday => DayBucket.Friday,
            DayOfWeek.Saturday => DayBucket.Saturday,
            _ => DayBucket.Weeknight,
        };

        internal static string DayBucketKey(DayBucket bucket) => bucket switch
        {
            DayBucket.Sunday => "sunday",
            DayBucket.Friday => "friday",
            DayBucket.Saturday => "saturday",
            _ => "weeknight",
        };

        internal static string SlotKeyString(SlotKey slot) => slot switch
        {
            SlotKey.Dead => "dead",
            SlotKey.Comedown => "comedown",
            SlotKey.Morning => "morning",
            SlotKey.Afternoon => "afternoon",
            SlotKey.EarlyEve => "earlyeve",
            _ => "primetime",
        };

        internal static int GetBpmTarget(SlotKey slot, DayBucket day)
        {
            int adjustment = day switch
            {
                DayBucket.Sunday => -10,
                DayBucket.Friday => +4,
                DayBucket.Saturday => +8,
                _ => 0,
            };
            return Slots[slot].BaseBpmTarget + adjustment;
        }

        internal static SlotKey AdjacentSlot(SlotKey slot)
        {
            int index = Array.IndexOf(SlotOrder, slot);
            return SlotOrder[(index + 1) % SlotOrder.Length];
        }
    }
}
