using Translators.Models;

namespace BL.Services
{
    /// <summary>Outcome of an account-management guard check or operation.</summary>
    public enum AccountGuardOutcome
    {
        Allowed,
        Forbidden,
        BadRequest,
        Conflict,
        NotFound
    }

    /// <summary>Result of <see cref="UserAccountGuard.Check"/>.</summary>
    public record AccountGuardResult(AccountGuardOutcome Outcome, string? Message)
    {
        public bool IsAllowed => Outcome == AccountGuardOutcome.Allowed;
        public static AccountGuardResult Allowed { get; } = new(AccountGuardOutcome.Allowed, null);
    }

    /// <summary>
    /// A requested change to a login account. <see cref="NewRole"/> being non-null means the caller is
    /// setting the role (even to the same value), which requires SuperAdmin.
    /// </summary>
    public record AccountChange(
        UserRole? NewRole = null,
        bool? NewEnabled = null,
        bool? NewActivated = null,
        bool Delete = false,
        bool PasswordReset = false);

    /// <summary>
    /// Thrown by account-management operations when a guard rule (or lookup) fails.
    /// Controllers map <see cref="Outcome"/> to an HTTP status code.
    /// </summary>
    public class UserAccountException : Exception
    {
        public AccountGuardOutcome Outcome { get; }

        public UserAccountException(AccountGuardOutcome outcome, string message) : base(message)
        {
            Outcome = outcome;
        }
    }

    /// <summary>
    /// Pure (no I/O) guard rules for account management:
    /// <list type="bullet">
    /// <item>Only Admin/SuperAdmin may manage accounts; setting a role requires SuperAdmin.</item>
    /// <item>Only a SuperAdmin may modify, reset or delete a SuperAdmin account.</item>
    /// <item>No self role change, self disable/deactivate, or self delete.</item>
    /// <item>The last enabled SuperAdmin cannot be demoted, disabled, deactivated or deleted.</item>
    /// </list>
    /// </summary>
    public static class UserAccountGuard
    {
        /// <summary>
        /// Checks whether <paramref name="callerRole"/> (user <paramref name="callerId"/>) may apply
        /// <paramref name="change"/> to the target account.
        /// </summary>
        /// <param name="enabledSuperAdminCount">Number of enabled SuperAdmin accounts before the change.</param>
        public static AccountGuardResult Check(
            int callerId,
            UserRole callerRole,
            int targetId,
            UserRole targetRole,
            bool targetEnabled,
            AccountChange change,
            int enabledSuperAdminCount)
        {
            if (callerRole < UserRole.Admin)
            {
                return Fail(AccountGuardOutcome.Forbidden, "Only an Admin or SuperAdmin can manage user accounts.");
            }
            if (change.NewRole.HasValue && callerRole != UserRole.SuperAdmin)
            {
                return Fail(AccountGuardOutcome.Forbidden, "Only a SuperAdmin can change user roles.");
            }
            if (targetRole == UserRole.SuperAdmin && callerRole != UserRole.SuperAdmin)
            {
                return Fail(AccountGuardOutcome.Forbidden, "Only a SuperAdmin can modify SuperAdmin accounts.");
            }

            if (callerId == targetId)
            {
                if (change.Delete)
                {
                    return Fail(AccountGuardOutcome.BadRequest, "You cannot delete your own account.");
                }
                if (change.NewRole.HasValue && change.NewRole.Value != targetRole)
                {
                    return Fail(AccountGuardOutcome.BadRequest, "You cannot change your own role.");
                }
                if (change.NewEnabled == false || change.NewActivated == false)
                {
                    return Fail(AccountGuardOutcome.BadRequest, "You cannot disable or deactivate your own account.");
                }
            }

            var removesSuperAdmin = targetRole == UserRole.SuperAdmin && targetEnabled &&
                (change.Delete
                 || change.NewEnabled == false
                 || change.NewActivated == false
                 || (change.NewRole.HasValue && change.NewRole.Value != UserRole.SuperAdmin));
            if (removesSuperAdmin && enabledSuperAdminCount <= 1)
            {
                return Fail(AccountGuardOutcome.Conflict, "The last enabled SuperAdmin cannot be demoted, disabled, deactivated or deleted.");
            }

            return AccountGuardResult.Allowed;
        }

        /// <summary>
        /// Checks deleting several accounts in one operation (the login accounts of a deleted soldier): each one must be
        /// deletable by the caller, and the enabled SuperAdmins deleted earlier in the list no longer count.
        /// </summary>
        /// <param name="enabledSuperAdminCount">Number of enabled SuperAdmin accounts before the deletion.</param>
        public static AccountGuardResult CheckDeleteAll(
            int callerId,
            UserRole callerRole,
            IEnumerable<(int Id, UserRole Role, bool Enabled)> targets,
            int enabledSuperAdminCount)
        {
            var remaining = enabledSuperAdminCount;
            foreach (var target in targets)
            {
                var result = Check(callerId, callerRole, target.Id, target.Role, target.Enabled, new AccountChange(Delete: true), remaining);
                if (!result.IsAllowed)
                {
                    return result;
                }
                if (target.Role == UserRole.SuperAdmin && target.Enabled)
                {
                    remaining--;
                }
            }
            return AccountGuardResult.Allowed;
        }

        private static AccountGuardResult Fail(AccountGuardOutcome outcome, string message) => new(outcome, message);
    }
}
