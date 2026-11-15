using BL.Services;
using DataLayer.Models;

namespace ShabzakAPI.Tests
{
    public class CandidateAcceptanceFilterTests
    {
        private static SoldierMissionCandidate Row(int soldierId, int instanceId, int slotId = 1) =>
            new() { SoldierId = soldierId, MissionInstanceId = instanceId, MissionPositionId = slotId, CandidateId = "c1" };

        private static readonly HashSet<int> Instances = [100, 101];
        private static readonly HashSet<int> Soldiers = [5, 6, 7];
        private static readonly HashSet<int> Slots = [1, 2];

        private static CandidateAcceptance Select(IEnumerable<SoldierMissionCandidate> rows, params (int SoldierId, int InstanceId)[] existing) =>
            CandidateAcceptanceFilter.Select(rows, Instances, Soldiers, Slots, existing.ToHashSet());

        [Fact]
        public void NewAssignments_AreAllAccepted()
        {
            var rows = new[] { Row(5, 100), Row(6, 100, 2), Row(7, 101) };

            var result = Select(rows);

            Assert.Equal(rows, result.Accepted);
            Assert.Equal(0, result.SkippedDuplicates);
            Assert.Equal(0, result.SkippedDangling);
        }

        [Fact]
        public void ExistingAssignments_AreNotInsertedAgain()
        {
            // The soldier was already assigned to instance 100 (e.g. a skipped instance saved by an older run, or a manual
            // assignment made after the run): only the new row of instance 101 is accepted.
            var result = Select([Row(5, 100), Row(5, 101)], (5, 100));

            var accepted = Assert.Single(result.Accepted);
            Assert.Equal(101, accepted.MissionInstanceId);
            Assert.Equal(1, result.SkippedDuplicates);
        }

        [Fact]
        public void RepeatedRows_OfTheSameSoldierAndInstance_AreAcceptedOnce()
        {
            var result = Select([Row(5, 100, 1), Row(5, 100, 2)]);

            Assert.Single(result.Accepted);
            Assert.Equal(1, result.SkippedDuplicates);
        }

        [Fact]
        public void RowsOfDeletedInstancesSoldiersOrSlots_AreSkipped()
        {
            var result = Select([Row(5, 999), Row(99, 100), Row(6, 100, 42), Row(7, 101)]);

            var accepted = Assert.Single(result.Accepted);
            Assert.Equal(7, accepted.SoldierId);
            Assert.Equal(3, result.SkippedDangling);
            Assert.Equal(0, result.SkippedDuplicates);
        }

        [Fact]
        public void OtherSoldiersInTheSameInstance_AreNotDuplicates()
        {
            var result = Select([Row(6, 100), Row(7, 100)], (5, 100));

            Assert.Equal(2, result.Accepted.Count);
            Assert.Equal(0, result.SkippedDuplicates);
        }
    }
}
