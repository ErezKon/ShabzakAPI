using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLayer.Models
{
    /// <summary>
    /// Enum of military positions/roles a soldier can hold.
    /// Ranges from basic (Simple) through specialized roles (Medic, Sniper, DroneOperator)
    /// to command positions (PlatoonCommander, CompanyCommander).
    /// </summary>
    public enum Position
    {
        Simple,
        Marksman,
        GrenadeLauncher,
        Medic,
        Negev,
        Hamal,
        Sniper,
        Translator,
        ShootingInstructor,
        KravMagaInstructor,
        DroneOperator,
        PlatoonCommanderComms,
        CompanyCommanderComms,
        ClassCommander,
        Sergant,
        PlatoonCommander,
        CompanyDeputy,
        CompanyCommander
    }
}
