using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;

namespace Translators.Translators
{
    /// <summary>
    /// Translates User entities between DataLayer DB models and BL models.
    /// Converts UserRole enums between DB and BL types.
    /// </summary>
    public abstract class UserTranslator
    {

        /// <summary>
        /// Converts a DB User entity to a BL User model.
        /// </summary>
        /// <returns>The translated BL user, or null if input is null.</returns>
        public static User ToBL(DataLayer.Models.User user)
        {
            if (user == null)
            {
                return null;
            }
            return new User
            {
                Id = user.Id,
                Name = user.Name,
                Password = user.Password,
                Salt = user.Salt,
                Activated = user.Activated,
                Enabled = user.Enabled,
                Role = (UserRole)Enum.Parse(typeof(UserRole), user.Role.ToString()),
                SoldierId = user.SoldierId,
            };
        }


        /// <summary>
        /// Converts a BL User model to a DB User entity.
        /// </summary>
        public static DataLayer.Models.User ToDB(User user)
        {
            return new DataLayer.Models.User()
            {
                Id = user.Id,
                Name = user.Name,
                Password = user.Password,
                Salt = user.Salt,
                Activated = user.Activated,
                Enabled = user.Enabled,
                Role = (DataLayer.Models.UserRole)Enum.Parse(typeof(DataLayer.Models.UserRole), user.Role.ToString()),
                SoldierId = user.SoldierId,
            };
        }
    }
}
