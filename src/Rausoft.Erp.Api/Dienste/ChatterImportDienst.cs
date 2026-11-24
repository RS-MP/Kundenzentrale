using System.Text;
using Rausoft.Erp.OdooImport;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// Odoo-Chatter → Kunden-Timeline aus der Administration (Testbetrieb: manuell,
/// die nächtliche Automatik kommt zum Produktivstart). Drei Modi mit denselben
/// Klassen wie das Konsolen-Tool:
/// <list type="bullet">
/// <item><c>analyse</c> — Chronologie eines Kunden mit Filterentscheidung (nur lesen)</item>
/// <item><c>dryrun</c> — zählt, was übernommen würde (nur lesen)</item>
/// <item><c>import</c> — schreibt Aktivitäten und lädt Anhänge in die Dokumentablage</item>
/// </list>
/// Der Bericht wird im Prozess gepuffert und über die API angezeigt; es läuft
/// immer nur ein Lauf gleichzeitig.
/// </summary>
public class ChatterImportDienst(
    IServiceScopeFactory scopeFactory, IConfiguration konfiguration, ILogger<ChatterImportDienst> logger)
{
    public const string ModusAnalyse = "analyse";
    public const string ModusDryRun = "dryrun";
    public const string ModusImport = "import";
    /// <summary>Zählt, was Odoo zu früheren Website-Anfragen enthält (Verkaufschancen, nur lesen).</summary>
    public const string ModusAnfragen = "anfragen";
    /// <summary>Website-Anfragen aus Odoo übernehmen — Probelauf (nur lesen, nichts speichern).</summary>
    public const string ModusAnfragenProbe = "anfragen-probe";
    /// <summary>Website-Anfragen aus Odoo übernehmen (Odoo nur lesen, Anfragen in der Kundenzentrale anlegen).</summary>
    public const string ModusAnfragenImport = "anfragen-import";

    private readonly object _sperre = new();
    private Puffer _bericht = new();

    public bool Laeuft { get; private set; }
    public string? Modus { get; private set; }
    public string? Kunde { get; private set; }
    public DateTime? GestartetAm { get; private set; }
    public DateTime? BeendetAm { get; private set; }
    public string? Fehler { get; private set; }
    public string Bericht => _bericht.ToString();

    /// <summary>Startet einen Lauf im Hintergrund; false, wenn bereits einer läuft.</summary>
    public bool Starten(string modus, string? kunde, bool anhaengeLaden = false)
    {
        lock (_sperre)
        {
            if (Laeuft)
                return false;
            Laeuft = true;
            Modus = modus;
            Kunde = string.IsNullOrWhiteSpace(kunde) ? null : kunde.Trim();
            GestartetAm = DateTime.UtcNow;
            BeendetAm = null;
            Fehler = null;
            _bericht = new Puffer();
        }
        _ = Task.Run(() => AusfuehrenAsync(modus, Kunde, anhaengeLaden));
        return true;
    }

    private async Task AusfuehrenAsync(string modus, string? kunde, bool anhaengeLaden)
    {
        var aus = _bericht;
        try
        {
            var (odoo, verbindung) = await OdooImportDienst.VerbindenAsync(scopeFactory, konfiguration);
            logger.LogInformation("Odoo-Chatter-Lauf gestartet: {Modus}, Kunde {Kunde}", modus, kunde ?? "alle");

            switch (modus)
            {
                case ModusAnalyse:
                    await new ChatterAnalyse(odoo, kunde, ausgabeDatei: null, ausgabe: aus).AusfuehrenAsync();
                    break;
                case ModusAnfragen:
                    await new AnfragenAnalyse(odoo, ausgabe: aus).AusfuehrenAsync();
                    break;
                case ModusAnfragenProbe:
                case ModusAnfragenImport:
                    await new AnfragenImporter(odoo, dryRun: modus == ModusAnfragenProbe,
                        verbindung: verbindung, ausgabe: aus).AusfuehrenAsync();
                    break;
                case ModusDryRun:
                    await new ChatterImporter(odoo, dryRun: true, nurKunde: kunde, anhaengeLaden: anhaengeLaden,
                        verbindung: verbindung, ausgabe: aus).AusfuehrenAsync();
                    break;
                case ModusImport:
                    await new ChatterImporter(odoo, dryRun: false, nurKunde: kunde, anhaengeLaden: anhaengeLaden,
                        verbindung: verbindung, ausgabe: aus).AusfuehrenAsync();
                    // Neu importierter Odoo-Verlauf: Vorgänge mit Auftragsnummer an ihren Strang hängen
                    using (var scope = scopeFactory.CreateScope())
                    {
                        var abgleich = await scope.ServiceProvider.GetRequiredService<VorgangsAbgleichDienst>()
                            .AbgleichenAsync(CancellationToken.None);
                        aus.WriteLine($"Vorgänge: {abgleich.StraengeVerknuepft} mit dem Odoo-Verlauf verknüpft, " +
                                      $"{abgleich.SchritteAbgehakt} Rechnungsschritt(e) abgehakt.{(abgleich.Hinweis is { } h ? " " + h : "")}");
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Unbekannter Modus '{modus}'.");
            }
            logger.LogInformation("Odoo-Chatter-Lauf abgeschlossen: {Modus}", modus);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Odoo-Chatter-Lauf fehlgeschlagen ({Modus}).", modus);
            Fehler = ex.Message;
            aus.WriteLine();
            aus.WriteLine("FEHLER: " + ex.Message);
        }
        finally
        {
            lock (_sperre)
            {
                Laeuft = false;
                BeendetAm = DateTime.UtcNow;
            }
        }
    }

    /// <summary>Threadsicherer Textpuffer: der Lauf schreibt, die API liest währenddessen.</summary>
    private sealed class Puffer : TextWriter
    {
        private const int MaxZeichen = 4 * 1024 * 1024;
        private readonly StringBuilder _sb = new();
        private readonly object _l = new();
        private bool _abgeschnitten;

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => Anhaengen(value.ToString());
        public override void Write(string? value) => Anhaengen(value);
        public override void WriteLine(string? value) => Anhaengen((value ?? "") + "\n");
        public override void WriteLine() => Anhaengen("\n");

        private void Anhaengen(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            lock (_l)
            {
                if (_abgeschnitten)
                    return;
                if (_sb.Length + text.Length > MaxZeichen)
                {
                    _sb.Append("\n… (Bericht gekürzt — vollständig im Konsolen-Tool mit --ausgabe)\n");
                    _abgeschnitten = true;
                    return;
                }
                _sb.Append(text);
            }
        }

        public override string ToString()
        {
            lock (_l)
                return _sb.ToString();
        }
    }
}
