using BL.Cache;
using BL.Extensions;
using BL.Logging;
using BL.Models.Users;
using DataLayer;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Translators.Encryption;
using Translators.Models;

namespace BL.Services
{
    /// <summary>
    /// Business logic service for user authentication and account management.
    /// Handles user creation from soldier records, login verification, and password operations.
    /// </summary>
    public class UserService
    {
        private readonly UsersCache _usersCache;
        private readonly SoldiersCache _soldiersCache;
        /// <summary>
        /// Initializes the user service with the users and soldiers caches.
        /// </summary>
        /// <param name="userCache">The users cache instance.</param>
        /// <param name="soldiersCache">The soldiers cache (soldier names, units and phones for account DTOs / default passwords).</param>
        public UserService(UsersCache userCache, SoldiersCache soldiersCache)
        {
            _usersCache = userCache;
            _soldiersCache = soldiersCache;
        }
        /// <summary>
        /// Creates a new user account in the database. Generates a salt, hashes the password
        /// with SHA-512(password + salt), and saves to DB.
        /// </summary>
        /// <param name="name">The username for the new user.</param>
        /// <param name="password">The password for the new user.</param>
        /// <param name="soldierId">The ID of the linked soldier (optional).</param>
        /// <param name="role">The role of the new user (default: Regular).</param>
        /// <returns>The created user entity.</returns>
        public User CreateUser(string name, string password, int? soldierId = null, UserRole role = UserRole.Regular)
        {
            var user = new User
            {
                Name = name,
                Password = password,
                Role = role,
                Activated = false,
                Enabled = true,
                SoldierId = soldierId
            };
            user.Salt = Guid.NewGuid().ToString();
            using var db = DataLayer.DbFactory.Create();
            // SECURITY: New accounts use PBKDF2-HMAC-SHA512 (100k iterations) instead
            // of single-round SHA-512. The PBKDF2 hash is self-describing (embeds its own salt).
            user.Password = Sha512Encryptor.HashWithPbkdf2(password);
            var dbUser = user.ToDB();//.Encrypt();
            db.Users.Add(dbUser);
            db.SaveChanges();
            user.Id = dbUser.Id;
            _usersCache.AddUser(dbUser);
            return user;
        }

        /// <summary>
        /// Creates a user account from a soldier record.
        /// Username = SHA-512(personalNumber), Password = SHA-512(phone).
        /// The user is linked to the soldier via SoldierId.
        /// </summary>
        /// <param name="soldier">The soldier to create an account for.</param>
        /// <returns>The created user entity.</returns>
        public User CreateFromSoldier(Soldier soldier)
        {
            var username = Sha512Encryptor.Encrypt(soldier.PersonalNumber);
            var password = DefaultPasswordHash(soldier);
            return CreateUser(username, password, soldier.Id);
        }

        /// <summary>
        /// True if a login account already exists for the soldier (linked by SoldierId, or with the same hashed username).
        /// </summary>
        public bool HasAccount(Soldier soldier)
        {
            var username = Sha512Encryptor.Encrypt(soldier.PersonalNumber);
            return _usersCache.GetAllUsers().Any(u => u.SoldierId == soldier.Id || u.Name == username)
                || _usersCache.GetUser(username) != null;
        }

        /// <summary>
        /// The client-side hash of a soldier's default password: SHA-512(phone without dashes).
        /// Stored as PBKDF2 of this value (same derivation as account creation).
        /// </summary>
        private static string DefaultPasswordHash(Soldier soldier) =>
            Sha512Encryptor.Encrypt(soldier.Phone.Replace("-", string.Empty));

        /// <summary>
        /// Returns the caller's identity (no secrets) or null if the user no longer exists.
        /// </summary>
        public MeDto? GetMe(int userId)
        {
            var user = _usersCache.GetUserById(userId);
            if (user == null)
            {
                return null;
            }
            var soldier = user.SoldierId.HasValue ? _soldiersCache.GetSoldierById(user.SoldierId.Value) : null;
            var role = ToRole(user.Role);
            return new MeDto
            {
                Id = user.Id,
                Role = role,
                RoleName = role.ToString(),
                Enabled = user.Enabled,
                Activated = user.Activated,
                SoldierId = user.SoldierId,
                SoldierName = soldier?.Name
            };
        }

