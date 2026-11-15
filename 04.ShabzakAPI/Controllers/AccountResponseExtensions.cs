using BL.Services;
using Microsoft.AspNetCore.Mvc;
using ShabzakAPI.ViewModels;

namespace ShabzakAPI.Controllers
{
    /// <summary>
    /// Maps a <see cref="UserAccountException"/> (an account-guard refusal or an unknown account) to its HTTP status and a
    /// <see cref="GeneralResponse{T}"/> error: 403 permission, 409 last enabled SuperAdmin, 404 unknown, 400 otherwise
    /// (e.g. a self change). Used by the account endpoints and by <c>Soldiers/DeleteSoldier</c> (which deletes the
    /// soldier's login account too).
    /// </summary>
    internal static class AccountResponseExtensions
    {
        public static ObjectResult AccountError<T>(this ControllerBase controller, UserAccountException ex)
        {
            var status = ex.Outcome switch
            {
                AccountGuardOutcome.Forbidden => StatusCodes.Status403Forbidden,
                AccountGuardOutcome.Conflict => StatusCodes.Status409Conflict,
                AccountGuardOutcome.NotFound => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status400BadRequest
            };
            return controller.StatusCode(status, new GeneralResponse<T> { Success = false, ErrorMessage = ex.Message });
        }
    }
}
