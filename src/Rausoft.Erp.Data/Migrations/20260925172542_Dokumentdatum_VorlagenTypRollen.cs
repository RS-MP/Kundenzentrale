using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rausoft.Erp.Data.Migrations
{
    /// <inheritdoc />
    public partial class Dokumentdatum_VorlagenTypRollen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "GueltigAb",
                table: "VorlagenVersionen",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<int>(
                name: "ErlaubteRollen",
                table: "Vorlagen",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Typ",
                table: "Vorlagen",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "Dokumentdatum",
                table: "Vertragsbestandteile",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DokumentdatumQuelle",
                table: "Vertragsbestandteile",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Folge",
                table: "Vertragsbestandteile",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Bestehende Versionen galten ab Freigabe
            migrationBuilder.Sql("UPDATE VorlagenVersionen SET GueltigAb = CAST(FreigegebenAm AS date)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GueltigAb",
                table: "VorlagenVersionen");

            migrationBuilder.DropColumn(
                name: "ErlaubteRollen",
                table: "Vorlagen");

            migrationBuilder.DropColumn(
                name: "Typ",
                table: "Vorlagen");

            migrationBuilder.DropColumn(
                name: "Dokumentdatum",
                table: "Vertragsbestandteile");

            migrationBuilder.DropColumn(
                name: "DokumentdatumQuelle",
                table: "Vertragsbestandteile");

            migrationBuilder.DropColumn(
                name: "Folge",
                table: "Vertragsbestandteile");
        }
    }
}
