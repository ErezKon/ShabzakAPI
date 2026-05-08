using BL.Cache;
using BL.Extensions;
using BL.Models;
using BL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ShabzakAPI.ViewModels;
using Translators.Models;

namespace ShabzakAPI.Controllers
{
    /// <summary>
    /// Controller responsible for all mission-related operations including CRUD,
    /// soldier assignment, auto-assignment (batch and interactive), and soldier replacement.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class MissionController : ControllerBase
    {
        private readonly MissionService _missionService;

        private readonly SoldiersCache _soldiersCache;
        private readonly AutoAssignService _autoAssignService;
        public MissionController(MissionService missionService, SoldiersCache soldiersCache, AutoAssignService autoAssignService)
        {
            _missionService = missionService;
            _soldiersCache = soldiersCache;
            _autoAssignService = autoAssignService;
        }

        /// <summary>
        /// Retrieves all missions with their instances, assigned soldiers, and position requirements.
        /// </summary>
        /// <returns>A list of all missions with full nested data.</returns>
        [HttpGet("GetMissions")]
        public List<Mission> GetMissions()
        {
            var ret = _missionService.GetMissions();
            return ret;
        }

        /// <summary>
        /// Creates a new mission with its positions and time instances.
        /// For special missions, a single instance is created from start time + duration.
        /// </summary>
        /// <param name="mission">The mission to create, including positions and instances.</param>
        /// <returns>The created mission with generated IDs.</returns>
        [HttpPost("AddMission")]
        public Mission AddMission(Mission mission)
        {

            var ret = _missionService.AddMission(mission);
            return ret;
        }

        /// <summary>
        /// Updates an existing mission's details, instances, and soldier assignments.
        /// Replaces all existing instances with the ones provided in the request.
        /// </summary>
        /// <param name="mission">The updated mission data including instances and soldier assignments.</param>
        /// <returns>The updated mission.</returns>
        [HttpPost("UpdateMission")]
        public Mission UpdateMission(AddEditMission mission)
        {
            var parsedMission = new Mission
            {
                Id = mission.Id,
                Name = mission.Name,
                Description = mission.Description,
                IsSpecial = mission.IsSpecial,
                FromTime = mission.FromTime,
                ToTime = mission.ToTime,
                Duration = mission.Duration,
                SoldiersRequired = mission.SoldiersRequired,
                CommandersRequired = mission.CommandersRequired,
                ActualHours = mission.ActualHours,
                RequiredRestAfter = mission.RequiredRestAfter,
                Positions = mission.Positions,
                MissionInstances = mission.MissionInstances
                    ?.Select(i =>
                    {
                        return new MissionInstance
                        {
                            Id = i.Id,
                            FromTime = i.FromTime,
                            ToTime = i.ToTime,
                            SoldierMissions = i.SoldierMissions
                                ?.Select(s =>
                                {
                                    return new SoldierMission
                                    {
                                        Id = s.Id,
                                        Soldier = _soldiersCache.GetSoldierById(s.Id),
                                        MissionPosition = new MissionPosition
                                        {
                                            MissionId = mission.Id,
                                            Position = s.Position
                                        }
                                    };
                                })
                                ?.ToList() ?? []
                        };
                    })
                    ?.ToList() ?? []
            };
            var ret = _missionService.UpdateMission(parsedMission);
            return ret;
        }

        /// <summary>
        /// Deletes a mission and all its related instances and assignments (cascade delete).
        /// </summary>
        /// <param name="missionId">The ID of the mission to delete.</param>
        /// <returns>The deleted mission's ID.</returns>
        [HttpPost("DeleteMission")]
        public int DeleteMission(int missionId)
        {

            var ret = _missionService.DeleteMission(missionId);
            return ret;
        }

        /// <summary>
        /// Assigns one or more soldiers to a specific mission instance with their designated positions.
        /// </summary>
        /// <param name="soldiers">Request containing mission ID, instance ID, and list of soldier-position pairs.</param>
        /// <returns>The updated mission with the new assignments.</returns>
        [HttpPost("AssignSoldiers")]
        public Mission AssignSoldiers(AssignSoldierToMissionRequest soldiers)
        {
            var ret = _missionService.AssignSoldiersToMission(soldiers.MissionId, soldiers.MissionInstanceId, soldiers.Soldiers
                .Select(s => new DataLayer.Models.SoldierMission
                {
                    SoldierId = s.SoldierId,
                    MissionPositionId = s.MissionPositionId,
                    MissionInstanceId = soldiers.MissionInstanceId,

                }).ToList());
            return ret;
        }

        /// <summary>
        /// Removes a soldier's assignment from a specific mission instance.
        /// </summary>
        /// <param name="missionId">The mission ID.</param>
        /// <param name="missionInstanceId">The instance ID to remove from.</param>
        /// <param name="soldierId">The soldier ID to unassign.</param>
        /// <returns>The updated mission.</returns>
        [HttpPost("UnassignSoldier")]
        public Mission UnassignSoldier(int missionId, int missionInstanceId, int soldierId)
        {

            var ret = _missionService.UnassignSoldiersToMission(missionId, missionInstanceId, soldierId);
            return ret;
        }


        /// <summary>
        /// Returns all soldiers with their availability status for a given mission instance.
        /// Each soldier includes rest-time-before/after metrics and whether they're already assigned.
        /// Results are sorted by best availability (most rest time).
        /// </summary>
        /// <param name="request">Contains the mission instance ID and optional soldier pool filter.</param>
        /// <returns>Ranked list of soldiers with availability and rest-time data.</returns>
        [HttpPost("GetAvailableSoldiers")]
        public List<GetAvailableSoldiersModel> GetAvailableSoldiers(GetAvailableSoldiersRequest request)
        {
            var ret = _missionService.GetAvailableSoldiers(request.MissionInstanceId, request.SoldiersPool);
            int? val = 0;
            var temp = val ?? int.MaxValue;
            return ret;
        }

        /// <summary>
        /// Retrieves all time-slot instances for a specific mission.
        /// </summary>
        /// <param name="missionId">The mission ID to get instances for.</param>
        /// <returns>List of mission instances (time slots).</returns>
        [HttpPost("GetMissionInstances")]
        public List<MissionInstance> GetMissionInstances(int missionId)
        {
            var ret = _missionService.GetMissionInstances(missionId);
            return ret;
        }

        /// <summary>
        /// Assigns a list of soldiers to a single mission instance. 
        /// Handles both adding new and removing old assignments to sync with the provided list.
        /// All soldiers must target the same instance.
        /// </summary>
        /// <param name="soldiers">The soldier-mission assignments (all must be for the same instance).</param>
        [HttpPost("AssignSoldiersToMissionInstance")]
        public void AssignSoldiersToMissionInstance(List<SoldierMission> soldiers)
        {
            _missionService.AssignSoldiersToMissionInstance(soldiers);
        }

        /// <summary>
        /// Runs the batch auto-assignment algorithm. Generates multiple candidate schedules
        /// (one per starting mission), ranks soldiers using rest/position/fairness scoring,
        /// fills mission instances, and stores candidates in the DB for later acceptance.
        /// </summary>
        /// <param name="model">Date range, selected missions/soldiers, max schedules, and scoring options.</param>
        /// <returns>List of candidate schedules with validation results. The best is marked with IsBestCandidate.</returns>
        [HttpPost("AutoAssign")]
        public List<AssignmentValidationModel> AutoAssign(AutoAssignModel model)
        {
            var ret = _autoAssignService.AutoAssign(
                model.StartDate,
                model.EndDate,
                model.Missions,
                model.Soldiers,
                model.MaxSchedules,
                model.ScoringOptions);
            return ret;
        }

        /// <summary>
        /// Accepts a previously generated auto-assign candidate schedule.
        /// Converts candidate assignments into real SoldierMission records and clears all candidates.
        /// </summary>
        /// <param name="candidateId">The GUID of the candidate schedule to accept.</param>
        /// <returns>All missions with updated assignments.</returns>
        [HttpPost("AcceptAssignCandidate")]
        public List<Mission> AcceptAssignCandidate(string candidateId)
        {
            var ret = _autoAssignService.AcceptAutoAssignCandidate(candidateId);
            return ret;
        }

        /// <summary>
        /// Retrieves mission instances within a date range, optionally filtering to unassigned-only.
        /// When fullDay is true, the range is expanded to cover entire days.
        /// </summary>
        /// <param name="model">From/To dates, fullDay flag, and unassignedOnly flag.</param>
        /// <returns>Filtered list of mission instances.</returns>
        [HttpPost("GetMissionInstancesInRange")]
        public List<MissionInstance> GetMissionInstancesInRange(GetMissionInstancesRangesVM model)
        {
            var ret = _missionService.GetMissionInstancesInRange(model.From, model.To, model.FullDay, model.UnassignedOnly);
            return ret;
        }

        /// <summary>
        /// Returns all pending auto-assign candidate schedule GUIDs that have not yet been accepted.
        /// </summary>
        /// <returns>List of candidate GUIDs.</returns>
        [HttpGet("GetAllCandidates")]
        public List<string> GetAllCandidates()
        {
            var ret = _autoAssignService.GetAllCandidates();
            return ret;
        }

        /// <summary>
        /// Retrieves the full details of a specific auto-assign candidate schedule from its JSON file.
        /// </summary>
        /// <param name="guid">The candidate schedule GUID.</param>
        /// <returns>The candidate schedule with valid/faulty/skipped instance breakdowns.</returns>
        [HttpPost("GetCandidate")]
        public AssignmentValidationModel GetCandidate(string guid)
        {
            var ret = _autoAssignService.GetCandidate(guid);
            return ret;
        }

        /// <summary>
        /// Removes a soldier from a mission instance and updates the instance's filled status.
        /// </summary>
        /// <param name="soldierId">The soldier to remove.</param>
        /// <param name="missionInstanceId">The instance to remove the soldier from.</param>
        /// <returns>All missions with updated assignments.</returns>
        [HttpPost("RemoveSoldierFromMissionInstance")]
        public List<Mission> RemoveSoldierFromMissionInstance(int soldierId, int missionInstanceId)
        {
            _autoAssignService.RemoveSoldierFromMissionInstance(soldierId, missionInstanceId);
            return GetMissions();

        }

        /// <summary>
        /// Starts an interactive (step-by-step) auto-assign session. The algorithm processes
        /// instances one by one and pauses to let the user review/override soldier picks.
        /// Pause behavior is controlled by the PauseOn option (EveryInstance or FaultyOnly).
        /// </summary>
        /// <param name="model">Date range, missions, soldiers, scoring options, pause strategy, and display flags.</param>
        /// <returns>The first step of the interactive session (Paused with pending instance, or Completed).</returns>
        [HttpPost("StartInteractiveAutoAssign")]
        public ActionResult<InteractiveAutoAssignStep> StartInteractiveAutoAssign(StartInteractiveAutoAssignModel model)
        {
            try
            {
                var ret = _autoAssignService.StartInteractive(
                    model.StartDate,
                    model.EndDate,
                    model.Missions,
                    model.Soldiers,
                    model.ScoringOptions,
                    model.PauseOn,
                    model.ShowAllSoldiersOnPause);
                return Ok(ret);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Continues an active interactive auto-assign session. The user provides soldier picks
        /// for the current instance or skips it. The algorithm advances to the next instance.
        /// </summary>
        /// <param name="model">Session ID, user's soldier picks, and skip flag.</param>
        /// <returns>The next step (Paused with next pending instance, or Completed with final results).</returns>
        [HttpPost("ContinueInteractiveAutoAssign")]
        public ActionResult<InteractiveAutoAssignStep> ContinueInteractiveAutoAssign(ContinueInteractiveAutoAssignModel model)
        {
            try
            {
                var ret = _autoAssignService.ContinueInteractive(
                    model.SessionId,
                    model.Picks,
                    model.SkipInstance);
                return Ok(ret);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Cancels an active interactive auto-assign session and discards its state.
        /// </summary>
        /// <param name="sessionId">The session ID to cancel.</param>
        /// <returns>No content on success.</returns>
        [HttpPost("CancelInteractiveAutoAssign")]
        public ActionResult CancelInteractiveAutoAssign(string sessionId)
        {
            _autoAssignService.CancelInteractive(sessionId);
            return NoContent();
        }

        /// <summary>
        /// Returns ranked replacement candidates for a soldier in a mission instance.
        /// Candidates are scored by position match, rest time, and assignment fairness.
        /// Includes overlap detection (candidates already assigned to overlapping instances).
        /// </summary>
        /// <param name="request">The mission instance ID and the soldier ID to exclude (the one being replaced).</param>
        /// <returns>Ranked list of replacement candidates with scores and overlap info.</returns>
        [HttpPost("GetReplacementCandidates")]
        public List<ReplacementCandidateModel> GetReplacementCandidates(GetReplacementCandidatesRequest request)
        {
            var ret = _missionService.GetReplacementCandidates(request.MissionInstanceId, request.ExcludeSoldierId);
            return ret;
        }

        /// <summary>
        /// Replaces a soldier in a mission instance with another soldier.
        /// Supports swap mode: when Swap=true, the old soldier takes the new soldier's slot
        /// in SwapMissionInstanceId, and the new soldier takes the old soldier's slot.
        /// </summary>
        /// <param name="request">Contains instance ID, old/new soldier IDs, swap flag, and optional swap instance ID.</param>
        /// <returns>All missions with updated assignments.</returns>
        [HttpPost("ReplaceSoldierInMissionInstance")]
        public List<Mission> ReplaceSoldierInMissionInstance(ReplaceSoldierRequest request)
        {
            _missionService.ReplaceSoldierInMissionInstance(
                request.MissionInstanceId,
                request.OldSoldierId,
                request.NewSoldierId,
                request.Swap,
                request.SwapMissionInstanceId);
            return GetMissions();
        }
    }
}
