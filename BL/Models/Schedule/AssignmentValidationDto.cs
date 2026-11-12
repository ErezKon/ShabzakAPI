namespace BL.Models.Schedule
{
    /// <summary>
    /// Result of validating one soldier for one mission instance (POST /api/Schedule/ValidateAssignments).
    /// <see cref="Violations"/> / <see cref="Warnings"/> hold codes from <see cref="AssignmentConstraintCodes"/>.
    /// </summary>
    public class AssignmentValidationDto
    {
        public int SoldierId { get; set; }
        public string SoldierName { get; set; } = string.Empty;
        public bool IsValid { get; set; }
        public List<string> Violations { get; set; } = [];
        public List<string> Warnings { get; set; } = [];

        /// <summary>"exact" | "similar" | "none".</summary>
        public string PositionFit { get; set; } = PositionFitKind.None;
        public int? SuggestedMissionPositionId { get; set; }
        public int? SuggestedPosition { get; set; }

        public double? RestBeforeHours { get; set; }
        public double? RequiredRestBeforeHours { get; set; }
        public double? RestAfterHours { get; set; }
        public double? RequiredRestAfterHours { get; set; }

        public OverlapInfoDto? Overlap { get; set; }
        public VacationInfoDto? Vacation { get; set; }
    }

    /// <summary>The soldier's assignment that overlaps the validated instance.</summary>
    public class OverlapInfoDto
    {
        public int InstanceId { get; set; }
        public string MissionName { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
    }

    /// <summary>A Pending/Approved vacation that overlaps the validated instance. <see cref="Status"/> is "Pending" or "Approved".</summary>
    public class VacationInfoDto
    {
        public int Id { get; set; }
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>Values of <see cref="AssignmentValidationDto.PositionFit"/>.</summary>
    public static class PositionFitKind
    {
        public const string Exact = "exact";
        public const string Similar = "similar";
        public const string None = "none";
    }

    /// <summary>Violation and warning codes produced by the assignment constraint evaluator.</summary>
    public static class AssignmentConstraintCodes
    {
        public const string NotFound = "NOT_FOUND";
        public const string Inactive = "INACTIVE";
        public const string AlreadyAssigned = "ALREADY_ASSIGNED";
        public const string Overlap = "OVERLAP";
        public const string OnVacation = "ON_VACATION";
        public const string InstanceFull = "INSTANCE_FULL";
        public const string PositionMismatch = "POSITION_MISMATCH";
        public const string RestBefore = "REST_BEFORE";
        public const string RestAfter = "REST_AFTER";
        public const string SimilarPosition = "SIMILAR_POSITION";
    }
}
