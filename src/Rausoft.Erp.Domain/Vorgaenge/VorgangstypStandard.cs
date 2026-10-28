namespace Rausoft.Erp.Domain.Vorgaenge;

/// <summary>
/// Die vier Vorgangstypen aus der Entscheidung vom 28.10.2026 als Startpunkt.
/// Werden auf Knopfdruck angelegt, solange es noch keinen Vorgangstyp gibt; danach
/// ist alles Vorlagenpflege in der Oberfläche. Bereiche werden über den Namen
/// zugeordnet (fehlende legt der Aufrufer an).
/// </summary>
public static class VorgangstypStandard
{
    public sealed record Schritt(string Titel, string Bereich, int FristTage, int? WartetAufNr = null,
        string? Bedingung = null, Benachrichtigung Benachrichtigung = Benachrichtigung.Keine,
        string? Pflichtfeld = null, AutoAbhaken AutoAbhaken = AutoAbhaken.Nie);

    public sealed record Typ(string Name, string Beschreibung, IReadOnlyList<Schritt> Schritte);

    public const string Vertrieb = "Vertrieb";
    public const string Technik = "Technik";
    public const string Buchhaltung = "Buchhaltung";
    public const string Support = "Support";

    public static readonly IReadOnlyList<Typ> Alle =
    [
        new("Neue Bestellung", "Neukunde: Mandant, Vertrag, Rechnung, Zugang, Schulung",
        [
            new("Mandant in der Shared Cloud anlegen", Technik, 3, Benachrichtigung: Benachrichtigung.Mail, Pflichtfeld: "Mandanten-ID"),
            new("Vertrag an Kunden senden", Vertrieb, 2),
            new("Unterschriebenen Vertrag erfassen", Vertrieb, 14, WartetAufNr: 2, AutoAbhaken: AutoAbhaken.VertragUnterschrieben),
            new("Rechnung in Odoo stellen", Buchhaltung, 3, Benachrichtigung: Benachrichtigung.Mail, AutoAbhaken: AutoAbhaken.OdooRechnung),
            new("Zugangsdaten an Kunden senden", Technik, 2, WartetAufNr: 1, Benachrichtigung: Benachrichtigung.Teams),
            new("Schulungstermin vereinbaren", Support, 7, Bedingung: "Schulung bestellt", Benachrichtigung: Benachrichtigung.Mail),
        ]),
        new("Upgrade / Upsell", "Bestandskunde: Freischaltung, Nachtrag, Rechnung",
        [
            new("Module und Lizenzen in der Shared Cloud freischalten", Technik, 3, Benachrichtigung: Benachrichtigung.Mail),
            new("Vertragsnachtrag an Kunden senden", Vertrieb, 2, Bedingung: "Vertragsnachtrag nötig"),
            new("Rechnung in Odoo stellen", Buchhaltung, 3, Benachrichtigung: Benachrichtigung.Mail, AutoAbhaken: AutoAbhaken.OdooRechnung),
            new("Kunden über die Freischaltung informieren", Vertrieb, 1, WartetAufNr: 1),
            new("Schulungstermin vereinbaren", Support, 7, Bedingung: "Schulung bestellt", Benachrichtigung: Benachrichtigung.Mail),
        ]),
        new("Stundenauftrag", "Projekt ohne Software, z. B. Einrichtung oder Beratung",
        [
            new("Termin mit Kunden abstimmen", Technik, 5, Benachrichtigung: Benachrichtigung.Mail),
            new("Leistung erbringen und Stunden erfassen", Technik, 20, WartetAufNr: 1),
            new("Rechnung in Odoo stellen", Buchhaltung, 3, WartetAufNr: 2, Benachrichtigung: Benachrichtigung.Mail, AutoAbhaken: AutoAbhaken.OdooRechnung),
        ]),
        new("Verkauf ohne Zwischenschritte", "Nur Rechnung, z. B. Zusatzlizenz",
        [
            new("Rechnung in Odoo stellen", Buchhaltung, 3, Benachrichtigung: Benachrichtigung.Mail, AutoAbhaken: AutoAbhaken.OdooRechnung),
        ]),
    ];
}
