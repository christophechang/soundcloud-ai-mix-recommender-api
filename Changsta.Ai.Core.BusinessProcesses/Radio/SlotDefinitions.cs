using System;
using System.Collections.Generic;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
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

        /// <summary>
        /// Day-of-week BPM nudge. Part of the scoring algorithm rather than product tuning, so it
        /// stays in code while the per-slot base targets come from configuration.
        /// </summary>
        internal static int GetDayBpmAdjustment(DayBucket day) => day switch
        {
            DayBucket.Sunday => -10,
            DayBucket.Friday => +4,
            DayBucket.Saturday => +8,
            _ => 0,
        };

        internal static SlotKey AdjacentSlot(SlotKey slot)
        {
            int index = Array.IndexOf(SlotOrder, slot);
            return SlotOrder[(index + 1) % SlotOrder.Length];
        }
    }
}
