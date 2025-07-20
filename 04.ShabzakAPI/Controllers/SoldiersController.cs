using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Translators.Models;
using System.IO;
using BL;
using BL.Cache;
using BL.Services;
using BL.Extensions;
using Newtonsoft.Json;
using ShabzakAPI.ViewModels;
using BL.Models;
using static System.Net.WebRequestMethods;

namespace _04.ShabzakAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SoldiersController : ControllerBase
    {
        private readonly SoldiersCache _soldiersCache;
        private readonly SoldierService _soldierService;
        public SoldiersController(SoldiersCache soldiersCache, SoldierService soldierService) 
        {
            _soldiersCache = soldiersCache;
            _soldierService = soldierService;
            //soldierService.LoadSoldiersFromFile(@"C:\Users\Erez_Konforti\OneDrive - Dell Technologies\Documents\Shabzak Assets\soldiers.txt");
        }

        [HttpGet("LoadCSV")]
        public List<Soldier> LoadCSV()
        {
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

            using var db = new DataLayer.ShabzakDB();
           // using var remote = new DataLayer.RemoteDB();

            var missionsJson = System.IO.File.ReadAllText(@"C:\Users\Erez_Konforti\OneDrive - Dell Technologies\Documents\Shabzak data\missions-detailed.json");
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

            return null;
        }

        [HttpGet("ReloadCache")]
        public string ReloadCache()
        {
            SoldiersCache.ReloadCache();
            return "Soldiers cache reloaded.";
        }

        [HttpGet("GetSoldiers")]
        public List<Soldier> GetSoldiers()
        {
            var soldiers = _soldiersCache.GetSoldiers();
            return soldiers;
        }

        [HttpPost("UpdateSoldier")]
        public Soldier UpdateSoldier(Soldier soldier)
        {
            var ret = _soldierService.Update(soldier);
            return ret;
        }

        [HttpPost("AddSoldier")]
        public Soldier AddSoldier(Soldier soldier)
        {
            var ret = _soldierService.AddSoldier(soldier);
            return ret;
        }

        [HttpPost("DeleteSoldier")]
        public int DeleteSoldier(int soldierId)
        {
            _soldierService.DeleteSoldier(soldierId);
            return soldierId;
        }

        [HttpPost("RequestVacation")]
        public Vacation RequestVacation(RequestVacationModel request)
        {
            var ret = _soldierService.RequestVacation(request.SoldierId, request.From, request.To);
            return ret;
        }

        [HttpPost("RespondToVacationRequest")]
        public Vacation RespondToVacationRequest(RespondToVacationRequestModel response)
        {
            var ret = _soldierService.RespondToVacationRequest(response.VacationId, response.Response);
            return ret;
        }

        [HttpPost("GetVacations")]
        public List<Vacation> GetVacations(GetVacationsFilterModel filter)
        {
            var ret = _soldierService.GetVacations(filter.SoldierId, filter.Status);
            return ret;
        }

        [HttpPost("GetSummary")]
        public SoldierSummary GetSummary(int soldierId)
        {
            var ret = _soldierService.GetSummary(soldierId);
            return ret;
        }
    }
}
