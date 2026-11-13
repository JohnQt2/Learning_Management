using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMS.Core.Infrastructure.Persistence.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class QuizRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "Quizzes",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Quizzes");
        }
    }
}
