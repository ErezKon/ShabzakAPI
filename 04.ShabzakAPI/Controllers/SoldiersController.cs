﻿using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Translators.Models;
using System.IO;
using BL;
using BL.Cache;
using BL.Services;
using BL.Extensions;

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
            var encryptor = new AESEncryptor();
            var soldiers = db.Soldiers.ToList();
            foreach(var sol in soldiers)
            {
                if(string.IsNullOrEmpty(sol.Platoon))
                {
                    sol.Platoon = encryptor.Encrypt("N/A");
                }
            }
            db.SaveChanges();
            return null;
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
    }
}