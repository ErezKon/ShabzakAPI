namespace ShabzakAPI.ViewModels
{
    /// <summary>
    /// Body of POST /api/Schedule/GetAssignments. Datetimes are ISO local wall-clock (no offset);
    /// the range must be at most 62 days. Instances overlapping [From, To) are returned.
    /// </summary>
    public class GetScheduleAssignmentsRequest
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public List<int>? MissionIds { get; set; }

        /// <summary>When set, only instances where any of these soldiers is assigned (each with all its assignments).</summary>
        public List<int>? SoldierIds { get; set; }
        public bool OnlyUnfilled { get; set; }
    }
}
