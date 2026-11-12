using Translators.Models;

namespace BL.Models.Schedule
{
    /// <summary>
    /// Soldier without contact details (GET /api/Soldiers/GetSoldiersLite). Positions are enum numbers.
    /// </summary>
    public class SoldierLiteDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Platoon { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
        public List<int> Positions { get; set; } = [];
        public bool Active { get; set; }

        public static SoldierLiteDto From(Soldier soldier) => new()
        {
            Id = soldier.Id,
            Name = soldier.Name,
            Platoon = soldier.Platoon,
            Company = soldier.Company,
            Positions = soldier.Positions?.Select(p => (int)p).ToList() ?? [],
            Active = soldier.Active
        };
    }

    /// <summary>
    /// Single soldier including contact details (GET /api/Soldiers/GetSoldierById; self or Admin+ only).
    /// </summary>
    public class SoldierDetailsDto : SoldierLiteDto
    {
        public string Phone { get; set; } = string.Empty;
        public string PersonalNumber { get; set; } = string.Empty;
        public bool HasUserAccount { get; set; }

        public static SoldierDetailsDto From(Soldier soldier, bool hasUserAccount) => new()
        {
            Id = soldier.Id,
            Name = soldier.Name,
            Platoon = soldier.Platoon,
            Company = soldier.Company,
            Positions = soldier.Positions?.Select(p => (int)p).ToList() ?? [],
            Active = soldier.Active,
            Phone = soldier.Phone,
            PersonalNumber = soldier.PersonalNumber,
            HasUserAccount = hasUserAccount
        };
    }
}
