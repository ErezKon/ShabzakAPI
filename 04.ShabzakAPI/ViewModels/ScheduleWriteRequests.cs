namespace ShabzakAPI.ViewModels
{
    /// <summary>Body of POST /api/Schedule/AssignSoldier.</summary>
    public class AssignSoldierRequest
    {
        public int MissionInstanceId { get; set; }
        public int SoldierId { get; set; }

        /// <summary>Slot to use; otherwise the best free fit is chosen.</summary>
        public int? MissionPositionId { get; set; }

        /// <summary>Apply despite constraint violations (never for ALREADY_ASSIGNED).</summary>
        public bool AllowViolations { get; set; }
    }

    /// <summary>Body of POST /api/Schedule/UnassignSoldier.</summary>
    public class UnassignSoldierRequest
    {
        public int MissionInstanceId { get; set; }
        public int SoldierId { get; set; }
    }

    /// <summary>
    /// Body of POST /api/Schedule/ReplaceSoldier. With <see cref="Swap"/>, the new soldier must be assigned to
    /// <see cref="SwapMissionInstanceId"/>, and the old soldier takes their place there.
    /// </summary>
    public class ScheduleReplaceSoldierRequest
    {
        public int MissionInstanceId { get; set; }
        public int OldSoldierId { get; set; }
        public int NewSoldierId { get; set; }
        public bool Swap { get; set; }
        public int? SwapMissionInstanceId { get; set; }
        public bool AllowViolations { get; set; }
    }

    /// <summary>Body of POST /api/Schedule/MoveSoldier.</summary>
    public class MoveSoldierRequest
    {
        public int SoldierId { get; set; }
        public int FromMissionInstanceId { get; set; }
        public int ToMissionInstanceId { get; set; }

        /// <summary>Slot in the target instance; otherwise the same slot (same mission) or the best free fit.</summary>
        public int? ToMissionPositionId { get; set; }
        public bool AllowViolations { get; set; }
    }
}
