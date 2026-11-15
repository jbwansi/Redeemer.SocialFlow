using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Persistence;

public sealed class SocialConnectionConsolidationTests
{
    [Theory]
    [InlineData(SocialPlatform.Facebook)] [InlineData(SocialPlatform.LinkedIn)] [InlineData(SocialPlatform.Instagram)]
    public async Task MigrationArchivesAndRebindsDuplicatesWithoutChangingCurrentGrantOrPublicationHistory(SocialPlatform platform)
    {
        await using var sqlite=new SqliteConnection("Data Source=:memory:"); await sqlite.OpenAsync();
        var options=new DbContextOptionsBuilder<SocialFlowDbContext>().UseSqlite(sqlite).Options;
        var workspace=Workspace.Create("First",WorkspaceType.Organization,"owner");
        var second=Workspace.Create("Second",WorkspaceType.Organization,"owner");
        await using var db=new SocialFlowDbContext(options,new ExplicitWorkspaceContext(workspace.Id));
        await db.GetService<IMigrator>().MigrateAsync("20261103144907_AddLinkedInAutomation");
        db.Workspaces.AddRange(workspace,second); await db.SaveChangesAsync();
        SocialConnection Row(string external,SocialConnectionStatus status,int age)=>new(){WorkspaceId=workspace.Id,Platform=platform,ExternalAccountId=external,DisplayName="Same name",AccountType="Account",ConnectionStatus=status,ConnectedAtUtc=DateTimeOffset.UtcNow.AddDays(-age),ProtectedCredential=status==SocialConnectionStatus.Revoked?"":"unchanged-row-bound-ciphertext"};
        var old=Row("stable",SocialConnectionStatus.Revoked,10);
        var current=Row("stable",SocialConnectionStatus.Connected,5);
        var other=Row("different",SocialConnectionStatus.Connected,1);
        var revokedOld=Row("revoked-only",SocialConnectionStatus.Revoked,9);
        var revokedNewest=Row("revoked-only",SocialConnectionStatus.Revoked,1);
        // Seed the historical schema without using properties introduced by later migrations.
        async Task InsertLegacy(SocialConnection c) => await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO SocialConnections (Id,WorkspaceId,Platform,ExternalAccountId,DisplayName,AccountType,ConnectionStatus,ConnectedByUserId,ConnectedAtUtc,ProtectedCredential,PublishingPermissionGranted,Revision) VALUES ({c.Id},{c.WorkspaceId},{(int)c.Platform},{c.ExternalAccountId},{c.DisplayName},{c.AccountType},{(int)c.ConnectionStatus},{c.ConnectedByUserId},{c.ConnectedAtUtc},{c.ProtectedCredential},{c.PublishingPermissionGranted},{c.Revision})");
        foreach (var row in new[] {old,current,other,revokedOld,revokedNewest}) await InsertLegacy(row);
        var post=SocialPost.Create("Preserve","Content",platform); db.SocialPosts.Add(post); await db.SaveChangesAsync();
        db.PostSocialDestinations.AddRange(new PostSocialDestination{WorkspaceId=workspace.Id,SocialPostId=post.Id,SocialConnectionId=old.Id},new PostSocialDestination{WorkspaceId=workspace.Id,SocialPostId=post.Id,SocialConnectionId=current.Id});
        db.LinkedInAutomations.Add(new(){WorkspaceId=workspace.Id,SocialPostId=post.Id,SocialConnectionId=old.Id,State=LinkedInAutomationState.Authorized,AuthorizedByUserId="owner"});
        await db.SaveChangesAsync();
        var operation=Guid.NewGuid(); var now=DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO PublicationOperations (OperationId, WorkspaceId, SocialPostId, Provider, Destination, Content, Platform, State, ClaimedAt, UpdatedAt) VALUES ({operation},{workspace.Id},{post.Id},'Simulated','stable','Historical content',{(int)platform},1,{now},{now})");
        await using(var foreign=new SocialFlowDbContext(options,new ExplicitWorkspaceContext(second.Id))) {
            var row=Row("stable",SocialConnectionStatus.Connected,1); row.WorkspaceId=second.Id; await InsertLegacy(row);
        }
        db.ChangeTracker.Clear();
        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        var rows=await db.SocialConnections.ToListAsync();
        Assert.Equal(3,rows.Count); Assert.Contains(rows,x=>x.Id==current.Id && x.ProtectedCredential==current.ProtectedCredential);
        Assert.Contains(rows,x=>x.Id==revokedNewest.Id && x.ConnectionStatus==SocialConnectionStatus.Revoked);
        Assert.Equal(current.Id,(await db.PostSocialDestinations.SingleAsync()).SocialConnectionId);
        var plan=await db.LinkedInAutomations.SingleAsync(); Assert.Equal(current.Id,plan.SocialConnectionId); Assert.Equal(LinkedInAutomationState.Blocked,plan.State);
        var history=await db.PublicationOperations.SingleAsync(); Assert.Equal(operation,history.OperationId); Assert.Equal("Historical content",history.Content); Assert.Equal("stable",history.Destination);
        await using(var foreign=new SocialFlowDbContext(options,new ExplicitWorkspaceContext(second.Id))) Assert.Single(await foreign.SocialConnections.ToListAsync());
        async Task<long> Count(string table) { using var command=sqlite.CreateCommand(); command.CommandText="SELECT COUNT(*) FROM "+table; return (long)(await command.ExecuteScalarAsync())!; }
        Assert.Equal(2,await Count("SocialConnectionConsolidationArchive"));
        Assert.Equal(1,await Count("SocialDestinationConsolidationArchive"));
        Assert.Equal(1,await Count("LinkedInAutomationConsolidationArchive"));
        using(var check=sqlite.CreateCommand()) { check.CommandText="PRAGMA foreign_key_check"; using var reader=await check.ExecuteReaderAsync(); Assert.False(await reader.ReadAsync()); }
        db.SocialConnections.Add(Row("stable",SocialConnectionStatus.Revoked,0));
        await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
    }
}
