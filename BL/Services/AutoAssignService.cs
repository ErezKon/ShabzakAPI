using BL.Cache;
using DataLayer.Models;
using BL.Models;
using BL.Logging;
using BL.Extensions;

namespace BL.Services
{
    public class AutoAssignService
    {
        private readonly SoldiersCache _soldiersCache;
        private readonly MissionsCache _missionsCache;
        private readonly MissionService _missionService;
        private readonly PositionHelper posHelper;

        private Dictionary<int, List<Position>> soldierPositionCache = new Dictionary<int, List<Position>>();
        public AutoAssignService(SoldiersCache soldiersCache, MissionsCache missionsCache, MissionService missionService)
        {
            _soldiersCache = soldiersCache;
            _missionsCache = missionsCache;
            _missionService = missionService;

            posHelper = new PositionHelper();

            SoldiersCache.ReloadCache();
            MissionsCache.ReloadCache();
        }

        public void AutoAssign(DateTime from, DateTime to, List<Mission>? missions = null)
        {
            if (missions == null || missions.Count == 0)
            {
                missions = _missionsCache.GetDBMissions();
            }
            var instances = GetMissionInstances(from, to, missions);
            foreach (var instance in instances)
            {
                Logger.LogToMemory($"Assigning to instance: {instance.Id}", LogLevel.Info);
                if(IsInstanceFilled(instance))
                {
                    Logger.LogToMemory($"Instance already filled, skipping.", LogLevel.Info);
                    continue;
                }
                var positions = instance.Mission.MissionPositions;
                var availableSoldiers = _missionService.GetAvailableSoldiers(instance.Id);
                var candidateSoldiers = new List<CandidateSoldierAssignment>();

                foreach (var soldier in availableSoldiers)
                {
                    Logger.LogToMemory($"Ranking soldier: {soldier.Soldier.Id}-{soldier.Soldier.Name}:", LogLevel.Info, ConsoleColor.Red);
                    var rank = RankSoldierForPosition(soldier, instance);
                    Logger.LogToMemory($"Soldier {soldier.Soldier.Id} Total score : {rank}", LogLevel.Info ,ConsoleColor.Green);
                    candidateSoldiers.Add(new CandidateSoldierAssignment
                    {
                        Soldier = _soldiersCache.GetDbSoldierById(soldier.Soldier.Id),
                        MissionInstance = instance,
                        Rank = rank,
                    });
                }
                candidateSoldiers = candidateSoldiers
                    .OrderByDescending(cs => cs.Rank)
                    .Where(cs => cs.Rank > 0)
                    .ToList();
                AssignSoldiersToInstance(candidateSoldiers, instance);
            }
            Logger.DumpMemoryLogs();
        }

        public bool ValidateAssignments(List<MissionInstance> instances)
        {
            var valid = true;
            using var db = new DataLayer.ShabzakDB();
            foreach (var instance in instances)
            {
                
            }
            return valid;
        }

        public bool IsInstanceFilled(MissionInstance instance)
        {
            var totalPositions = instance.Mission.MissionPositions.Sum(mp => mp.Count);
            using var db = new DataLayer.ShabzakDB();
            var totalAssignments = db.SoldierMission
                .Count(sm => sm.MissionInstanceId == instance.Id);
            return totalPositions == totalAssignments;
        }

