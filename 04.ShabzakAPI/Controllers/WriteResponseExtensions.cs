using BL.Logging;
using BL.Models.Schedule;
using Microsoft.AspNetCore.Mvc;
using ShabzakAPI.Auth;
using ShabzakAPI.ViewModels;

namespace ShabzakAPI.Controllers
{
    /// <summary>
    /// Runs a write of the agent-facing endpoints and maps the outcome to <see cref="GeneralResponse{T}"/> plus an HTTP status:
    /// 200 applied; 409 blocked by constraint violations (<c>value</c> carries the validations) or conflicting data;
    /// 404 unknown entity; 400 invalid request. Every call is logged with IDs only (never names or other PII).
    /// </summary>
    internal static class WriteResponseExtensions
    {
        public static ActionResult<GeneralResponse<T>> ExecuteWrite<T>(
            this ControllerBase controller,
            string operation,
            string details,
            Func<ScheduleWriteResult<T>> action)
        {
            int status;
            GeneralResponse<T> body;
            try
            {
                var result = action();
                status = result.IsBlocked ? StatusCodes.Status409Conflict : StatusCodes.Status200OK;
                body = new GeneralResponse<T> { Success = !result.IsBlocked, ErrorMessage = result.Message, Value = result.Value };
            }
            catch (KeyNotFoundException ex)
            {
                (status, body) = (StatusCodes.Status404NotFound, Failure<T>(ex));
            }
            catch (ScheduleConflictException ex)
            {
                (status, body) = (StatusCodes.Status409Conflict, Failure<T>(ex));
            }
            catch (ArgumentException ex)
            {
                (status, body) = (StatusCodes.Status400BadRequest, Failure<T>(ex));
            }
            Logger.Log($"{operation} {details} -> {status} by user ID={controller.User.GetUserId()}");
            return controller.StatusCode(status, body);
        }

        /// <summary>Same as above for writes that are never blocked (only fail with 400 / 404 / 409).</summary>
        public static ActionResult<GeneralResponse<T>> ExecuteWrite<T>(
            this ControllerBase controller,
            string operation,
            string details,
            Func<T> action) =>
            controller.ExecuteWrite<T>(operation, details, () => ScheduleWriteResult<T>.Ok(action()));

        private static GeneralResponse<T> Failure<T>(Exception ex) => new() { Success = false, ErrorMessage = ex.Message };
    }
}
