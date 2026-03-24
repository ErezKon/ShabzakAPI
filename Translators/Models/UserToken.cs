namespace Translators.Models
{
    public class UserToken
    {
        public int Id { get; set; }
        public string Token { get; set; }
        public DateTime Expiration { get; set; }
        public string Extra { get; set; }
        public int UserId { get; set; }
        public User User { get; set; }
    }
}
