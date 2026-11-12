using BL.Models.Schedule;
using BL.Services;
using DataLayer.Models;

namespace ShabzakAPI.Tests
{
    public class AssignmentConstraintEvaluatorTests
    {
        private static readonly DateTime Day = new(2026, 11, 12);
        private const int SoldierId = 7;

        /// <summary>Instance 100 of mission 10, 08:00–12:00, slots: Simple×2 (id 1), Medic×1 (id 2).</summary>
        private static ConstraintInstance Instance(
            int? requiredRestAfter = 8,
            IReadOnlyList<ConstraintSlot>? slots = null,
            IReadOnlyList<ConstraintSlotAssignment>? assignments = null) =>
            new(100, 10, "Guard", Day.AddHours(8), Day.AddHours(12), requiredRestAfter,
                slots ?? [new(1, Position.Simple, 2), new(2, Position.Medic, 1)],
                assignments ?? []);

        private static ConstraintSoldier Soldier(params Position[] positions) =>
            new(SoldierId, "Test Soldier", true, positions.Length > 0 ? positions : [Position.Simple]);

        private static ConstraintAssignment Other(int instanceId, double fromHour, double toHour, int? requiredRestAfter = null) =>
            new(instanceId, "Other mission", Day.AddHours(fromHour), Day.AddHours(toHour), requiredRestAfter);

        private static AssignmentValidationDto Eval(
            ConstraintInstance instance,
            ConstraintSoldier soldier,
            IEnumerable<ConstraintAssignment>? assignments = null,
            IEnumerable<ConstraintVacation>? vacations = null,
            ConstraintOptions? options = null) =>
            AssignmentConstraintEvaluator.Evaluate(instance, soldier, assignments ?? [], vacations ?? [], options);

        [Fact]
        public void FreeSoldier_IsValid_WithExactSuggestedSlot()
        {
            var r = Eval(Instance(), Soldier(Position.Simple));

            Assert.True(r.IsValid);
            Assert.Empty(r.Violations);
            Assert.Empty(r.Warnings);
            Assert.Equal(PositionFitKind.Exact, r.PositionFit);
            Assert.Equal(1, r.SuggestedMissionPositionId);
            Assert.Equal((int)Position.Simple, r.SuggestedPosition);
            Assert.Null(r.RestBeforeHours);
            Assert.Null(r.RestAfterHours);
            Assert.Null(r.Overlap);
            Assert.Null(r.Vacation);
        }

        [Fact]
        public void Overlap_IsViolation_WithDetails()
        {
            var r = Eval(Instance(), Soldier(), [Other(200, 10, 14)]);

            Assert.False(r.IsValid);
            Assert.Contains(AssignmentConstraintCodes.Overlap, r.Violations);
            Assert.NotNull(r.Overlap);
            Assert.Equal(200, r.Overlap!.InstanceId);
            Assert.Equal("Other mission", r.Overlap.MissionName);
            Assert.Equal("2026-11-12T10:00:00", r.Overlap.From);
            Assert.Equal("2026-11-12T14:00:00", r.Overlap.To);
        }

        [Fact]
        public void BackToBack_IsNotOverlap_ButViolatesDefaultRestBefore()
        {
            var r = Eval(Instance(), Soldier(), [Other(200, 4, 8)]);

            Assert.DoesNotContain(AssignmentConstraintCodes.Overlap, r.Violations);
            Assert.Contains(AssignmentConstraintCodes.RestBefore, r.Violations);
            Assert.Equal(0, r.RestBeforeHours);
            Assert.Equal(8, r.RequiredRestBeforeHours);
        }

        [Theory]
        [InlineData(4, true)]
        [InlineData(6, true)]
        [InlineData(12, false)]
        public void RestBefore_UsesPreviousMissionsRequiredRest(int previousRequiredRest, bool expectedValid)
        {
            // Previous assignment ends at 02:00 → 6h gap before the 08:00 start.
            var r = Eval(Instance(), Soldier(), [Other(200, 0, 2, previousRequiredRest)]);

            Assert.Equal(expectedValid, r.IsValid);
            Assert.Equal(expectedValid, !r.Violations.Contains(AssignmentConstraintCodes.RestBefore));
            Assert.Equal(6, r.RestBeforeHours);
            Assert.Equal(previousRequiredRest, r.RequiredRestBeforeHours);
        }

