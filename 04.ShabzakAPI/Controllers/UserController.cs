using BL.Cache;
using BL.Extensions;
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
    /// Controller for user authentication, password management, and bulk user creation from soldier records.
    /// Login is anonymous; all other endpoints require a valid JWT Bearer token.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly UserService _userService;
        private readonly SoldiersCache _soldiersCache;
        /// <summary>
        /// Initializes a new instance of the UserController class.
        /// </summary>
        /// <param name="userService">The user service instance.</param>
        /// <param name="soldiersCache">The soldiers cache instance.</param>
        public UserController(UserService userService, SoldiersCache soldiersCache) 
        {
            _soldiersCache = soldiersCache;
            _userService = userService;
        }

        /// <summary>
        /// Resets a user's password. Requires authentication (JWT).
        /// </summary>
        /// <param name="model">Username, new password, and whether the password is already encrypted.</param>
        /// <returns>Success/failure response with message.</returns>
        [HttpPost("ResetPassword")]
        public GeneralResponse<string> ResetPassword(ResetPasswordVM model)
        {
            using var db = DbFactory.Create();
            var encUsername = model.Encrypted ? model.Username : Sha512Encryptor.Encrypt(model.Username);
            var cache = UsersCache.GetInstance();
            var user = cache.GetUser(encUsername);

            if (user == null)
            {
                return new GeneralResponse<string> { Success = false, ErrorMessage = "User not found." };
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
        /// Password = SHA-512(phone + generated_salt). Only creates users for soldiers that don't already have accounts.
        /// Requires Admin or SuperAdmin role.
        /// </summary>
        /// <param name="soldierIds">List of soldier IDs to create user accounts for.</param>
        /// <returns>Response containing the number of users created.</returns>
        [HttpPost("CreateUsersForSoldiers")]
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
                    if(soldier.PersonalNumber.Equals("N/A"))
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
    }
}
