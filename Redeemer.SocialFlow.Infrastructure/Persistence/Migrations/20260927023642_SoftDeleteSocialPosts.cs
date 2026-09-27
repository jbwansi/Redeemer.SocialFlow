using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SoftDeleteSocialPosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiGenerations_SocialPosts_SocialPostId",
                table: "AiGenerations");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "SocialPosts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "SocialPosts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddForeignKey(
                name: "FK_AiGenerations_SocialPosts_SocialPostId",
                table: "AiGenerations",
                column: "SocialPostId",
                principalTable: "SocialPosts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiGenerations_SocialPosts_SocialPostId",
                table: "AiGenerations");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "SocialPosts");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "SocialPosts");

            migrationBuilder.AddForeignKey(
                name: "FK_AiGenerations_SocialPosts_SocialPostId",
                table: "AiGenerations",
                column: "SocialPostId",
                principalTable: "SocialPosts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
