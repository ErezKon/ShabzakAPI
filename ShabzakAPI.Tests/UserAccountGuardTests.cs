using BL.Services;
using Translators.Models;

namespace ShabzakAPI.Tests
{
    public class UserAccountGuardTests
    {
        private static AccountGuardResult Check(
            UserRole callerRole,
            UserRole targetRole,
            AccountChange change,
            int callerId = 1,
            int targetId = 2,
            bool targetEnabled = true,
            int enabledSuperAdmins = 2) =>
            UserAccountGuard.Check(callerId, callerRole, targetId, targetRole, targetEnabled, change, enabledSuperAdmins);

        [Fact]
        public void RegularCaller_IsForbidden()
        {
            var r = Check(UserRole.Regular, UserRole.Regular, new AccountChange(NewEnabled: false));

            Assert.Equal(AccountGuardOutcome.Forbidden, r.Outcome);
        }

        [Fact]
        public void Admin_CannotSetRoles()
        {
            Assert.Equal(AccountGuardOutcome.Forbidden, Check(UserRole.Admin, UserRole.Regular, new AccountChange(NewRole: UserRole.Admin)).Outcome);
            // Even setting the current role is a role change attempt.
            Assert.Equal(AccountGuardOutcome.Forbidden, Check(UserRole.Admin, UserRole.Regular, new AccountChange(NewRole: UserRole.Regular)).Outcome);
        }

        [Theory]
        [InlineData(false, null, false, false)]
        [InlineData(null, false, false, false)]
        [InlineData(null, null, true, false)]
        [InlineData(null, null, false, true)]
        public void Admin_CannotTouchSuperAdminAccounts(bool? enabled, bool? activated, bool delete, bool passwordReset)
        {
            var r = Check(UserRole.Admin, UserRole.SuperAdmin, new AccountChange(null, enabled, activated, delete, passwordReset));

            Assert.Equal(AccountGuardOutcome.Forbidden, r.Outcome);
        }

        [Fact]
        public void Admin_CanManageRegularAndAdminAccounts()
        {
            Assert.True(Check(UserRole.Admin, UserRole.Regular, new AccountChange(NewEnabled: false)).IsAllowed);
            Assert.True(Check(UserRole.Admin, UserRole.Admin, new AccountChange(NewActivated: true)).IsAllowed);
            Assert.True(Check(UserRole.Admin, UserRole.Admin, new AccountChange(Delete: true)).IsAllowed);
            Assert.True(Check(UserRole.Admin, UserRole.Regular, new AccountChange(PasswordReset: true)).IsAllowed);
        }

        [Fact]
        public void SuperAdmin_CanChangeRoles()
        {
            Assert.True(Check(UserRole.SuperAdmin, UserRole.Regular, new AccountChange(NewRole: UserRole.Admin)).IsAllowed);
            Assert.True(Check(UserRole.SuperAdmin, UserRole.SuperAdmin, new AccountChange(NewRole: UserRole.Admin), enabledSuperAdmins: 2).IsAllowed);
        }

        [Theory]
        [InlineData(UserRole.Admin)]
        [InlineData(UserRole.SuperAdmin)]
        public void SelfChanges_AreRejected(UserRole role)
        {
            Assert.Equal(AccountGuardOutcome.BadRequest, Check(role, role, new AccountChange(Delete: true), callerId: 5, targetId: 5).Outcome);
            Assert.Equal(AccountGuardOutcome.BadRequest, Check(role, role, new AccountChange(NewEnabled: false), callerId: 5, targetId: 5).Outcome);
            Assert.Equal(AccountGuardOutcome.BadRequest, Check(role, role, new AccountChange(NewActivated: false), callerId: 5, targetId: 5).Outcome);
        }

        [Fact]
        public void SuperAdmin_CannotChangeOwnRole_ButMayRestateIt()
        {
            Assert.Equal(AccountGuardOutcome.BadRequest,
                Check(UserRole.SuperAdmin, UserRole.SuperAdmin, new AccountChange(NewRole: UserRole.Admin), callerId: 5, targetId: 5).Outcome);
            Assert.True(Check(UserRole.SuperAdmin, UserRole.SuperAdmin, new AccountChange(NewRole: UserRole.SuperAdmin), callerId: 5, targetId: 5).IsAllowed);
        }

