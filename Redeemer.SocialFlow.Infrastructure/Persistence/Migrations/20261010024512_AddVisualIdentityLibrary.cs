using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVisualIdentityLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SourceIdentityId",
                table: "PostVisuals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IdentityLogos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdentityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StorageKey = table.Column<string>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    ByteLength = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    IsReady = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityLogos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VisualIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdentityId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Slogan = table.Column<string>(type: "TEXT", nullable: true),
                    Style = table.Column<string>(type: "TEXT", nullable: false),
                    Instructions = table.Column<string>(type: "TEXT", nullable: true),
                    ColorsJson = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    LogoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisualIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisualIdentities_IdentityLogos_LogoId",
                        column: x => x.LogoId,
                        principalTable: "IdentityLogos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "VisualIdentities",
                columns: new[] { "Id", "ColorsJson", "CreatedAt", "IdentityId", "Instructions", "IsActive", "IsDefault", "IsDeleted", "LogoId", "Name", "Revision", "Slogan", "Style" },
                values: new object[] { new Guid("8f116275-3b02-442f-a930-719e33b10601"), "[\"#DA2E29\",\"#D4AF37\",\"#000000\",\"#FFFFFF\"]", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "RH", null, true, true, false, null, "Redeemer Holding", 1L, "Transformer par les valeurs", "professionnel, humain, moderne et épuré" });

            migrationBuilder.CreateIndex(
                name: "IX_PostVisuals_SourceIdentityId",
                table: "PostVisuals",
                column: "SourceIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_IdentityLogos_IdentityId",
                table: "IdentityLogos",
                column: "IdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_IdentityLogos_StorageKey",
                table: "IdentityLogos",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VisualIdentities_IdentityId",
                table: "VisualIdentities",
                column: "IdentityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VisualIdentities_IsDefault",
                table: "VisualIdentities",
                column: "IsDefault",
                unique: true,
                filter: "\"IsDefault\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_VisualIdentities_LogoId",
                table: "VisualIdentities",
                column: "LogoId");

            migrationBuilder.AddForeignKey(
                name: "FK_PostVisuals_VisualIdentities_SourceIdentityId",
                table: "PostVisuals",
                column: "SourceIdentityId",
                principalTable: "VisualIdentities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_IdentityLogos_VisualIdentities_IdentityId",
                table: "IdentityLogos",
                column: "IdentityId",
                principalTable: "VisualIdentities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PostVisuals_VisualIdentities_SourceIdentityId",
                table: "PostVisuals");

            migrationBuilder.DropForeignKey(
                name: "FK_IdentityLogos_VisualIdentities_IdentityId",
                table: "IdentityLogos");

            migrationBuilder.DropTable(
                name: "VisualIdentities");

            migrationBuilder.DropTable(
                name: "IdentityLogos");

            migrationBuilder.DropIndex(
                name: "IX_PostVisuals_SourceIdentityId",
                table: "PostVisuals");

            migrationBuilder.DropColumn(
                name: "SourceIdentityId",
                table: "PostVisuals");
        }
    }
}
