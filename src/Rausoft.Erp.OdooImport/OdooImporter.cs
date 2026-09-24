using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Customers;

namespace Rausoft.Erp.OdooImport;

/// <summary>
/// Importiert Kunden, Ansprechpartner, Adressen und Partner aus Odoo (res.partner).
/// Idempotent: Datensätze werden über die OdooId wiedererkannt und aktualisiert,
/// der Import kann während des Parallelbetriebs beliebig oft laufen.
///
/// Zuordnungsregeln (abgestimmt 2026-08-13):
/// - Kundennummer: x_studio_kundenr ist maßgeblich; ohne KundenNr gilt die Odoo-ID
///   (bei Kollision mit einer echten KundenNr mit Präfix "O").
/// - Namen: x_studio_vorname/x_studio_nachname; Fallback: letztes Wort des
///   Namensfelds = Nachname, Rest = Vorname.
/// - Anrede: x_studio_anrede (Mr./Ms.), Fallback x_title_custom (Mister/Madam/Frau).
/// - Betriebsart: x_studio_hosting (Shared Cloud/Private Cloud/OnPrem).
/// - Partner: x_studio_vertriebspartner; CSB = Tipp-Provision 15 %,
///   Finesolutions = Rechnungsempfänger, "Finesolutions-Direktkunde" = nur vermittelt.
/// - Rechnungs-E-Mail (x_studio_e_mail_rechnungen): Rolle Rechnungsempfang am
///   passenden Ansprechpartner, sonst eigener Sammel-Ansprechpartner.
/// </summary>
public class OdooImporter(OdooClient odoo, bool dryRun, string? verbindung = null)
{
    private const int BatchGroesse = 1000;

    private readonly Dictionary<int, string> _laender = [];
    private readonly Statistik _stat = new();

