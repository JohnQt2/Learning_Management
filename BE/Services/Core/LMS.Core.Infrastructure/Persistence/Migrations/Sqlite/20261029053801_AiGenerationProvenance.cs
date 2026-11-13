using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Core.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AiGenerationProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Difficulty",
                table: "Questions",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceChunkIndex",
                table: "Questions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceEndCharacter",
                table: "Questions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceStartCharacter",
                table: "Questions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ChunkCount",
                table: "AIGenerationLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DurationMilliseconds",
                table: "AIGenerationLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GeneratedQuestionCount",
                table: "AIGenerationLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProviderRequestCount",
                table: "AIGenerationLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestedQuestionCount",
                table: "AIGenerationLogs",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Difficulty",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "SourceChunkIndex",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "SourceEndCharacter",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "SourceStartCharacter",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "ChunkCount",
                table: "AIGenerationLogs");

            migrationBuilder.DropColumn(
                name: "DurationMilliseconds",
                table: "AIGenerationLogs");

            migrationBuilder.DropColumn(
                name: "GeneratedQuestionCount",
                table: "AIGenerationLogs");

            migrationBuilder.DropColumn(
                name: "ProviderRequestCount",
                table: "AIGenerationLogs");

            migrationBuilder.DropColumn(
                name: "RequestedQuestionCount",
                table: "AIGenerationLogs");
        }
    }
}
