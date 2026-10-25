using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceWorkspaceInvariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TEMP TABLE WorkspaceForeignKeyValidation (Violations INTEGER CHECK (Violations = 0));
                INSERT INTO WorkspaceForeignKeyValidation SELECT count(*) FROM pragma_foreign_key_check;
                DROP TABLE WorkspaceForeignKeyValidation;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER Workspace_LastOwner_Delete BEFORE DELETE ON WorkspaceMemberships
                WHEN OLD.Role = 1 AND OLD.Status = 1 AND NOT EXISTS (
                  SELECT 1 FROM WorkspaceMemberships WHERE WorkspaceId = OLD.WorkspaceId
                  AND UserId <> OLD.UserId AND Role = 1 AND Status = 1)
                BEGIN SELECT RAISE(ABORT, 'The last active owner cannot be removed'); END;
                CREATE TRIGGER Workspace_LastOwner_Update BEFORE UPDATE ON WorkspaceMemberships
                WHEN OLD.Role = 1 AND OLD.Status = 1 AND (NEW.Role <> 1 OR NEW.Status <> 1 OR NEW.WorkspaceId <> OLD.WorkspaceId)
                  AND NOT EXISTS (SELECT 1 FROM WorkspaceMemberships WHERE WorkspaceId = OLD.WorkspaceId
                    AND UserId <> OLD.UserId AND Role = 1 AND Status = 1)
                BEGIN SELECT RAISE(ABORT, 'The last active owner cannot be removed'); END;
                """);
            foreach (var table in new[] { "SocialPosts", "AiGenerations", "KnowledgeDocuments", "KnowledgeChunks", "PublicationOperations", "PostVisuals", "VisualIdentities", "IdentityLogos", "WorkspaceMemberships" })
                migrationBuilder.Sql($"""
                    CREATE TRIGGER "{table}_WorkspaceImmutable" BEFORE UPDATE OF WorkspaceId ON "{table}"
                    WHEN NEW.WorkspaceId <> OLD.WorkspaceId
                    BEGIN SELECT RAISE(ABORT, 'Workspace ownership is immutable'); END;
                    """);
            foreach (var operation in new[] { "INSERT", "UPDATE" })
                migrationBuilder.Sql($"""
                    CREATE TRIGGER "PublicationSnapshotWorkspace_{operation}" BEFORE {operation} ON PublicationOperations
                    WHEN NEW.Visual IS NOT NULL AND json_type(NEW.Visual) <> 'null' AND NOT EXISTS (
                        SELECT 1 FROM PostVisuals v WHERE v.WorkspaceId = NEW.WorkspaceId
                        AND v.SocialPostId = NEW.SocialPostId AND lower(v.Id) = lower(json_extract(NEW.Visual, '$.VisualId')))
                    BEGIN SELECT RAISE(ABORT, 'Publication snapshot must belong to the same workspace and post'); END;
                    """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Workspace protection cannot be removed. Restore a verified backup instead.");

        }
    }
}