    public async Task AusfuehrenAsync()
    {
        Console.WriteLine(dryRun
            ? "=== ODOO-IMPORT (DRY-RUN — es wird nichts gespeichert) ==="
            : "=== ODOO-IMPORT ===");

        var verbindungsZeichenfolge = verbindung
            ?? Environment.GetEnvironmentVariable("KUNDENZENTRALE_DB")
            ?? @"Server=(localdb)\MSSQLLocalDB;Database=Kundenzentrale;Trusted_Connection=True;Encrypt=False";
        var options = new DbContextOptionsBuilder<ErpDbContext>().UseSqlServer(verbindungsZeichenfolge).Options;
        await using var db = new ErpDbContext(options);

        await LadeLaenderAsync();
        var firmen = await LadeAlleAsync(
            [new object[] { "is_company", "=", true }],
            ["id", "name", "active", "x_studio_kundenr", "email", "phone", "street", "street2", "zip", "city",
             "country_id", "vat", "lang", "x_studio_hosting", "x_studio_vertriebspartner",
             "x_studio_e_mail_rechnungen", "x_studio_blocked", "x_studio_wartung",
             "x_studio_hauptansprechpartner", "x_studio_technischer_ansprechpartner"]);
        var personen = await LadeAlleAsync(
            [new object[] { "is_company", "=", false }],
            ["id", "name", "active", "parent_id", "type", "function", "email", "phone",
             "street", "street2", "zip", "city", "country_id",
             "x_studio_anrede", "x_title_custom", "x_studio_vorname", "x_studio_nachname"]);
        Console.WriteLine($"Geladen aus Odoo: {firmen.Count} Firmen, {personen.Count} Personen/Adresskinder");

        // Bestehende Datensätze für Upsert einlesen
        var kunden = await db.Kunden.Include(k => k.Ansprechpartner).Include(k => k.Adressen)
            .ToListAsync();
        var kundenNachOdooId = kunden.Where(k => k.OdooId is not null)
            .ToDictionary(k => k.OdooId!.Value);
        var partnerNachName = await db.Partner.ToDictionaryAsync(p => p.Name, StringComparer.OrdinalIgnoreCase);
        // Für die automatische Kündigungs-Erkennung (Odoo-Prozess: Kündigung = Wartungs-Haken raus)
        var kundenMitLaufendemAbo = (await db.Abos
                .Where(a => a.Status == Domain.Subscriptions.AboStatus.Laufend)
                .Select(a => a.KundeId).Distinct().ToListAsync())
            .ToHashSet();
        var aktiveBestandsVertraege = (await db.Vertraege
                .Where(v => v.Status == Domain.Contracts.VertragsStatus.Aktiv
                    && v.Notiz != null && v.Notiz.StartsWith("Bestandsimport"))
                .ToListAsync())
            .GroupBy(v => v.KundeId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var vertraegeGekuendigt = 0;
        var vergebeneNummern = kunden.Select(k => k.Kundennummer).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 1) Partner aus x_studio_vertriebspartner
        foreach (var firma in firmen)
        {
            var partnerName = NormalisierterPartnerName(Many2OneLabel(firma, "x_studio_vertriebspartner"));
            if (partnerName is null || partnerNachName.ContainsKey(partnerName))
                continue;

            var partner = new Partner
            {
                Name = partnerName,
                Provisionsmodell = partnerName.Equals("Finesolutions", StringComparison.OrdinalIgnoreCase)
                    ? Provisionsmodell.Vertriebspartner
                    : Provisionsmodell.TippProvision,
                ProvisionProzent = partnerName.Equals("CSB", StringComparison.OrdinalIgnoreCase) ? 15m : null,
            };
            partnerNachName[partnerName] = partner;
            db.Partner.Add(partner);
            _stat.PartnerNeu++;
        }

        // Alle KundenNr sammeln, um Kollisionen mit Odoo-ID-Fallbacks zu erkennen
        var echteKundenNummern = firmen
            .Select(f => Text(f, "x_studio_kundenr"))
            .Where(n => n is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 2) Kunden
        var kundenNachOdoo = new Dictionary<int, Kunde>();
        foreach (var firma in firmen)
        {
            var odooId = firma.GetProperty("id").GetInt32();
            var istNeu = !kundenNachOdooId.TryGetValue(odooId, out var kunde);
            if (istNeu)
            {
                kunde = new Kunde { Kundennummer = "", Firmenname = "", OdooId = odooId };
                db.Kunden.Add(kunde);
                _stat.KundenNeu++;

                var kundenNr = Text(firma, "x_studio_kundenr");
                if (kundenNr is null)
                {
                    // Fallback Odoo-ID; Präfix nur bei Kollision mit einer echten KundenNr
                    kundenNr = echteKundenNummern.Contains(odooId.ToString()) ? $"O{odooId}" : odooId.ToString();
                    _stat.KundenOhneNummer++;
                }
                if (!vergebeneNummern.Add(kundenNr))
                {
                    _stat.Warnungen.Add($"KundenNr '{kundenNr}' mehrfach vergeben (Odoo-Id {odooId}) — mit Suffix importiert.");
                    kundenNr = $"{kundenNr}-{odooId}";
                    vergebeneNummern.Add(kundenNr);
                }
                kunde.Kundennummer = Kuerzen(kundenNr, 20)!;
            }
            else
            {
                _stat.KundenAktualisiert++;
            }

            // Für Merkmal-Träger: erkennen, ob Odoo den Kunden geändert hat (→ Merkmal prüfen)
            var vorher = istNeu ? null
                : $"{kunde!.Firmenname}|{kunde.UStIdNr}|{kunde.Status}|{kunde.Betriebsart}";

            kunde!.Firmenname = Kuerzen(Text(firma, "name"), 200) ?? $"(ohne Name, Odoo {odooId})";
            kunde.UStIdNr = Kuerzen(Text(firma, "vat"), 20);
            kunde.Status = firma.GetProperty("active").GetBoolean() && !Wahr(firma, "x_studio_blocked")
                ? KundenStatus.Aktiv
                : KundenStatus.Inaktiv;
            // Kunde.Merkmale wird bewusst NICHT importiert — Merkmale (z. B. "Insolvenz")
            // werden manuell in der Kundenzentrale gepflegt und bleiben stehen,
            // bis sie bewusst geändert werden (Entscheidung 09/2026: Odoo-Stichwörter
            // enthalten zu viele fremde Informationen).

            kunde.Betriebsart = Text(firma, "x_studio_hosting") switch
            {
                "Shared Cloud" => Betriebsart.SharedCloud,
                "Private Cloud" => Betriebsart.PrivateCloud,
                "OnPrem" => Betriebsart.OnPrem,
                _ => kunde.Betriebsart,
            };

            var partnerLabel = Many2OneLabel(firma, "x_studio_vertriebspartner");
            var partnerName = NormalisierterPartnerName(partnerLabel);
            if (partnerName is not null && partnerNachName.TryGetValue(partnerName, out var partner))
            {
                kunde.VermitteltDurchPartner = partner;
                // Nur "Finesolutions" (nicht "…-Direktkunde") bekommt die Rechnung
                kunde.RechnungsempfaengerPartner =
                    partnerLabel!.Equals("Finesolutions", StringComparison.OrdinalIgnoreCase) ? partner : null;
            }

            // Odoo-Prozess: Kündigung = Wartungs-Haken entfernt. Aktive Bestandsverträge
            // ohne laufendes Abo werden dann automatisch auf Gekündigt gesetzt.
            if (!istNeu && !Wahr(firma, "x_studio_wartung")
                && !kundenMitLaufendemAbo.Contains(kunde.Id)
                && aktiveBestandsVertraege.TryGetValue(kunde.Id, out var kuendbare))
            {
                foreach (var vertrag in kuendbare.Where(v => v.Status == Domain.Contracts.VertragsStatus.Aktiv))
                {
                    vertrag.Status = Domain.Contracts.VertragsStatus.Gekuendigt;
                    db.Aktivitaeten.Add(new Domain.Shared.Aktivitaet
                    {
                        KundeId = kunde.Id,
                        Typ = Domain.Shared.AktivitaetsTyp.Vertrag,
                        Zeitpunkt = DateTime.UtcNow,
                        Richtung = Domain.Shared.AktivitaetsRichtung.Intern,
                        Betreff = $"Vertrag {vertrag.Vertragsnummer} auf Gekündigt gesetzt — Wartung in Odoo beendet (Haken entfernt)",
                        VerweisTyp = Domain.Shared.AktivitaetsVerweisTyp.Vertrag,
                        VerweisId = vertrag.Id,
                    });
                    vertraegeGekuendigt++;
                }
            }

            // Merkmal-Träger, dessen Odoo-Daten sich geändert haben → zur Prüfung markieren
            if (!istNeu && kunde.Merkmale is not null && !kunde.MerkmalePruefen
                && vorher != $"{kunde.Firmenname}|{kunde.UStIdNr}|{kunde.Status}|{kunde.Betriebsart}")
            {
                kunde.MerkmalePruefen = true;
                db.Aktivitaeten.Add(new Domain.Shared.Aktivitaet
                {
                    KundeId = kunde.Id,
                    Typ = Domain.Shared.AktivitaetsTyp.System,
                    Zeitpunkt = DateTime.UtcNow,
                    Richtung = Domain.Shared.AktivitaetsRichtung.Intern,
                    Betreff = $"Kundendaten in Odoo geändert — Merkmal „{kunde.Merkmale}“ bitte prüfen",
                });
            }

            // Hauptsitz-Adresse aus den Firmenfeldern (kein eigener Odoo-Datensatz)
            var strasse = Text(firma, "street");
            var ort = Text(firma, "city");
            if (strasse is not null && ort is not null)
            {
                var hauptsitz = kunde.Adressen.FirstOrDefault(a => a.Typ == AdressTyp.Hauptsitz && a.OdooId is null);
                if (hauptsitz is null)
                {
                    hauptsitz = new Adresse { KundeId = kunde.Id, Strasse = "", Plz = "", Ort = "", Typ = AdressTyp.Hauptsitz };
                    kunde.Adressen.Add(hauptsitz);
                    _stat.AdressenNeu++;
                }
                hauptsitz.Strasse = Kuerzen(strasse, 200)!;
                hauptsitz.Zusatz = Kuerzen(Text(firma, "street2"), 200);
                hauptsitz.Plz = Kuerzen(Text(firma, "zip"), 10) ?? "";
                hauptsitz.Ort = Kuerzen(ort, 200)!;
                hauptsitz.Land = Land(firma);
            }

            kundenNachOdoo[odooId] = kunde;
        }

        // 3) Personen: Ansprechpartner (type=contact) und Adresskinder (invoice/delivery/other)
        var ansprechpartnerNachOdoo = kunden.SelectMany(k => k.Ansprechpartner)
            .Where(a => a.OdooId is not null)
            .ToDictionary(a => a.OdooId!.Value);
        var adressenNachOdoo = kunden.SelectMany(k => k.Adressen)
            .Where(a => a.OdooId is not null)
            .ToDictionary(a => a.OdooId!.Value);

        foreach (var person in personen)
        {
            var odooId = person.GetProperty("id").GetInt32();
            var parentId = Many2OneId(person, "parent_id");
            if (parentId is null || !kundenNachOdoo.TryGetValue(parentId.Value, out var kunde))
            {
                _stat.PersonenOhneFirma++;
                continue;
            }

            var typ = Text(person, "type");
            if (typ == "contact")
            {
                if (!ansprechpartnerNachOdoo.TryGetValue(odooId, out var ap))
                {
                    ap = new Ansprechpartner { KundeId = kunde.Id, Nachname = "", OdooId = odooId };
                    kunde.Ansprechpartner.Add(ap);
                    ansprechpartnerNachOdoo[odooId] = ap;
                    _stat.AnsprechpartnerNeu++;
                }
                else
                {
                    _stat.AnsprechpartnerAktualisiert++;
                }

                var (vorname, nachname) = Namen(person);
                ap.Vorname = Kuerzen(vorname, 100);
                ap.Nachname = Kuerzen(nachname, 100) ?? $"(ohne Name, Odoo {odooId})";
                ap.Anrede = Anrede(person);
                ap.Position = Kuerzen(Text(person, "function"), 200);
                ap.Email = Kuerzen(Text(person, "email"), 200);
                ap.TelefonFestnetz = Kuerzen(Text(person, "phone"), 50);
                ap.Aktiv = person.GetProperty("active").GetBoolean();
            }
            else
            {
                if (!adressenNachOdoo.TryGetValue(odooId, out var adresse))
                {
                    adresse = new Adresse { KundeId = kunde.Id, Strasse = "", Plz = "", Ort = "", OdooId = odooId };
                    kunde.Adressen.Add(adresse);
                    adressenNachOdoo[odooId] = adresse;
                    _stat.AdressenNeu++;
                }
                adresse.Typ = typ == "invoice" ? AdressTyp.Rechnung : AdressTyp.Sonstige;
                adresse.Strasse = Kuerzen(Text(person, "street"), 200) ?? "";
                adresse.Zusatz = Kuerzen(Text(person, "street2"), 200);
                adresse.Plz = Kuerzen(Text(person, "zip"), 10) ?? "";
                adresse.Ort = Kuerzen(Text(person, "city"), 200) ?? "";
                adresse.Land = Land(person);
                adresse.Aktiv = person.GetProperty("active").GetBoolean();
            }
        }

        // 4) Rollen aus den Firmen-Verknüpfungen + Rechnungs-E-Mail
        foreach (var firma in firmen)
        {
            if (!kundenNachOdoo.TryGetValue(firma.GetProperty("id").GetInt32(), out var kunde))
                continue;

            SetzeRolle(firma, "x_studio_hauptansprechpartner", AnsprechpartnerRolle.Hauptansprechpartner);
            SetzeRolle(firma, "x_studio_technischer_ansprechpartner", AnsprechpartnerRolle.Technik);

            var rechnungsEmail = Text(firma, "x_studio_e_mail_rechnungen");
            if (rechnungsEmail is not null)
            {
                var email = Kuerzen(rechnungsEmail, 200)!;
                var vorhanden = kunde.Ansprechpartner.FirstOrDefault(a =>
                    string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase));
                if (vorhanden is not null)
                {
                    vorhanden.Rollen |= AnsprechpartnerRolle.Rechnungsempfang;
                }
                else
                {
                    var sammel = kunde.Ansprechpartner.FirstOrDefault(a =>
                        a.OdooId is null && a.Rollen.HasFlag(AnsprechpartnerRolle.Rechnungsempfang));
                    if (sammel is null)
                    {
                        kunde.Ansprechpartner.Add(new Ansprechpartner
                        {
                            KundeId = kunde.Id,
                            Anrede = Domain.Customers.Anrede.Keine,
                            Nachname = "Rechnungsempfang",
                            Email = email,
                            Rollen = AnsprechpartnerRolle.Rechnungsempfang,
                        });
                        _stat.RechnungsEmpfaengerNeu++;
                    }
                    else
                    {
                        sammel.Email = email;
                    }
                }
            }

            void SetzeRolle(JsonElement f, string feld, AnsprechpartnerRolle rolle)
            {
                var personId = Many2OneId(f, feld);
                if (personId is not null && ansprechpartnerNachOdoo.TryGetValue(personId.Value, out var ap))
                    ap.Rollen |= rolle;
            }
        }

