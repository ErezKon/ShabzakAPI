using BL.Cache;
using BL.Extensions;
using BL.Logging;
using BL.Models;
using BL.Models.Schedule;
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
            // SECURITY: Log only the soldier ID, not the full JSON — the original logged
            // PII (name, phone, personal number) to the log file.
            Logger.Log($"Adding Soldier (ID will be assigned after save)");
            try
            {
                NormalizePositions(soldier);
                using var db = DataLayer.DbFactory.Create();
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
            // SECURITY: Log only the soldier ID, not the full JSON — prevents PII leakage.
            Logger.Log($"Updating Soldier ID={soldier.Id}");
            try
            {
                using var db = DataLayer.DbFactory.Create();
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
        /// Sets a soldier's Active flag. This is the only way to change it (<see cref="Update(DataLayer.Models.Soldier)"/> ignores
        /// Active, because the UI soldier model has no such field). The soldier cache is reloaded synchronously so that
        /// assignment validation sees the new flag immediately.
        /// </summary>
        /// <param name="soldierId">The soldier's ID.</param>
        /// <param name="active">The new Active flag.</param>
        /// <returns>The soldier without contact details.</returns>
        /// <exception cref="KeyNotFoundException">Unknown soldier.</exception>
        public SoldierLiteDto SetActive(int soldierId, bool active)
        {
            using var db = DbFactory.Create();
            var soldier = db.Soldiers.FirstOrDefault(s => s.Id == soldierId)
                ?? throw new KeyNotFoundException($"Soldier {soldierId} not found.");
            if (soldier.Active != active)
            {
                soldier.Active = active;
                db.SaveChanges();
            }
            SoldiersCache.ReloadCache();
            return SoldierLiteDto.From(SoldiersCache.GetInstance().GetSoldierById(soldierId));
        }

        /// <summary>
        /// Deletes a soldier and all associated data (assignments, vacations) via cascade delete, together with the
        /// soldier's login account(s): <c>Users.SoldierId</c> has no FK, so they are deleted here, in the same SaveChanges.
        /// The accounts are deleted under the <see cref="UserAccountGuard"/> rules for the caller; when the caller may not
        /// delete one of them (a SuperAdmin account for an Admin, the caller's own account, the last enabled SuperAdmin),
        /// nothing is deleted. Removes the accounts from the users cache and reloads the soldiers cache afterwards.
        /// </summary>
        /// <param name="id">The ID of the soldier to delete.</param>
        /// <param name="callerId">The user ID of the caller (for the account guard).</param>
        /// <param name="callerRole">The role of the caller.</param>
        /// <returns>The IDs of the deleted login accounts (empty when the soldier had none).</returns>
        /// <exception cref="ArgumentException">Unknown soldier.</exception>
        /// <exception cref="UserAccountException">The caller may not delete the soldier's login account.</exception>
        public List<int> DeleteSoldier(int id, int callerId, UserRole callerRole)
        {
            try
            {
                using var db = DataLayer.DbFactory.Create();
                var soldier = db.Soldiers.FirstOrDefault(s => s.Id == id);
                if(soldier == null)
                {
                    throw new ArgumentException("Can't find soldier.");
                }
                var accounts = db.Users.Where(u => u.SoldierId == id).ToList();
                if (accounts.Count > 0)
                {
                    var enabledSuperAdmins = db.Users.Count(u => u.Role == DataLayer.Models.UserRole.SuperAdmin && u.Enabled);
                    var guard = UserAccountGuard.CheckDeleteAll(callerId, callerRole,
                        accounts.Select(a => (a.Id, (UserRole)(int)a.Role, a.Enabled)), enabledSuperAdmins);
                    if (!guard.IsAllowed)
                    {
                        throw new UserAccountException(guard.Outcome, $"The soldier's login account cannot be deleted: {guard.Message}");
                    }
                    db.Users.RemoveRange(accounts);
                }
                db.Soldiers.Remove(soldier);
                // FIX: SaveChanges BEFORE ReloadAsync. The original reloaded the cache
                // before committing the delete — the soldier was still in DB during reload.
                db.SaveChanges();
                var usersCache = UsersCache.GetInstance();
                foreach (var account in accounts)
                {
                    usersCache.RemoveUser(account.Id);
                }
                SoldiersCache.ReloadAsync();
                var deletedAccounts = accounts.Select(a => a.Id).ToList();
                Logger.Log($"Soldier ID={id} deleted by user ID={callerId}" +
                    (deletedAccounts.Count > 0 ? $", with login account ID(s) {string.Join(", ", deletedAccounts)}" : ""));
                return deletedAccounts;
            }
            catch (UserAccountException ex)
            {
                Logger.Log($"Deleting soldier ID={id} refused for user ID={callerId}: {ex.Message}");
                throw;
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
            using var db = DataLayer.DbFactory.Create();
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
            using var db = DbFactory.Create();
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
            using var db = DbFactory.Create();
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
            using var db = DbFactory.Create();
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
            using var db = DbFactory.Create();
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
        /// <summary>
        /// Ensures non-commanding soldiers have the 'Simple' position.
        /// If the soldier holds no commanding position and 'Simple' is missing, it is added.
        /// </summary>
        private void NormalizePositions(DataLayer.Models.Soldier soldier)
        {
            var positions = soldier.GetSoldierPositions();
            bool isCommander = positions.Any(p => positionHelper.CommandingPositions.Contains(p));
            if (!isCommander && !positions.Contains(DataLayer.Models.Position.Simple))
            {
                positions.Add(DataLayer.Models.Position.Simple);
                soldier.Position = string.Join(",", positions.Select(p => ((int)p).ToString()));
            }
        }
    }
}
