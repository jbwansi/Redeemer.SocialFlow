using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceSocialConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SocialConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Platform = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalAccountId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AccountType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ConnectionStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    ConnectedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    ConnectedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastValidatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ProtectedCredential = table.Column<string>(type: "TEXT", nullable: false),
                    PublishingPermissionGranted = table.Column<bool>(type: "INTEGER", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialConnections", x => x.Id);
                    table.UniqueConstraint("AK_SocialConnections_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.ForeignKey(
                        name: "FK_SocialConnections_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SocialOAuthSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Platform = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    StateHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ProtectedData = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialOAuthSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocialOAuthSessions_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PostSocialDestinations",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SocialPostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SocialConnectionId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostSocialDestinations", x => new { x.WorkspaceId, x.SocialPostId, x.SocialConnectionId });
                    table.ForeignKey(
                        name: "FK_PostSocialDestinations_SocialConnections_WorkspaceId_SocialConnectionId",
                        columns: x => new { x.WorkspaceId, x.SocialConnectionId },
                        principalTable: "SocialConnections",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PostSocialDestinations_SocialPosts_WorkspaceId_SocialPostId",
                        columns: x => new { x.WorkspaceId, x.SocialPostId },
                        principalTable: "SocialPosts",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PostSocialDestinations_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PostSocialDestinations_WorkspaceId",
                table: "PostSocialDestinations",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_PostSocialDestinations_WorkspaceId_SocialConnectionId",
                table: "PostSocialDestinations",
                columns: new[] { "WorkspaceId", "SocialConnectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SocialConnections_WorkspaceId",
                table: "SocialConnections",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialConnections_WorkspaceId_Platform_ExternalAccountId",
                table: "SocialConnections",
                columns: new[] { "WorkspaceId", "Platform", "ExternalAccountId" },
                unique: true,
                filter: "ConnectionStatus <> 3");

            migrationBuilder.CreateIndex(
                name: "IX_SocialOAuthSessions_StateHash",
                table: "SocialOAuthSessions",
                column: "StateHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SocialOAuthSessions_WorkspaceId",
                table: "SocialOAuthSessions",
                column: "WorkspaceId");
            foreach (var table in new[] { "SocialConnections", "SocialOAuthSessions", "PostSocialDestinations" })
                migrationBuilder.Sql($"""
                    CREATE TRIGGER "{table}_WorkspaceImmutable" BEFORE UPDATE OF WorkspaceId ON "{table}"
                    WHEN NEW.WorkspaceId <> OLD.WorkspaceId
                    BEGIN SELECT RAISE(ABORT, 'Workspace ownership is immutable'); END;
                    """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Restore a verified backup to preserve social connection history.");
        }
    }
}