        [Theory]
        [InlineData(1, false)] // ends 01:00 → 7h gap < default 8h
        [InlineData(0, true)]  // ends 00:00 → 8h gap
        public void RestBefore_DefaultsTo8Hours(double previousEndHour, bool expectedValid)
        {
            var r = Eval(Instance(), Soldier(), [Other(200, previousEndHour - 4, previousEndHour, null)]);

            Assert.Equal(expectedValid, r.IsValid);
            Assert.Equal(AssignmentConstraintEvaluator.DefaultRequiredRestHours, r.RequiredRestBeforeHours);
        }

        [Fact]
        public void RestBefore_UsesOnlyTheNearestPreviousAssignment()
        {
            var r = Eval(Instance(), Soldier(), [Other(200, -4, 0, 2), Other(201, 4, 6, 1)]);

            Assert.True(r.IsValid);
            Assert.Equal(2, r.RestBeforeHours);
            Assert.Equal(1, r.RequiredRestBeforeHours);
        }

        [Theory]
        [InlineData(8, false)]
        [InlineData(4, true)]
        public void RestAfter_UsesThisMissionsRequiredRest(int instanceRequiredRest, bool expectedValid)
        {
            // Next assignment starts at 16:00 → 4h gap after the 12:00 end.
            var r = Eval(Instance(instanceRequiredRest), Soldier(), [Other(200, 16, 20, 24)]);

            Assert.Equal(expectedValid, r.IsValid);
            Assert.Equal(expectedValid, !r.Violations.Contains(AssignmentConstraintCodes.RestAfter));
            Assert.Equal(4, r.RestAfterHours);
            Assert.Equal(instanceRequiredRest, r.RequiredRestAfterHours);
        }

        [Theory]
        [InlineData(19, false)] // 7h gap < default 8h
        [InlineData(20, true)]  // 8h gap
        public void RestAfter_DefaultsTo8Hours(double nextStartHour, bool expectedValid)
        {
            var r = Eval(Instance(requiredRestAfter: null), Soldier(), [Other(200, nextStartHour, nextStartHour + 4)]);

            Assert.Equal(expectedValid, r.IsValid);
            Assert.Equal(AssignmentConstraintEvaluator.DefaultRequiredRestHours, r.RequiredRestAfterHours);
        }

        [Fact]
        public void ZeroRequiredRest_AllowsBackToBackOnBothSides()
        {
            var r = Eval(Instance(requiredRestAfter: 0), Soldier(), [Other(200, 4, 8, 0), Other(201, 12, 16, 0)]);

            Assert.True(r.IsValid);
            Assert.Equal(0, r.RestBeforeHours);
            Assert.Equal(0, r.RestAfterHours);
        }

        [Theory]
        [InlineData(VacationRequestStatus.Pending, false)]
        [InlineData(VacationRequestStatus.Approved, false)]
        [InlineData(VacationRequestStatus.Denied, true)]
        public void Vacation_PendingOrApprovedBlocks_DeniedIsIgnored(VacationRequestStatus status, bool expectedValid)
        {
            var vacation = new ConstraintVacation(55, Day, Day.AddDays(1), status);

            var r = Eval(Instance(), Soldier(), vacations: [vacation]);

            Assert.Equal(expectedValid, r.IsValid);
            if (expectedValid)
            {
                Assert.Null(r.Vacation);
            }
            else
            {
                Assert.Equal([AssignmentConstraintCodes.OnVacation], r.Violations);
                Assert.Equal(55, r.Vacation!.Id);
                Assert.Equal(status.ToString(), r.Vacation.Status);
                Assert.Equal("2026-11-12T00:00:00", r.Vacation.From);
            }
        }

