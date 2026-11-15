namespace ShabzakAPI.ViewModels
{
    /// <summary>
    /// Body of POST /api/Schedule/GetInstances: the instances to look up by ID (1 to 200; repeated IDs are ignored).
    /// </summary>
    public class GetScheduleInstancesRequest
    {
        public List<int>? InstanceIds { get; set; }
    }
}
