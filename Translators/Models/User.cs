namespace Translators.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Password { get; set; }
        public string Salt { get; set; }
        public UserRole Role { get; set; }
        public bool Enabled { get; set; }
        public bool Activated { get; set; }
        public int? SoldierId { get; set; }
        public Soldier? Soldier { get; set; }
    }
}
