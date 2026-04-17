namespace BL.Models
{
    public class InteractiveAutoAssignStep
    {
        public string SessionId { get; set; }
        public InteractiveAutoAssignStatus Status { get; set; }
        public int CurrentIndex { get; set; }
        public int TotalInstancesCount { get; set; }
        public PendingInstanceView? Pending { get; set; }
        public AssignmentValidationModel? Result { get; set; }
    }
}
