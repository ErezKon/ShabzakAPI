using Translators.Models;

namespace BL.Services
{
    /// <summary>
    /// Pure removal of soldiers' contact details from responses for non-admins. The phone and the personal number are also the
    /// default login credentials (username = personal number, password = phone), so they must never reach another soldier.
    /// </summary>
    public static class ContactDetailsRedaction
    {
        /// <summary>
        /// Blanks the phone and the personal number, and drops the vacations, of every soldier assigned in the missions' instances
        /// (the same fields <c>Soldiers/GetSoldiers</c> hides from non-admins). Changes the objects in place, so use it only on a
        /// freshly built graph (like <see cref="MissionService.GetMissions"/>'s), never on cached objects.
        /// </summary>
        /// <returns>The same list.</returns>
        public static List<Mission> RemoveFromAssignedSoldiers(List<Mission> missions)
        {
            var soldiers = missions
                .SelectMany(m => m.MissionInstances ?? [])
                .SelectMany(i => i.SoldierMissions ?? [])
                .Select(sm => sm.Soldier)
                .Where(s => s != null);
            foreach (var soldier in soldiers)
            {
                soldier.Phone = string.Empty;
                soldier.PersonalNumber = string.Empty;
                soldier.Vacations = [];
            }
            return missions;
        }
    }
}