        /// <summary>
        /// Lists all login accounts as whitelisted DTOs (no hashed username, password or salt).
        /// </summary>
        public List<UserAccountDto> GetAllUsers() => _usersCache.GetAllUsers()
            .OrderBy(u => u.Id)
            .Select(ToAccountDto)
            .ToList();

        /// <summary>
        /// Updates a login account's role / enabled / activated flags after applying <see cref="UserAccountGuard"/>.
        /// </summary>
        /// <exception cref="UserAccountException">On a guard violation or unknown user.</exception>
        public UserAccountDto UpdateUser(int callerId, UserRole callerRole, int userId, UserRole? role, bool? enabled, bool? activated)
        {
            using var db = DbFactory.Create();
            var user = FindForChange(db, callerId, callerRole, userId, new AccountChange(role, enabled, activated));
            if (role.HasValue)
            {
                user.Role = (DataLayer.Models.UserRole)(int)role.Value;
            }
            if (enabled.HasValue)
            {
                user.Enabled = enabled.Value;
            }
            if (activated.HasValue)
            {
                user.Activated = activated.Value;
            }
            db.SaveChanges();
            _usersCache.AddOrReplaceUser(user);
            Logger.Log($"User ID={user.Id} updated by user ID={callerId} (role={role?.ToString() ?? "-"}, enabled={enabled?.ToString() ?? "-"}, activated={activated?.ToString() ?? "-"})");
            return ToAccountDto(user);
        }

        /// <summary>
        /// Deletes a login account (the linked soldier is not touched) after applying <see cref="UserAccountGuard"/>.
        /// </summary>
        /// <exception cref="UserAccountException">On a guard violation or unknown user.</exception>
        public int DeleteUser(int callerId, UserRole callerRole, int userId)
        {
            using var db = DbFactory.Create();
            var user = FindForChange(db, callerId, callerRole, userId, new AccountChange(Delete: true));
            db.Users.Remove(user);
            db.SaveChanges();
            _usersCache.RemoveUser(userId);
            Logger.Log($"User ID={userId} deleted by user ID={callerId}");
            return userId;
        }

        /// <summary>
        /// Resets an account's password to the default (PBKDF2(SHA-512(linked soldier's phone without dashes)))
        /// after applying <see cref="UserAccountGuard"/>. Never accepts or returns a password.
        /// </summary>
        /// <exception cref="UserAccountException">On a guard violation, unknown user, or missing soldier phone.</exception>
        public void ResetPasswordToDefault(int callerId, UserRole callerRole, int userId)
        {
            using var db = DbFactory.Create();
            var user = FindForChange(db, callerId, callerRole, userId, new AccountChange(PasswordReset: true));
            var soldier = user.SoldierId.HasValue ? _soldiersCache.GetSoldierById(user.SoldierId.Value) : null;
            if (soldier == null || string.IsNullOrWhiteSpace(soldier.Phone) || soldier.Phone == "N/A")
            {
                throw new UserAccountException(AccountGuardOutcome.BadRequest, "The account has no linked soldier with a phone number, so the default password cannot be derived.");
            }
            user.Password = Sha512Encryptor.HashWithPbkdf2(DefaultPasswordHash(soldier));
            db.SaveChanges();
            _usersCache.AddOrReplaceUser(user);
            Logger.Log($"Password of user ID={userId} reset to default by user ID={callerId}");
        }

        /// <summary>
        /// Checks whether the caller may change the password of the account identified by its hashed username
        /// (used by ResetPassword for admins). Returns the account's ID.
        /// </summary>
        /// <exception cref="UserAccountException">On a guard violation or unknown user.</exception>
        public int EnsureCanResetPassword(int callerId, UserRole callerRole, DataLayer.Models.User target)
        {
            if (callerId == target.Id)
            {
                return target.Id;
            }
            var result = UserAccountGuard.Check(callerId, callerRole, target.Id, ToRole(target.Role), target.Enabled,
                new AccountChange(PasswordReset: true), CountEnabledSuperAdmins());
            if (!result.IsAllowed)
            {
                throw new UserAccountException(result.Outcome, result.Message!);
            }
            return target.Id;
        }

