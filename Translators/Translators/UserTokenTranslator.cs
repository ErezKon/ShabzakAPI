using Translators.Models;

namespace Translators.Translators
{
    public abstract class UserTokenTranslator
    {
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
