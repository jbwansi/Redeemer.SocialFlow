using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceTerminology : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TerminologyEvidenceJson",
                table: "AiGenerations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkspaceTerms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Canonical = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SearchKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AliasesJson = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    UsageContext = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    VerifiedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceTerms", x => x.Id);
                    table.UniqueConstraint("AK_WorkspaceTerms_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.ForeignKey(
                        name: "FK_WorkspaceTerms_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceTermRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TermId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceTermRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceTermRevisions_WorkspaceTerms_WorkspaceId_TermId",
                        columns: x => new { x.WorkspaceId, x.TermId },
                        principalTable: "WorkspaceTerms",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkspaceTermRevisions_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceTermRevisions_WorkspaceId",
                table: "WorkspaceTermRevisions",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceTermRevisions_WorkspaceId_TermId_Revision",
                table: "WorkspaceTermRevisions",
                columns: new[] { "WorkspaceId", "TermId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceTerms_WorkspaceId",
                table: "WorkspaceTerms",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceTerms_WorkspaceId_SearchKey_Language_UsageContext",
                table: "WorkspaceTerms",
                columns: new[] { "WorkspaceId", "SearchKey", "Language", "UsageContext" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkspaceTermRevisions");

            migrationBuilder.DropTable(
                name: "WorkspaceTerms");

            migrationBuilder.DropColumn(
                name: "TerminologyEvidenceJson",
                table: "AiGenerations");
        }
    }
}
