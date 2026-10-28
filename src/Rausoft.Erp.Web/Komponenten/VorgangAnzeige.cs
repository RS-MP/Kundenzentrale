namespace Rausoft.Erp.Web.Komponenten;

/// <summary>Anzeigetexte und Statusfarben der Vorgänge — eine Stelle für Übersicht, Vorgang und Akte.</summary>
public static class VorgangAnzeige
{
    public static string StatusText(string status) => status switch
    {
        "neu" => "Neu",
        "laeuft" => "Läuft",
        "wartet" => "Wartet auf Kunden",
        "haengt" => "Hängt",
        "fertig" => "Abgeschlossen",
        _ => status,
    };

    /// <summary>Klasse für den Statuspunkt (.status.…): Farbe nur für den Status.</summary>
    public static string StatusKlasse(string status) => status switch
    {
        "haengt" => "kritisch",
        "wartet" => "warnung",
        "fertig" => "gut",
        "laeuft" => "akzent",
        _ => "",
    };

    public static string SchrittText(string status) => status switch
    {
        "gesperrt" => "wartet auf Vorgänger",
        "offen" => "offen",
        "inarbeit" => "in Arbeit",
        "wartet" => "wartet auf Kunden",
        "wiedervorlage" => "Wiedervorlage erreicht",
        "erledigt" => "erledigt",
        "entfallen" => "entfällt",
        _ => status,
    };

    public static string BenachrichtigungText(string wert) => wert switch
    {
        "Mail" => "Mail",
        "Teams" => "Teams",
        _ => "keine",
    };

    public static string AutoText(string wert) => wert switch
    {
        "OdooRechnung" => "der Odoo-Import die Rechnung zum Auftrag findet",
        "VertragUnterschrieben" => "der Vertrag in der Kundenzentrale auf „unterschrieben“ steht",
        _ => "",
    };

    /// <summary>Hinweis am Schritt, ob und wann das System ihn selbst abhakt (ehrlich zum Ausbaustand).</summary>
    public static string AutoHinweis(string wert, bool mitOdooAuftrag) => wert switch
    {
        "OdooRechnung" when mitOdooAuftrag =>
            "Hakt sich ab, sobald in Odoo eine gebuchte Rechnung zum Auftrag existiert (Abgleich stündlich) — oder von Hand.",
        "OdooRechnung" =>
            "Ohne Odoo-Auftragsnummer kein automatischer Abgleich — bitte von Hand abhaken.",
        "VertragUnterschrieben" =>
            "Soll sich später abhaken, wenn der Vertrag als unterschrieben erfasst ist — diesen Status gibt es im Vertragsmodul noch nicht, bitte von Hand.",
        _ => "",
    };

    public static string AutoKurz(string wert) => wert switch
    {
        "OdooRechnung" => "Rechnung in Odoo",
        "VertragUnterschrieben" => "Vertrag unterschrieben",
        _ => "nie",
    };

    public static string Datum(DateOnly d) => d.ToString("dd.MM.yyyy");

    /// <summary>„heute", „morgen", „in 3 Tagen", „seit 2 Tagen" — relativ zu heute.</summary>
    public static string Relativ(DateOnly d)
    {
        var tage = d.DayNumber - DateOnly.FromDateTime(DateTime.Now).DayNumber;
        return tage switch
        {
            0 => "heute",
            1 => "morgen",
            -1 => "seit gestern",
            > 1 => $"in {tage} Tagen",
            _ => $"seit {-tage} Tagen",
        };
    }

    /// <summary>Fehlertext einer API-Antwort (BadRequest liefert Klartext oder ProblemDetails).</summary>
    public static async Task<string> FehlerAsync(HttpResponseMessage antwort)
    {
        var text = await antwort.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text))
            return $"Fehler {(int)antwort.StatusCode}.";
        if (text.StartsWith('{'))
        {
            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(text);
                if (json.RootElement.TryGetProperty("title", out var t))
                    return t.GetString() ?? text;
            }
            catch (System.Text.Json.JsonException) { }
        }
        return text.Trim('"');
    }
}
