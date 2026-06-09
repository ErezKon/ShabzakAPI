namespace DataLayer
{
    /// <summary>
    /// Static factory for creating the appropriate DbContext based on runtime configuration.
    /// Set UseLocal = true (e.g. in Program.cs) to use the local SQLite database instead of Azure SQL Server.
    /// </summary>
    public static class DbFactory
    {
        /// <summary>
        /// When true, Create() returns a LocalDB (SQLite) instance instead of ShabzakDB (SQL Server).
        /// </summary>
        public static bool UseLocal { get; set; } = false;

        /// <summary>
        /// Creates a new DbContext instance based on the current UseLocal setting.
        /// </summary>
        public static ShabzakDB Create()
        {
            return UseLocal ? new LocalDB() : new ShabzakDB();
        }
    }
}
