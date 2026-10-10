using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPostVisuals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SelectedVisualId",
                table: "SocialPosts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PostVisuals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SocialPostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Origin = table.Column<int>(type: "INTEGER", nullable: false),
                    StorageKey = table.Column<string>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    ByteLength = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ValidatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    IsReady = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PurgedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    GenerationMetadataJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostVisuals", x => x.Id);
                    table.UniqueConstraint("AK_PostVisuals_SocialPostId_Id", x => new { x.SocialPostId, x.Id });
                    table.ForeignKey(
                        name: "FK_PostVisuals_SocialPosts_SocialPostId",
                        column: x => x.SocialPostId,
                        principalTable: "SocialPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SocialPosts_Id_SelectedVisualId",
                table: "SocialPosts",
                columns: new[] { "Id", "SelectedVisualId" });

            migrationBuilder.CreateIndex(
                name: "IX_PostVisuals_StorageKey",
                table: "PostVisuals",
                column: "StorageKey",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SocialPosts_PostVisuals_Id_SelectedVisualId",
                table: "SocialPosts",
                columns: new[] { "Id", "SelectedVisualId" },
                principalTable: "PostVisuals",
                principalColumns: new[] { "SocialPostId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SocialPosts_PostVisuals_Id_SelectedVisualId",
                table: "SocialPosts");

            migrationBuilder.DropTable(
                name: "PostVisuals");

            migrationBuilder.DropIndex(
                name: "IX_SocialPosts_Id_SelectedVisualId",
                table: "SocialPosts");

            migrationBuilder.DropColumn(
                name: "SelectedVisualId",
                table: "SocialPosts");
        }
    }
}
