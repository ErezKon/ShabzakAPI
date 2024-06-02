﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;

namespace Translators.Translators
{
    public abstract class SoldierTranslator
    {
        public static Soldier ToBL(DataLayer.Models.Soldier sol)
        {
            if(sol == null)
            {
                return null;
            }
            return new Soldier()
            {
                Id = sol.Id,
                Name = sol.Name,
                PersonalNumber = sol.PersonalNumber,
                Phone = sol.Phone,
                Platoon = sol.Platoon,
                Company = sol.Company,
                Active = sol.Active,
                Positions = sol.Position.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s =>
                    {
                        if(int.TryParse(s, out int numericValue))
                        {
                            return (Position)numericValue;
                        }
                        return Position.Simple;
                    })
                    .ToList(),
                Vacations = sol.Vacations
                ?.Select(v => VacationTranslator.ToBL(v))
                ?.ToList()
                ?? [],
                Missions = sol.Missions
                    ?.Select(m => SoldierMissionTranslator.ToBL(m, false))
                    ?.ToList() ?? []
            };
        }


        public static DataLayer.Models.Soldier ToDB(Soldier sol)
        {
            return new DataLayer.Models.Soldier()
            {
                Id = sol.Id,
                Name = sol.Name,
                PersonalNumber = sol.PersonalNumber,
                Phone = sol.Phone,
                Platoon = sol.Platoon,
                Company = sol.Company,
                Active = sol.Active,
                Position = string.Join(",", sol.Positions.Select(p => ((int)p).ToString()).ToArray()),
                Vacations = sol.Vacations
                ?.Select(v => VacationTranslator.ToDB(v))
                ?.ToList()
                ?? []
            };
        }
    }
}
