using BL.Cache;
using BL.Extensions;
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
        public MissionController(MissionService missionService)
        {
            _missionService = missionService;
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
        public Mission UpdateMission(Mission mission)
        {

            var ret = _missionService.UpdateMission(mission);
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
    }
}
