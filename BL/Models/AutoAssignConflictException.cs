namespace BL.Models
{
    /// <summary>
    /// Batch auto-assign cannot run now (interactive auto-assign sessions are active). Mapped to HTTP 409 by
    /// <c>Mission/AutoAssign</c>. Derives from <see cref="InvalidOperationException"/>, which the service threw before.
    /// </summary>
    public class AutoAssignConflictException : InvalidOperationException
    {
        public AutoAssignConflictException(string message) : base(message)
        {
        }
    }
}
