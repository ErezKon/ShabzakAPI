using BL.Cache;
using BL.Extensions;
using BL.Models.Users;
using BL.Services;
using DataLayer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ShabzakAPI.Auth;
using ShabzakAPI.ViewModels;
using System.Runtime.CompilerServices;
using Translators.Encryption;
using Translators.Models;

namespace ShabzakAPI.Controllers
{
    /// <summary>
    /// Controller for user authentication, password management, bulk user creation from soldier records,
    /// and login-account management. Login is anonymous; all other endpoints require a valid JWT Bearer token.
    /// Account management is AdminOrAbove; role changes and SuperAdmin accounts require SuperAdmin (see <see cref="UserAccountGuard"/>).
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly UserService _userService;
        private readonly SoldiersCache _soldiersCache;
        private readonly UsersCache _usersCache;
        /// <summary>
        /// Initializes a new instance of the UserController class.
        /// </summary>
        /// <param name="userService">The user service instance.</param>
        /// <param name="soldiersCache">The soldiers cache instance.</param>
        /// <param name="usersCache">The users cache instance.</param>
        public UserController(UserService userService, SoldiersCache soldiersCache, UsersCache usersCache) 
        {
            _soldiersCache = soldiersCache;
            _userService = userService;
            _usersCache = usersCache;
        }

        /// <summary>
        /// Returns the caller's identity (no secrets): id, role, enabled/activated flags and linked soldier.
        /// </summary>
        [HttpGet("Me")]
        public ActionResult<MeDto> Me()
        {
            var userId = User.GetUserId();
            var me = userId.HasValue ? _userService.GetMe(userId.Value) : null;
            return me == null ? Unauthorized() : Ok(me);
        }