        [Fact]
        public void Vacation_TouchingTheInstance_IsNotAConflict()
        {
            var r = Eval(Instance(), Soldier(), vacations: [new ConstraintVacation(1, Day.AddHours(12), Day.AddHours(20), VacationRequestStatus.Approved)]);

            Assert.True(r.IsValid);
        }

        [Fact]
        public void PositionFit_ExactSlotIsPreferredOverSimilar()
        {
            // A Medic-only soldier is "similar" for the Simple slot but "exact" for the Medic slot.
            var r = Eval(Instance(), Soldier(Position.Medic));

            Assert.True(r.IsValid);
            Assert.Equal(PositionFitKind.Exact, r.PositionFit);
            Assert.Equal(2, r.SuggestedMissionPositionId);
            Assert.Equal((int)Position.Medic, r.SuggestedPosition);
        }

        [Fact]
        public void PositionFit_Similar_IsValidWithWarning()
        {
            var r = Eval(Instance(slots: [new(3, Position.Marksman, 1)]), Soldier(Position.Sniper));

            Assert.True(r.IsValid);
            Assert.Equal(PositionFitKind.Similar, r.PositionFit);
            Assert.Equal([AssignmentConstraintCodes.SimilarPosition], r.Warnings);
            Assert.Equal(3, r.SuggestedMissionPositionId);
        }

        [Fact]
        public void PositionFit_None_IsMismatch()
        {
            var r = Eval(Instance(slots: [new(2, Position.Medic, 1)]), Soldier(Position.Negev));

            Assert.False(r.IsValid);
            Assert.Equal(PositionFitKind.None, r.PositionFit);
            Assert.Equal([AssignmentConstraintCodes.PositionMismatch], r.Violations);
            Assert.Null(r.SuggestedMissionPositionId);
            Assert.Null(r.SuggestedPosition);
        }

        [Theory]
        [InlineData(Position.ClassCommander, Position.PlatoonCommander, PositionFitKind.Similar)]
        [InlineData(Position.Sergant, Position.PlatoonCommander, PositionFitKind.Similar)]
        [InlineData(Position.CompanyDeputy, Position.CompanyCommander, PositionFitKind.Similar)]
        [InlineData(Position.PlatoonCommander, Position.Sergant, PositionFitKind.None)]
        [InlineData(Position.Simple, Position.ClassCommander, PositionFitKind.None)]
        [InlineData(Position.Simple, Position.Negev, PositionFitKind.Similar)]
        public void PositionFit_FollowsPositionHelperSimilarity(Position slotPosition, Position soldierPosition, string expectedFit)
        {
            var r = Eval(Instance(slots: [new(9, slotPosition, 1)]), Soldier(soldierPosition));

            Assert.Equal(expectedFit, r.PositionFit);
        }

        [Fact]
        public void FullInstance_IsViolation_WithoutSuggestion()
        {
            var full = Instance(slots: [new(1, Position.Simple, 1)], assignments: [new(1, 1)]);

            var r = Eval(full, Soldier(Position.Simple));

            Assert.Equal([AssignmentConstraintCodes.InstanceFull], r.Violations);
            Assert.Equal(PositionFitKind.Exact, r.PositionFit);
            Assert.Null(r.SuggestedMissionPositionId);
        }

        [Fact]
        public void IgnoreSoldierId_FreesTheReplacedSoldiersSlot()
        {
            var full = Instance(slots: [new(1, Position.Simple, 1)], assignments: [new(1, 1)]);

            var r = Eval(full, Soldier(Position.Simple), options: new ConstraintOptions(IgnoreSoldierId: 1));

            Assert.True(r.IsValid);
            Assert.Equal(1, r.SuggestedMissionPositionId);
        }

        [Fact]
        public void AlreadyAssigned_IsViolation()
        {
            var r = Eval(Instance(assignments: [new(SoldierId, 1)]), Soldier(Position.Simple));

            Assert.Contains(AssignmentConstraintCodes.AlreadyAssigned, r.Violations);
        }

