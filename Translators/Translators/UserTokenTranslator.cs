using Translators.Models;

namespace Translators.Translators
{
    /// <summary>
    /// Translates UserToken entities between DataLayer DB models and BL models.
    /// Used for authentication token management.
    /// </summary>
    public abstract class UserTokenTranslator
    {
        /// <summary>
        /// Converts a DB UserToken entity to a BL UserToken model.
        /// </summary>
        /// <returns>The translated BL token, or null if input is null.</returns>
        public static UserToken ToBL(DataLayer.Models.UserToken user)
        {
            if (user == null)
            {
                return null;
            }
            return new UserToken
            {
                Id = user.Id,
                Token = user.Token,
                Expiration = user.Expiration,
                UserId = user.Id,
                Extra = user.Extra,
                User = UserTranslator.ToBL(user.User)
            };
        }


        /// <summary>
        /// Converts a BL UserToken model to a DB UserToken entity.
        /// </summary>
        public static DataLayer.Models.UserToken ToDB(UserToken user)
        {
            return new DataLayer.Models.UserToken()
            {
                Id = user.Id,
                Token = user.Token,
                Expiration = user.Expiration,
                UserId = user.Id,
                Extra = user.Extra,
                User = UserTranslator.ToDB(user.User)
            };
        }
    }
}
