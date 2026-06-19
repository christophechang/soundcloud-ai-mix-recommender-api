using System;
using System.Collections.Generic;
using Changsta.Ai.Core.Domain;

namespace Changsta.Ai.Core.BusinessProcesses.Radio
{
    // Internal abstraction over RadioScheduler so GetRadioScheduleUseCase can be unit-tested
    // with a substitute scheduler instead of always running the full scoring pipeline.
    internal interface IRadioScheduler
    {
        RadioSchedule Build(IReadOnlyList<Mix> catalogue, DateOnly date);
    }
}
