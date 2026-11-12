using BL.Services;
using DataLayer.Models;

namespace ShabzakAPI.Tests
{
    public class ScheduleInstanceMapperTests
    {
        private static MissionInstance Instance(params (int SoldierMissionId, int SoldierId, int MissionPositionId)[] assignments)
        {
            var simple = new MissionPositions { Id = 1, MissionId = 10, Position = Position.Simple, Count = 2 };
            var medic = new MissionPositions { Id = 2, MissionId = 10, Position = Position.Medic, Count = 1 };
            var mission = new Mission { Id = 10, Name = "encrypted", MissionPositions = [simple, medic], MissionInstances = [] };
            return new MissionInstance
            {
                Id = 100,
                MissionId = 10,
                FromTime = new DateTime(2026, 11, 12, 22, 0, 0),
                ToTime = new DateTime(2026, 11, 13, 2, 30, 0),
                Mission = mission,
                Soldiers = assignments
                    .Select(a => new SoldierMission
                    {
                        Id = a.SoldierMissionId,
                        SoldierId = a.SoldierId,
                        MissionInstanceId = 100,
                        MissionPositionId = a.MissionPositionId,
                        MissionPosition = a.MissionPositionId == 1 ? simple : medic
                    })
                    .ToList()
            };
        }

        private static string Name(int soldierId) => $"Soldier {soldierId}";

        [Fact]
        public void PartiallyStaffed_ReportsCountsAndMissingSlots()
        {
            var dto = ScheduleInstanceMapper.ToDto(Instance((11, 5, 1)), "Guard", Name);

            Assert.Equal(100, dto.InstanceId);
            Assert.Equal(10, dto.MissionId);
            Assert.Equal("Guard", dto.MissionName);
            Assert.Equal("2026-11-12T22:00:00", dto.From);
            Assert.Equal("2026-11-13T02:30:00", dto.To);
            Assert.Equal(3, dto.RequiredCount);
            Assert.Equal(1, dto.AssignedCount);
            Assert.False(dto.IsFilled);

            var assignment = Assert.Single(dto.Assignments);
            Assert.Equal(11, assignment.SoldierMissionId);
            Assert.Equal(5, assignment.SoldierId);
            Assert.Equal("Soldier 5", assignment.SoldierName);
            Assert.Equal(1, assignment.MissionPositionId);
            Assert.Equal((int)Position.Simple, assignment.Position);

            Assert.Collection(dto.MissingPositions,
                m => { Assert.Equal(1, m.MissionPositionId); Assert.Equal((int)Position.Simple, m.Position); Assert.Equal(1, m.Missing); },
                m => { Assert.Equal(2, m.MissionPositionId); Assert.Equal((int)Position.Medic, m.Position); Assert.Equal(1, m.Missing); });
        }

        [Fact]
        public void FullyStaffed_IsFilled_WithNoMissingSlots()
        {
            var dto = ScheduleInstanceMapper.ToDto(Instance((11, 5, 1), (12, 6, 1), (13, 7, 2)), "Guard", Name);

            Assert.True(dto.IsFilled);
            Assert.Equal(3, dto.AssignedCount);
            Assert.Empty(dto.MissingPositions);
            // Higher position numbers (e.g. Medic) are listed first.
            Assert.Equal((int)Position.Medic, dto.Assignments[0].Position);
        }

        [Fact]
        public void IsFilled_IsCountBased_EvenIfASlotIsStillMissing()
        {
            // Three soldiers in the Simple slot (over capacity) while the Medic slot is empty.
            var dto = ScheduleInstanceMapper.ToDto(Instance((11, 5, 1), (12, 6, 1), (13, 7, 1)), "Guard", Name);

            Assert.True(dto.IsFilled);
            var missing = Assert.Single(dto.MissingPositions);
            Assert.Equal(2, missing.MissionPositionId);
        }

        [Fact]
        public void EmptyInstance_ListsAllSlotsAsMissing()
        {
            var dto = ScheduleInstanceMapper.ToDto(Instance(), "Guard", Name);

            Assert.False(dto.IsFilled);
            Assert.Empty(dto.Assignments);
            Assert.Equal(3, dto.MissingPositions.Sum(m => m.Missing));
        }
    }
}
