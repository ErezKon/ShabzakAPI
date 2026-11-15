using DataLayer.Models;

namespace BL.Services
{
    /// <summary>The candidate rows to turn into assignments, and how many were left out and why.</summary>
    public sealed record CandidateAcceptance(IReadOnlyList<SoldierMissionCandidate> Accepted, int SkippedDuplicates, int SkippedDangling);

    /// <summary>
    /// Pure selection of the candidate rows that <c>AutoAssignService.AcceptAutoAssignCandidate</c> may insert as assignments.
    /// <list type="bullet">
    /// <item>Dangling rows are left out: their instance, soldier or position slot no longer exists (deleted after the run).
    /// Candidate rows have no foreign keys, and inserting one would make the whole acceptance fail.</item>
    /// <item>Duplicates are left out: the soldier is already assigned to that instance, or an earlier row of the candidate has the
    /// same soldier and instance. Candidates saved before the fix in <c>FinalizeScheduleResult</c> also contain the existing
    /// assignments of the instances the run skipped, which used to be inserted a second time.</item>
    /// </list>
    /// </summary>
    public static class CandidateAcceptanceFilter
    {
        public static CandidateAcceptance Select(
            IEnumerable<SoldierMissionCandidate> rows,
            IReadOnlySet<int> existingInstanceIds,
            IReadOnlySet<int> existingSoldierIds,
            IReadOnlySet<int> existingSlotIds,
            IReadOnlySet<(int SoldierId, int InstanceId)> existingAssignments)
        {
            var accepted = new List<SoldierMissionCandidate>();
            var seen = new HashSet<(int SoldierId, int InstanceId)>();
            var duplicates = 0;
            var dangling = 0;
            foreach (var row in rows)
            {
                if (!existingInstanceIds.Contains(row.MissionInstanceId) || !existingSoldierIds.Contains(row.SoldierId) || !existingSlotIds.Contains(row.MissionPositionId))
                {
                    dangling++;
                    continue;
                }
                var key = (row.SoldierId, row.MissionInstanceId);
                if (existingAssignments.Contains(key) || !seen.Add(key))
                {
                    duplicates++;
                    continue;
                }
                accepted.Add(row);
            }
            return new CandidateAcceptance(accepted, duplicates, dangling);
        }
    }
}
