namespace ShabzakAPI.ViewModels
{
    public class ResetPasswordVM
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public bool Encrypted { get; set; } = false;
    }
}
