using BL.Models.Schedule;
using BL.Services;
using DataLayer.Models;

namespace ShabzakAPI.Tests
{
    public class ScheduleChangePlannerTests
    {
        private static readonly DateTime Day = new(2026, 11, 12);

        /// <summary>Slots of mission 10: Simple×2 (id 1), Medic×1 (id 2).</summary>
        private static readonly ConstraintSlot[] Slots = [new(1, Position.Simple, 2), new(2, Position.Medic, 1)];

        private static ConstraintInstance Instance(int id, double fromHour, double toHour, params ConstraintSlotAssignment[] assignments) =>
            new(id, 10, "Guard", Day.AddHours(fromHour), Day.AddHours(toHour), 8, Slots, assignments);

        private static ConstraintSlotAssignment Seat(int soldierId, int slotId) => new(soldierId, slotId);

        private static ConstraintSoldier Soldier(int id, params Position[] positions) =>
            new(id, $"Soldier {id}", true, positions.Length > 0 ? positions : [Position.Simple]);

        private static ConstraintAssignment Assignment(ConstraintInstance instance) =>
            new(instance.InstanceId, instance.MissionName, instance.From, instance.To, instance.RequiredRestAfter);

        private static AssignmentValidationDto Evaluate(PlannedPlacement placement, params ConstraintAssignment[] soldierAssignments) =>
            AssignmentConstraintEvaluator.Evaluate(placement.Instance, placement.Soldier, soldierAssignments, [], placement.Options);

        private static AssignmentValidationDto Validation(int soldierId, params string[] violations) =>
            new() { SoldierId = soldierId, Violations = [.. violations], IsValid = violations.Length == 0 };

        [Fact]
        public void Assign_ValidatesTheRequestedSlot()
        {
            var placement = ScheduleChangePlanner.Assign(Instance(100, 8, 12), Soldier(7), requestedSlotId: 2);

            Assert.Equal(2, placement.SlotId);
            Assert.Equal(2, placement.Options.MissionPositionId);
            Assert.Null(placement.Options.IgnoreSoldierId);
            Assert.Null(placement.Options.ExcludeInstanceIds);
        }

        [Fact]
        public void Assign_WithoutARequest_TakesTheBestFreeFit()
        {
            var placement = ScheduleChangePlanner.Assign(Instance(100, 8, 12), Soldier(7, Position.Medic));

            Assert.Equal(2, placement.SlotId);
            Assert.True(Evaluate(placement).IsValid);
        }

        [Fact]
        public void Replace_FreesTheOldSoldiersSeat_AndPrefersIt()
        {
            // Full instance: 1 and 2 in the Simple slot, 3 in the Medic slot. Replace the medic with a Simple+Medic soldier.
            var instance = Instance(100, 8, 12, Seat(1, 1), Seat(2, 1), Seat(3, 2));

            var placement = ScheduleChangePlanner.Replace(instance, oldSoldierId: 3, Soldier(7, Position.Simple, Position.Medic));
            var validation = Evaluate(placement);

            Assert.Equal(2, placement.SlotId);
            Assert.Equal(new ConstraintOptions(2, 3), placement.Options);
            Assert.True(validation.IsValid);
            Assert.Equal(2, validation.SuggestedMissionPositionId);
        }

        [Fact]
        public void Replace_WithASoldierWhoCannotFillTheVacatedSlot_IsAPositionMismatch()
        {
            var instance = Instance(100, 8, 12, Seat(1, 1), Seat(2, 1), Seat(3, 2));

            var placement = ScheduleChangePlanner.Replace(instance, oldSoldierId: 3, Soldier(7, Position.Simple));
            var validation = Evaluate(placement);

            Assert.Equal(2, placement.SlotId);
            Assert.Equal([AssignmentConstraintCodes.PositionMismatch], validation.Violations);
        }

        [Fact]
        public void Swap_EachSoldierTakesTheOthersSeat_AndLeavesTheirOwnInstance()
        {
            var original = Instance(100, 8, 12, Seat(1, 1));
            var swapInstance = Instance(200, 12, 16, Seat(7, 2));

            var (newIntoOriginal, oldIntoSwap) = ScheduleChangePlanner.Swap(original, Soldier(1, Position.Simple, Position.Medic), swapInstance, Soldier(7, Position.Simple, Position.Medic));

            Assert.Same(original, newIntoOriginal.Instance);
            Assert.Equal(7, newIntoOriginal.Soldier.SoldierId);
            Assert.Equal(1, newIntoOriginal.SlotId);
            Assert.Equal(1, newIntoOriginal.Options.IgnoreSoldierId);
            Assert.Equal([200], newIntoOriginal.Options.ExcludeInstanceIds!);

            Assert.Same(swapInstance, oldIntoSwap.Instance);
            Assert.Equal(1, oldIntoSwap.Soldier.SoldierId);
            Assert.Equal(2, oldIntoSwap.SlotId);
            Assert.Equal(7, oldIntoSwap.Options.IgnoreSoldierId);
            Assert.Equal([100], oldIntoSwap.Options.ExcludeInstanceIds!);
        }

        [Fact]
        public void Swap_BetweenOverlappingInstances_IsValid_OnlyThanksToTheExclusions()
        {
            // Soldier 1 is in A (08–12), soldier 7 in B (10–14): each one's own instance overlaps the other one.
            var a = Instance(100, 8, 12, Seat(1, 1));
            var b = Instance(200, 10, 14, Seat(7, 1));
            var s1 = Soldier(1);
            var s7 = Soldier(7);

            var (sevenIntoA, oneIntoB) = ScheduleChangePlanner.Swap(a, s1, b, s7);
            var withoutExclusion = AssignmentConstraintEvaluator.Evaluate(a, s7, [Assignment(b)], [], new ConstraintOptions(IgnoreSoldierId: 1));

            Assert.True(Evaluate(sevenIntoA, Assignment(b)).IsValid);
            Assert.True(Evaluate(oneIntoB, Assignment(a)).IsValid);
            Assert.Contains(AssignmentConstraintCodes.Overlap, withoutExclusion.Violations);
        }

