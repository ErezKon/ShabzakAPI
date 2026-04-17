using System.Text.Json.Serialization;

namespace BL.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum InteractiveAutoAssignStatus
    {
        Paused,
        Completed,
        Cancelled
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum InteractivePauseOn
    {
        FaultyOnly,
        EveryInstance
    }
}
