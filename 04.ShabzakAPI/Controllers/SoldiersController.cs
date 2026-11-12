using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Translators.Models;
using System.IO;
using BL;
using BL.Cache;
using BL.Services;
using BL.Extensions;
using Newtonsoft.Json;
using ShabzakAPI.Auth;
using ShabzakAPI.Controllers;
using ShabzakAPI.ViewModels;
using BL.Logging;
using BL.Models;
using BL.Models.Schedule;
using static System.Net.WebRequestMethods;

namespace _04.ShabzakAPI.Controllers
{
    /// <summary>
    /// Controller for soldier management: CRUD operations, vacation handling, cache management, and soldier summaries.
    /// Management endpoints are AdminOrAbove; GetSummary/GetVacations/RequestVacation are self-scoped for Regular users,
    /// and GetSoldiers redacts phone/personal number (and other soldiers' vacations) for Regular users.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class SoldiersController : ControllerBase
    {
        private readonly SoldiersCache _soldiersCache;
        private readonly SoldierService _soldierService;
        private readonly UsersCache _usersCache;
        public SoldiersController(SoldiersCache soldiersCache, SoldierService soldierService, UsersCache usersCache) 
        {
            _soldiersCache = soldiersCache;
            _soldierService = soldierService;
            _usersCache = usersCache;
            //soldierService.LoadSoldiersFromFile(@"/Shabzak/ShabzakAPI/DataLayer/soldiers.txt");
        }

        /// <summary>
        /// Development utility: loads soldiers from a tab-delimited file and missions from a JSON file into the database.
        /// WARNING: This is a dev/seed endpoint and should not be used in production.
        /// </summary>
        /// <returns>null (side-effect only).</returns>
        [HttpGet("LoadCSV")]
        [Authorize(Policy = Policies.AdminOrAbove)]
        public IActionResult LoadCSV([FromServices] IWebHostEnvironment env)
        {
            // SECURITY: Dev-only seed endpoint. Blocked in production.
            if (!env.IsDevelopment())
            {
                return NotFound();
            }
            var soldiersPath = Path.Combine(env.ContentRootPath, "..", "DataLayer", "soldiers.txt");
            _soldierService.LoadSoldiersFromFile(soldiersPath);
            //var path = @"C:\Parse\1.txt";
            //var solDic = System.IO.File.ReadAllLines(path)
            //    .Select(s =>
            //    {
            //        var spl = s.Split(new[] { ',' });
            //        if(spl.Length != 4)
            //        {
            //            return null;
            //        }
            //        return new {
            //            Phone = spl[2],
            //            FirstName = spl[0],
            //            LastName = spl[1],
            //            Pakal = spl[3]
            //        };
            //    })
            //    .Where(r => r != null && !string.IsNullOrEmpty(r.Phone))
            //    .GroupBy(x => x.Phone)
            //    .ToDictionary(r => r.Key, r=> r.First());

            //var csvPath = @"C:\Parse\9213.csv";
            //var csvLoader = new CSVLoader();
            //var soldiers = csvLoader.ParseSoldiers(csvPath.Replace(".csv", " 2.csv"));
            //var newLines = new List<string>();
            //foreach (var soldier in soldiers)
            //{
            //    if(string.IsNullOrEmpty(soldier.Phone))
            //    {
            //        continue;
            //    }
            //    var phone = soldier.Phone.Replace("-", string.Empty);
            //    if(!solDic.ContainsKey(phone))
            //    {
            //        continue;
            //    }
            //    var sol = solDic[phone];
            //    var newLine = $"{soldier.PersonalNumber},{sol.FirstName},{sol.LastName},{soldier.Phone},{soldier.Platoon},{sol.Pakal}";
            //    newLines.Add(newLine);
            //}
            //System.IO.File.WriteAllLines(csvPath.Replace(".csv", " 2.csv"), newLines);

            //var csvLoader = new CSVLoader();
            //var csvPath = @"C:\Parse\9213 2.csv";
            //var soldiers = csvLoader.ParseSoldiersToDB(csvPath);
            //return null;

            using var db = DataLayer.DbFactory.Create();
           // using var remote = new DataLayer.RemoteDB();

            var missionsPath = Path.Combine(env.ContentRootPath, "..", "DataLayer", "missions-detailed.json");
            var missionsJson = System.IO.File.ReadAllText(missionsPath);
            var missions = JsonConvert.DeserializeObject<List<DataLayer.Models.Mission>>(missionsJson);
            foreach (var mission in missions)
            {
                mission.Id = 0;

                foreach (var instance in mission.MissionInstances)
                {
                    instance.MissionId = mission.Id;
                    instance.Mission = mission;
                }
                foreach (var pos in mission.MissionPositions)
                {
                    pos.MissionId = mission.Id;
                    pos.Mission = mission;
                }
            }

            //var soldiers = db.Soldiers
            //    .ToList();

            //foreach (var soldier in soldiers)
            //{
            //    soldier.Id = 0;
            //}

            db.Missions.AddRange(missions);
            db.SaveChanges();

            return Ok("Seed data loaded successfully.");
        }

        /// <summary>
        /// Forces an immediate reload of the soldiers cache from the database.
        /// </summary>
        /// <returns>Confirmation message.</returns>
        [HttpGet("ReloadCache")]
        [Authorize(Policy = Policies.AdminOrAbove)]
        public string ReloadCache()
        {
            SoldiersCache.ReloadCache();
            return "Soldiers cache reloaded.";
        }

        /// <summary>
        /// Retrieves all soldiers from the cache. Optionally forces a cache reload before returning.
        /// Soldier PII fields are decrypted before being returned.
        /// For non-admin callers, copies are returned with phone and personal number blanked and
        /// vacations kept only on the caller's own soldier (cache objects are never mutated).
        /// </summary>
        /// <param name="reloadCache">If true, reloads the cache from DB before returning.</param>
        /// <returns>List of all soldiers with their positions, missions, and vacations.</returns>
        [HttpGet("GetSoldiers")]
        public List<Soldier> GetSoldiers(bool reloadCache = false)
        {
            var soldiers = _soldiersCache.GetSoldiers(reloadCache);
            if (User.IsAdminOrAbove())
            {
                return soldiers;
            }
            var ownSoldierId = User.GetSoldierId();
            return soldiers
                .Select(s => new Soldier
                {
                    Id = s.Id,
                    Name = s.Name,
                    PersonalNumber = string.Empty,
                    Phone = string.Empty,
                    Platoon = s.Platoon,
                    Company = s.Company,
                    Active = s.Active,
                    Positions = s.Positions,
                    Missions = s.Missions,
                    Vacations = s.Id == ownSoldierId ? s.Vacations : []
                })
                .ToList();
        }

        /// <summary>
        /// Compact soldier list without contact details: id, name, platoon, company, positions (enum numbers), active.
        /// Available to any authenticated user.
        /// </summary>
        /// <param name="includeInactive">If true, inactive soldiers are included.</param>
        [HttpGet("GetSoldiersLite")]
        public List<SoldierLiteDto> GetSoldiersLite(bool includeInactive = false)
        {
            return _soldiersCache.GetSoldiers()
                .Where(s => includeInactive || s.Active)
                .OrderBy(s => s.Id)
                .Select(SoldierLiteDto.From)
                .ToList();
        }

        /// <summary>
        /// One soldier including phone and personal number, plus whether a login account exists.
        /// Allowed for the soldier themself or Admin+ (403 otherwise); access is logged with IDs only.
        /// </summary>
        /// <param name="soldierId">The soldier's ID.</param>
        [HttpGet("GetSoldierById")]
        public ActionResult<SoldierDetailsDto> GetSoldierById(int soldierId)
        {
            if (!IsSelfOrAdmin(soldierId))
            {
                return Forbid();
            }
            var soldier = _soldiersCache.GetSoldierById(soldierId);
            if (soldier == null)
            {
                return NotFound(new { message = $"Soldier {soldierId} not found." });
            }
            Logger.Log($"Soldier details ID={soldierId} read by user ID={User.GetUserId()}");
            var hasAccount = _usersCache.GetAllUsers().Any(u => u.SoldierId == soldierId);
            return SoldierDetailsDto.From(soldier, hasAccount);
        }

        /// <summary>
        /// Updates a soldier's details (name, phone, position, platoon, company).
        /// PII fields are encrypted before saving to the database.
        /// </summary>
        /// <param name="soldier">The updated soldier data.</param>
        /// <returns>The updated soldier with decrypted fields.</returns>
        [HttpPost("UpdateSoldier")]
        [Authorize(Policy = Policies.AdminOrAbove)]
        public Soldier UpdateSoldier(Soldier soldier)
        {
            var ret = _soldierService.Update(soldier);
            return ret;
        }

        /// <summary>
        /// Creates a new soldier. Automatically normalizes positions (adds 'Simple' position
        /// for non-commanding soldiers if missing). PII is encrypted before storage.
        /// </summary>
        /// <param name="soldier">The soldier to create.</param>
        /// <returns>The created soldier with generated ID and decrypted fields.</returns>
        [HttpPost("AddSoldier")]
        [Authorize(Policy = Policies.AdminOrAbove)]
        public Soldier AddSoldier(Soldier soldier)
        {
            var ret = _soldierService.AddSoldier(soldier);
            return ret;
        }

        /// <summary>
        /// Activates or deactivates a soldier (UpdateSoldier ignores the Active flag). Inactive soldiers are hidden from
        /// GetSoldiersLite by default and fail assignment validation (INACTIVE). Value: the soldier without contact details.
        /// </summary>
        [HttpPost("SetSoldierActive")]
        [Authorize(Policy = Policies.AdminOrAbove)]
        public ActionResult<GeneralResponse<SoldierLiteDto>> SetSoldierActive(SetSoldierActiveRequest request) =>
            this.ExecuteWrite(
                "Soldiers/SetSoldierActive",
                $"soldier={request.SoldierId} active={request.Active}",
                () => _soldierService.SetActive(request.SoldierId, request.Active));

        /// <summary>
        /// Deletes a soldier and all their associated assignments (cascade delete).
        /// </summary>
        /// <param name="soldierId">The ID of the soldier to delete.</param>
        /// <returns>The deleted soldier's ID.</returns>
        [HttpPost("DeleteSoldier")]
        [Authorize(Policy = Policies.AdminOrAbove)]
        public int DeleteSoldier(int soldierId)
        {
            _soldierService.DeleteSoldier(soldierId);
            return soldierId;
        }

        /// <summary>
        /// Creates a vacation request for a soldier with Pending status.
        /// Pending/approved vacations block the soldier from auto-assignment during that period.
        /// Regular users may only request vacations for their own soldier (403 otherwise).
        /// </summary>
        /// <param name="request">Soldier ID, from date, and to date.</param>
        /// <returns>The created vacation request.</returns>
        [HttpPost("RequestVacation")]
        public ActionResult<Vacation> RequestVacation(RequestVacationModel request)
        {
            if (!IsSelfOrAdmin(request.SoldierId))
            {
                return Forbid();
            }
            var ret = _soldierService.RequestVacation(request.SoldierId, request.From, request.To);
            return ret;
        }

        /// <summary>
        /// Approves or denies a pending vacation request.
        /// Denied vacations are ignored by the auto-assignment algorithm.
        /// </summary>
        /// <param name="response">Vacation ID and the approval/denial response.</param>
        /// <returns>The updated vacation record.</returns>
        [HttpPost("RespondToVacationRequest")]
        [Authorize(Policy = Policies.AdminOrAbove)]
        public Vacation RespondToVacationRequest(RespondToVacationRequestModel response)
        {
            var ret = _soldierService.RespondToVacationRequest(response.VacationId, response.Response);
            return ret;
        }

        /// <summary>
        /// Retrieves vacations with optional filtering by soldier ID and/or status.
        /// For Regular users the soldier filter is forced to their own soldier (empty list if none is linked).
        /// </summary>
        /// <param name="filter">Optional soldier ID and vacation status filters.</param>
        /// <returns>Filtered list of vacation records.</returns>
        [HttpPost("GetVacations")]
        public List<Vacation> GetVacations(GetVacationsFilterModel filter)
        {
            var soldierId = filter.SoldierId;
            if (!User.IsAdminOrAbove())
            {
                soldierId = User.GetSoldierId();
                if (soldierId == null)
                {
                    return [];
                }
            }
            var ret = _soldierService.GetVacations(soldierId, filter.Status);
            return ret;
        }

        /// <summary>
        /// Retrieves a soldier's assignment summary: total missions, total hours,
        /// and a breakdown of assignments per mission name.
        /// Regular users may only read their own summary (403 otherwise).
        /// </summary>
        /// <param name="soldierId">The soldier's ID.</param>
        /// <returns>Summary with totals and per-mission breakdown.</returns>
        [HttpPost("GetSummary")]
        public ActionResult<SoldierSummary> GetSummary(int soldierId)
        {
            if (!IsSelfOrAdmin(soldierId))
            {
                return Forbid();
            }
            var ret = _soldierService.GetSummary(soldierId);
            return ret;
        }

        private bool IsSelfOrAdmin(int soldierId) =>
            User.IsAdminOrAbove() || User.GetSoldierId() == soldierId;
    }
}
