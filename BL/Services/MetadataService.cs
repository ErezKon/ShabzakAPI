using BL.Cache;
using BL.Extensions;
using BL.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BL.Services
{
    /// <summary>
    /// Service for computing assignment statistics and fairness metadata.
    /// Provides aggregated data for charts: assignments per soldier, hours per soldier, and per-mission breakdowns.
    /// Supports filtering by soldier type (All, NonCommanders, CommandersOnly, OfficersOnly).
    /// </summary>
    public class MetadataService
    {
        private readonly SoldiersCache _soldiersCache;
        private readonly MissionsCache _missionsCache;
        /// <summary>
        /// Initializes the metadata service with required caches.
        /// </summary>
        public MetadataService(SoldiersCache soldiersCache, MissionsCache missionsCache)
        { 
            _soldiersCache = soldiersCache;
            _missionsCache = missionsCache;
        }

        /// <summary>
        /// Counts the total assignments per soldier within a date range.
        /// Filters soldiers by type and only counts instances that fall within the range.
        /// </summary>
        /// <param name="from">Start of the date range.</param>
        /// <param name="to">End of the date range.</param>
        /// <param name="type">Filter: All, NonCommanders, CommandersOnly, or OfficersOnly.</param>
        /// <returns>List of soldier-assignment-count pairs.</returns>
        public List<AssignmentsPerSoldier> GetAssignmentsPerSoldiers(DateTime? from, DateTime to, SoldierMetadataType? type = SoldierMetadataType.All)
        {
            var ret = new List<AssignmentsPerSoldier>();
            using var db = new DataLayer.ShabzakDB();
            DateTime absoluteFrom;
            if(!db.MissionInstances.Any())
            {
                return ret;
            }
            if (!from.HasValue)
            {
                absoluteFrom = db.MissionInstances.Min(mi => mi.FromTime);
            } else
            {
                absoluteFrom = from.Value;
            }
            var countPerSoldier = db.SoldierMission
                .Where(sm => sm.MissionInstance.FromTime >= absoluteFrom && sm.MissionInstance.ToTime <= to.AddDays(10))
                .GroupBy(sm => sm.SoldierId)
                .ToDictionary(k => k.Key, v => v.Count());

            foreach (var soldierCount in countPerSoldier)
            {
                var soldier = _soldiersCache.GetSoldierById(soldierCount.Key);
                if(type == SoldierMetadataType.All || 
                    (type == SoldierMetadataType.NonCommanders && !soldier.IsCommander()) ||
                    (type == SoldierMetadataType.CommandersOnly && soldier.IsCommander()) ||
                    (type == SoldierMetadataType.OfficersOnly && soldier.IsOfficer()))
                {
                    ret.Add(new AssignmentsPerSoldier
                    {
                        Soldier = soldier,
                        From = absoluteFrom,
                        To = to,
                        TotalAssignments = soldierCount.Value
                    });
                }
            }
            return ret;
        }
        /// <summary>
        /// Calculates total duty hours per soldier within a date range.
        /// Uses ActualHours from mission if defined, otherwise computes from instance duration.
        /// </summary>
        /// <param name="from">Start of the date range.</param>
        /// <param name="to">End of the date range.</param>
        /// <param name="type">Filter: All, NonCommanders, CommandersOnly, or OfficersOnly.</param>
        /// <returns>List of soldier-hours pairs.</returns>
        public List<HoursPerSoldier> GetHoursPerSoldiers(DateTime? from, DateTime to, SoldierMetadataType? type = SoldierMetadataType.All)
        {
            var ret = new List<HoursPerSoldier>();
            using var db = new DataLayer.ShabzakDB();
            DateTime absoluteFrom;
            if (!db.MissionInstances.Any())
            {
                return ret;
            }
            if (!from.HasValue)
            {
                absoluteFrom = db.MissionInstances.Min(mi => mi.FromTime);
            }
            else
            {
                absoluteFrom = from.Value;
            }
            var countPerSoldier = db.SoldierMission
                .Include(sm => sm.MissionInstance)
                .Include(sm => sm.MissionInstance.Mission)
                .Where(sm => sm.MissionInstance.FromTime >= absoluteFrom && sm.MissionInstance.ToTime <= to.AddDays(10))
                .GroupBy(sm => sm.SoldierId)
                .ToDictionary(k => k.Key, v => v
                    .Select(sm => new
                    {
                        sm.SoldierId,
                        sm.MissionInstance,
                        sm.MissionInstance.Mission
                    })
                    .ToList()
                );

            foreach (var soldierCount in countPerSoldier)
            {
                var soldier = _soldiersCache.GetSoldierById(soldierCount.Key);
                if ((type == SoldierMetadataType.NonCommanders && soldier.IsCommander()) || 
                    (type == SoldierMetadataType.CommandersOnly && !soldier.IsCommander()) ||
                    (type == SoldierMetadataType.OfficersOnly && !soldier.IsOfficer()))
                {
                    continue;
                }
                var hours = new HoursPerSoldier
                {
                    Soldier = soldier,
                    From = absoluteFrom,
                    To = to,
                    TotalHours = 0
                };
                foreach(var hour in soldierCount.Value)
                {
                    if (hour.Mission?.ActualHours.HasValue == true && hour.Mission.ActualHours.Value > 0)
                    {
                        hours.TotalHours += hour.Mission.ActualHours.Value;
                    }
                    else
                    {
                        var span = hour.MissionInstance.ToTime - hour.MissionInstance.FromTime;
                        hours.TotalHours += (span.TotalMilliseconds / 1000 / 60 / 60);
                    }
                }
                ret.Add(hours);
            }
            return ret;
        }

        /// <summary>
        /// Computes a per-mission assignment breakdown for each soldier within a date range.
        /// Returns how many times each soldier was assigned to each specific mission.
        /// </summary>
        /// <param name="from">Start of the date range.</param>
        /// <param name="to">End of the date range.</param>
        /// <param name="type">Filter: All, NonCommanders, CommandersOnly, or OfficersOnly.</param>
        /// <returns>List of breakdown models with per-soldier per-mission assignment counts.</returns>
        public List<AssignmentsBreakdown> GetAssignmentsBreakdownPerSoldiers(DateTime? from, DateTime to, SoldierMetadataType? type = SoldierMetadataType.All)
        {
            var ret = new List<AssignmentsBreakdown>();
            using var db = new DataLayer.ShabzakDB();
            DateTime absoluteFrom;
            if (!db.MissionInstances.Any())
            {
                return ret;
            }
            if (!from.HasValue)
            {
                absoluteFrom = db.MissionInstances.Min(mi => mi.FromTime);
            }
            else
            {
                absoluteFrom = from.Value;
            }
            var countPerSoldier = db.SoldierMission
                .Include(sm => sm.MissionInstance)
                .Where(sm => sm.MissionInstance.FromTime >= absoluteFrom && sm.MissionInstance.ToTime <= to.AddDays(10))
                .GroupBy(sm => sm.SoldierId)
                .ToDictionary(k => k.Key, v => v
                    .Select(kv => new
                    {
                        kv.MissionInstance.MissionId,
                        kv.MissionInstanceId
                    })
                    .GroupBy(kk => kk.MissionId)
                    .ToDictionary(kk => kk.Key, kv => kv.Count())
                );

            foreach (var soldierCount in countPerSoldier)
            {
                var soldier = _soldiersCache.GetSoldierById(soldierCount.Key);
                if ((type == SoldierMetadataType.NonCommanders && soldier.IsCommander()) || 
                    (type == SoldierMetadataType.CommandersOnly && !soldier.IsCommander()) ||
                    (type == SoldierMetadataType.OfficersOnly && !soldier.IsOfficer()))
                {
                    continue;
                }
                var hours = new AssignmentsBreakdown
                {
                    Soldier = soldier,
                    From = absoluteFrom,
                    To = to,
                    AssignmentsPerMissionId = new()
                };
                hours.Soldier.Missions = [];
                foreach (var hour in soldierCount.Value)
                {
                    hours.AssignmentsPerMissionId[hour.Key] = hour.Value;
                }
                ret.Add(hours);
            }
            return ret;
        }
    }
}