        private void AssignSoldiersToInstance(List<CandidateSoldierAssignment> soldiers, MissionInstance missionInstance)
        {
            using var db = new DataLayer.ShabzakDB();
            //var groupedByScore = soldiers
            //    .GroupBy(s => s.Rank)
            //    .ToDictionary(k => k.Key, v => v.ToList().Shuffle().ToList());
            //soldiers = groupedByScore.SelectMany(gbs => gbs.Value)
            //    .ToList();

            foreach (var soldier in soldiers)
            {
                if(soldier.Soldier.Id == 43)
                {
                    var zubery = true;
                }
                soldier.MissionsAssignedTo = db.SoldierMission
                    .Count(sm => sm.SoldierId == soldier.Soldier.Id);
            }

            soldiers = soldiers
                .OrderByDescending(cs => cs.Rank)
                .ThenBy(cs => cs.MissionsAssignedTo)
                .ToList();

            var missionPositions = missionInstance.Mission.MissionPositions
                .OrderByDescending(mp => mp.Position)
                .Select(mp => new MissionPositionAssignmentCounter
                {
                    MissionPositionId = mp.Id,
                    Position = mp.Position,
                    Count = mp.Count
                })
                .ToList();
            var assignments = new List<SoldierMission>();
            foreach (var soldier in soldiers)
            {
                if (!missionPositions.Any(mp => mp.Count > 0))
                {
                    break;
                }
                var soldierPositions = GetSoldierSimilarPositions(soldier.Soldier)
                    .OrderDescending()
                    .ToList();
                var soldierAssigned = false;
                foreach (var soldierPosition in soldierPositions)
                {
                    var position = missionPositions.FirstOrDefault(mp => mp.Position == soldierPosition);
                    if (position != null && position.Count - 1 >= 0)
                    {
                        if(soldier.Soldier.Id == 43)
                        {
                            var zubery = true;
                        }
                        assignments.Add(new SoldierMission
                        {
                            Id = 0,
                            SoldierId = soldier.Soldier.Id,
                            MissionInstanceId = missionInstance.Id,
                            MissionPositionId = position.MissionPositionId
                        });
                        soldierAssigned = true;
                        position.Count--;
                        break;
                    }
                }
                if (soldierAssigned)
                {
                    continue;
                }
                //soldierPositions = GetSoldierSimilarPositions(soldier.Soldier)
                //    .OrderDescending()
                //    .ToList();
                //foreach (var soldierPosition in soldierPositions)
                //{
                //    var position = missionPositions.FirstOrDefault(mp => mp.Position == soldierPosition);
                //    if (position != null && position.Count - 1 >= 0)
                //    {
                //        if (soldier.Soldier.Id == 43)
                //        {
                //            var zubery = true;
                //        }
                //        assignments.Add(new SoldierMission
                //        {
                //            Id = 0,
                //            SoldierId = soldier.Soldier.Id,
                //            MissionInstanceId = missionInstance.Id,
                //            MissionPositionId = position.MissionPositionId
                //        });
                //        soldierAssigned = true;
                //        position.Count--;
                //        break;
                //    }
                //}
            }
            db.SoldierMission.AddRange(assignments);
            db.SaveChanges();
        }

        private List<MissionInstance> GetMissionInstances(DateTime from, DateTime to, List<Mission> missions)
        {
            var ret = new List<MissionInstance>();
            foreach (var mission in missions)
            {
                var instances = mission.MissionInstances
                    .Where(mi => mi.FromTime >= from && mi.ToTime <= to)
                    .ToList();

                ret.AddRange(instances);
            }
            return ret;
        }

        private double RankSoldierForPosition(GetAvailableSoldiersModel soldier, MissionInstance missionInstance)
        {
            if(soldier.Soldier.Id == 43)
            {
                var zubery = true;
            }
            var baseScore = 1.0;
            var soldierRestMultiplier = GetSoldierRestMultiplier(soldier);
            Logger.LogToMemory($"\tSoldier Rest Multiplier is: {soldierRestMultiplier}");
            if (soldierRestMultiplier == 0.0)
            {
                return 0.0;
            }
            baseScore *= soldierRestMultiplier;
            var dbSoldier = _soldiersCache.GetDbSoldierById(soldier.Soldier.Id);

            var positionScoreMultiplier = GetSoldierPositionMultiplier(dbSoldier, missionInstance);
            Logger.LogToMemory($"\tSoldier Position Multiplier is: {positionScoreMultiplier}");
            if (positionScoreMultiplier == 0.0)
            {
                return 0.0;
            }
            baseScore *= positionScoreMultiplier;

            var missionAvg = RankSoldierByMissionAvg(soldier, missionInstance);
            Logger.LogToMemory($"\tSoldier Mission Avg Multiplier is: {missionAvg}");
            baseScore *= missionAvg;

            return baseScore;
        }


        private double RankSoldierByMissionAvg(GetAvailableSoldiersModel soldier, MissionInstance missionInstance)
        {
            using var db = new DataLayer.ShabzakDB();
            var soldiers = db.SoldierMission.Where(sm => sm.MissionInstance.MissionId == missionInstance.MissionId)
                .GroupBy(sm => sm.SoldierId)
                .ToDictionary(k => k.Key, v => v.ToList());

            double total = soldiers.Keys
                .Count;
            if(total == 0)
            {
                return 1.0;
            }
            double assignmentsSum = soldiers.SelectMany(s => s.Value).Count();
            var avg = assignmentsSum / total;
            var soldierMissionAssignments = db.SoldierMission
                .Count(sm => sm.SoldierId == soldier.Soldier.Id && sm.MissionInstance.MissionId == missionInstance.MissionId);
            if(soldierMissionAssignments <= avg)
            {
                return 1.0;
            }
            var marginError = avg * 1.25;
            if (soldierMissionAssignments <= marginError)
            {
                return 0.85;
            }
            marginError = avg * 1.5;
            if (soldierMissionAssignments <= marginError)
            {
                return 0.7;
            }
            marginError = avg * 1.75;
            if (soldierMissionAssignments <= marginError)
            {
                return 0.5;
            }
            marginError = avg * 2;
            if (soldierMissionAssignments <= marginError)
            {
                return 0.2;
            }
            return 0.0;
        }

