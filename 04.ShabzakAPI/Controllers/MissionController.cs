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

        [HttpGet("GetMissions")]
        public List<Mission> GetMissions()
        {
            var ret = _missionService.GetMissions();
            return ret;
        }

        [HttpPost("AddMission")]
        public Mission AddMission(Mission mission)
        {

            var ret = _missionService.AddMission(mission);
            return ret;
        }

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

        [HttpPost("DeleteMission")]
        public int DeleteMission(int missionId)
        {

            var ret = _missionService.DeleteMission(missionId);
            return ret;
        }

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

        [HttpPost("UnassignSoldier")]
        public Mission UnassignSoldier(int missionId, int missionInstanceId, int soldierId)
        {

            var ret = _missionService.UnassignSoldiersToMission(missionId, missionInstanceId, soldierId);
            return ret;
        }


        [HttpPost("GetAvailableSoldiers")]
        public List<GetAvailableSoldiersModel> GetAvailableSoldiers(GetAvailableSoldiersRequest request)
        {
            var ret = _missionService.GetAvailableSoldiers(request.MissionInstanceId, request.SoldiersPool);
            int? val = 0;
            var temp = val ?? int.MaxValue;
            return ret;
        }

        [HttpPost("GetMissionInstances")]
        public List<MissionInstance> GetMissionInstances(int missionId)
        {
            var ret = _missionService.GetMissionInstances(missionId);
            return ret;
        }

        [HttpPost("AssignSoldiersToMissionInstance")]
        public void AssignSoldiersToMissionInstance(List<SoldierMission> soldiers)
        {
            _missionService.AssignSoldiersToMissionInstance(soldiers);
        }

        [HttpPost("AutoAssign")]
        public List<AssignmentValidationModel> AutoAssign(AutoAssignModel model)
        {
            var ret = _autoAssignService.AutoAssign(model.StartDate, model.EndDate, model.Missions, model.Soldiers);
            return [ret];
        }

        [HttpPost("AcceptAssignCandidate")]
        public List<Mission> AcceptAssignCandidate(string candidateId)
        {
            var ret = _autoAssignService.AcceptAutoAssignCandidate(candidateId);
            return ret;
        }

        [HttpPost("GetMissionInstancesInRange")]
        public List<MissionInstance> GetMissionInstancesInRange(GetMissionInstancesRangesVM model)
        {
            var ret = _missionService.GetMissionInstancesInRange(model.From, model.To, model.FullDay, model.UnassignedOnly);
            return ret;
        }

        [HttpGet("GetAllCandidates")]
        public List<string> GetAllCandidates()
        {
            var ret = _autoAssignService.GetAllCandidates();
            return ret;
        }

        [HttpPost("GetCandidate")]
        public AssignmentValidationModel GetCandidate(string guid)
        {
            var ret = _autoAssignService.GetCandidate(guid);
            return ret;
        }

        [HttpPost("RemoveSoldierFromMissionInstance")]
        public List<Mission> RemoveSoldierFromMissionInstance(int soldierId, int missionInstanceId)
        {
            _autoAssignService.RemoveSoldierFromMissionInstance(soldierId, missionInstanceId);
            return GetMissions();

        }
    }
}
