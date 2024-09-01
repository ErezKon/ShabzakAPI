using BL.Cache;
using BL.Models;
using BL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ShabzakAPI.ViewModels;

namespace ShabzakAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MetadataController : ControllerBase
    {
        private readonly MetadataService _metadataService;
        private readonly MissionsCache _missionsCache;
        public MetadataController(MetadataService metadataService, MissionsCache missionsCache) { 
            _metadataService = metadataService;
            _missionsCache = missionsCache;
        }

        [HttpPost("GetAssignmentsPerSoldiers")]
        public List<AssignmentsPerSoldier> GetAssignmentsPerSoldiers(MetadataModel range) 
        {
            var ret = _metadataService.GetAssignmentsPerSoldiers(range.From, range.To, range.SoldierType);
            return ret;
        }

        [HttpPost("GetHoursPerSoldiers")]
        public List<HoursPerSoldier> GetHoursPerSoldiers(MetadataModel range)
        {
            var ret = _metadataService.GetHoursPerSoldiers(range.From, range.To, range.SoldierType);
            return ret;
        }

        [HttpPost("GetAssignmentsBreakdownPerSoldiers")]
        public List<AssignmentsBreakdownVM> GetAssignmentsBreakdownPerSoldiers(MetadataModel range)
        {
            var ret = _metadataService.GetAssignmentsBreakdownPerSoldiers(range.From, range.To, range.SoldierType)
                .Select(r => new AssignmentsBreakdownVM
                {
                    Soldier = r.Soldier,
                    From = r.From,
                    To = r.To,
                    Breakdown = r.AssignmentsPerMissionId.Select(ass => new BreakdownVM
                    {
                        Mission = _missionsCache.GetMissionById(ass.Key, false),
                        Count = ass.Value
                    })
                    .ToList()
                })
                .ToList();
            return ret;
        }
    }
}
