using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VisualIdentityApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdentityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    ApprovedBy = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisualIdentityApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisualIdentityApprovals_VisualIdentities_WorkspaceId_IdentityId",
                        columns: x => new { x.WorkspaceId, x.IdentityId },
                        principalTable: "VisualIdentities",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VisualIdentityApprovals_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceEditorialProfiles",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    OrganizationName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Mission = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Values = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Positioning = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Audiences = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Tone = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Themes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Rules = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Avoid = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceEditorialProfiles", x => x.WorkspaceId);
                    table.ForeignKey(
                        name: "FK_WorkspaceEditorialProfiles_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VisualIdentityApprovals_WorkspaceId",
                table: "VisualIdentityApprovals",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_VisualIdentityApprovals_WorkspaceId_IdentityId_Revision",
                table: "VisualIdentityApprovals",
                columns: new[] { "WorkspaceId", "IdentityId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceEditorialProfiles_WorkspaceId",
                table: "WorkspaceEditorialProfiles",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VisualIdentityApprovals");

            migrationBuilder.DropTable(
                name: "WorkspaceEditorialProfiles");
        }
    }
}
