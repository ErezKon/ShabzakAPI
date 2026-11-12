namespace ShabzakAPI.ViewModels
{
    /// <summary>
    /// Body of POST /api/Schedule/ValidateAssignments: validates each soldier for one mission instance.
    /// </summary>
    public class ValidateAssignmentsRequest
    {
        public int MissionInstanceId { get; set; }
        public List<int> SoldierIds { get; set; } = [];

        /// <summary>Validate against this slot only (otherwise the best free slot is suggested).</summary>
        public int? MissionPositionId { get; set; }

        /// <summary>Frees this soldier's slot in the instance (replacement scenario).</summary>
        public int? IgnoreSoldierId { get; set; }

        /// <summary>Instances ignored for overlap and rest checks (move / swap scenarios).</summary>
        public List<int>? ExcludeInstanceIds { get; set; }
    }
}
