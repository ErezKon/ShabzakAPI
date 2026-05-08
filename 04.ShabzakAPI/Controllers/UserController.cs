using BL.Cache;
using BL.Extensions;
using BL.Services;
using DataLayer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ShabzakAPI.ViewModels;
using System.Runtime.CompilerServices;
using Translators.Encryption;
using Translators.Models;

namespace ShabzakAPI.Controllers
{
    /// <summary>
    /// Controller for user authentication, password management, and bulk user creation from soldier records.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
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
        /// Resets a user's password. If the request is pre-encrypted (from frontend),
        /// hashes directly. Otherwise, applies SHA-512 hashing with the user's salt.
        /// </summary>
        /// <param name="model">Username, new password, and whether the password is already encrypted.</param>
        /// <returns>Success/failure response with message.</returns>
        [HttpPost("ResetPassword")]
        public string ResetPassword(ResetPasswordVM model)
        {
            using var db = new ShabzakDB();
            var encUsername = model.Encrypted ? model.Username : Sha512Encryptor.Encrypt(model.Username);
            var cache = UsersCache.GetInstance();
            var user = cache.GetUser(encUsername);

            if (user == null)
            {
                return "User not found.";
            }
            var encPass = model.Encrypted ? model.Password : Sha512Encryptor.Encrypt(model.Password);
            var pass = Sha512Encryptor.Encrypt($"{encPass}{user.Salt}");
            user.Password = pass;
            db.SaveChanges();
            return pass;
        }

        /// <summary>
        /// Authenticates a user. Looks up by SHA-512 hashed username (derived from personalNumber),
        /// verifies password with SHA-512(password + salt). Returns user details with soldier data if linked.
        /// </summary>
        /// <param name="login">Username and password (both pre-hashed by the frontend).</param>
        /// <returns>Response containing the authenticated User object, or error message.</returns>
        [HttpPost("Login")]
        public GeneralResponse<User> Login(LoginVM login)
        {
            var ip = Request.HttpContext.Connection.RemoteIpAddress.MapToIPv4().ToString();
            try
            {
                var ret = _userService.Login(login.UserName, login.Password, ip);
                return new GeneralResponse<User>
                {
                    Success = true,
                    Value = ret
                };
            }
            catch (Exception ex)
            {

                return new GeneralResponse<User>
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Bulk creates user accounts from soldier records. Username = SHA-512(personalNumber),
        /// Password = SHA-512(phone + generated_salt). Only creates users for soldiers that don't already have accounts.
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
