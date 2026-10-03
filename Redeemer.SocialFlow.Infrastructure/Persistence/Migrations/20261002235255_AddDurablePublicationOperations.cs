using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurablePublicationOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PublicationOperations",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SocialPostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    Destination = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    Platform = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ExternalId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicationOperations", x => x.OperationId);
                    table.ForeignKey(
                        name: "FK_PublicationOperations_SocialPosts_SocialPostId",
                        column: x => x.SocialPostId,
                        principalTable: "SocialPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PublicationOperations_SocialPostId",
                table: "PublicationOperations",
                column: "SocialPostId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PublicationOperations");
        }
    }
}
