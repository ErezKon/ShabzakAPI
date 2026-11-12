using BL.Models.Schedule;

namespace BL.Services
{
    /// <summary>
    /// Pure mapping of a loaded mission instance (with Mission.MissionPositions and Soldiers[.MissionPosition]) to
    /// <see cref="ScheduleInstanceDto"/>: live filled state, required/assigned counts and missing slots.
    /// </summary>
    public static class ScheduleInstanceMapper
    {
        /// <param name="instance">DB instance with <c>Mission.MissionPositions</c> and <c>Soldiers</c> loaded.</param>
        /// <param name="missionName">Decrypted mission name.</param>
        /// <param name="soldierName">Soldier ID → display name.</param>
        public static ScheduleInstanceDto ToDto(DataLayer.Models.MissionInstance instance, string missionName, Func<int, string> soldierName)
        {
            var slots = instance.Mission?.MissionPositions?.ToList() ?? [];
            var assignments = instance.Soldiers ?? [];
            var required = slots.Sum(p => p.Count);
            return new ScheduleInstanceDto
            {
                InstanceId = instance.Id,
                MissionId = instance.MissionId,
                MissionName = missionName,
                From = ScheduleDateFormat.ToIso(instance.FromTime),
                To = ScheduleDateFormat.ToIso(instance.ToTime),
                RequiredCount = required,
                AssignedCount = assignments.Count,
                IsFilled = assignments.Count >= required,
                Assignments = assignments
                    .Select(a => new ScheduleAssignmentDto
                    {
                        SoldierMissionId = a.Id,
                        SoldierId = a.SoldierId,
                        SoldierName = soldierName(a.SoldierId),
                        MissionPositionId = a.MissionPositionId,
                        Position = (int)(a.MissionPosition?.Position
                            ?? slots.FirstOrDefault(p => p.Id == a.MissionPositionId)?.Position
                            ?? DataLayer.Models.Position.Simple)
                    })
                    .OrderByDescending(a => a.Position)
                    .ThenBy(a => a.SoldierMissionId)
                    .ToList(),
                MissingPositions = slots
                    .Select(p => new MissingPositionDto
                    {
                        MissionPositionId = p.Id,
                        Position = (int)p.Position,
                        Missing = p.Count - assignments.Count(a => a.MissionPositionId == p.Id)
                    })
                    .Where(m => m.Missing > 0)
                    .ToList()
            };
        }
    }
}
