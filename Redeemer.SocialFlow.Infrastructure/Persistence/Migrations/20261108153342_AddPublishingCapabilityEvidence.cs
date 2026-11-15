using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishingCapabilityEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishingCapabilityCheckedAtUtc",
                table: "SocialConnections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublishingCapabilityReason",
                table: "SocialConnections",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublishingCapabilitySource",
                table: "SocialConnections",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PublishingCapabilityStatus",
                table: "SocialConnections",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublishingCapabilityTask",
                table: "SocialConnections",
                type: "TEXT",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublishingCapabilityCheckedAtUtc",
                table: "SocialConnections");

            migrationBuilder.DropColumn(
                name: "PublishingCapabilityReason",
                table: "SocialConnections");

            migrationBuilder.DropColumn(
                name: "PublishingCapabilitySource",
                table: "SocialConnections");

            migrationBuilder.DropColumn(
                name: "PublishingCapabilityStatus",
                table: "SocialConnections");

            migrationBuilder.DropColumn(
                name: "PublishingCapabilityTask",
                table: "SocialConnections");
        }
    }
}