        _stat.Ausgeben();
        if (vertraegeGekuendigt > 0)
            Console.WriteLine($"Verträge auf Gekündigt gesetzt (Wartungs-Haken in Odoo entfernt): {vertraegeGekuendigt}");

        if (dryRun)
        {
            Console.WriteLine("DRY-RUN: keine Änderungen gespeichert.");
            return;
        }

        var geschrieben = await db.SaveChangesAsync();
        Console.WriteLine($"Gespeichert ({geschrieben} Datensätze inkl. Audit-Log).");

        var maxNummer = await db.Kunden.Select(k => k.Kundennummer).ToListAsync();
        var maxNumerisch = maxNummer.Select(n => int.TryParse(n, out var z) ? z : 0).DefaultIfEmpty(0).Max();
        Console.WriteLine($"Höchste numerische Kundennummer: {maxNumerisch} — " +
            "Nummernkreis 'Kunde' ggf. darüber ansetzen, sobald die Nummernvergabe hierher wandert.");
    }

    // --- Odoo-Zugriff ---------------------------------------------------------

    private async Task LadeLaenderAsync()
    {
        var laender = await odoo.AusfuehrenAsync("res.country", "search_read", [new object[] { }],
            new Dictionary<string, object> { ["fields"] = new[] { "id", "code" } });
        foreach (var land in laender.EnumerateArray())
        {
            if (land.GetProperty("code").ValueKind == JsonValueKind.String)
                _laender[land.GetProperty("id").GetInt32()] = land.GetProperty("code").GetString()!;
        }
    }

    private async Task<List<JsonElement>> LadeAlleAsync(object[] domain, string[] felder)
    {
        var alle = new List<JsonElement>();
        for (var offset = 0; ; offset += BatchGroesse)
        {
            var batch = await odoo.AusfuehrenAsync("res.partner", "search_read", [domain],
                new Dictionary<string, object>
                {
                    ["fields"] = felder,
                    ["limit"] = BatchGroesse,
                    ["offset"] = offset,
                    ["order"] = "id",
                    // auch archivierte Datensätze mitnehmen
                    ["context"] = new Dictionary<string, object> { ["active_test"] = false },
                });
            var anzahl = 0;
            foreach (var element in batch.EnumerateArray())
            {
                alle.Add(element);
                anzahl++;
            }
            if (anzahl < BatchGroesse)
                return alle;
        }
    }

    // --- Zuordnungshelfer -----------------------------------------------------

    private (string? Vorname, string? Nachname) Namen(JsonElement person)
    {
        var vorname = Text(person, "x_studio_vorname");
        var nachname = Text(person, "x_studio_nachname");
        if (nachname is not null)
            return (vorname, nachname);

        // Fallback: Namensfeld splitten — letztes Wort = Nachname (abgestimmt)
        var name = Text(person, "name");
        if (name is null)
            return (vorname, null);
        var teile = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return teile.Length <= 1
            ? (vorname, name)
            : (vorname ?? string.Join(' ', teile[..^1]), teile[^1]);
    }

    private static Anrede Anrede(JsonElement person)
    {
        var anrede = Many2OneLabel(person, "x_studio_anrede");
        if (anrede is not null)
            return anrede.StartsWith("Mr", StringComparison.OrdinalIgnoreCase)
                ? Domain.Customers.Anrede.Herr
                : Domain.Customers.Anrede.Frau;

        return Text(person, "x_title_custom") switch
        {
            "Mister" => Domain.Customers.Anrede.Herr,
            "Madam" or "Frau" => Domain.Customers.Anrede.Frau,
            _ => Domain.Customers.Anrede.Keine,
        };
    }

    private string Land(JsonElement element)
    {
        var landId = Many2OneId(element, "country_id");
        return landId is not null && _laender.TryGetValue(landId.Value, out var code) ? code : "DE";
    }

    /// <summary>"Finesolutions-Direktkunde" ist kein eigener Partner, sondern Finesolutions ohne Rechnungsweg.</summary>
    private static string? NormalisierterPartnerName(string? label) =>
        label is null ? null
        : label.Equals("Finesolutions-Direktkunde", StringComparison.OrdinalIgnoreCase) ? "Finesolutions"
        : label;

    private static string? Text(JsonElement element, string feld) =>
        element.TryGetProperty(feld, out var wert) && wert.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(wert.GetString())
            ? wert.GetString()!.Trim()
            : null;

    private static bool Wahr(JsonElement element, string feld) =>
        element.TryGetProperty(feld, out var wert) && wert.ValueKind == JsonValueKind.True;

    private static int? Many2OneId(JsonElement element, string feld) =>
        element.TryGetProperty(feld, out var wert) && wert.ValueKind == JsonValueKind.Array
            ? wert[0].GetInt32()
            : null;

    private static string? Many2OneLabel(JsonElement element, string feld) =>
        element.TryGetProperty(feld, out var wert) && wert.ValueKind == JsonValueKind.Array
            ? wert[1].GetString()
            : null;

    private static string? Kuerzen(string? text, int max) =>
        text is null ? null : text.Length <= max ? text : text[..max];

    private class Statistik
    {
        public int KundenNeu, KundenAktualisiert, KundenOhneNummer;
        public int AnsprechpartnerNeu, AnsprechpartnerAktualisiert, PersonenOhneFirma;
        public int AdressenNeu, PartnerNeu, RechnungsEmpfaengerNeu;
        public List<string> Warnungen { get; } = [];

        public void Ausgeben()
        {
            Console.WriteLine();
            Console.WriteLine($"Kunden:          {KundenNeu} neu, {KundenAktualisiert} aktualisiert " +
                $"({KundenOhneNummer} ohne KundenNr → Odoo-ID als Nummer)");
            Console.WriteLine($"Ansprechpartner: {AnsprechpartnerNeu} neu, {AnsprechpartnerAktualisiert} aktualisiert, " +
                $"{PersonenOhneFirma} ohne zuordenbare Firma übersprungen");
            Console.WriteLine($"Adressen:        {AdressenNeu} neu · Partner: {PartnerNeu} neu · " +
                $"Sammel-Rechnungsempfänger: {RechnungsEmpfaengerNeu}");
            foreach (var warnung in Warnungen.Take(20))
                Console.WriteLine($"  WARNUNG: {warnung}");
            if (Warnungen.Count > 20)
                Console.WriteLine($"  … und {Warnungen.Count - 20} weitere Warnungen");
        }
    }
}
