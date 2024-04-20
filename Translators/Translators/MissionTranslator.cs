﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;

namespace Translators.Translators
{
    public abstract class MissionTranslator
    {
        public static Mission ToBL(DataLayer.Models.Mission mis)
        {
            return new Mission()
            {
                Id = mis.Id,
                Name = mis.Name,
                Description = mis.Description,
                CommandersRequired = mis.CommandersRequired,
                SoldiersRequired = mis.SoldiersRequired,
                Duration = mis.Duration,
                FromTime = mis.FromTime,
                ToTime = mis.ToTime,
                IsSpecial = mis.IsSpecial,
                Positions = mis.MissionPositions
                    .Select(mp => MissionPositionTranslator.ToBL(mp))
                    .ToList()
                    ?? []
            };
        }


        public static DataLayer.Models.Mission ToDB(Mission mis)
        {
            return new DataLayer.Models.Mission()
            {
                Id = mis.Id,
                Name = mis.Name,
                Description = mis.Description,
                CommandersRequired = mis.CommandersRequired,
                SoldiersRequired = mis.SoldiersRequired,
                Duration = mis.Duration,
                FromTime = mis.FromTime,
                ToTime = mis.ToTime,
                IsSpecial = mis.IsSpecial,
                MissionPositions = mis.Positions
                    .Select(mp => MissionPositionTranslator.ToDB(mp))
                    .ToList()
                    ?? []
            };
        }
    }
}
