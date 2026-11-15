using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateSocialConnectionIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keep the non-revoked identity (and therefore its row-bound ciphertext) when present.
            // Otherwise keep the most recently connected revoked row, with deterministic ID tie-breaking.
            // Archives are immutable migration audit tables, deliberately outside the runtime EF model.
            migrationBuilder.Sql("""
                CREATE TABLE SocialConnectionConsolidationArchive AS
                WITH Ranked AS (
                    SELECT Id, WorkspaceId,
                        FIRST_VALUE(Id) OVER (
                            PARTITION BY WorkspaceId, Platform, ExternalAccountId
                            ORDER BY CASE WHEN ConnectionStatus <> 3 THEN 0 ELSE 1 END,
                                     julianday(ConnectedAtUtc) DESC, Id
                        ) AS CanonicalConnectionId
                    FROM SocialConnections
                )
                SELECT c.*, r.CanonicalConnectionId, CURRENT_TIMESTAMP AS ArchivedAtUtc
                FROM SocialConnections c JOIN Ranked r ON c.Id = r.Id AND c.WorkspaceId = r.WorkspaceId
                WHERE c.Id <> r.CanonicalConnectionId;
                CREATE UNIQUE INDEX IX_SocialConnectionConsolidationArchive_Id ON SocialConnectionConsolidationArchive(Id);

                CREATE TABLE SocialDestinationConsolidationArchive AS
                SELECT d.*, a.CanonicalConnectionId FROM PostSocialDestinations d
                JOIN SocialConnectionConsolidationArchive a ON d.WorkspaceId = a.WorkspaceId AND d.SocialConnectionId = a.Id;
                CREATE TABLE LinkedInAutomationConsolidationArchive AS
                SELECT p.*, a.CanonicalConnectionId FROM LinkedInAutomations p
                JOIN SocialConnectionConsolidationArchive a ON p.WorkspaceId = a.WorkspaceId AND p.SocialConnectionId = a.Id;

                INSERT OR IGNORE INTO PostSocialDestinations (WorkspaceId, SocialPostId, SocialConnectionId)
                SELECT WorkspaceId, SocialPostId, CanonicalConnectionId FROM SocialDestinationConsolidationArchive;
                DELETE FROM PostSocialDestinations
                WHERE EXISTS (SELECT 1 FROM SocialConnectionConsolidationArchive a
                    WHERE a.WorkspaceId = PostSocialDestinations.WorkspaceId AND a.Id = PostSocialDestinations.SocialConnectionId);

                UPDATE LinkedInAutomations SET
                    SocialConnectionId = (SELECT a.CanonicalConnectionId FROM SocialConnectionConsolidationArchive a
                        WHERE a.WorkspaceId = LinkedInAutomations.WorkspaceId AND a.Id = LinkedInAutomations.SocialConnectionId),
                    State = 2, BlockReason = 'Connection consolidated; explicit replanning required', Revision = hex(randomblob(16))
                WHERE EXISTS (SELECT 1 FROM SocialConnectionConsolidationArchive a
                    WHERE a.WorkspaceId = LinkedInAutomations.WorkspaceId AND a.Id = LinkedInAutomations.SocialConnectionId);

                DELETE FROM SocialConnections WHERE Id IN (SELECT Id FROM SocialConnectionConsolidationArchive);
                """);
            migrationBuilder.DropIndex(
                name: "IX_SocialConnections_WorkspaceId_Platform_ExternalAccountId",
                table: "SocialConnections");

            migrationBuilder.CreateIndex(
                name: "IX_SocialConnections_WorkspaceId_Platform_ExternalAccountId",
                table: "SocialConnections",
                columns: new[] { "WorkspaceId", "Platform", "ExternalAccountId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("Identity consolidation cannot be reversed automatically. Restore a verified backup or reconcile the retained audit archives explicitly.");
        }
    }
}
