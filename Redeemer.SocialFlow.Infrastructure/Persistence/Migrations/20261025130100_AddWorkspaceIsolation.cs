using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Redeemer.SocialFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQLite must rebuild cyclic relationships (post/selected visual and identity/logo).
            // EF reenables FK enforcement after all rebuilds; validation follows in the next migration.
            migrationBuilder.Sql("PRAGMA foreign_keys = OFF;", suppressTransaction: true);
            migrationBuilder.DropForeignKey(
                name: "FK_AiGenerations_SocialPosts_SocialPostId",
                table: "AiGenerations");

            migrationBuilder.DropForeignKey(
                name: "FK_IdentityLogos_VisualIdentities_IdentityId",
                table: "IdentityLogos");

            migrationBuilder.DropForeignKey(
                name: "FK_KnowledgeChunks_KnowledgeDocuments_KnowledgeDocumentId",
                table: "KnowledgeChunks");

            migrationBuilder.DropForeignKey(
                name: "FK_PostVisuals_SocialPosts_SocialPostId",
                table: "PostVisuals");

            migrationBuilder.DropForeignKey(
                name: "FK_PostVisuals_VisualIdentities_SourceIdentityId",
                table: "PostVisuals");

            migrationBuilder.DropForeignKey(
                name: "FK_PublicationOperations_SocialPosts_SocialPostId",
                table: "PublicationOperations");

            migrationBuilder.DropForeignKey(
                name: "FK_SocialPosts_PostVisuals_Id_SelectedVisualId",
                table: "SocialPosts");

            migrationBuilder.DropForeignKey(
                name: "FK_VisualIdentities_IdentityLogos_LogoId",
                table: "VisualIdentities");

            migrationBuilder.DropIndex(
                name: "IX_VisualIdentities_IdentityId",
                table: "VisualIdentities");

            migrationBuilder.DropIndex(
                name: "IX_VisualIdentities_IsDefault",
                table: "VisualIdentities");

            migrationBuilder.DropIndex(
                name: "IX_VisualIdentities_LogoId",
                table: "VisualIdentities");

            migrationBuilder.DropIndex(
                name: "IX_SocialPosts_Id_SelectedVisualId",
                table: "SocialPosts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PostVisuals_SocialPostId_Id",
                table: "PostVisuals");

            migrationBuilder.DropIndex(
                name: "IX_PostVisuals_SourceIdentityId",
                table: "PostVisuals");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeChunks_KnowledgeDocumentId",
                table: "KnowledgeChunks");

            migrationBuilder.DropIndex(
                name: "IX_IdentityLogos_IdentityId",
                table: "IdentityLogos");

            migrationBuilder.DropIndex(
                name: "IX_AiGenerations_SocialPostId",
                table: "AiGenerations");

migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "VisualIdentities",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("a661b775-1b98-4c97-8ec0-2a461f0c7001"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "SocialPosts",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("a661b775-1b98-4c97-8ec0-2a461f0c7001"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "PublicationOperations",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("a661b775-1b98-4c97-8ec0-2a461f0c7001"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "PostVisuals",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("a661b775-1b98-4c97-8ec0-2a461f0c7001"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "KnowledgeDocuments",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("a661b775-1b98-4c97-8ec0-2a461f0c7001"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "KnowledgeChunks",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("a661b775-1b98-4c97-8ec0-2a461f0c7001"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "IdentityLogos",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("a661b775-1b98-4c97-8ec0-2a461f0c7001"));

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "AiGenerations",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("a661b775-1b98-4c97-8ec0-2a461f0c7001"));

            migrationBuilder.AddUniqueConstraint(
                name: "AK_VisualIdentities_WorkspaceId_Id",
                table: "VisualIdentities",
                columns: new[] { "WorkspaceId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_SocialPosts_WorkspaceId_Id",
                table: "SocialPosts",
                columns: new[] { "WorkspaceId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PostVisuals_WorkspaceId_Id",
                table: "PostVisuals",
                columns: new[] { "WorkspaceId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PostVisuals_WorkspaceId_SocialPostId_Id",
                table: "PostVisuals",
                columns: new[] { "WorkspaceId", "SocialPostId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_KnowledgeDocuments_WorkspaceId_Id",
                table: "KnowledgeDocuments",
                columns: new[] { "WorkspaceId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_IdentityLogos_WorkspaceId_IdentityId_Id",
                table: "IdentityLogos",
                columns: new[] { "WorkspaceId", "IdentityId", "Id" });

            migrationBuilder.CreateTable(
                name: "Workspaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PersonalOwnerUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspaces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceMemberships",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    JoinedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceMemberships", x => new { x.WorkspaceId, x.UserId });
                    table.ForeignKey(
                        name: "FK_WorkspaceMemberships_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Workspaces",
                columns: new[] { "Id", "CreatedAtUtc", "Name", "PersonalOwnerUserId", "Status", "Type" },
                values: new object[] { new Guid("a661b775-1b98-4c97-8ec0-2a461f0c7001"), new DateTimeOffset(new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Redeemer Holding", null, 2, 2 });

migrationBuilder.CreateIndex(
                name: "IX_VisualIdentities_WorkspaceId",
                table: "VisualIdentities",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_VisualIdentities_WorkspaceId_Id_LogoId",
                table: "VisualIdentities",
                columns: new[] { "WorkspaceId", "Id", "LogoId" });

            migrationBuilder.CreateIndex(
                name: "IX_VisualIdentities_WorkspaceId_IdentityId",
                table: "VisualIdentities",
                columns: new[] { "WorkspaceId", "IdentityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VisualIdentities_WorkspaceId_IsDefault",
                table: "VisualIdentities",
                columns: new[] { "WorkspaceId", "IsDefault" },
                unique: true,
                filter: "\"IsDefault\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPosts_WorkspaceId",
                table: "SocialPosts",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPosts_WorkspaceId_Id_SelectedVisualId",
                table: "SocialPosts",
                columns: new[] { "WorkspaceId", "Id", "SelectedVisualId" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicationOperations_WorkspaceId",
                table: "PublicationOperations",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_PublicationOperations_WorkspaceId_SocialPostId",
                table: "PublicationOperations",
                columns: new[] { "WorkspaceId", "SocialPostId" });

            migrationBuilder.CreateIndex(
                name: "IX_PostVisuals_WorkspaceId",
                table: "PostVisuals",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_PostVisuals_WorkspaceId_SourceIdentityId",
                table: "PostVisuals",
                columns: new[] { "WorkspaceId", "SourceIdentityId" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeDocuments_WorkspaceId",
                table: "KnowledgeDocuments",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeChunks_WorkspaceId",
                table: "KnowledgeChunks",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeChunks_WorkspaceId_KnowledgeDocumentId",
                table: "KnowledgeChunks",
                columns: new[] { "WorkspaceId", "KnowledgeDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_IdentityLogos_WorkspaceId",
                table: "IdentityLogos",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_AiGenerations_WorkspaceId",
                table: "AiGenerations",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_AiGenerations_WorkspaceId_SocialPostId",
                table: "AiGenerations",
                columns: new[] { "WorkspaceId", "SocialPostId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceMemberships_UserId_Status",
                table: "WorkspaceMemberships",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_PersonalOwnerUserId",
                table: "Workspaces",
                column: "PersonalOwnerUserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AiGenerations_SocialPosts_WorkspaceId_SocialPostId",
                table: "AiGenerations",
                columns: new[] { "WorkspaceId", "SocialPostId" },
                principalTable: "SocialPosts",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AiGenerations_Workspaces_WorkspaceId",
                table: "AiGenerations",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_IdentityLogos_VisualIdentities_WorkspaceId_IdentityId",
                table: "IdentityLogos",
                columns: new[] { "WorkspaceId", "IdentityId" },
                principalTable: "VisualIdentities",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_IdentityLogos_Workspaces_WorkspaceId",
                table: "IdentityLogos",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_KnowledgeChunks_KnowledgeDocuments_WorkspaceId_KnowledgeDocumentId",
                table: "KnowledgeChunks",
                columns: new[] { "WorkspaceId", "KnowledgeDocumentId" },
                principalTable: "KnowledgeDocuments",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_KnowledgeChunks_Workspaces_WorkspaceId",
                table: "KnowledgeChunks",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_KnowledgeDocuments_Workspaces_WorkspaceId",
                table: "KnowledgeDocuments",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PostVisuals_SocialPosts_WorkspaceId_SocialPostId",
                table: "PostVisuals",
                columns: new[] { "WorkspaceId", "SocialPostId" },
                principalTable: "SocialPosts",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PostVisuals_VisualIdentities_WorkspaceId_SourceIdentityId",
                table: "PostVisuals",
                columns: new[] { "WorkspaceId", "SourceIdentityId" },
                principalTable: "VisualIdentities",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PostVisuals_Workspaces_WorkspaceId",
                table: "PostVisuals",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PublicationOperations_SocialPosts_WorkspaceId_SocialPostId",
                table: "PublicationOperations",
                columns: new[] { "WorkspaceId", "SocialPostId" },
                principalTable: "SocialPosts",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PublicationOperations_Workspaces_WorkspaceId",
                table: "PublicationOperations",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SocialPosts_PostVisuals_WorkspaceId_Id_SelectedVisualId",
                table: "SocialPosts",
                columns: new[] { "WorkspaceId", "Id", "SelectedVisualId" },
                principalTable: "PostVisuals",
                principalColumns: new[] { "WorkspaceId", "SocialPostId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SocialPosts_Workspaces_WorkspaceId",
                table: "SocialPosts",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VisualIdentities_IdentityLogos_WorkspaceId_Id_LogoId",
                table: "VisualIdentities",
                columns: new[] { "WorkspaceId", "Id", "LogoId" },
                principalTable: "IdentityLogos",
                principalColumns: new[] { "WorkspaceId", "IdentityId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VisualIdentities_Workspaces_WorkspaceId",
                table: "VisualIdentities",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Workspace isolation cannot be removed safely. Restore a verified pre-migration backup instead.");
    }
}
