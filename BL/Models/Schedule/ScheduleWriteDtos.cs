namespace BL.Models.Schedule
{
    /// <summary>
    /// Outcome of a schedule or mission write. <see cref="IsBlocked"/> means nothing was written and <see cref="Message"/>
    /// explains why; <see cref="Value"/> still carries the validation details. Controllers map a blocked result to HTTP 409.
    /// </summary>
    public sealed class ScheduleWriteResult<T>
    {
        private ScheduleWriteResult(T value, string? blockedReason)
        {
            Value = value;
            Message = blockedReason;
        }

        public T Value { get; }

        /// <summary>Why the write was not applied (null when it was applied).</summary>
        public string? Message { get; }

        public bool IsBlocked => Message != null;

        public static ScheduleWriteResult<T> Ok(T value) => new(value, null);

        public static ScheduleWriteResult<T> Blocked(T value, string reason) => new(value, reason);
    }

    /// <summary>The request conflicts with existing data (e.g. overlapping instances, a referenced slot). Mapped to HTTP 409.</summary>
    public class ScheduleConflictException : Exception
    {
        public ScheduleConflictException(string message) : base(message)
        {
        }
    }

    /// <summary>Value of POST /api/Schedule/AssignSoldier. <see cref="Instance"/> is null when the write was blocked.</summary>
    public class AssignSoldierResultDto
    {
        public ScheduleInstanceDto? Instance { get; set; }
        public AssignmentValidationDto Validation { get; set; } = new();
    }

    /// <summary>
    /// Value of POST /api/Schedule/ReplaceSoldier and MoveSoldier: the affected instances after the change (empty when blocked)
    /// and the validation of every placement (replace: the new soldier; swap: both soldiers; move: the moved soldier).
    /// </summary>
    public class ScheduleChangeResultDto
    {
        public List<ScheduleInstanceDto> Instances { get; set; } = [];
        public List<AssignmentValidationDto> Validations { get; set; } = [];
    }
}
