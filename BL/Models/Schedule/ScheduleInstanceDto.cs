namespace BL.Models.Schedule
{
    /// <summary>
    /// Compact view of one mission instance (POST /api/Schedule/GetAssignments).
    /// <see cref="IsFilled"/> is computed live: <c>AssignedCount &gt;= RequiredCount</c>.
    /// Datetimes are ISO local strings (<see cref="ScheduleDateFormat"/>); positions are enum numbers.
    /// </summary>
    public class ScheduleInstanceDto
    {
        public int InstanceId { get; set; }
        public int MissionId { get; set; }
        public string MissionName { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public bool IsFilled { get; set; }
        public int RequiredCount { get; set; }
        public int AssignedCount { get; set; }
        public List<ScheduleAssignmentDto> Assignments { get; set; } = [];
        public List<MissingPositionDto> MissingPositions { get; set; } = [];
    }

    /// <summary>One soldier assigned to an instance slot.</summary>
    public class ScheduleAssignmentDto
    {
        public int SoldierMissionId { get; set; }
        public int SoldierId { get; set; }
        public string SoldierName { get; set; } = string.Empty;
        public int MissionPositionId { get; set; }
        public int Position { get; set; }
    }

    /// <summary>A slot (mission position) with unfilled capacity.</summary>
    public class MissingPositionDto
    {
        public int MissionPositionId { get; set; }
        public int Position { get; set; }
        public int Missing { get; set; }
    }
}
