using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DataLayer.Models;

namespace BL.Extensions
{
    public static class PositionExtension
    {
        private static readonly HashSet<Position> commandingPositionsBL =
        [
            Position.ClassCommander,
            Position.Sergant,
            Position.PlatoonCommander,
            Position.CompanyDeputy,
            Position.CompanyCommander
        ];
        private static readonly HashSet<Position> officerPositionsBL =
        [
            Position.PlatoonCommander,
            Position.CompanyDeputy,
            Position.CompanyCommander
        ];
        private static readonly HashSet<Translators.Models.Position> commandingPositions =
        [
            Translators.Models.Position.ClassCommander,
            Translators.Models.Position.Sergant,
            Translators.Models.Position.PlatoonCommander,
            Translators.Models.Position.CompanyDeputy,
            Translators.Models.Position.CompanyCommander
        ];
        private static readonly HashSet<Translators.Models.Position> officerPositions =
        [
            Translators.Models.Position.PlatoonCommander,
            Translators.Models.Position.CompanyDeputy,
            Translators.Models.Position.CompanyCommander
        ];

        public static bool IsCommandingPosition(this Position position)
        {
            return commandingPositionsBL.Contains(position);
        }
        public static bool IsOfficerPosition(this Position position)
        {
            return officerPositionsBL.Contains(position);
        }

        public static bool IsCommandingPosition(this Translators.Models.Position position)
        {
            return commandingPositions.Contains(position);
        }
        public static bool IsOfficerPosition(this Translators.Models.Position position)
        {
            return officerPositions.Contains(position);
        }
    }
}
