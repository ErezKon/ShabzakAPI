using BL.Cache;
using BL.Extensions;
using BL.Logging;
using DataLayer;
using Newtonsoft.Json;
using Translators.Models;
using Translators.Translators;

using VacationRequestStatus = DataLayer.Models.VacationRequestStatus;

namespace BL.Services
{
    public class SoldierService
    {
        private readonly PositionHelper positionHelper = new();
        public SoldierService()
        {
        }
        public Soldier AddSoldier(Soldier soldier)
        {
            return AddSoldier(soldier.ToDB());
        }

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

        private void NormalizePositions(DataLayer.Models.Soldier soldier)
        {
            var positions = soldier.GetSoldierPositions();
            var positionAdded = false;
            var commandingPositions = positionHelper.CommandingPositions;
            if(commandingPositions.Any(cp => positions.Contains(cp)))
            {
                return;
            }
            if (!positions.Any(p => p == DataLayer.Models.Position.Simple))
            {
                foreach (var position in positions)
                {
                    if (positionHelper.SimplePositions.Contains(position))
                    {
                        positions.Add(DataLayer.Models.Position.Simple);
                        positionAdded = true;
                        break;
                    }
                }
                if (positionAdded)
                {
                    soldier.Position = string.Join(",", positions
                        .Order()
                        .Select(p => ((int)p).ToString())
                        .ToArray());
                }
            }
        }
        public Soldier Update(Soldier soldier) => Update(soldier.ToDB());
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
                Logger.Log($"Error while adding soldier:\n{ex}");
                throw;
            }
        }

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
            using var db = new ShabzakDB();
            db.Vacations.Add(vacation);
            db.SaveChanges();
            return VacationTranslator.ToBL(vacation);
        }

        public Vacation RespondToVacationRequest(int vacationId, VacationRequestStatus response)
        {
            using var db = new ShabzakDB();
            var vacation = db.Vacations.FirstOrDefault(vac => vac.Id == vacationId)
                ?? throw new ArgumentNullException($"Vacation ID {vacationId} not found.");
            vacation.Approved = response;
            db.SaveChanges();
            return VacationTranslator.ToBL(vacation);
        }

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
    }
}
