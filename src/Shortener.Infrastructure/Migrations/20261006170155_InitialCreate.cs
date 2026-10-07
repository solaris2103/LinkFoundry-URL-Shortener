using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shortener.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "Links" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Links" PRIMARY KEY AUTOINCREMENT,
                    "Code" TEXT NOT NULL CONSTRAINT "AK_Links_Code" UNIQUE,
                    "DestinationUrl" TEXT NOT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    "ExpiresAt" TEXT NULL,
                    "IsActive" INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS "Clicks" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Clicks" PRIMARY KEY AUTOINCREMENT,
                    "Code" TEXT NOT NULL,
                    "OccurredAt" TEXT NOT NULL,
                    "ReferrerHost" TEXT NULL,
                    CONSTRAINT "FK_Clicks_Links_Code" FOREIGN KEY ("Code") REFERENCES "Links" ("Code") ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS "IX_Clicks_Code_OccurredAt" ON "Clicks" ("Code", "OccurredAt");
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Links_Code" ON "Links" ("Code");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Clicks");

            migrationBuilder.DropTable(
                name: "Links");
        }
    }
}
