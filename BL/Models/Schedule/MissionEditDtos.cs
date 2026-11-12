namespace BL.Models.Schedule
{
    /// <summary>A mission position slot in a create/update request: enum number × count; <see cref="Id"/> is null for a new slot.</summary>
    public sealed record MissionSlotInput(int? Id, int Position, int Count);

    /// <summary>Instance generation range: ISO local datetimes, or dates (from → the mission's FromTime, to → its ToTime on that day).</summary>
    public sealed record GenerateRangeInput(string? From, string? To);

    /// <summary>Input of <c>MissionEditService.CreateMission</c> (POST /api/Mission/CreateMission). FromTime/ToTime are "HH:mm".</summary>
    public sealed record MissionCreateInput(
        string? Name,
        string? Description,
        int SoldiersRequired,
        int CommandersRequired,
        int Duration,
        string? FromTime,
        string? ToTime,
        bool IsSpecial,
        double? ActualHours,
        int? RequiredRestAfter,
        IReadOnlyList<MissionSlotInput>? Positions,
        DateTime? SpecialStart,
        GenerateRangeInput? Generate);

    /// <summary>
    /// Input of <c>MissionEditService.UpdateMissionDetails</c> (POST /api/Mission/UpdateMissionDetails): null leaves a field unchanged,
    /// an empty string clears Description/FromTime/ToTime, and <see cref="Positions"/> is the complete desired slot list.
    /// </summary>
    public sealed record MissionUpdateInput(
        int Id,
        string? Name = null,
        string? Description = null,
        int? SoldiersRequired = null,
        int? CommandersRequired = null,
        int? Duration = null,
        string? FromTime = null,
        string? ToTime = null,
        double? ActualHours = null,
        int? RequiredRestAfter = null,
        IReadOnlyList<MissionSlotInput>? Positions = null);

    /// <summary>Value of POST /api/Mission/CreateMission.</summary>
    public class CreateMissionResultDto
    {
        public MissionSummaryDto Mission { get; set; } = new();

        /// <summary>Instances created with the mission (1 for a special mission).</summary>
        public int GeneratedInstances { get; set; }
    }

    /// <summary>An instance time range (ISO local).</summary>
    public class TimeSlotDto
    {
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
    }

    /// <summary>Value of POST /api/Mission/GenerateMissionInstances.</summary>
    public class GenerateInstancesResultDto
    {
        /// <summary>Instances created (or that would be created, when dryRun).</summary>
        public int Count { get; set; }

        /// <summary>Generated slots skipped because they overlap existing instances of the mission.</summary>
        public int SkippedExisting { get; set; }

        /// <summary>Resolved range start (a date takes the mission's FromTime).</summary>
        public string From { get; set; } = string.Empty;

        /// <summary>Resolved range end (a date takes the mission's ToTime).</summary>
        public string To { get; set; } = string.Empty;

        /// <summary>The first 50 new slots.</summary>
        public List<TimeSlotDto> Preview { get; set; } = [];

        /// <summary>The last new slot (null when there is none).</summary>
        public TimeSlotDto? LastSlot { get; set; }
    }

    /// <summary>
    /// Value of POST /api/Mission/UpdateMissionInstance: the instance with its new times (null when blocked; not saved when dryRun)
    /// and the re-validation of every assigned soldier at the new times.
    /// </summary>
    public class UpdateInstanceResultDto
    {
        public ScheduleInstanceDto? Instance { get; set; }
        public List<AssignmentValidationDto> Validations { get; set; } = [];
    }

    /// <summary>Value of POST /api/Mission/DeleteMissionInstance.</summary>
    public class DeleteInstanceResultDto
    {
        public int DeletedAssignments { get; set; }
    }
}
