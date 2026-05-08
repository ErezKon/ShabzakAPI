using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DataLayer.Models;

namespace BL.Extensions
{
    /// <summary>
    /// Extension methods for Position enums to determine commanding/officer rank classification.
    /// Supports both DB-layer and BL-layer Position enums.
    /// </summary>
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

        /// <summary>
        /// Checks if a DB Position is a commanding rank (ClassCommander through CompanyCommander).
        /// </summary>
        public static bool IsCommandingPosition(this Position position)
        {
            return commandingPositionsBL.Contains(position);
        }
        /// <summary>
        /// Checks if a DB Position is an officer rank (PlatoonCommander through CompanyCommander).
        /// </summary>
        public static bool IsOfficerPosition(this Position position)
        {
            return officerPositionsBL.Contains(position);
        }

        /// <summary>
        /// Checks if a BL Position is a commanding rank.
        /// </summary>
        public static bool IsCommandingPosition(this Translators.Models.Position position)
        {
            return commandingPositions.Contains(position);
        }
        /// <summary>
        /// Checks if a BL Position is an officer rank.
        /// </summary>
        public static bool IsOfficerPosition(this Translators.Models.Position position)
        {
            return officerPositions.Contains(position);
        }
    }
}
