using BL.Models.Schedule;
using BL.Services;
using DataLayer.Models;

namespace ShabzakAPI.Tests
{
    public class AssignmentSlotSelectorTests
    {
        private static readonly DateTime Day = new(2026, 11, 12);

        private static ConstraintInstance Instance(IReadOnlyList<ConstraintSlot> slots, params ConstraintSlotAssignment[] assignments) =>
            new(100, 10, "Patrol", Day.AddHours(8), Day.AddHours(12), 8, slots, assignments);

        private static ConstraintSlotAssignment Seat(int soldierId, int slotId) => new(soldierId, slotId);

        /// <summary>Simple×2 (id 1), Medic×1 (id 2).</summary>
        private static readonly ConstraintSlot[] SimpleAndMedic = [new(1, Position.Simple, 2), new(2, Position.Medic, 1)];

        private static int? Select(ConstraintInstance instance, Position[] positions, int? requested = null, int? preferred = null, int? ignore = null) =>
            AssignmentSlotSelector.SelectTargetSlot(instance, positions, requested, preferred, ignore);

        [Fact]
        public void RequestedSlot_IsUsedAsIs_EvenWithoutAFit()
        {
            Assert.Equal(2, Select(Instance(SimpleAndMedic), [Position.Simple], requested: 2));
        }

        [Fact]
        public void ExactFreeSlot_WinsOverASimilarOne()
        {
            // A medic is "similar" for the Simple slot and "exact" for the Medic slot.
            Assert.Equal(2, Select(Instance(SimpleAndMedic), [Position.Medic]));
        }

        [Fact]
        public void SimilarFreeSlot_IsUsedWhenNoSlotFitsExactly()
        {
            var instance = Instance([new(3, Position.Marksman, 1), new(4, Position.Medic, 1)]);

            Assert.Equal(3, Select(instance, [Position.Sniper]));
        }

        [Fact]
        public void Ties_AreBrokenByMostMissingThenLowestId()
        {
            var slots = new ConstraintSlot[] { new(5, Position.Simple, 2), new(4, Position.Simple, 2), new(6, Position.Simple, 2) };

            Assert.Equal(4, Select(Instance(slots), [Position.Simple]));
            Assert.Equal(6, Select(Instance(slots, Seat(20, 4), Seat(21, 5)), [Position.Simple]));
        }

        [Fact]
        public void PreferredSlot_WinsWhenTheSoldierFitsIt()
        {
            // Without a preference the Simple slot wins (exact for both, more missing).
            var instance = Instance(SimpleAndMedic);
            var soldier = new[] { Position.Simple, Position.Medic };

            Assert.Equal(1, Select(instance, soldier));
            Assert.Equal(2, Select(instance, soldier, preferred: 2));
        }

        [Fact]
        public void PreferredSlot_WinsWithASimilarFit_OverAnExactSlotElsewhere()
        {
            var instance = Instance([new(3, Position.Marksman, 1), new(7, Position.Sniper, 1)]);

            Assert.Equal(7, Select(instance, [Position.Sniper]));
            Assert.Equal(3, Select(instance, [Position.Sniper], preferred: 3));
        }

        [Fact]
        public void PreferredSlot_IsSkipped_WhenTheSoldierDoesNotFitIt_AndAnotherFreeSlotFits()
        {
            Assert.Equal(1, Select(Instance(SimpleAndMedic), [Position.Simple], preferred: 2));
        }

        [Fact]
        public void PreferredSlot_IsUsed_WhenNoFreeSlotFits()
        {
            Assert.Equal(2, Select(Instance(SimpleAndMedic), [Position.ClassCommander], preferred: 2));
        }

        [Fact]
        public void NoFitAndNoPreference_UsesTheFreeSlotWithMostMissing()
        {
            Assert.Equal(1, Select(Instance(SimpleAndMedic), [Position.ClassCommander]));
        }

        [Fact]
        public void FullPreferredSlot_IsNotUsed()
        {
            var instance = Instance(SimpleAndMedic, Seat(30, 2));

            Assert.Equal(1, Select(instance, [Position.Simple, Position.Medic], preferred: 2));
        }

        [Fact]
        public void AllSlotsFull_UsesTheBestFittingSlot()
        {
            var full = Instance(SimpleAndMedic, Seat(30, 1), Seat(31, 1), Seat(32, 2));

            Assert.Equal(2, Select(full, [Position.Medic, Position.Negev]));
        }

        [Fact]
        public void AllSlotsFull_WithoutAnyFit_UsesTheLowestSlot()
        {
            var full = Instance(SimpleAndMedic, Seat(30, 1), Seat(31, 1), Seat(32, 2));

            Assert.Equal(1, Select(full, [Position.CompanyCommander]));
        }

        [Fact]
        public void MissionWithoutSlots_ReturnsNull()
        {
            Assert.Null(Select(Instance([]), [Position.Simple]));
        }

        [Fact]
        public void IgnoreSoldierId_FreesThatSoldiersSeat()
        {
            var full = Instance([new(1, Position.Simple, 1), new(2, Position.Medic, 1)], Seat(30, 1), Seat(31, 2));

            // Everything full: the medic would over-fill the exact Medic slot; with soldier 30 ignored the free Simple seat wins.
            Assert.Equal(2, Select(full, [Position.Medic]));
            Assert.Equal(1, Select(full, [Position.Medic], ignore: 30));
        }

        [Theory]
        [InlineData(Position.Medic, Position.Medic, PositionFitKind.Exact)]
        [InlineData(Position.Simple, Position.Negev, PositionFitKind.Similar)]
        [InlineData(Position.Marksman, Position.Sniper, PositionFitKind.Similar)]
        [InlineData(Position.ClassCommander, Position.PlatoonCommander, PositionFitKind.Similar)]
        [InlineData(Position.Simple, Position.ClassCommander, PositionFitKind.None)]
        [InlineData(Position.Sniper, Position.Marksman, PositionFitKind.None)]
        public void Fit_FollowsPositionHelper(Position slot, Position soldier, string expected)
        {
            Assert.Equal(expected, AssignmentSlotSelector.Fit(slot, [soldier]));
        }

        [Theory]
        [InlineData(new[] { Position.Simple })]
        [InlineData(new[] { Position.Medic })]
        [InlineData(new[] { Position.Simple, Position.Medic })]
        [InlineData(new[] { Position.Negev })]
        public void TargetSlot_MatchesTheEvaluatorsSuggestion(Position[] positions)
        {
            var instance = Instance(SimpleAndMedic, Seat(30, 1));
            var soldier = new ConstraintSoldier(7, "Test", true, positions);

            var validation = AssignmentConstraintEvaluator.Evaluate(instance, soldier, [], []);

            Assert.NotNull(validation.SuggestedMissionPositionId);
            Assert.Equal(validation.SuggestedMissionPositionId, Select(instance, positions));
        }
    }
}
