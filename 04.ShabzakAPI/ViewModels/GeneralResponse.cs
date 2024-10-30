namespace ShabzakAPI.ViewModels
{
    public class GeneralResponse<T>
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public T? Value { get; set; }
    }
}
