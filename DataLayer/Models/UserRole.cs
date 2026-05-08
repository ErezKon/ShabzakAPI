namespace DataLayer.Models
{
    /// <summary>
    /// Enum of user access levels. Regular users have read-only access,
    /// Admins can manage soldiers/missions, SuperAdmins have full system control.
    /// </summary>
    public enum UserRole
    {
        Regular,
        Admin,
        SuperAdmin
    }
}
