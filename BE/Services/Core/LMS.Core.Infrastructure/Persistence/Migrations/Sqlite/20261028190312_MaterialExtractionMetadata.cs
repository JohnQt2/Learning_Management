using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Core.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class MaterialExtractionMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "LearningMaterials",
                type: "TEXT",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExtractionMessage",
                table: "LearningMaterials",
                type: "TEXT",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExtractionStatus",
                table: "LearningMaterials",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "Legacy");

            migrationBuilder.AddColumn<string>(
                name: "FileFormat",
                table: "LearningMaterials",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "LearningMaterials",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "SizeBytes",
                table: "LearningMaterials",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UploadedAt",
                table: "LearningMaterials",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UploadedById",
                table: "LearningMaterials",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LearningMaterials_UploadedById",
                table: "LearningMaterials",
                column: "UploadedById");

            migrationBuilder.AddForeignKey(
                name: "FK_LearningMaterials_Users_UploadedById",
                table: "LearningMaterials",
                column: "UploadedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LearningMaterials_Users_UploadedById",
                table: "LearningMaterials");

            migrationBuilder.DropIndex(
                name: "IX_LearningMaterials_UploadedById",
                table: "LearningMaterials");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "LearningMaterials");

            migrationBuilder.DropColumn(
                name: "ExtractionMessage",
                table: "LearningMaterials");

            migrationBuilder.DropColumn(
                name: "ExtractionStatus",
                table: "LearningMaterials");

            migrationBuilder.DropColumn(
                name: "FileFormat",
                table: "LearningMaterials");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "LearningMaterials");

            migrationBuilder.DropColumn(
                name: "SizeBytes",
                table: "LearningMaterials");

            migrationBuilder.DropColumn(
                name: "UploadedAt",
                table: "LearningMaterials");

            migrationBuilder.DropColumn(
                name: "UploadedById",
                table: "LearningMaterials");
        }
    }
}
