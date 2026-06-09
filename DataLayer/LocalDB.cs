using Microsoft.EntityFrameworkCore;

namespace DataLayer
{
    /// <summary>
    /// Local SQLite DbContext for offline development.
    /// Inherits all entity configurations from ShabzakDB, only overrides the connection provider.
    /// The ShabzakLocal.db file is created automatically via EnsureCreated() on first run.
    /// </summary>
    public class LocalDB : ShabzakDB
    {
        /// <summary>
        /// Configures the SQLite connection. Uses a local file-based database.
        /// Does NOT call base.OnConfiguring to avoid the SQL Server configuration.
        /// </summary>
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ShabzakLocal.db");
                optionsBuilder.UseSqlite($"Data Source={dbPath}");
            }
            optionsBuilder.EnableSensitiveDataLogging();
        }
    }
}