        [Fact]
        public void Self_PasswordResetAndEnable_AreAllowed()
        {
            Assert.True(Check(UserRole.Admin, UserRole.Admin, new AccountChange(PasswordReset: true), callerId: 5, targetId: 5).IsAllowed);
            Assert.True(Check(UserRole.Admin, UserRole.Admin, new AccountChange(NewEnabled: true, NewActivated: true), callerId: 5, targetId: 5).IsAllowed);
        }

        [Theory]
        [InlineData(UserRole.Admin, null, null, false)]
        [InlineData(null, false, null, false)]
        [InlineData(null, null, false, false)]
        [InlineData(null, null, null, true)]
        public void LastEnabledSuperAdmin_IsProtected(UserRole? newRole, bool? enabled, bool? activated, bool delete)
        {
            var change = new AccountChange(newRole, enabled, activated, delete);

            var last = Check(UserRole.SuperAdmin, UserRole.SuperAdmin, change, enabledSuperAdmins: 1);
            var notLast = Check(UserRole.SuperAdmin, UserRole.SuperAdmin, change, enabledSuperAdmins: 2);

            Assert.Equal(AccountGuardOutcome.Conflict, last.Outcome);
            Assert.True(notLast.IsAllowed);
        }

        [Fact]
        public void DisabledSuperAdmin_CanBeDeleted_WhenAnotherIsEnabled()
        {
            var r = Check(UserRole.SuperAdmin, UserRole.SuperAdmin, new AccountChange(Delete: true), targetEnabled: false, enabledSuperAdmins: 1);

            Assert.True(r.IsAllowed);
        }

        // CheckDeleteAll: the login accounts deleted with a soldier (Soldiers/DeleteSoldier).

        [Fact]
        public void DeleteAll_NoAccounts_IsAllowed()
        {
            Assert.True(UserAccountGuard.CheckDeleteAll(1, UserRole.Admin, [], enabledSuperAdminCount: 1).IsAllowed);
        }

        [Fact]
        public void DeleteAll_AdminMayDeleteRegularAndAdminAccounts()
        {
            var r = UserAccountGuard.CheckDeleteAll(1, UserRole.Admin, [(2, UserRole.Regular, true), (3, UserRole.Admin, true)], enabledSuperAdminCount: 1);

            Assert.True(r.IsAllowed);
        }

        [Fact]
        public void DeleteAll_AdminCannotDeleteASuperAdminAccount()
        {
            var r = UserAccountGuard.CheckDeleteAll(1, UserRole.Admin, [(2, UserRole.SuperAdmin, true)], enabledSuperAdminCount: 2);

            Assert.Equal(AccountGuardOutcome.Forbidden, r.Outcome);
        }

        [Fact]
        public void DeleteAll_CannotDeleteOwnAccount()
        {
            var r = UserAccountGuard.CheckDeleteAll(5, UserRole.SuperAdmin, [(5, UserRole.SuperAdmin, true)], enabledSuperAdminCount: 2);

            Assert.Equal(AccountGuardOutcome.BadRequest, r.Outcome);
        }

        [Fact]
        public void DeleteAll_KeepsTheLastEnabledSuperAdmin_CountingEarlierDeletions()
        {
            Assert.True(UserAccountGuard.CheckDeleteAll(1, UserRole.SuperAdmin, [(2, UserRole.SuperAdmin, true)], enabledSuperAdminCount: 2).IsAllowed);
            Assert.Equal(AccountGuardOutcome.Conflict,
                UserAccountGuard.CheckDeleteAll(1, UserRole.SuperAdmin, [(2, UserRole.SuperAdmin, true)], enabledSuperAdminCount: 1).Outcome);
            // Two enabled SuperAdmins in one deletion with two in total: the second would be the last one.
            Assert.Equal(AccountGuardOutcome.Conflict,
                UserAccountGuard.CheckDeleteAll(1, UserRole.SuperAdmin, [(2, UserRole.SuperAdmin, true), (3, UserRole.SuperAdmin, true)], enabledSuperAdminCount: 2).Outcome);
        }
    }
}
