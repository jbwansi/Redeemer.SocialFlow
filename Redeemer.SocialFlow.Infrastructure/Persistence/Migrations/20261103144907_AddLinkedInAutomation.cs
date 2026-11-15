using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkedInAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LinkedInAutomations",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SocialPostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SocialConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorizedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    AuthorizedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ScheduledAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PostUpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    BlockReason = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkedInAutomations", x => new { x.WorkspaceId, x.SocialPostId });
                    table.ForeignKey(
                        name: "FK_LinkedInAutomations_SocialConnections_WorkspaceId_SocialConnectionId",
                        columns: x => new { x.WorkspaceId, x.SocialConnectionId },
                        principalTable: "SocialConnections",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LinkedInAutomations_SocialPosts_WorkspaceId_SocialPostId",
                        columns: x => new { x.WorkspaceId, x.SocialPostId },
                        principalTable: "SocialPosts",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LinkedInAutomations_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LinkedInAutomations_WorkspaceId",
                table: "LinkedInAutomations",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_LinkedInAutomations_WorkspaceId_SocialConnectionId",
                table: "LinkedInAutomations",
                columns: new[] { "WorkspaceId", "SocialConnectionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LinkedInAutomations");
        }
    }
}
