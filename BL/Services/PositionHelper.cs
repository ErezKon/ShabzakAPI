using DataLayer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BL.Services
{
    public class PositionHelper
    {
        private Dictionary<Position, List<Position>> posDic;
        private static readonly IEnumerable<Position> simplePositions = 
        [
            Position.Simple,
            Position.ShootingInstructor,
            Position.GrenadeLauncher,
            Position.Translator,
            Position.Marksman,
            Position.DroneOperator,
            Position.KravMagaInstructor,
            Position.Medic,
            Position.Negev,
            Position.Sniper
        ];
        private static readonly IEnumerable<Position> commandingPositions = 
        [
            Position.ClassCommander,
            Position.Sergant,
            Position.PlatoonCommander,
            Position.CompanyDeputy,
            Position.CompanyCommander
        ];
        private static readonly IEnumerable<Position> officerPositions =
        [
            Position.PlatoonCommander,
            Position.CompanyDeputy,
            Position.CompanyCommander
        ];

        public PositionHelper()
        {
            posDic = new()
            {
                {
                    Position.Simple,
                    new List<Position> {
                        Position.Simple,
                        Position.ShootingInstructor,
                        Position.GrenadeLauncher,
                        Position.Translator,
                        Position.Marksman,
                        Position.DroneOperator,
                        Position.KravMagaInstructor,
                        Position.Medic,
                        Position.Negev,
                        Position.Sniper
                    }
                },
                {
                    Position.Marksman,
                    new List<Position>
                    {
                        Position.Marksman,
                        Position.Sniper
                    }
                },
                {
                    Position.ClassCommander,
                    new List<Position>
                    {
                        Position.ClassCommander,
                        Position.Sergant,
                        Position.PlatoonCommander
                    }
                },
                {
                    Position.Sergant,
                    new List<Position>
                    {
                        Position.Sergant,
                        Position.PlatoonCommander
                    }
                },
                {
                    Position.CompanyDeputy,
                    new List<Position>
                    {
                        Position.CompanyDeputy,
                        Position.CompanyCommander
                    }
                }
            };
        }

        public List<Position> GetSimilarPositions(Position position)
        {
            if(posDic.TryGetValue(position, out var ret))
            {
                return ret;
            }

            return [position];
        }

        public List<Position> SimplePositions { 
            get
            {
                return simplePositions.ToList();
            }
        }

        public List<Position> CommandingPositions
        {
            get
            {
                return commandingPositions.ToList();
            }
        }

        public List<Position> OfficerPositions
        {
            get
            {
                return officerPositions.ToList();
            }
        }

    }
}
