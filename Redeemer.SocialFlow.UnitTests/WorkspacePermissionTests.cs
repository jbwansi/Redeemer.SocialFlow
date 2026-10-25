using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Exceptions;
using Xunit;
namespace Redeemer.SocialFlow.UnitTests;
public sealed class WorkspacePermissionTests
{
    [Theory]
    [InlineData(WorkspaceRole.Owner, true, true)]
    [InlineData(WorkspaceRole.Admin, true, true)]
    [InlineData(WorkspaceRole.Editor, true, false)]
    [InlineData(WorkspaceRole.Viewer, false, false)]
    public void RoleMatrix(WorkspaceRole role, bool editorial, bool team)
    {
        Assert.True(WorkspacePermissions.Allows(role, WorkspacePermission.Read));
        Assert.Equal(editorial, WorkspacePermissions.Allows(role, WorkspacePermission.Edit));
        Assert.Equal(editorial, WorkspacePermissions.Allows(role, WorkspacePermission.Approve));
        Assert.Equal(team, WorkspacePermissions.Allows(role, WorkspacePermission.ManageTeam));
    }
    [Fact]
    public void AdminCannotGrantOrChangeOwnership_AndUnknownRolesFailClosed()
    {
        Assert.False(WorkspacePermissions.CanManage(WorkspaceRole.Admin, WorkspaceRole.Admin, WorkspaceRole.Owner));
        Assert.False(WorkspacePermissions.CanManage(WorkspaceRole.Admin, WorkspaceRole.Owner, WorkspaceRole.Editor));
        Assert.True(WorkspacePermissions.CanManage(WorkspaceRole.Admin, WorkspaceRole.Editor, WorkspaceRole.Admin));
        Assert.False(WorkspacePermissions.Allows((WorkspaceRole)99, WorkspacePermission.Read));
        Assert.False(WorkspacePermissions.CanManage(WorkspaceRole.Owner, WorkspaceRole.Editor, (WorkspaceRole)99));
    }
    [Fact]
    public void LastOwnerCannotLeaveOrBeDemoted_AndChangesRefreshRevision()
    {
        var member = WorkspaceMembership.Join(Guid.NewGuid(), "owner");
        Assert.Throws<DomainException>(() => member.Change(WorkspaceRole.Editor, MembershipStatus.Active, 1));
        Assert.Throws<DomainException>(() => member.Change(WorkspaceRole.Owner, MembershipStatus.Inactive, 1));
        var revision = member.Revision;
        member.Change(WorkspaceRole.Viewer, MembershipStatus.Active, 2);
        Assert.NotEqual(revision, member.Revision);
    }
}
