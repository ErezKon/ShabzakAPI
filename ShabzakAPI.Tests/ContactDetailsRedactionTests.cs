using BL.Services;
using Translators.Models;

namespace ShabzakAPI.Tests
{
    public class ContactDetailsRedactionTests
    {
        private static Soldier Soldier(int id) => new()
        {
            Id = id,
            Name = $"Soldier {id}",
            PersonalNumber = $"80000{id}",
            Phone = $"050000000{id}",
            Platoon = "1",
            Company = "A",
            Active = true,
            Positions = [Position.Simple, Position.Medic],
            Vacations = [new Vacation { Id = 10 + id }],
            Missions = [],
        };

        private static Mission MissionWith(params MissionInstance[] instances) => new()
        {
            Id = 1,
            Name = "Guard",
            Positions = [],
            MissionInstances = [.. instances],
        };

        private static MissionInstance InstanceWith(int id, params Soldier[] soldiers) => new()
        {
            Id = id,
            FromTime = "16/11/2026 08:00",
            ToTime = "16/11/2026 12:00",
            SoldierMissions = soldiers.Select(s => new SoldierMission { Id = 100 + s.Id, Soldier = s }).ToList(),
        };

        [Fact]
        public void EveryAssignedSoldier_LosesPhonePersonalNumberAndVacations()
        {
            var missions = new List<Mission> { MissionWith(InstanceWith(1, Soldier(1), Soldier(2)), InstanceWith(2, Soldier(3))) };

            var result = ContactDetailsRedaction.RemoveFromAssignedSoldiers(missions);

            var soldiers = result.SelectMany(m => m.MissionInstances).SelectMany(i => i.SoldierMissions).Select(sm => sm.Soldier).ToList();
            Assert.Equal(3, soldiers.Count);
            Assert.All(soldiers, s =>
            {
                Assert.Equal(string.Empty, s.Phone);
                Assert.Equal(string.Empty, s.PersonalNumber);
                Assert.Empty(s.Vacations);
            });
        }

        [Fact]
        public void ScheduleData_IsKept()
        {
            var missions = new List<Mission> { MissionWith(InstanceWith(7, Soldier(4))) };

            ContactDetailsRedaction.RemoveFromAssignedSoldiers(missions);

            var instance = Assert.Single(missions[0].MissionInstances);
            Assert.Equal(7, instance.Id);
            Assert.Equal("16/11/2026 08:00", instance.FromTime);
            var assignment = Assert.Single(instance.SoldierMissions);
            Assert.Equal(104, assignment.Id);
            Assert.Equal(4, assignment.Soldier.Id);
            Assert.Equal("Soldier 4", assignment.Soldier.Name);
            Assert.Equal("1", assignment.Soldier.Platoon);
            Assert.Equal("A", assignment.Soldier.Company);
            Assert.Equal([Position.Simple, Position.Medic], assignment.Soldier.Positions);
        }

        [Fact]
        public void MissingCollectionsAndSoldiers_AreTolerated()
        {
            var missions = new List<Mission>
            {
                new() { Id = 1, Name = "No instances", Positions = [], MissionInstances = null! },
                MissionWith(new MissionInstance { Id = 2, FromTime = "", ToTime = "", SoldierMissions = null! }),
                MissionWith(new MissionInstance { Id = 3, FromTime = "", ToTime = "", SoldierMissions = [new SoldierMission { Id = 9, Soldier = null! }] }),
            };

            var result = ContactDetailsRedaction.RemoveFromAssignedSoldiers(missions);

            Assert.Same(missions, result);
        }
    }
}