        private DataLayer.Models.User FindForChange(ShabzakDB db, int callerId, UserRole callerRole, int userId, AccountChange change)
        {
            var user = db.Users.FirstOrDefault(u => u.Id == userId)
                ?? throw new UserAccountException(AccountGuardOutcome.NotFound, $"User {userId} not found.");
            var enabledSuperAdmins = db.Users.Count(u => u.Role == DataLayer.Models.UserRole.SuperAdmin && u.Enabled);
            var result = UserAccountGuard.Check(callerId, callerRole, user.Id, ToRole(user.Role), user.Enabled, change, enabledSuperAdmins);
            if (!result.IsAllowed)
            {
                throw new UserAccountException(result.Outcome, result.Message!);
            }
            return user;
        }

        private int CountEnabledSuperAdmins() =>
            _usersCache.GetAllUsers().Count(u => u.Role == DataLayer.Models.UserRole.SuperAdmin && u.Enabled);

        private static UserRole ToRole(DataLayer.Models.UserRole role) => (UserRole)(int)role;

        private UserAccountDto ToAccountDto(DataLayer.Models.User user)
        {
            var soldier = user.SoldierId.HasValue ? _soldiersCache.GetSoldierById(user.SoldierId.Value) : null;
            var role = ToRole(user.Role);
            return new UserAccountDto
            {
                Id = user.Id,
                SoldierId = user.SoldierId,
                SoldierName = soldier?.Name,
                Platoon = soldier?.Platoon,
                Company = soldier?.Company,
                Role = role,
                RoleName = role.ToString(),
                Enabled = user.Enabled,
                Activated = user.Activated
            };
        }

        /// <summary>
        /// Authenticates a user. Looks up by hashed username, verifies password as
        /// SHA-512(password + salt). On success, loads linked soldier data and clears sensitive fields.
        /// </summary>
        /// <param name="username">SHA-512 hashed username (derived from personalNumber).</param>
        /// <param name="password">SHA-512 hashed password (derived from phone).</param>
        /// <param name="ip">The client's IP address (for logging).</param>
        /// <returns>The authenticated user with linked soldier data (password/salt cleared).</returns>
        /// <exception cref="ArgumentNullException">Thrown if username or password is null.</exception>
        public User? Login(string username, string password, string ip)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentNullException("Username or password cannot be null.");
            }
            var user = _usersCache.GetUser(username) ?? throw new ArgumentException("User not found.");
            // SECURITY: Verify() auto-detects hash format (PBKDF2 vs legacy SHA-512)
            // and uses constant-time comparison to prevent timing attacks.
            if (!Sha512Encryptor.Verify(password, user.Password, user.Salt))
            {
                return null;
            }

            // Transparent upgrade: if the user still has a legacy SHA-512 hash,
            // re-hash with PBKDF2 now that we've confirmed the password is correct.
            if (Sha512Encryptor.NeedsUpgrade(user.Password))
            {
                try
                {
                    var upgraded = Sha512Encryptor.HashWithPbkdf2(password);
                    using var upgradeDb = DataLayer.DbFactory.Create();
                    var dbUser = upgradeDb.Users.FirstOrDefault(u => u.Id == user.Id);
                    if (dbUser != null)
                    {
                        dbUser.Password = upgraded;
                        upgradeDb.SaveChanges();
                        user.Password = upgraded; // update cache too
                        Logger.Log($"Upgraded password hash to PBKDF2 for user ID={user.Id}");
                    }
                }
                catch (Exception ex)
                {
                    // Non-fatal: login still succeeds, upgrade will retry next login.
                    Logger.Log($"Failed to upgrade password hash for user ID={user.Id}: {ex.Message}", LogLevel.Warning);
                }
            }

            var ret = user.ToBL().LoadSoldier();
            ret.Password = "";
            ret.Salt = "";
            ret.Soldier.Missions = ret.Soldier?.Missions
                ?.Where(m => DateTime.Parse(m.MissionInstance.FromTime) > DateTime.Now)
                ?.ToList() ?? [];
            return ret;
        }
    }
}
