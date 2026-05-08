using BL.Cache;
using BL.Extensions;
using BL.Logging;
using BL.Models;
using DataLayer;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Translators.Encryption;
using Translators.Models;
using Translators.Translators;

using VacationRequestStatus = DataLayer.Models.VacationRequestStatus;

namespace BL.Services
{
    /// <summary>
    /// Business logic service for soldier management.
    /// Handles CRUD operations, vacation requests, position normalization, and soldier summaries.
    /// </summary>
    public class SoldierService
    {
        private readonly PositionHelper positionHelper = new();
        public SoldierService()
        {
        }

        /// <summary>
        /// Creates a new soldier. Normalizes positions (ensures non-commanding soldiers have 'Simple'),
        /// encrypts PII, saves to DB, and updates the cache.
        /// </summary>
        /// <param name="soldier">The soldier to create (BL model).</param>
        /// <returns>The created soldier with generated ID and decrypted fields.</returns>
        public Soldier AddSoldier(Soldier soldier)
        {
            return AddSoldier(soldier.ToDB());
        }

        /// <summary>
        /// Creates a new soldier. Normalizes positions (ensures non-commanding soldiers have 'Simple'),
        /// encrypts PII, saves to DB, and updates the cache.
        /// </summary>
        /// <param name="soldier">The soldier to create (DB model).</param>
        /// <returns>The created soldier with generated ID and decrypted fields.</returns>
        public Soldier AddSoldier(DataLayer.Models.Soldier soldier)
        {
            Logger.Log($"Adding Soldier:\n {JsonConvert.SerializeObject(soldier, Formatting.Indented)}");
            try
            {
                NormalizePositions(soldier);
                using var db = new DataLayer.ShabzakDB();
                db.Soldiers.Add(soldier.Encrypt());
                db.SaveChanges();
                SoldiersCache.ReloadAsync();
                return soldier.Decrypt().ToBL();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error while adding soldier:\n{ex}");
                throw;
            }
        }

        /// <summary>
        /// Updates an existing soldier's details. Normalizes positions, encrypts PII,
        /// saves to DB, and updates the cache.
        /// </summary>
        /// <param name="soldier">The updated soldier data (BL model).</param>
        /// <returns>The updated soldier with decrypted fields.</returns>
        public Soldier Update(Soldier soldier) => Update(soldier.ToDB());

        /// <summary>
        /// Updates an existing soldier's details. Normalizes positions, encrypts PII,
        /// saves to DB, and updates the cache.
        /// </summary>
        /// <param name="soldier">The updated soldier data (DB model).</param>
        /// <returns>The updated soldier with decrypted fields.</returns>
        public Soldier Update(DataLayer.Models.Soldier soldier)
        {
            Logger.Log($"Updating Soldier:\n {JsonConvert.SerializeObject(soldier, Formatting.Indented)}");
            try
            {
                using var db = new DataLayer.ShabzakDB();
                var sol = db.Soldiers
                    .FirstOrDefault(s => s.Id == soldier.Id)
                    ?? throw new ArgumentException("Soldier not found.");

                sol.Name = soldier.Name;
                sol.Phone = soldier.Phone;
                sol.Position = soldier.Position;
                sol.Company = soldier.Company;
                sol.PersonalNumber = soldier.PersonalNumber;
                sol.Platoon = soldier.Platoon;
                sol.Encrypt();
                db.SaveChanges();
                SoldiersCache.ReloadAsync();
                return sol.Decrypt().ToBL();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error while updating soldier:\n{ex}");
                throw;
            }
        }

        /// <summary>
        /// Deletes a soldier and all associated data (assignments, vacations) via cascade delete.
        /// Reloads the soldiers cache after deletion.
        /// </summary>
        /// <param name="id">The ID of the soldier to delete.</param>
        public void DeleteSoldier(int id)
        {
            try
            {
                using var db = new DataLayer.ShabzakDB();
                var soldier = db.Soldiers.FirstOrDefault(s => s.Id == id);
                if(soldier == null)
                {
                    throw new ArgumentException("Can't find soldier.");
                }
                db.Soldiers.Remove(soldier);
                SoldiersCache.ReloadAsync();
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error while deleting soldier:\n{ex}");
                throw;
            }
        }

