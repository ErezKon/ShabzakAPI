using BL.Cache;
using BL.Models;
using BL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ShabzakAPI.Auth;
using ShabzakAPI.ViewModels;

namespace ShabzakAPI.Controllers
{
    /// <summary>
    /// Controller for assignment statistics and fairness metadata.
    /// Provides aggregated data for charts and reports on how evenly soldiers are assigned.
    /// All endpoints are AdminOrAbove.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class MetadataController : ControllerBase
    {
        private readonly MetadataService _metadataService;
        private readonly MissionsCache _missionsCache;

        /// <summary>
        /// Initializes a new instance of the MetadataController class.
        /// </summary>
        /// <param name="metadataService">The metadata service.</param>
        /// <param name="missionsCache">The missions cache.</param>
        public MetadataController(MetadataService metadataService, MissionsCache missionsCache) { 
            _metadataService = metadataService;
            _missionsCache = missionsCache;
        }

        /// <summary>
        /// Returns the total number of assignments per soldier within a date range.
        /// Can filter by soldier type: All, NonCommanders, CommandersOnly, or OfficersOnly.
        /// </summary>
        /// <param name="request">From/To dates and soldier type filter.</param>
        /// <returns>Dictionary mapping soldier name to assignment count.</returns>
        [HttpPost("GetAssignmentsPerSoldiers")]
        [Authorize(Policy = Policies.AdminOrAbove)]
        public List<AssignmentsPerSoldier> GetAssignmentsPerSoldiers(MetadataModel request) 
        {
            var ret = _metadataService.GetAssignmentsPerSoldiers(request.From, request.To, request.SoldierType);
            return ret;
        }

        /// <summary>
        /// Returns the total hours of duty per soldier within a date range.
        /// Uses ActualHours if defined on the mission, otherwise calculates from instance duration.
        /// </summary>
        /// <param name="request">From/To dates and soldier type filter.</param>
        /// <returns>Dictionary mapping soldier name to total hours.</returns>
        [HttpPost("GetHoursPerSoldiers")]
        [Authorize(Policy = Policies.AdminOrAbove)]
        public List<HoursPerSoldier> GetHoursPerSoldiers(MetadataModel request)
        {
            var ret = _metadataService.GetHoursPerSoldiers(request.From, request.To, request.SoldierType);
            return ret;
        }

        /// <summary>
        /// Returns a per-mission assignment breakdown for each soldier within a date range.
        /// Outer key = soldier name, inner key = mission name, value = assignment count.
        /// </summary>
        /// <param name="request">From/To dates and soldier type filter.</param>
        /// <returns>Nested dictionary: soldier → mission → count.</returns>
        [HttpPost("GetAssignmentsBreakdownPerSoldiers")]
        [Authorize(Policy = Policies.AdminOrAbove)]
        public List<AssignmentsBreakdownVM> GetAssignmentsBreakdownPerSoldiers(MetadataModel request)
        {
            var ret = _metadataService.GetAssignmentsBreakdownPerSoldiers(request.From, request.To, request.SoldierType)
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
