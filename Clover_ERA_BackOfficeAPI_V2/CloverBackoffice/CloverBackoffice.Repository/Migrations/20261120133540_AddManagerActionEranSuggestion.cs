using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloverBackoffice.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddManagerActionEranSuggestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EngineActionKey",
                schema: "public",
                table: "ManagerActions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EngineCycleKey",
                schema: "public",
                table: "ManagerActions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "EngineDeliveryId",
                schema: "public",
                table: "ManagerActions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EngineRung",
                schema: "public",
                table: "ManagerActions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EngineSuggestedOn",
                schema: "public",
                table: "ManagerActions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EngineSuggestionText",
                schema: "public",
                table: "ManagerActions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuggestionChoice",
                schema: "public",
                table: "ManagerActions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EngineActionKey",
                schema: "public",
                table: "ManagerActions");

            migrationBuilder.DropColumn(
                name: "EngineCycleKey",
                schema: "public",
                table: "ManagerActions");

            migrationBuilder.DropColumn(
                name: "EngineDeliveryId",
                schema: "public",
                table: "ManagerActions");

            migrationBuilder.DropColumn(
                name: "EngineRung",
                schema: "public",
                table: "ManagerActions");

            migrationBuilder.DropColumn(
                name: "EngineSuggestedOn",
                schema: "public",
                table: "ManagerActions");

            migrationBuilder.DropColumn(
                name: "EngineSuggestionText",
                schema: "public",
                table: "ManagerActions");

            migrationBuilder.DropColumn(
                name: "SuggestionChoice",
                schema: "public",
                table: "ManagerActions");
        }
    }
}
