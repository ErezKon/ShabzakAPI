namespace BL.Models.Schedule
{
    /// <summary>
    /// Compact mission definition plus instance/assignment counts (GET /api/Mission/GetMissionsSummary).
    /// <see cref="FromTime"/>/<see cref="ToTime"/> are the mission's daily "HH:mm" window (null when not set);
    /// <see cref="FirstInstanceFrom"/>/<see cref="LastInstanceTo"/> are ISO local datetimes.
    /// <see cref="FutureInstanceCount"/> counts instances starting at or after the server's current time.
    /// </summary>
    public class MissionSummaryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsSpecial { get; set; }
        public bool RequiredInstances { get; set; }
        public int Duration { get; set; }
        public string? FromTime { get; set; }
        public string? ToTime { get; set; }

        public int SoldiersRequired { get; set; }
        public int CommandersRequired { get; set; }
        public double? ActualHours { get; set; }
        public int? RequiredRestAfter { get; set; }
        public List<MissionPositionDto> Positions { get; set; } = [];

        public int InstanceCount { get; set; }
        public int FutureInstanceCount { get; set; }
        public string? FirstInstanceFrom { get; set; }
        public string? LastInstanceTo { get; set; }
        public int AssignmentCount { get; set; }
    }

    /// <summary>A mission's required position slot: <see cref="Position"/> (enum number) × <see cref="Count"/>.</summary>
    public class MissionPositionDto
    {
        public int Id { get; set; }
        public int Position { get; set; }
        public int Count { get; set; }
    }
}
