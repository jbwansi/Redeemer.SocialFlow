using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceEditorialAngles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EditorialAngleJson",
                table: "AiGenerations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkspaceEditorialAngles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Objective = table.Column<string>(type: "TEXT", nullable: false),
                    Instructions = table.Column<string>(type: "TEXT", nullable: false),
                    CallToAction = table.Column<string>(type: "TEXT", nullable: false),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceEditorialAngles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceEditorialAngles_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceEditorialAngles_WorkspaceId",
                table: "WorkspaceEditorialAngles",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceEditorialAngles_WorkspaceId_Active_DisplayOrder",
                table: "WorkspaceEditorialAngles",
                columns: new[] { "WorkspaceId", "Active", "DisplayOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkspaceEditorialAngles");

            migrationBuilder.DropColumn(
                name: "EditorialAngleJson",
                table: "AiGenerations");
        }
    }
}
