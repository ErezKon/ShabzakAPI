using BL.Models.Schedule;
using BL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShabzakAPI.Auth;
using ShabzakAPI.ViewModels;

namespace ShabzakAPI.Controllers
{
    /// <summary>
    /// Compact, agent-friendly schedule endpoints. Datetimes are ISO local wall-clock strings (yyyy-MM-ddTHH:mm:ss, no offset);
    /// positions are enum numbers. Query errors: 400 / 404 with <c>{ message }</c>. Writes (Admin or SuperAdmin) return
    /// <see cref="GeneralResponse{T}"/>: 200 applied, 409 blocked by violations (value carries the validations) or conflicting
    /// data, 404 unknown instance/soldier/assignment, 400 invalid request.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ScheduleController : ControllerBase
    {
        private readonly ScheduleService _scheduleService;

        /// <summary>
        /// Initializes a new instance of the ScheduleController class.
        /// </summary>
        /// <param name="scheduleService">The schedule query service.</param>
        public ScheduleController(ScheduleService scheduleService)
        {
            _scheduleService = scheduleService;
        }

        /// <summary>
        /// Returns the instances overlapping [from, to) (at most 62 days) sorted by start, with assignments and missing slots.
        /// Optional filters: missionIds, soldierIds (instances where any of them is assigned), onlyUnfilled.
        /// Available to any authenticated user (same data as the /assignments page).
        /// </summary>
        [HttpPost("GetAssignments")]
        public ActionResult<List<ScheduleInstanceDto>> GetAssignments(GetScheduleAssignmentsRequest request)
        {
            try
            {
                return _scheduleService.GetAssignments(request.From, request.To, request.MissionIds, request.SoldierIds, request.OnlyUnfilled);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Validates each soldier for one mission instance and returns violations (INACTIVE, ALREADY_ASSIGNED, OVERLAP,
        /// ON_VACATION, INSTANCE_FULL, POSITION_MISMATCH, REST_BEFORE, REST_AFTER, NOT_FOUND), warnings (SIMILAR_POSITION),
        /// the suggested slot and rest details. Admin or SuperAdmin only.
        /// </summary>
        [HttpPost("ValidateAssignments")]
        [Authorize(Policy = Policies.AdminOrAbove)]
        public ActionResult<List<AssignmentValidationDto>> ValidateAssignments(ValidateAssignmentsRequest request)
        {
            try
            {
                return _scheduleService.ValidateAssignments(
                    request.MissionInstanceId,
                    request.SoldierIds,
                    request.MissionPositionId,
                    request.IgnoreSoldierId,
                    request.ExcludeInstanceIds);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Assigns a soldier to an instance (requested slot, or the best free fit). Blocked (409) by any violation unless
        /// allowViolations; ALREADY_ASSIGNED always blocks. Value: <c>{ instance (null when blocked), validation }</c>.
        /// </summary>
        [HttpPost("AssignSoldier")]
        [Authorize(Policy = Policies.AdminOrAbove)]
        public ActionResult<GeneralResponse<AssignSoldierResultDto>> AssignSoldier(AssignSoldierRequest request) =>
            this.ExecuteWrite(
                "Schedule/AssignSoldier",
                $"instance={request.MissionInstanceId} soldier={request.SoldierId} slot={request.MissionPositionId?.ToString() ?? "auto"} allowViolations={request.AllowViolations}",
                () => _scheduleService.AssignSoldier(request.MissionInstanceId, request.SoldierId, request.MissionPositionId, request.AllowViolations));

        /// <summary>Removes a soldier from an instance. Value: the instance after the change.</summary>
        [HttpPost("UnassignSoldier")]
        [Authorize(Policy = Policies.AdminOrAbove)]
        public ActionResult<GeneralResponse<ScheduleInstanceDto>> UnassignSoldier(UnassignSoldierRequest request) =>
            this.ExecuteWrite(
                "Schedule/UnassignSoldier",
                $"instance={request.MissionInstanceId} soldier={request.SoldierId}",
                () => _scheduleService.UnassignSoldier(request.MissionInstanceId, request.SoldierId));

        /// <summary>
        /// Replaces a soldier in an instance (the new soldier prefers the vacated slot), or swaps two soldiers between two instances.
        /// Value: <c>{ instances (empty when blocked), validations }</c>.
        /// </summary>
        [HttpPost("ReplaceSoldier")]
        [Authorize(Policy = Policies.AdminOrAbove)]
        public ActionResult<GeneralResponse<ScheduleChangeResultDto>> ReplaceSoldier(ScheduleReplaceSoldierRequest request) =>
            this.ExecuteWrite(
                "Schedule/ReplaceSoldier",
                $"instance={request.MissionInstanceId} old={request.OldSoldierId} new={request.NewSoldierId} swap={request.Swap} swapInstance={request.SwapMissionInstanceId?.ToString() ?? "-"} allowViolations={request.AllowViolations}",
                () => _scheduleService.ReplaceSoldier(
                    request.MissionInstanceId,
                    request.OldSoldierId,
                    request.NewSoldierId,
                    request.Swap,
                    request.SwapMissionInstanceId,
                    request.AllowViolations));

        /// <summary>
        /// Moves a soldier between two instances (the source is excluded from the overlap and rest checks).
        /// Value: <c>{ instances [from, to] (empty when blocked), validations }</c>.
        /// </summary>
        [HttpPost("MoveSoldier")]
        [Authorize(Policy = Policies.AdminOrAbove)]
        public ActionResult<GeneralResponse<ScheduleChangeResultDto>> MoveSoldier(MoveSoldierRequest request) =>
            this.ExecuteWrite(
                "Schedule/MoveSoldier",
                $"soldier={request.SoldierId} from={request.FromMissionInstanceId} to={request.ToMissionInstanceId} slot={request.ToMissionPositionId?.ToString() ?? "auto"} allowViolations={request.AllowViolations}",
                () => _scheduleService.MoveSoldier(
                    request.SoldierId,
                    request.FromMissionInstanceId,
                    request.ToMissionInstanceId,
                    request.ToMissionPositionId,
                    request.AllowViolations));
    }
}
