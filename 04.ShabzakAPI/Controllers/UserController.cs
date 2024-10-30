using BL.Cache;
using BL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ShabzakAPI.ViewModels;
using System.Runtime.CompilerServices;
using Translators.Encryption;
using Translators.Models;

namespace ShabzakAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly UserService _userService;
        private readonly SoldiersCache _soldiersCache;
        public UserController(UserService userService, SoldiersCache soldiersCache) 
        {
            _soldiersCache = soldiersCache;
            _userService = userService;
        }

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