        /// <summary>
        /// Loads soldiers from a tab-delimited text file into the database.
        /// Format: Name\tPersonalNumber\tPhone\tPosition (per line). Encrypts PII before saving.
        /// Used for initial data seeding only.
        /// </summary>
        /// <param name="filePath">Path to the soldiers text file.</param>
        public void LoadSoldiersFromFile(string filePath)
        {
            var lines = File.ReadAllLines(filePath).Skip(1);
            var data = lines
                .Select(l =>
                {
                    var spl = l.Split("\t");
                    var ret = new DataLayer.Models.Soldier
                    {
                        Id = 0,
                        Name = spl[1],
                        PersonalNumber = spl[2],
                        Phone = spl[3],
                        Platoon = spl[4],
                        Company = spl[5],
                        Position = spl[6],
                        Active = spl[7] == "1"

                    };
                    return ret;
                })
                .ToList();
            using var db = new DataLayer.ShabzakDB();
            db.Soldiers.AddRange(data);
            db.SaveChanges();
        }

        /// <summary>
        /// Creates a vacation request for a soldier with Pending status.
        /// Pending and approved vacations block auto-assignment during that period.
        /// </summary>
        /// <param name="soldierId">The soldier's ID.</param>
        /// <param name="from">Vacation start date.</param>
        /// <param name="to">Vacation end date.</param>
        /// <returns>The created vacation record.</returns>
        public Vacation RequestVacation(int soldierId, DateTime from, DateTime to)
        {
            if (from >= to)
            {
                throw new ArgumentException("End date cannot be greater or equal to start date.");
            }
            var vacation = new DataLayer.Models.Vacation
            {
                SoldierId = soldierId,
                From = from,
                To = to,
                Approved = VacationRequestStatus.Pending
            };
            vacation.Soldier = null;
            using var db = new ShabzakDB();
            db.Vacations.Add(vacation);
            db.SaveChanges();
            return VacationTranslator.ToBL(vacation);
        }

        /// <summary>
        /// Approves or denies a pending vacation request.
        /// </summary>
        /// <param name="vacationId">The vacation request ID.</param>
        /// <param name="response">The approval status (Approved or Denied).</param>
        /// <returns>The updated vacation record.</returns>
        public Vacation RespondToVacationRequest(int vacationId, VacationRequestStatus response)
        {
            using var db = new ShabzakDB();
            var vacation = db.Vacations.FirstOrDefault(vac => vac.Id == vacationId)
                ?? throw new ArgumentNullException($"Vacation ID {vacationId} not found.");
            vacation.Approved = response;
            db.SaveChanges();
            return VacationTranslator.ToBL(vacation);
        }

        /// <summary>
        /// Retrieves vacations with optional filtering by soldier and/or status.
        /// </summary>
        /// <param name="soldierId">Optional: filter by soldier ID.</param>
        /// <param name="status">Optional: filter by vacation status (Pending/Approved/Denied).</param>
        /// <returns>List of matching vacation records.</returns>
        public List<Vacation> GetVacations(int? soldierId, VacationRequestStatus? status)
        {
            using var db = new ShabzakDB();
            var vacations = db.Vacations
                .AsQueryable();

            if(soldierId.HasValue)
            {
                vacations = vacations
                    .Where(vac => vac.SoldierId == soldierId);
            }
            if (status.HasValue)
            {
                vacations = vacations
                    .Where(vac => vac.Approved == status);
            }
            var ret = vacations
                .ToList()
                .Select(vac => VacationTranslator.ToBL(vac))
                .ToList();
            return ret;
        }

        /// <summary>
        /// Computes a soldier's assignment summary: total distinct missions, total hours worked,
        /// and a per-mission breakdown with count and hours.
        /// </summary>
        /// <param name="soldierId">The soldier's ID.</param>
        /// <returns>Summary containing totals and per-mission breakdown.</returns>
        public SoldierSummary GetSummary(int soldierId)
        {
            using var db = new ShabzakDB();
            var data = db.SoldierMission
                .Include(sm => sm.MissionInstance)
                .ThenInclude(mi => mi.Mission)
                .Where(sm => sm.SoldierId == soldierId)
                .ToList();
            var ret = new SoldierSummary
            {
                TotalMissions = data.Count,
                TotalHours = data.Sum(d => (d.MissionInstance.ToTime - d.MissionInstance.FromTime).TotalHours),
                MissionBreakdown = []
            };
            var countDic = new Dictionary<string, int>();
            foreach(var soldierMission in data)
            {
                var breakdown = new SoldierMissionBreakdown();
                if(!countDic.ContainsKey(soldierMission.MissionInstance.Mission.Name))
                {
                    countDic.Add(soldierMission.MissionInstance.Mission.Name, 0);
                }
                countDic[soldierMission.MissionInstance.Mission.Name]++;
            }
            var encryptor = new AESEncryptor();
            foreach (var missionCount in countDic)
            {
                ret.MissionBreakdown.Add(new SoldierMissionBreakdown
                {
                    MissionName = encryptor.Decrypt(missionCount.Key),
                    Count = missionCount.Value
                });
            }
            return ret;
        }
    }
}
