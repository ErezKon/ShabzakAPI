using BL.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;

namespace BL
{
    internal enum CSVIndex
    {
        PersonalNumber = 0,
        FirstName = 1,
        LastName = 2,
        Phone = 3,
        Platoon = 4,
        Position = 5
    }
    public class CSVLoader
    {
        private readonly static string na = "N/A";

        /// <summary>
        /// Parse The CSV file and retrieves Soldier BL model list.
        /// </summary>
        /// <param name="path">The CSV file path.</param>
        /// <param name="delimiter">The delimiter of the CSV file. (Default: ',')</param>
        /// <param name="skipFirstRow">Skip headers row. (Default: true)</param>
        /// <returns></returns>
        public List<Soldier> ParseSoldiers(string path, char delimiter = ',', bool skipFirstRow = true)
        {
            if(string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentNullException("path cannot be null");
            }

            if(!File.Exists(path))
            {
                throw new FileNotFoundException($"{path} cannot be found");
            }

            var lines = File.ReadAllLines(path)
                .ToList();

            if(skipFirstRow)
            {
                lines = lines
                    .Skip(1)
                    .ToList();
            }

            var ret = new List<Soldier>();

            foreach(var line in lines)
            {
                var spl = line.Split(delimiter);
                try
                {
                    var soldier = new Soldier
                    {
                        PersonalNumber = spl[(int)CSVIndex.PersonalNumber] ?? na,
                        Name = $"{spl[(int)CSVIndex.FirstName]} {spl[(int)CSVIndex.LastName]}" ?? na,
                        Phone = spl[(int)CSVIndex.Phone] ?? na,
                        Platoon = spl[(int)CSVIndex.Platoon] ?? na,
                        Positions = ParsePosition(spl[(int)CSVIndex.Position])
                    };
                    ret.Add(soldier);
                }
                catch(Exception ex)
                {
                    continue; 
                }
            }
            return ret;
        }


        public List<DataLayer.Models.Soldier> ParseSoldiersToDB(string path, char delimiter = ',', bool skipFirstRow = true)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentNullException("path cannot be null");
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"{path} cannot be found");
            }

            var lines = File.ReadAllLines(path)
                .ToList();

            if (skipFirstRow)
            {
                lines = lines
                    .Skip(1)
                    .ToList();
            }

            var ret = new List<DataLayer.Models.Soldier>();

            foreach (var line in lines)
            {
                var spl = line.Split(delimiter);
                try
                {
                    var soldier = new DataLayer.Models.Soldier
                    {
                        PersonalNumber = spl[(int)CSVIndex.PersonalNumber] ?? na,
                        Name = $"{spl[(int)CSVIndex.FirstName]} {spl[(int)CSVIndex.LastName]}" ?? na,
                        Phone = spl[(int)CSVIndex.Phone] ?? na,
                        Platoon = spl[(int)CSVIndex.Platoon] ?? na,
                        Position = string.Join(',', ParsePosition(spl[(int)CSVIndex.Position])
                            .Select(p => p.ToString())
                            .ToArray())
                    };
                    ret.Add(soldier);
                }
                catch (Exception ex)
                {
                    continue;
                }
            }

            using (var db = new DataLayer.ShabzakDB())
            {

                foreach (var soldier in ret)
                {
                    if(soldier.PersonalNumber != na)
                    {
                        var exists = db.Soldiers
                            .Count(s => s.PersonalNumber.Equals(soldier.PersonalNumber)) > 0;
                        if(exists)
                        {
                            continue;
                        }
                    }
                    soldier.Company = "C";
                    db.Soldiers.Add(soldier.Encrypt());
                }
                db.SaveChanges();
            }
            return ret;
        }

        private List<Position> ParsePosition(string position)
        {
            var ret = new List<Position>();

            var spl = position.Split('|');
            foreach(var pos in spl) 
            {
                var parsed = pos.Replace("\"", string.Empty).Trim();
                switch(parsed)
                {
                    case "חפש":
                        ret.Add(Position.Simple);
                        break;
                    case "קלע":
                        ret.Add(Position.Marksman);
                        break;
                    case "מטול":
                        ret.Add(Position.GrenadeLauncher);
                        break;
                    case "חובש":
                        ret.Add(Position.Medic);
                        break;
                    case "נגב":
                        ret.Add(Position.Negev);
                        break;
                    case "חמל":
                        ret.Add(Position.Hamal);
                        break;
                    case "צלף":
                        ret.Add(Position.Sniper);
                        break;
                    case "רחפן":
                    case "רחפניסט":
                    case "מפעיל רחפן":
                        ret.Add(Position.DroneOperator);
                        break;
                    case "מתורגמן":
                        ret.Add(Position.Translator);
                        break;
                    case "מדריך ירי":
                    case "מדריך קליעה":
                        ret.Add(Position.ShootingInstructor);
                        break;
                    case "מדריך קמג":
                    case "מדריך קרב מגע":
                        ret.Add(Position.KravMagaInstructor);
                        break;
                    case "קשר ממ":
                        ret.Add(Position.PlatoonCommanderComms);
                        break;
                    case "קשר מפ":
                        ret.Add(Position.CompanyCommanderComms);
                        break;
                    case "מכ":
                        ret.Add(Position.ClassCommander);
                        break;
                    case "סמל":
                        ret.Add(Position.Sergant);
                        break;
                    case "ממ":
                        ret.Add(Position.PlatoonCommander);
                        break;
                    case "סמפ":
                        ret.Add(Position.CompanyDeputy);
                        break;
                    case "מפ":
                        ret.Add(Position.CompanyCommander);
                        break;
                }
            }

            return ret;
        }
    }
}