        private double RankSoldierByHoursAvg(GetAvailableSoldiersModel soldier, MissionInstance missionInstance)
        {
            using var db = new DataLayer.ShabzakDB();
            var soldiers = db.SoldierMission.Where(sm => sm.MissionInstance.MissionId == missionInstance.MissionId)
                .GroupBy(sm => sm.SoldierId)
                .ToDictionary(k => k.Key, v => v.ToList());

            double total = soldiers.Keys
                .Count;
            if (total == 0)
            {
                return 1.0;
            }
            double assignmentsSum = soldiers.SelectMany(s => s.Value).Count();
            var avg = assignmentsSum / total;
            var soldierMissionAssignments = db.SoldierMission
                .Count(sm => sm.SoldierId == soldier.Soldier.Id && sm.MissionInstance.MissionId == missionInstance.MissionId);
            if (soldierMissionAssignments < avg)
            {
                return 1.0;
            }
            var marginError = avg * 1.25;
            if (soldierMissionAssignments < marginError)
            {
                return 0.85;
            }
            marginError = avg * 1.5;
            if (soldierMissionAssignments < marginError)
            {
                return 0.7;
            }
            marginError = avg * 1.75;
            if (soldierMissionAssignments < marginError)
            {
                return 0.5;
            }
            marginError = avg * 2;
            if (soldierMissionAssignments < marginError)
            {
                return 0.2;
            }
            return 0.0;
        }


        private double GetSoldierPositionMultiplier(Soldier soldier, MissionInstance missionInstance)
        {
            var missionPositions = missionInstance.Mission.MissionPositions;
            foreach (var soldierPosition in soldier.GetSoldierPositions())
            {
                if (missionPositions.Any(mp => mp.Position == soldierPosition))
                {
                    return 1;
                }
            }
            var soldierPositions = GetSoldierSimilarPositions(soldier);
            var matchingCount = 0;
            foreach (var soldierPosition in soldierPositions)
            {
                if(missionPositions.Any(mp => mp.Position == soldierPosition))
                {
                    matchingCount++;
                }
            }
            return matchingCount switch
            {
                0 => 0,
                1 => 0.7,
                2 => 0.8,
                3 => 0.9,
                _ => 1
            };
        }

        private double GetSoldierRestMultiplier(GetAvailableSoldiersModel soldier)
        {
            soldier.RestTimeBefore = soldier.RestTimeBefore ?? int.MaxValue;
            soldier.RestTimeAfter = soldier.RestTimeAfter ?? int.MaxValue;
            if (soldier.RestTimeBefore == 0 || soldier.RestTimeAfter == 0)
            {
                return 0.0;
            }
            if (soldier.RestTimeBefore < 8 && soldier.RestTimeAfter < 8)
            {
                return 0.1;
            }
            if (soldier.RestTimeBefore < 8 || soldier.RestTimeAfter < 8)
            {
                return 0.3;
            }
            if (soldier.RestTimeBefore == int.MaxValue || soldier.RestTimeAfter == int.MaxValue)
            {
                return 1.0;
            }
            if(soldier.RestTimeBefore >= 12) 
            {
                return 0.9;
            }
            if (soldier.RestTimeBefore >= 10)
            {
                return 0.8;
            }
            if (soldier.RestTimeBefore >= 8)
            {
                return 0.7;
            }

            return 0.0;
        }

        private List<Position> GetSoldierPositions(Soldier soldier)
        {
            return soldier.Position.Split([','], StringSplitOptions.RemoveEmptyEntries)
                    .Select(s =>
                    {
                        if (int.TryParse(s, out int numericValue))
                        {
                            return (Position)numericValue;
                        }
                        return Position.Simple;
                    })
                    .ToList();
        }

        private List<Position> GetSoldierSimilarPositions(Soldier soldier)
        {
            if(soldierPositionCache.ContainsKey(soldier.Id))
            {
                return soldierPositionCache[soldier.Id];
            }
            var pos = soldier.GetSoldierPositions()
                    .SelectMany(s => posHelper.GetSimilarPositions(s))
                    .Distinct()
                    .ToList();
            soldierPositionCache[soldier.Id] = pos;
            return pos;
        }
    }
}
