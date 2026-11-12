using BL.Models.Schedule;
using BL.Services;
using DataLayer.Models;

namespace ShabzakAPI.Tests
{
    public class MissionSlotPlannerTests
    {
        /// <summary>Simple×2 (id 1), Medic×1 (id 2), Negev×1 (id 3).</summary>
        private static readonly ConstraintSlot[] Existing = [new(1, Position.Simple, 2), new(2, Position.Medic, 1), new(3, Position.Negev, 1)];

        [Fact]
        public void Create_AddsEverySlot()
        {
            var plan = MissionSlotPlanner.Plan([], [new(null, (int)Position.Simple, 2), new(null, (int)Position.Medic, 1)]);

            Assert.Empty(plan.Updated);
            Assert.Empty(plan.RemovedIds);
            Assert.Equal([new ConstraintSlot(0, Position.Simple, 2), new ConstraintSlot(0, Position.Medic, 1)], plan.Added);
            Assert.Equal(3, plan.RequiredCount);
        }

        [Fact]
        public void Update_UpsertsAndRemovesMissingSlots()
        {
            var plan = MissionSlotPlanner.Plan(Existing, [new(1, (int)Position.Simple, 3), new(null, (int)Position.ClassCommander, 1)]);

            Assert.Equal([new ConstraintSlot(1, Position.Simple, 3)], plan.Updated);
            Assert.Equal([new ConstraintSlot(0, Position.ClassCommander, 1)], plan.Added);
            Assert.Equal([2, 3], plan.RemovedIds);
            Assert.Equal(4, plan.RequiredCount);
        }

        [Fact]
        public void Update_CanChangeASlotsPosition()
        {
            var plan = MissionSlotPlanner.Plan(Existing, [new(1, (int)Position.Simple, 2), new(2, (int)Position.Sniper, 1), new(3, (int)Position.Negev, 1)]);

            Assert.Contains(new ConstraintSlot(2, Position.Sniper, 1), plan.Updated);
            Assert.Empty(plan.Added);
            Assert.Empty(plan.RemovedIds);
        }

        [Fact]
        public void EmptyList_Throws()
        {
            Assert.Throws<ArgumentException>(() => MissionSlotPlanner.Plan(Existing, null));
            Assert.Throws<ArgumentException>(() => MissionSlotPlanner.Plan(Existing, []));
        }

        [Theory]
        [InlineData(-1, 1)]
        [InlineData(18, 1)]
        [InlineData(0, 0)]
        public void InvalidPositionOrCount_Throws(int position, int count)
        {
            Assert.Throws<ArgumentException>(() => MissionSlotPlanner.Plan([], [new(null, position, count)]));
        }

        [Fact]
        public void SlotIdOfAnotherMission_Throws()
        {
            Assert.Throws<ArgumentException>(() => MissionSlotPlanner.Plan(Existing, [new(99, (int)Position.Simple, 1)]));
            Assert.Throws<ArgumentException>(() => MissionSlotPlanner.Plan([], [new(1, (int)Position.Simple, 1)]));
        }

        [Fact]
        public void RepeatedSlotIdOrPosition_Throws()
        {
            Assert.Throws<ArgumentException>(() => MissionSlotPlanner.Plan(Existing, [new(1, (int)Position.Simple, 1), new(1, (int)Position.Medic, 1)]));
            Assert.Throws<ArgumentException>(() => MissionSlotPlanner.Plan(Existing, [new(1, (int)Position.Simple, 1), new(null, (int)Position.Simple, 1)]));
        }
    }
}