        [Fact]
        public void ExcludeInstanceIds_IgnoresOverlapAndRest_ForMoves()
        {
            // The soldier is moving out of instance 200 (overlapping) and 201 ends one hour before the start.
            var assignments = new[] { Other(200, 9, 13), Other(201, 3, 7, 12) };

            var withoutExclude = Eval(Instance(), Soldier(), assignments);
            var withExclude = Eval(Instance(), Soldier(), assignments, options: new ConstraintOptions(ExcludeInstanceIds: [200, 201]));

            Assert.Contains(AssignmentConstraintCodes.Overlap, withoutExclude.Violations);
            Assert.Contains(AssignmentConstraintCodes.RestBefore, withoutExclude.Violations);
            Assert.True(withExclude.IsValid);
            Assert.Null(withExclude.RestBeforeHours);
        }

        [Fact]
        public void Swap_ValidatesWithIgnoreAndExclude()
        {
            // Y (soldier 7) is in instance 200 at the same time as instance 100 (where X = soldier 1 is assigned).
            // Swap: Y takes X's slot in 100 (ignore X) and leaves 200 (exclude 200).
            var instanceA = Instance(slots: [new(1, Position.Simple, 1)], assignments: [new(1, 1)]);

            var r = Eval(instanceA, Soldier(Position.Simple), [Other(200, 8, 12)],
                options: new ConstraintOptions(IgnoreSoldierId: 1, ExcludeInstanceIds: [200]));

            Assert.True(r.IsValid);
            Assert.Equal(1, r.SuggestedMissionPositionId);
        }

        [Fact]
        public void Inactive_IsViolation()
        {
            var r = Eval(Instance(), new ConstraintSoldier(SoldierId, "Inactive", false, [Position.Simple]));

            Assert.Equal([AssignmentConstraintCodes.Inactive], r.Violations);
        }

        [Fact]
        public void RequestedSlot_RestrictsFitAndCapacity()
        {
            var instance = Instance(slots: [new(1, Position.Simple, 1), new(2, Position.Medic, 1)], assignments: [new(1, 1)]);
            var soldier = Soldier(Position.Simple, Position.Medic);

            var fullSlot = Eval(instance, soldier, options: new ConstraintOptions(MissionPositionId: 1));
            var freeSlot = Eval(instance, soldier, options: new ConstraintOptions(MissionPositionId: 2));

            Assert.Equal([AssignmentConstraintCodes.InstanceFull], fullSlot.Violations);
            Assert.Null(fullSlot.SuggestedMissionPositionId);
            Assert.True(freeSlot.IsValid);
            Assert.Equal(2, freeSlot.SuggestedMissionPositionId);
        }

        [Fact]
        public void UnknownRequestedSlot_Throws()
        {
            Assert.Throws<ArgumentException>(() => Eval(Instance(), Soldier(), options: new ConstraintOptions(MissionPositionId: 999)));
        }

        [Fact]
        public void Violations_AreReportedInADeterministicOrder()
        {
            var instance = Instance(slots: [new(2, Position.Medic, 1)], assignments: [new(1, 2)]);
            var soldier = new ConstraintSoldier(SoldierId, "Busy", false, [Position.Negev]);
            var assignments = new[] { Other(200, 9, 10), Other(201, 2, 6), Other(202, 14, 15) };
            var vacations = new[] { new ConstraintVacation(1, Day.AddHours(11), Day.AddHours(13), VacationRequestStatus.Pending) };

            var r = Eval(instance, soldier, assignments, vacations);

            Assert.Equal(
                [
                    AssignmentConstraintCodes.Inactive,
                    AssignmentConstraintCodes.Overlap,
                    AssignmentConstraintCodes.OnVacation,
                    AssignmentConstraintCodes.InstanceFull,
                    AssignmentConstraintCodes.PositionMismatch,
                    AssignmentConstraintCodes.RestBefore,
                    AssignmentConstraintCodes.RestAfter
                ],
                r.Violations);
        }

        [Fact]
        public void NotFound_ReturnsInvalidResult()
        {
            var r = AssignmentConstraintEvaluator.NotFound(42);

            Assert.False(r.IsValid);
            Assert.Equal(42, r.SoldierId);
            Assert.Equal([AssignmentConstraintCodes.NotFound], r.Violations);
        }
    }
}