        /// <summary>
        /// Resets a user's password. Requires authentication (JWT).
        /// Regular users may only reset their own password (403 otherwise); admins may reset others,
        /// but only a SuperAdmin may reset a SuperAdmin's password.
        /// </summary>
        /// <param name="model">Username, new password, and whether the password is already encrypted.</param>
        /// <returns>Success/failure response with message.</returns>
        [HttpPost("ResetPassword")]
        public ActionResult<GeneralResponse<string>> ResetPassword(ResetPasswordVM model)
        {
            using var db = DbFactory.Create();
            var encUsername = model.Encrypted ? model.Username : Sha512Encryptor.Encrypt(model.Username);
            var user = _usersCache.GetUser(encUsername);
            var callerId = User.GetUserId() ?? 0;

            if (!User.IsAdminOrAbove() && (user == null || user.Id != callerId))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new GeneralResponse<string> { Success = false, ErrorMessage = "You can only reset your own password." });
            }
            if (user == null)
            {
                return new GeneralResponse<string> { Success = false, ErrorMessage = "User not found." };
            }
            try
            {
                _userService.EnsureCanResetPassword(callerId, User.GetRole(), user);
            }
            catch (UserAccountException ex)
            {
                return AccountError<string>(ex);
            }
            var encPass = model.Encrypted ? model.Password : Sha512Encryptor.Encrypt(model.Password);
            var pass = Sha512Encryptor.HashWithPbkdf2(encPass);
            user.Password = pass;

            // FIX: Attach the cached entity to the new DbContext so EF tracks the change.
            // Previously, user.Password was set on the cached object but SaveChanges()
            // used a fresh DbContext that wasn't tracking it — the change was never persisted.
            db.Users.Attach(user);
            db.Entry(user).Property(u => u.Password).IsModified = true;
            db.SaveChanges();

            return new GeneralResponse<string> { Success = true, Value = "Password reset successfully." };
        }

        /// <summary>
        /// Authenticates a user. Looks up by SHA-512 hashed username (derived from personalNumber),
        /// verifies password with SHA-512(password + salt). Returns user profile + signed JWT token.
        /// This is the only anonymous endpoint — no JWT required.
        /// </summary>
        /// <param name="login">Username and password (both pre-hashed by the frontend).</param>
        /// <returns>Response containing LoginResponse (User + JWT token), or error message.</returns>
        [AllowAnonymous] // Login is the only endpoint that doesn't require JWT.
        [HttpPost("Login")]
        public GeneralResponse<LoginResponse> Login(LoginVM login)
        {
            var ip = Request.HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "unknown";
            try
            {
                var ret = _userService.Login(login.UserName, login.Password, ip);
                if (ret == null)
                {
                    return new GeneralResponse<LoginResponse>
                    {
                        Success = false,
                        ErrorMessage = "Invalid username or password."
                    };
                }

                // Generate a signed JWT token for the authenticated user.
                var token = JwtHelper.GenerateToken(ret);

                return new GeneralResponse<LoginResponse>
                {
                    Success = true,
                    Value = new LoginResponse { User = ret, Token = token }
                };
            }
            catch (Exception ex)
            {
                return new GeneralResponse<LoginResponse>
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Bulk creates user accounts from soldier records. Username = SHA-512(personalNumber),
        /// Password = SHA-512(phone + generated_salt). Only creates users for soldiers that don't already have accounts
        /// (soldiers with an existing account or personal number "N/A" are skipped).
        /// Requires Admin or SuperAdmin role.
        /// </summary>
        /// <param name="soldierIds">List of soldier IDs to create user accounts for.</param>
        /// <returns>Response containing the number of users created.</returns>
        [HttpPost("CreateUsersForSoldiers")]
        [Authorize(Policy = Policies.AdminOrAbove)]
        public GeneralResponse<int> CreateUsersForSoldiers(List<int> soldierIds)
        {
            var ret = new List<User>();
            var soldiers = _soldiersCache.GetSoldiers()
                .Where(s => soldierIds.Contains(s.Id))
                .ToList();
            try
            {
                foreach (var soldier in soldiers)
                {
                    if(soldier.PersonalNumber.Equals("N/A") || _userService.HasAccount(soldier))
                    {
                        continue;
                    }
                    ret.Add(_userService.CreateFromSoldier(soldier));
                }

                return new GeneralResponse<int>
                {
                    Success = true,
                    Value = ret.Count
                };
            }
            catch (Exception ex)
            {
                return new GeneralResponse<int>
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
            
        }

        /// <summary>
        /// Lists all login accounts (whitelisted fields; no hashed username, password or salt).
        /// </summary>
        [HttpGet("GetUsers")]
        [Authorize(Policy = Policies.AdminOrAbove)]
        public List<UserAccountDto> GetUsers() => _userService.GetAllUsers();

        /// <summary>
        /// Updates an account's role / enabled / activated flags. Setting the role requires SuperAdmin.
        /// Returns 403 (permission), 400 (self change), 409 (last enabled SuperAdmin) or 404 on guard failures.
        /// </summary>
        [HttpPost("UpdateUser")]
        [Authorize(Policy = Policies.AdminOrAbove)]
        public ActionResult<GeneralResponse<UserAccountDto>> UpdateUser(UpdateUserRequest request)
        {
            if (request.Role.HasValue && !Enum.IsDefined(request.Role.Value))
            {
                return BadRequest(new GeneralResponse<UserAccountDto> { Success = false, ErrorMessage = "Unknown role." });
            }
            if (!request.Role.HasValue && !request.Enabled.HasValue && !request.Activated.HasValue)
            {
                return BadRequest(new GeneralResponse<UserAccountDto> { Success = false, ErrorMessage = "Nothing to update." });
            }
            return ExecuteAccountChange(() => _userService.UpdateUser(
                User.GetUserId() ?? 0, User.GetRole(), request.UserId, request.Role, request.Enabled, request.Activated));
        }

        /// <summary>
        /// Deletes a login account (the soldier record is kept). Guard rules as in UpdateUser.
        /// </summary>
        [HttpPost("DeleteUser")]
        [Authorize(Policy = Policies.AdminOrAbove)]
        public ActionResult<GeneralResponse<int>> DeleteUser(int userId) =>
            ExecuteAccountChange(() => _userService.DeleteUser(User.GetUserId() ?? 0, User.GetRole(), userId));

        /// <summary>
        /// Resets an account's password to the default (derived from the linked soldier's phone).
        /// Never accepts or returns a password.
        /// </summary>
        [HttpPost("ResetPasswordToDefault")]
        [Authorize(Policy = Policies.AdminOrAbove)]
        public ActionResult<GeneralResponse<string>> ResetPasswordToDefault(int userId) =>
            ExecuteAccountChange(() =>
            {
                _userService.ResetPasswordToDefault(User.GetUserId() ?? 0, User.GetRole(), userId);
                return "Password reset to default.";
            });

        private ActionResult<GeneralResponse<T>> ExecuteAccountChange<T>(Func<T> action)
        {
            try
            {
                return new GeneralResponse<T> { Success = true, Value = action() };
            }
            catch (UserAccountException ex)
            {
                return AccountError<T>(ex);
            }
        }

        private ObjectResult AccountError<T>(UserAccountException ex)
        {
            var status = ex.Outcome switch
            {
                AccountGuardOutcome.Forbidden => StatusCodes.Status403Forbidden,
                AccountGuardOutcome.Conflict => StatusCodes.Status409Conflict,
                AccountGuardOutcome.NotFound => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status400BadRequest
            };
            return StatusCode(status, new GeneralResponse<T> { Success = false, ErrorMessage = ex.Message });
        }
    }
}
