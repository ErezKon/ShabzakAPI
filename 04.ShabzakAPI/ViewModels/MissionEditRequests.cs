using BL.Models.Schedule;
using BL.Services;

namespace ShabzakAPI.ViewModels
{
    /// <summary>A position slot: enum number × count. <see cref="Id"/> identifies an existing slot (UpdateMissionDetails only).</summary>
    public class MissionPositionRequest
    {
        public int? Id { get; set; }
        public int Position { get; set; }
        public int Count { get; set; }

        public MissionSlotInput ToInput() => new(Id, Position, Count);
    }

    /// <summary>Instance generation range: ISO local datetimes, or dates (from → the mission's FromTime, to → its ToTime on that day).</summary>
    public class InstanceRangeRequest
    {
        public string? From { get; set; }
        public string? To { get; set; }
    }

    /// <summary>One instance: ISO local datetimes without offset.</summary>
    public class InstanceTimesRequest
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
    }

    /// <summary>
    /// Body of POST /api/Mission/CreateMission. FromTime/ToTime are the mission's daily times ("HH:mm"). A special mission needs
    /// <see cref="SpecialStart"/> (one instance of Duration hours); a recurring one may pass <see cref="Generate"/>.
    /// </summary>
    public class CreateMissionRequest
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public int SoldiersRequired { get; set; }
        public int CommandersRequired { get; set; }
        public int Duration { get; set; }
        public string? FromTime { get; set; }
        public string? ToTime { get; set; }
        public bool IsSpecial { get; set; }
        public double? ActualHours { get; set; }
        public int? RequiredRestAfter { get; set; }
        public List<MissionPositionRequest>? Positions { get; set; }
        public DateTime? SpecialStart { get; set; }
        public InstanceRangeRequest? Generate { get; set; }

        public MissionCreateInput ToInput() => new(
            Name,
            Description,
            SoldiersRequired,
            CommandersRequired,
            Duration,
            FromTime,
            ToTime,
            IsSpecial,
            ActualHours,
            RequiredRestAfter,
            Positions?.Select(p => p.ToInput()).ToList(),
            SpecialStart,
            Generate == null ? null : new GenerateRangeInput(Generate.From, Generate.To));
    }

    /// <summary>
    /// Body of POST /api/Mission/UpdateMissionDetails. Null fields are unchanged; an empty string clears Description/FromTime/ToTime.
    /// <see cref="Positions"/> is the complete desired slot list (Id = update, no Id = add, missing = remove). Instances are never touched.
    /// </summary>
    public class UpdateMissionDetailsRequest
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public int? SoldiersRequired { get; set; }
        public int? CommandersRequired { get; set; }
        public int? Duration { get; set; }
        public string? FromTime { get; set; }
        public string? ToTime { get; set; }
        public double? ActualHours { get; set; }
        public int? RequiredRestAfter { get; set; }
        public List<MissionPositionRequest>? Positions { get; set; }

        public MissionUpdateInput ToInput() => new(
            Id,
            Name,
            Description,
            SoldiersRequired,
            CommandersRequired,
            Duration,
            FromTime,
            ToTime,
            ActualHours,
            RequiredRestAfter,
            Positions?.Select(p => p.ToInput()).ToList());
    }

    /// <summary>Body of POST /api/Mission/GenerateMissionInstances. From/To: ISO local datetimes or dates (see <see cref="InstanceRangeRequest"/>).</summary>
    public class GenerateMissionInstancesRequest
    {
        public int MissionId { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        public bool DryRun { get; set; }
    }

    /// <summary>Body of POST /api/Mission/AddMissionInstances.</summary>
    public class AddMissionInstancesRequest
    {
        public int MissionId { get; set; }
        public List<InstanceTimesRequest>? Instances { get; set; }

        public List<TimeSlot>? ToSlots() => Instances?.Select(i => new TimeSlot(i.From, i.To)).ToList();
    }

    /// <summary>Body of POST /api/Mission/UpdateMissionInstance. With <see cref="DryRun"/> the result is computed but not saved.</summary>
    public class UpdateMissionInstanceRequest
    {
        public int MissionInstanceId { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }

        /// <summary>Apply even when an assigned soldier would overlap another assignment.</summary>
        public bool AllowViolations { get; set; }
        public bool DryRun { get; set; }
    }

    /// <summary>Body of POST /api/Soldiers/SetSoldierActive.</summary>
    public class SetSoldierActiveRequest
    {
        public int SoldierId { get; set; }
        public bool Active { get; set; }
    }
}