        [Fact]
        public void Move_ExcludesTheSourceInstance_AndKeepsTheSlotWithinTheSameMission()
        {
            // The soldier sits in the Medic slot of A; B (same mission) also has a free Simple slot with more missing.
            var a = Instance(100, 8, 12, Seat(7, 2));
            var b = Instance(200, 10, 14);

            var placement = ScheduleChangePlanner.Move(b, Soldier(7, Position.Simple, Position.Medic), fromInstanceId: 100, currentSlotId: 2);

            Assert.Equal(2, placement.SlotId);
            Assert.Equal([100], placement.Options.ExcludeInstanceIds!);
            Assert.Null(placement.Options.IgnoreSoldierId);
            Assert.True(Evaluate(placement, Assignment(a)).IsValid);
        }

        [Fact]
        public void Move_ToAnotherMission_UsesTheRequestedSlotOrTheBestFit()
        {
            var other = new ConstraintInstance(300, 20, "Patrol", Day.AddHours(14), Day.AddHours(22), 8, [new(8, Position.Simple, 1), new(9, Position.Medic, 1)], []);
            var soldier = Soldier(7, Position.Simple, Position.Medic);

            Assert.Equal(8, ScheduleChangePlanner.Move(other, soldier, 100, currentSlotId: 2).SlotId);
            Assert.Equal(9, ScheduleChangePlanner.Move(other, soldier, 100, currentSlotId: 2, requestedSlotId: 9).SlotId);
        }

        [Fact]
        public void Move_IntoTheSameInstanceTwice_IsAlreadyAssigned()
        {
            var b = Instance(200, 10, 14, Seat(7, 1));

            var placement = ScheduleChangePlanner.Move(b, Soldier(7), fromInstanceId: 100, currentSlotId: 1);

            Assert.Contains(AssignmentConstraintCodes.AlreadyAssigned, Evaluate(placement).Violations);
        }

        [Fact]
        public void Reschedule_RevalidatesEveryAssignedSoldierInTheirOwnSlot()
        {
            // The instance moves to 10–14; soldier 1 also has 12–16 elsewhere, soldier 3 is free, soldier 99 is unknown.
            var moved = Instance(100, 10, 14, Seat(1, 1), Seat(3, 2), Seat(99, 1));
            var soldiers = new Dictionary<int, ConstraintSoldier> { [1] = Soldier(1), [3] = Soldier(3, Position.Medic) };
            var elsewhere = new ConstraintAssignment(500, "Patrol", Day.AddHours(12), Day.AddHours(16), 8);

            var placements = ScheduleChangePlanner.Reschedule(moved, soldiers);

            Assert.Equal([1, 3], placements.Select(p => p.Soldier.SoldierId));
            Assert.Equal(new ConstraintOptions(1, 1), placements[0].Options);
            Assert.Equal(new ConstraintOptions(2, 3), placements[1].Options);
            Assert.Equal([AssignmentConstraintCodes.Overlap], Evaluate(placements[0], elsewhere).Violations);
            Assert.True(Evaluate(placements[1]).IsValid);
        }

        [Fact]
        public void BlockReason_IsNull_WithoutViolations()
        {
            Assert.Null(ScheduleChangePlanner.BlockReason([Validation(5)], allowViolations: false, BlockingPolicy.Placement));
        }

        [Fact]
        public void BlockReason_AnyViolationBlocks_UnlessOverridden()
        {
            var validations = new[] { Validation(5, AssignmentConstraintCodes.RestBefore, AssignmentConstraintCodes.Overlap), Validation(6) };

            var blocked = ScheduleChangePlanner.BlockReason(validations, allowViolations: false, BlockingPolicy.Placement);

            Assert.Equal("Blocked by constraint violations: soldier 5: REST_BEFORE, OVERLAP. Set allowViolations=true to override.", blocked);
            Assert.Null(ScheduleChangePlanner.BlockReason(validations, allowViolations: true, BlockingPolicy.Placement));
        }

        [Theory]
        [InlineData(AssignmentConstraintCodes.AlreadyAssigned)]
        [InlineData(AssignmentConstraintCodes.NotFound)]
        public void BlockReason_HardViolations_CannotBeOverridden(string code)
        {
            var blocked = ScheduleChangePlanner.BlockReason([Validation(5, code)], allowViolations: true, BlockingPolicy.Placement);

            Assert.Equal($"Cannot apply the change: soldier 5: {code}. These violations cannot be overridden.", blocked);
        }

        [Fact]
        public void BlockReason_Reschedule_OnlyOverlapBlocks()
        {
            var restOnly = new[] { Validation(5, AssignmentConstraintCodes.RestBefore, AssignmentConstraintCodes.OnVacation) };
            var overlap = new[] { Validation(5, AssignmentConstraintCodes.RestBefore, AssignmentConstraintCodes.Overlap) };

            Assert.Null(ScheduleChangePlanner.BlockReason(restOnly, allowViolations: false, BlockingPolicy.Reschedule));
            Assert.Equal(
                "Blocked by constraint violations: soldier 5: OVERLAP. Set allowViolations=true to override.",
                ScheduleChangePlanner.BlockReason(overlap, allowViolations: false, BlockingPolicy.Reschedule));
            Assert.Null(ScheduleChangePlanner.BlockReason(overlap, allowViolations: true, BlockingPolicy.Reschedule));
        }
    }
}
