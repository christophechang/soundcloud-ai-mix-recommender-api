using System;

namespace Changsta.Ai.Core.Exceptions
{
    public sealed class RadioStationUnavailableException : Exception
    {
        public RadioStationUnavailableException(string stationId, string message)
            : base(message)
        {
            StationId = stationId;
        }

        public string StationId { get; }
    }
}
