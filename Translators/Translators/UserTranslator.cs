using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Models;

namespace Translators.Translators
{
    public abstract class UserTranslator
    {

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
