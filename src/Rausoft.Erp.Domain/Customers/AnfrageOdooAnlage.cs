using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Rausoft.Erp.Domain.Shared;

namespace Rausoft.Erp.Domain.Customers;

/// <summary>
/// Stand des Anlegens einer Website-Anfrage in Odoo — je Anfrage und Odoo-Datenbank ein
/// Datensatz. Er merkt sich nach jedem Schritt, was in Odoo schon steht (Firma, Kontakt,
/// Verkaufschance, Aktivität), damit ein abgebrochener Lauf fortgesetzt werden kann, ohne
/// etwas doppelt anzulegen.
///
/// Die Nummern gelten nur in der Datenbank <see cref="Datenbank"/>. Deshalb stehen sie
/// hier und nicht in <see cref="Anfrage.OdooLeadId"/>: Dieses Feld liest der Lauf
/// „anfragen-import“ als Nummer der Produktiv-Datenbank — eine Nummer aus der
/// Testdatenbank fiele dort mit einer fremden Verkaufschance zusammen. Nur ein
/// abgeschlossenes Anlegen in der Produktiv-Datenbank trägt die Nummer auch dort ein.
/// </summary>
public class AnfrageOdooAnlage
{
    public int Id { get; set; }

    public int AnfrageId { get; set; }
    public Anfrage? Anfrage { get; set; }

    /// <summary>Wohin geschrieben wurde: Test oder Produktiv (nie „Aus“).</summary>
    public OdooSchreibZiel Ziel { get; set; }
    /// <summary>Kennung der Odoo-Datenbank („rechner/datenbank“), siehe <see cref="OdooAnlageRegeln.Kennung"/>.</summary>
    public required string Datenbank { get; set; }

    public OdooAnlageStand Stand { get; set; }
    public DateTime BegonnenAm { get; set; }
    public int? BegonnenVonId { get; set; }
    public Mitarbeiter? BegonnenVon { get; set; }
    public DateTime? AbgeschlossenAm { get; set; }
    /// <summary>Gesetzt, solange ein Lauf schreibt — sperrt einen zweiten Lauf (Doppelklick, zweites Fenster).</summary>
    public DateTime? LaeuftSeit { get; set; }

    public int? FirmaId { get; set; }
    public bool FirmaNeu { get; set; }
    public int? KontaktId { get; set; }
    public bool KontaktNeu { get; set; }
    /// <summary>Der Schritt „Hauptansprechpartner“ ist erledigt (gesetzt oder bewusst gelassen).</summary>
    public bool HauptansprechpartnerErledigt { get; set; }
    public int? LeadId { get; set; }
    /// <summary>Die Verkaufschance wurde neu angelegt; sonst wurde eine vorhandene ergänzt.</summary>
    public bool LeadNeu { get; set; }
    public int? AktivitaetId { get; set; }
    /// <summary>Der Schritt „Aktivität“ ist erledigt (angelegt, oder eine gleiche offene bestand schon).</summary>
    public bool AktivitaetErledigt { get; set; }

    /// <summary>Fester Text zum letzten fehlgeschlagenen Schritt — nie Odoos Rohmeldung.</summary>
    public string? LetzterFehler { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

public enum OdooAnlageStand
{
    /// <summary>Begonnen, aber nicht alle Schritte sind durch — lässt sich fortsetzen.</summary>
    Unvollstaendig = 0,
    Fertig = 1,
}

/// <summary>Regeln, wann eine Anfrage in Odoo angelegt werden darf.</summary>
public static class OdooAnlageRegeln
{
    /// <summary>So lange gilt ein begonnener Lauf als „läuft noch“, falls er sich nicht selbst abmeldet.</summary>
    public const int LaufSperreMinuten = 3;

    /// <summary>Kennung einer Odoo-Datenbank: Rechnername und Datenbankname in Kleinbuchstaben.</summary>
    public static string Kennung(string? url, string? datenbank)
    {
        var host = Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && uri.Host.Length > 0
            ? uri.Host
            : (url ?? "").Trim();
        return $"{host}/{datenbank?.Trim()}".ToLowerInvariant();
    }

    public static string ZielText(OdooSchreibZiel ziel) =>
        ziel == OdooSchreibZiel.Produktiv ? "Odoo" : "der Odoo-Testdatenbank";

    /// <summary>
    /// Warum die Anfrage (jetzt) nicht in der Ziel-Datenbank angelegt werden kann; <c>null</c> = es geht.
    /// Was in der Produktiv-Datenbank steht, ist endgültig. Was in einer Testdatenbank steht,
    /// sperrt nur diese — nach deren Ablauf oder beim Wechsel auf „Produktiv“ geht es neu.
    /// </summary>
    public static string? Sperre(Anfrage a, IEnumerable<AnfrageOdooAnlage> anlagen, string zielKennung, DateTime jetztUtc)
    {
        var liste = anlagen.ToList();
        if (liste.FirstOrDefault(x => x is { Ziel: OdooSchreibZiel.Produktiv, Stand: OdooAnlageStand.Fertig }) is { } produktiv)
            return $"Diese Anfrage wurde {Am(produktiv)} in Odoo angelegt{Nr(produktiv)}.";
        // Aus Odoo übernommen oder mit einer Verkaufschance verknüpft: dort gibt es sie schon
        if (a.OdooLeadId is { } nr)
            return $"Diese Anfrage gehört schon zur Odoo-Verkaufschance Nr. {nr}.";

        var hier = liste.FirstOrDefault(x => x.Datenbank == zielKennung);
        if (hier is { Stand: OdooAnlageStand.Fertig })
            return $"Diese Anfrage wurde {Am(hier)} in {ZielText(hier.Ziel)} angelegt{Nr(hier)}.";
        if (hier?.LaeuftSeit is { } seit && seit > jetztUtc.AddMinutes(-LaufSperreMinuten))
            return "Diese Anfrage wird gerade in Odoo angelegt — bitte einen Moment warten.";
        if (EmailFuerOdoo(a.Email) is null)
            return "Ohne gültige E-Mail-Adresse lässt sich der Kontakt in Odoo nicht sicher wiederfinden — " +
                   "diese Anfrage bitte von Hand in Odoo anlegen.";
        return null;

        static string Am(AnfrageOdooAnlage x) =>
            x.AbgeschlossenAm is { } am ? $"am {Ortszeit.VonUtc(am):dd.MM.yyyy}" : "bereits";
        static string Nr(AnfrageOdooAnlage x) => x.LeadId is { } id ? $" (Verkaufschance Nr. {id})" : "";
    }

    /// <summary>E-Mail-Adresse in Kleinbuchstaben, wenn sie als Suchschlüssel taugt; sonst <c>null</c>.</summary>
    /// <remarks>
    /// „%“ und „\“ haben in Odoos Suche (=ilike) eine Sonderbedeutung und kommen in echten
    /// Adressen praktisch nicht vor — solche Adressen werden abgelehnt. „_“ ist üblich und
    /// wird beim Suchen maskiert (<see cref="IlikeMaske"/>).
    /// </remarks>
    public static string? EmailFuerOdoo(string? email)
    {
        var text = email?.Trim();
        return !string.IsNullOrEmpty(text) && !text.Contains('%') && !text.Contains('\\')
               && System.Net.Mail.MailAddress.TryCreate(text, out var adresse) && adresse.Address == text
               && adresse.Host.Contains('.')
            ? text.ToLowerInvariant()
            : null;
    }

    /// <summary>Maskiert die Platzhalter „_“, „%“ und „\“ für Odoos Suche mit (=)ilike.</summary>
    public static string IlikeMaske(string text) =>
        text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}

/// <summary>Person der Anfrage, so wie sie in Odoo stehen soll.</summary>
/// <param name="AnredeVorschlag">Nur bei eindeutigen Vornamen; der Mitarbeiter bestätigt oder ändert ihn.</param>
/// <param name="NameFeld">Odoo-Feld „Name“ des Kontakts — bei rausoft: Titel + Nachname.</param>
/// <param name="Anzeigename">Titel, Vorname und Nachname.</param>
public record OdooPerson(
    Anrede AnredeVorschlag,
    string? Titel,
    string? Vorname,
    string? Nachname,
    string NameFeld,
    string Anzeigename,
    IReadOnlyList<string> Hinweise);

/// <summary>
/// Zerlegt den Namen aus dem Formular in Titel, Vorname und Nachname und schlägt die Anrede
/// vor (Regeln aus dem bisherigen n8n-Workflow). Geschrieben wird die Anrede erst, wenn
/// der Mitarbeiter sie in der Vorschau bestätigt hat (Entscheidung MP 24.11.2026).
/// </summary>
public static partial class OdooPersonenRegeln
{
    /// <summary>Sammeladressen: daraus wird kein Personenname abgeleitet.</summary>
    private static readonly HashSet<string> Sammelpostfaecher = Woerter(
        "info office kontakt contact mail email post sales vertrieb service support einkauf buchhaltung " +
        "verwaltung empfang zentrale team hello hallo admin compliance export zoll customs it edv " +
        "elektronischepost anfrage anfragen bestellung order orders purchasing procurement legal recht hr " +
        "personal marketing noreply no-reply webmaster invoice rechnung rechnungen");

    private static readonly HashSet<string> Namenszusaetze = Woerter(
        "von van vom zu zum zur de der den di da del della dos du la le ten ter af y");

    private static readonly Dictionary<string, Anrede> Anreden = new()
    {
        ["herr"] = Anrede.Herr, ["hr"] = Anrede.Herr, ["mr"] = Anrede.Herr,
        ["frau"] = Anrede.Frau, ["fr"] = Anrede.Frau, ["ms"] = Anrede.Frau, ["mrs"] = Anrede.Frau,
    };

    private static readonly Dictionary<string, string> Titel = new()
    {
        ["dr"] = "Dr.", ["prof"] = "Prof.", ["dr-ing"] = "Dr.-Ing.", ["dr.-ing"] = "Dr.-Ing.",
    };

    [GeneratedRegex(@"^(dipl|mba|msc|bsc|ma|ba|llm|ll\.m|mag|ing|b\.?a|m\.?a|m\.?sc|b\.?sc)$")]
    private static partial Regex AbschlussOhneTitel();

    [GeneratedRegex(@"^dipl\.?-")]
    private static partial Regex Diplom();

    [GeneratedRegex(@"(^|[-'’])(\p{L})")]
    private static partial Regex Wortanfang();

    private static readonly HashSet<string> Weiblich = Woerter(
        "agnes agnieszka alexandra alice alina amelie ana anastasia angelika anita anja anke " +
        "anna annemarie annette annika antje astrid aurore aylin barbara beata beate beatrix bettina bianca " +
        "birgit brigitte britta carina carla carmen carola caroline celine chantal charlotte chloe christa " +
        "christel christiane christina christine claudia cordula corina corinna cornelia dagmar dana daniela " +
        "daria denise diana doris dorothea edith ekaterina elena elif elina elisabeth elke ellen emily emma " +
        "erika estelle esther eva ewa fabienne fatma franziska frauke friederike gabi gerda gertrud gisela " +
        "greta gudrun hanna hannah hedwig heide heidi heidrun heike helen helene helga hildegard ilona ilse " +
        "ina ines inga ingrid irene irina iris isabel isabell isabella isabelle ivana ivonne jacqueline jana " +
        "janina jasmin jelena jennifer jessica johanna jolanta josephine judith jule julia juliane julija " +
        "jutta karin karla katarina katarzyna katharina kathrin katja katrin kerstin kirsten klaudia kristina " +
        "lara larissa laura laure lea lena leonie lina linda lisa louisa lucia luisa lydia magdalena maike " +
        "malgorzata mandy manuela mareike maren margit maria marianne marie marina marion marlene marta " +
        "martha martina meike melanie mia michaela milena miriam mirka monika nadine nadja natalia natalie " +
        "nathalie nicole nina nora oksana olga paula petra pia ramona rebecca rebekka regina renata renate " +
        "rita rosemarie roswitha ruth sabine sabrina sandra sara sarah saskia selina silke silvia simona sina " +
        "sofia sonja sophia sophie stefanie stephanie susanne svenja svetlana sylvia tamara tanja tatjana " +
        "tetiana thekla theresa therese tina ulrike ursula ute valentina vanessa vera verena veronika " +
        "victoria viktoria waltraud wiebke yadira yasmin yulia yvonne zeynep");

    private static readonly HashSet<string> Maennlich = Woerter(
        "achim adrian ahmet alexander alexej alfred ali alois andre andrej andreas andrzej " +
        "anton antonio armin arne arnold axel benedikt benjamin bernd bernhard bjoern bjorn bodo boris brian " +
        "burkhard carl carlos carsten christian christoph christopher claus clemens constantin daniel daniele " +
        "david dennis detlef dieter dietmar dirk dmitri dmitrij dmitry dominik eberhard edgar egon elias emil " +
        "emre enno eric erich erik ernst erwin fabian felix ferdinand finn florian francesco francois frank " +
        "franz frederik friedrich fritz georg george georgios gerald gerd gerhard gerrit giorgio giuseppe " +
        "gregor guenter guenther gunnar gunter gunther gustav hakan hans harald harry hartmut hauke heiko " +
        "heinrich heinz helmut hendrik henning henrik henry herbert hermann holger horst hubert igor ingo " +
        "ivan jakob james jan jannik jean jens joachim jochen joerg johann johannes john jonas jonathan jorg " +
        "jorn jose josef joseph juan juergen julian jurgen justus kai karl karsten kay kevin klaus knut konrad " +
        "krzysztof kurt lars leo leon leonard levin lothar louis luca lucas ludwig lukas lutz magnus malte " +
        "manfred marc marcel marco marcus marek mario marius mark markus martin mathias mats matthias max " +
        "maximilian mehmet michael miguel mike moritz murat mustafa nico nicolas niklas nikolas nils noah " +
        "norbert olaf ole oleg oliver oskar otto pablo pascal patrick patrik paul pawel pedro peter philipp " +
        "philippe pierre piotr rainer ralf ralph raphael rasmus ravi reiner reinhard rene richard robert " +
        "roland rolf roman rudiger rudolf ruediger sebastian sergej sergey sergio siegfried simon soeren soren " +
        "stefan steffen stephan steven sven thilo thomas thorsten till tim timo tjark tobias tom tomasz " +
        "torben torsten udo uli ulrich uwe valentin viktor vincent vladimir volker walter wendelin werner " +
        "wilhelm william winfried wladimir wolfgang xaver yannic yannick yannik");

    /// <summary>Je nach Land männlich oder weiblich bzw. für beide üblich — dafür gibt es keinen Vorschlag.</summary>
    private static readonly HashSet<string> Uneindeutig = Woerter(
        "andrea simone gabriele nicola michele " +
        "kim sascha toni alex chris robin maxime dominique jo eike mika sam charlie kersten friedel luka");

    private static HashSet<string> Woerter(string liste) => liste.Split(' ').ToHashSet(StringComparer.Ordinal);

    /// <summary>Kleinbuchstaben ohne Akzente; „ß“ wird „ss“, Umlaute verlieren ihre Punkte („Jürgen“ → „jurgen“).</summary>
    public static string Falten(string? text)
    {
        var zerlegt = (text ?? "").ToLowerInvariant().Replace("ß", "ss").Normalize(NormalizationForm.FormD);
        var b = new StringBuilder(zerlegt.Length);
        foreach (var c in zerlegt)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                b.Append(c);
        return b.ToString();
    }

    /// <summary>„andreas“ → „Andreas“, „MUSTER“ → „Muster“; gemischte Schreibung („McDonald“) bleibt.</summary>
    public static string Schreibweise(string wort)
    {
        if (wort.Length == 0)
            return wort;
        var klein = wort.ToLowerInvariant();
        if (Namenszusaetze.Contains(klein))
            return klein;
        if (wort != klein && wort != wort.ToUpperInvariant())
            return wort;
        return Wortanfang().Replace(klein, m => m.Groups[1].Value + m.Groups[2].Value.ToUpperInvariant());
    }

    /// <summary>„erika.muster@…“ → (Erika, Muster); Sammelpostfächer und Adressen mit Ziffern ergeben nichts.</summary>
    public static (string? Vorname, string Nachname)? NameAusEmail(string? email)
    {
        var lokal = (email ?? "").Split('@')[0].ToLowerInvariant();
        if (lokal.Length == 0 || Sammelpostfaecher.Contains(lokal))
            return null;
        var teile = lokal.Split(['.', '_'], StringSplitOptions.RemoveEmptyEntries);
        if (teile.Length != 2 || teile.Any(t => t.Any(char.IsDigit)) || teile[1].Length < 2)
            return null;
        return (teile[0].Length > 1 ? Schreibweise(teile[0]) : null, Schreibweise(teile[1]));
    }

    /// <summary>Anrede nur bei eindeutigem Vornamen; sonst <see cref="Anrede.Keine"/> mit einem Hinweis.</summary>
    public static (Anrede Anrede, string? Hinweis) AnredeVorschlag(string? vorname)
    {
        if (string.IsNullOrWhiteSpace(vorname))
            return (Anrede.Keine, null);
        var erster = Falten(vorname.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault());
        if (Uneindeutig.Contains(erster))
            return (Anrede.Keine, $"Anrede unklar (Vorname „{vorname}“) — bitte wählen.");
        if (Weiblich.Contains(erster))
            return (Anrede.Frau, null);
        if (Maennlich.Contains(erster))
            return (Anrede.Herr, null);
        return (Anrede.Keine, $"Zum Vornamen „{vorname}“ gibt es keinen Anrede-Vorschlag — bitte wählen.");
    }

    private static bool IstVorname(string wort)
    {
        var k = Falten(wort);
        return Weiblich.Contains(k) || Maennlich.Contains(k) || Uneindeutig.Contains(k);
    }

    public static OdooPerson Aus(string? vorname, string? nachname, string? email)
    {
        var hinweise = new List<string>();
        var anrede = Anrede.Keine;
        var titel = new List<string>();
        string? vor = null, nach = null;

        // Vor- und Nachname stammen meist aus einem einzigen Namensfeld — deshalb neu zerlegen
        var roh = $"{vorname} {nachname}".Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (roh.Count == 2 && roh[0].EndsWith(',') && roh[0].Length > 1)
            roh = [roh[1], roh[0].TrimEnd(',')]; // „Muster, Erika“

        var worte = new List<string>();
        foreach (var wort in roh)
        {
            var k = wort.ToLowerInvariant().TrimEnd('.');
            if (Anreden.TryGetValue(k, out var genannt))
            {
                if (anrede == Anrede.Keine)
                    anrede = genannt;
            }
            else if (Titel.TryGetValue(k, out var t))
                titel.Add(t);
            else if (!AbschlussOhneTitel().IsMatch(k) && !Diplom().IsMatch(k))
                worte.Add(wort.TrimEnd(','));
        }

        var ausMail = NameAusEmail(email);
        if (worte.Count == 0)
        {
            if (ausMail is { } m)
            {
                (vor, nach) = (m.Vorname, m.Nachname);
                hinweise.Add("Name aus der E-Mail-Adresse abgeleitet.");
            }
        }
        else if (worte.Count == 1)
        {
            var wort = worte[0];
            if (ausMail is { Vorname: { } mailVor } m && Falten(mailVor) == Falten(wort))
            {
                (vor, nach) = (wort, m.Nachname);
                hinweise.Add($"Nachname „{m.Nachname}“ aus der E-Mail-Adresse ergänzt.");
            }
            else if (IstVorname(wort))
            {
                vor = wort;
                hinweise.Add("Nur der Vorname ist angegeben — der Nachname fehlt.");
            }
            else
            {
                nach = wort;
                hinweise.Add("Nur ein Namensteil ist angegeben — der Vorname fehlt.");
            }
        }
        else
        {
            // Der Nachname beginnt beim ersten Namenszusatz („von“, „van der“), sonst ist es das letzte Wort
            var ab = worte.Count - 1;
            for (var i = 1; i < worte.Count - 1; i++)
                if (Namenszusaetze.Contains(worte[i].ToLowerInvariant()))
                {
                    ab = i;
                    break;
                }
            vor = string.Join(' ', worte.Take(ab));
            nach = string.Join(' ', worte.Skip(ab));
        }

        vor = vor is null ? null : string.Join(' ', vor.Split(' ').Select(Schreibweise));
        nach = nach is null ? null : string.Join(' ', nach.Split(' ').Select(Schreibweise));

        if (anrede == Anrede.Keine)
        {
            var (vorschlag, hinweis) = AnredeVorschlag(vor);
            anrede = vorschlag;
            if (hinweis is not null)
                hinweise.Add(hinweis);
        }

        var vorsatz = string.Join(' ', titel.Distinct());
        string Zusammen(params string?[] teile) => string.Join(' ', teile.Where(t => !string.IsNullOrWhiteSpace(t)));
        var nameFeld = Zusammen(vorsatz, nach ?? vor) is { Length: > 0 } n ? n
            : !string.IsNullOrWhiteSpace(email) ? email.Trim()
            : "Unbekannter Kontakt";
        var anzeige = Zusammen(vorsatz, vor, nach) is { Length: > 0 } a ? a : nameFeld;
        return new OdooPerson(anrede, vorsatz.Length > 0 ? vorsatz : null, vor, nach, nameFeld, anzeige, hinweise);
    }
}

/// <summary>Kontakt in Odoo, der als Firma der Anfrage in Frage kommt.</summary>
public record OdooFirma(
    int Id,
    string Name,
    string? Email,
    string? Website,
    bool IstFirma,
    int Unterkontakte,
    int? HauptansprechpartnerId,
    string? HauptansprechpartnerName);

public record OdooFirmaBewertung(OdooFirma Firma, int Punkte, IReadOnlyList<string> Gruende);

/// <summary>
/// Dublettensuche der Firma in Odoo (Regeln aus dem bisherigen n8n-Workflow): gleiche
/// E-Mail-Domäne und gleicher Firmenname sind starke Treffer, ein ähnlicher Name ist nur
/// ein Kandidat, den der Mitarbeiter in der Vorschau wählen kann.
/// </summary>
public static partial class OdooFirmenRegeln
{
    /// <summary>Ab so vielen Punkten wird eine vorhandene Firma vorgeschlagen statt einer neuen.</summary>
    public const int SichererTreffer = 70;
    /// <summary>Ab so vielen Punkten erscheint eine Firma als wählbarer Kandidat.</summary>
    public const int Kandidat = 10;
    public const int MaxKandidaten = 6;

    private static readonly HashSet<string> Rechtsformen =
    [
        "gmbh", "mbh", "ag", "kg", "kgaa", "se", "ug", "haftungsbeschrankt", "ohg", "gbr", "ev", "eg", "co", "cie",
        "und", "u", "ltd", "limited", "llc", "inc", "corp", "plc", "sa", "sas", "sarl", "srl", "sl", "spa", "bv",
        "nv", "oy", "ab", "as", "aps", "sp", "zoo", "sro", "gesmbh", "ggmbh", "kft", "doo", "the",
    ];

    private static readonly HashSet<string> Fuellwoerter =
    [
        "holding", "group", "gruppe", "international", "deutschland", "germany", "europe", "europa", "austria",
        "schweiz", "services", "service", "management", "verwaltung", "verwaltungs", "beteiligungs", "beteiligung",
    ];

    private static readonly string[] ZweiteEbene = ["co", "com", "or", "ac", "gv", "org", "net"];

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex KeinZeichen();

    [GeneratedRegex(@"[\s,./()&+\-]+")]
    private static partial Regex WortTrenner();

    private static IEnumerable<string> Woerter(string? name) =>
        KeinZeichen().Replace(OdooPersonenRegeln.Falten(name).Replace("&", " und ").Replace("+", " und "), " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Firmenname ohne Rechtsform: „Muster &amp; Söhne GmbH“ → „muster sohne“.</summary>
    public static string Streng(string? name) => string.Join(' ', Woerter(name).Where(w => !Rechtsformen.Contains(w)));

    /// <summary>Wie <see cref="Streng"/>, zusätzlich ohne Füllwörter wie „Holding“ oder „Deutschland“.</summary>
    public static string Locker(string? name) =>
        string.Join(' ', Woerter(name).Where(w => !Rechtsformen.Contains(w) && !Fuellwoerter.Contains(w)));

    /// <summary>Längstes markantes Wort des Namens für die Suche in Odoo (Umlaute bleiben erhalten).</summary>
    public static string? SuchWort(string? name) =>
        WortTrenner().Split(name?.Trim() ?? "")
            .Where(w => w.Length >= 3)
            .Where(w => OdooPersonenRegeln.Falten(w) is var k && !Rechtsformen.Contains(k) && !Fuellwoerter.Contains(k))
            .OrderByDescending(w => w.Length)
            .FirstOrDefault();

    /// <summary>„mail.kunde.example“ → „kunde.example“; „firma.co.uk“ bleibt.</summary>
    public static string? BasisDomaene(string? domaene)
    {
        if (string.IsNullOrWhiteSpace(domaene))
            return null;
        var teile = domaene.Trim().ToLowerInvariant().Split('.');
        if (teile.Length <= 2)
            return string.Join('.', teile);
        var anzahl = ZweiteEbene.Contains(teile[^2]) && teile[^1].Length == 2 ? 3 : 2;
        return string.Join('.', teile[^anzahl..]);
    }

    /// <summary>Domäne der Firma aus der E-Mail-Adresse; <c>null</c> bei Freemailern (die sagen nichts über die Firma).</summary>
    public static string? FirmenDomaene(string? email)
    {
        var klammer = email?.LastIndexOf('@') ?? -1;
        if (email is null || klammer < 1 || klammer >= email.Length - 1)
            return null;
        var basis = BasisDomaene(email[(klammer + 1)..]);
        return basis is null || WebsiteAnfrageLeser.IstFreemailer(basis) ? null : basis;
    }

    /// <summary>
    /// Bewertet die in Odoo gefundenen Firmen, beste zuerst.
    /// </summary>
    /// <param name="kontaktFirmaId">Firma des Kontakts, der in Odoo dieselbe E-Mail-Adresse hat.</param>
    /// <param name="domaenenFirmenIds">Firmen, bei denen ein Ansprechpartner dieselbe E-Mail-Domäne hat.</param>
    public static List<OdooFirmaBewertung> Bewerten(
        IEnumerable<OdooFirma> firmen, string? firmenname, string? email,
        int? kontaktFirmaId, IReadOnlySet<int> domaenenFirmenIds)
    {
        var basis = FirmenDomaene(email);
        var streng = string.IsNullOrWhiteSpace(firmenname) ? null : Streng(firmenname);
        var locker = string.IsNullOrWhiteSpace(firmenname) ? null : Locker(firmenname);
        var suchwort = SuchWort(firmenname);

        return firmen.Select(f =>
            {
                var gruende = new List<string>();
                var punkte = 0;
                void Plus(int p, string grund)
                {
                    gruende.Add(grund);
                    punkte = Math.Max(punkte, p);
                }

                if (kontaktFirmaId == f.Id)
                    Plus(100, "Kontakt mit dieser E-Mail-Adresse gehört zu dieser Firma");
                var mail = OdooPersonenRegeln.Falten(f.Email);
                var eigeneDomaene = basis is not null
                                    && (mail.EndsWith("@" + basis) || mail.EndsWith("." + basis)
                                        || OdooPersonenRegeln.Falten(f.Website).Contains(basis));
                var nameStreng = !string.IsNullOrEmpty(streng) && Streng(f.Name) == streng;
                var nameLocker = !string.IsNullOrEmpty(locker) && Locker(f.Name) == locker;
                var nameSuchwort = suchwort is not null
                                   && OdooPersonenRegeln.Falten(f.Name).Contains(OdooPersonenRegeln.Falten(suchwort));
                // Ein einzelner Kontakt ohne Unterkontakte ist nur bei genau gleichem Namen eine Firma
                if (!f.IstFirma && f.Unterkontakte == 0 && !nameStreng && kontaktFirmaId != f.Id)
                    return new OdooFirmaBewertung(f, 0, ["Einzelkontakt"]);
                if (eigeneDomaene)
                    Plus(80, $"gleiche Domäne {basis}");
                else if (basis is not null && domaenenFirmenIds.Contains(f.Id))
                    Plus(80, $"Ansprechpartner mit Domäne {basis}");
                if (nameStreng)
                    Plus(70, "gleicher Firmenname");
                else if (nameLocker)
                    Plus(40, "ähnlicher Firmenname");
                else if (nameSuchwort)
                    Plus(10, $"Name enthält „{suchwort}“");
                if (gruende.Count == 0)
                    Plus(5, "Suchtreffer");
                if (gruende.Count > 1)
                    punkte += 5;
                return new OdooFirmaBewertung(f, punkte, gruende);
            })
            .OrderByDescending(b => b.Punkte).ThenBy(b => b.Firma.Id)
            .ToList();
    }

    /// <summary>Die Firma, die vorgeschlagen wird: der beste sichere Treffer, bei Gleichstand die älteste.</summary>
    public static OdooFirmaBewertung? Vorschlag(IReadOnlyList<OdooFirmaBewertung> bewertet) =>
        bewertet.FirstOrDefault(b => b.Punkte >= SichererTreffer);

    /// <summary>Wählbare Firmen für die Vorschau: der Vorschlag und die besten weiteren Kandidaten.</summary>
    public static List<OdooFirmaBewertung> Kandidaten(IReadOnlyList<OdooFirmaBewertung> bewertet) =>
        bewertet.Where(b => b.Punkte >= Kandidat).Take(MaxKandidaten).ToList();

    public static string? MehrfachHinweis(IReadOnlyList<OdooFirmaBewertung> bewertet)
    {
        if (Vorschlag(bewertet) is not { } bester)
            return null;
        var gleich = bewertet.Where(b => b.Punkte == bester.Punkte).ToList();
        return gleich.Count > 1
            ? "Mehrere gleich gut passende Firmen in Odoo: " +
              string.Join(", ", gleich.Select(b => $"{b.Firma.Name} (Nr. {b.Firma.Id})")) +
              " — vorgeschlagen ist die älteste; bitte in Odoo auf Dubletten prüfen."
            : null;
    }
}

/// <summary>
/// Was an der Verkaufschance und der Aktivität in Odoo steht (Zuordnung aus dem bisherigen
/// n8n-Workflow). Nicht übertragen werden die Klick-Kennungen (Entscheidung MP 17.11.2026)
/// sowie IP-Adresse und Browser-Kennung — die speichert die Kundenzentrale gar nicht erst.
/// </summary>
public static class OdooLeadRegeln
{
    public static readonly IReadOnlyList<string> NichtUebertragen =
        ["Klick-Kennungen (gclid, gbraid, wbraid, msclkid)", "IP-Adresse", "Browser-Kennung"];

    private static readonly string[] Suchmaschinen = ["google", "bing", "yahoo", "duckduckgo", "search", "search_engine"];
    private static readonly string[] BezahltMedien = ["cpc", "ppc", "paidsearch", "paid_search", "googleads", "google_ads", "adwords"];
    private static readonly string[] KiQuellen = ["chatgpt", "openai", "perplexity", "copilot", "gemini", "claude", "askai", "ask_ai", "ai"];

    /// <summary>Name der Verkaufschance: „Infomappe - Muster GmbH“, ohne Firma mit dem Namen der Person.</summary>
    public static string Titel(Anfrage a, OdooPerson person) =>
        $"{a.Formular} - {(string.IsNullOrWhiteSpace(a.Firma) ? person.Anzeigename : a.Firma.Trim())}";

    /// <summary>Zusammenfassung der Aktivität je Formular.</summary>
    public static string AktivitaetsText(string formular) => FormularStichwort(formular) switch
    {
        "infomappe" => "Infomappe Erstkontakt",
        "testversion" => "Testversion geladen",
        "checkliste" => "Checkliste Erstkontakt",
        "kontakt" => "Kontaktanfrage beantworten",
        _ => $"Website-Anfrage prüfen ({formular})",
    };

    /// <summary>Stichwort, an dem Phase und Aktivitätsart in Odoo zum Formular gefunden werden; <c>null</c> = keines.</summary>
    public static string? FormularStichwort(string formular)
    {
        var klein = formular.ToLowerInvariant();
        foreach (var wort in (string[])["infomappe", "testversion", "checkliste", "kontakt"])
            if (klein.Contains(wort))
                return wort;
        return klein.Contains("contact") ? "kontakt" : null;
    }

    /// <summary>utm_source für Odoo — Anzeigen ohne eigene Angabe gelten wie im Workflow als „google“.</summary>
    public static string? UtmQuelle(Anfrage a) =>
        a.Quelle ?? (a.UeberAnzeige ? "google" : OhneUtm(a) && VonGoogle(a.Verweis) ? "google" : null);

    public static string? UtmMedium(Anfrage a) =>
        a.Medium ?? (a.UeberAnzeige ? "cpc" : OhneUtm(a) && VonGoogle(a.Verweis) ? "organic" : null);

    public static string? UtmKampagne(Anfrage a) =>
        a.Kampagne ?? (a.KampagnenNr is { } nr ? $"google_ads_{nr}"
            : !a.UeberAnzeige && OhneUtm(a) && VonGoogle(a.Verweis) ? "organic_search" : null);

    private static bool OhneUtm(Anfrage a) => a.Quelle is null && a.Medium is null && a.Kampagne is null;

    private static bool VonGoogle(string? verweis) =>
        Uri.TryCreate(verweis, UriKind.Absolute, out var uri)
        && uri.Host.ToLowerInvariant() is var host
        && (host == "google.com" || host.EndsWith(".google.com") || host.StartsWith("www.google."));

    /// <summary>
    /// Die eigenen Textfelder der Verkaufschance (x_studio_…) mit Inhalt. Bewusst ohne
    /// x_studio_gclid, x_studio_gbraid, x_studio_wbraid und x_studio_msclkid.
    /// </summary>
    public static Dictionary<string, string> TextFelder(Anfrage a, OdooPerson person)
    {
        var felder = new Dictionary<string, string>();
        void Feld(string name, string? wert)
        {
            if (!string.IsNullOrWhiteSpace(wert))
                felder[name] = wert.Trim();
        }
        Feld("x_studio_vorname", person.Vorname);
        Feld("x_studio_form_source", a.Formular);
        Feld("x_studio_gad_campaignid", a.KampagnenNr);
        Feld("x_studio_utm_source", UtmQuelle(a));
        Feld("x_studio_utm_medium", UtmMedium(a));
        Feld("x_studio_utm_campaign", UtmKampagne(a));
        Feld("x_studio_utm_term", a.Suchbegriff);
        Feld("x_studio_utm_content", a.Anzeigenvariante);
        Feld("x_studio_doc_referrer", a.Verweis);
        Feld("x_studio_landing_page", a.Einstiegsseite);
        Feld("x_studio_last_page_before_form", a.LetzteSeite);
        return felder;
    }

    public const string ErsterBesuchFeld = "x_studio_first_touch_at";

    /// <summary>Erster Besuch im Format des Odoo-Felds (Datum/Zeit-Feld oder Text).</summary>
    public static string? ErsterBesuch(Anfrage a, string? feldTyp) => a.ErsterBesuch is not { } besuch ? null : feldTyp switch
    {
        "datetime" => besuch.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        "date" => besuch.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => besuch.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
    };

    /// <summary>Zeilen der Beschreibung der Verkaufschance bzw. der Notiz an einer vorhandenen.</summary>
    public static List<string> Beschreibung(Anfrage a, IReadOnlyDictionary<string, string> kampagnenNamen)
    {
        var zeilen = new List<string> { $"Formular: {a.Formular}" };
        void Zeile(string titel, string? wert)
        {
            if (!string.IsNullOrWhiteSpace(wert))
                zeilen.Add($"{titel}: {wert.Trim()}");
        }
        Zeile("Nachricht", a.Nachricht);
        Zeile("Website", a.Website);
        Zeile("Eingang", Ortszeit.VonUtc(a.EingegangenAm).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture));
        Zeile("Herkunft", AnfrageHerkunft.Text(a, kampagnenNamen));
        Zeile("Einstiegsseite", a.Einstiegsseite);
        Zeile("Letzte Seite vor dem Formular", a.LetzteSeite);
        Zeile("Erster Besuch", a.ErsterBesuch is { } besuch
            ? Ortszeit.VonUtc(besuch).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) : null);
        Zeile("Verweis", a.Verweis);
        Zeile("Suchbegriff", a.Suchbegriff);
        Zeile("Anzeigenvariante", a.Anzeigenvariante);
        zeilen.Add($"Datenschutzerklärung: {(a.DatenschutzBestaetigt ? "zugestimmt" : "keine Zustimmung im Text der Mail")}");
        return zeilen;
    }

    /// <summary>
    /// Wählt aus Odoos Liste den Eintrag, dessen Name zu einem der Stichwörter passt: erst
    /// genau gleich, dann als ganzes Wort, dann (ab fünf Zeichen) als Teil des Namens.
    /// </summary>
    public static int? Passend(IEnumerable<(int Id, string Name)> liste, params string?[] stichwoerter)
    {
        var eintraege = liste.Select(e => (e.Id, Name: OdooPersonenRegeln.Falten(e.Name))).ToList();
        foreach (var roh in stichwoerter)
        {
            if (string.IsNullOrWhiteSpace(roh))
                continue;
            var wort = OdooPersonenRegeln.Falten(roh);
            foreach (var pruefung in (Func<string, bool>[])
                     [
                         n => n == wort,
                         n => n.Split([' ', '/', '-', '(', ')', ',', '.'], StringSplitOptions.RemoveEmptyEntries).Contains(wort),
                         n => wort.Length >= 5 && n.Contains(wort),
                     ])
                if (eintraege.FirstOrDefault(e => pruefung(e.Name)) is { Id: > 0 } treffer)
                    return treffer.Id;
        }
        return null;
    }

    /// <summary>Stichwörter, mit denen die Quelle (utm.source) in Odoo gesucht wird.</summary>
    public static string?[] QuelleStichwoerter(Anfrage a)
    {
        var q = UtmQuelle(a)?.Trim().ToLowerInvariant();
        return q switch
        {
            null or "" => [],
            _ when Suchmaschinen.Contains(q) => ["suchmaschine", "search engine", "suchmaschinen", q],
            "newsletter" or "rapidmail" => ["newsletter"],
            "facebook" or "meta" => ["facebook", "meta"],
            "x" or "twitter" => ["x", "twitter"],
            "linkedin" => ["linkedin"],
            "referral" or "partner" => ["empfehlung", "referral", "partner"],
            _ when KiQuellen.Contains(q) => ["ki", "ai", "chatgpt", q],
            _ => [q],
        };
    }

    /// <summary>Stichwörter, mit denen das Medium (utm.medium) in Odoo gesucht wird.</summary>
    public static string?[] MediumStichwoerter(Anfrage a)
    {
        var q = UtmQuelle(a)?.Trim().ToLowerInvariant();
        var m = UtmMedium(a)?.Trim().ToLowerInvariant();
        if (a.UeberAnzeige || (m is not null && BezahltMedien.Contains(m)))
            return ["google adwords", "adwords", "google ads", "cpc"];
        return (q, m) switch
        {
            (_, "email" or "e-mail" or "newsletter") => ["e-mail", "email"],
            ("facebook" or "meta", _) => ["facebook"],
            ("linkedin", _) => ["linkedin"],
            ("x" or "twitter", _) => ["x", "twitter"],
            (_, "phone" or "telefon" or "call") => ["telefon", "phone"],
            (_, "banner" or "display") => ["banner"],
            (_, "direct" or "(none)" or "none") => ["direkt", "direct"],
            (null, null) => [],
            _ => ["website"],
        };
    }

    /// <summary>
    /// Text für ein HTML-Feld in Odoo: die HTML-Sonderzeichen maskiert, Zeilenumbrüche als &lt;br&gt;.
    /// Umlaute bleiben Buchstaben, damit Odoos Suche sie findet.
    /// </summary>
    public static string Html(string? text) =>
        (text ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;")
            .Replace("\r\n", "\n").Replace("\n", "<br>");
}

/// <summary>
/// Vorbelegung der Auswahlfelder beim Anlegen in Odoo: Die Kundenzentrale merkt sich die
/// letzte Wahl als Namen (nicht als Odoo-Nummer) in der Einstellung
/// <see cref="EinstellungsSchluessel.AnfragenOdooVorbelegung"/> — „Schlüssel = Name; …“.
/// So stehen keine internen Odoo-Nummern im Programm; gelesen wird alles über den API-Key.
/// </summary>
public static class OdooVorbelegung
{
    public const string Zuweisung = "Zuweisung";
    public static string Phase(string formular) => $"Phase:{Sauber(formular)}";
    public static string Aktivitaet(string formular) => $"Aktivität:{Sauber(formular)}";
    public static string Quelle(Anfrage a) => $"Quelle:{Sauber(OdooLeadRegeln.UtmQuelle(a) ?? "ohne")}";
    public static string Medium(Anfrage a) =>
        $"Medium:{Sauber(OdooLeadRegeln.UtmQuelle(a) ?? "ohne")}/{Sauber(OdooLeadRegeln.UtmMedium(a) ?? "ohne")}";

    private static string Sauber(string text) =>
        text.Replace(';', ',').Replace('=', '-').Replace('\n', ' ').Replace('\r', ' ').Trim();

    public static Dictionary<string, string> Lesen(string? einstellung)
    {
        var werte = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var teil in (einstellung ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var trenner = teil.IndexOf('=');
            if (trenner <= 0)
                continue;
            var (schluessel, wert) = (teil[..trenner].Trim(), teil[(trenner + 1)..].Trim());
            if (schluessel.Length > 0 && wert.Length > 0)
                werte[schluessel] = wert;
        }
        return werte;
    }

    /// <summary>Nummer des gemerkten Eintrags in Odoos aktueller Liste; <c>null</c>, wenn nichts gemerkt ist oder der Name dort fehlt.</summary>
    public static int? Finden(IReadOnlyDictionary<string, string> werte, string schluessel, IEnumerable<(int Id, string Name)> liste) =>
        werte.TryGetValue(schluessel, out var name)
        && liste.FirstOrDefault(e => string.Equals(Sauber(e.Name), name, StringComparison.OrdinalIgnoreCase)) is { Id: > 0 } treffer
            ? treffer.Id
            : null;

    /// <summary>
    /// Trägt die Wahl ein und liefert den neuen Wert der Einstellung. Passt er nicht mehr in
    /// die Spalte, fallen die ältesten Einträge heraus.
    /// </summary>
    public static string Merken(string? einstellung, IEnumerable<KeyValuePair<string, string?>> wahl, int maxLaenge)
    {
        var werte = Lesen(einstellung).ToList();
        foreach (var (schluessel, name) in wahl)
        {
            werte.RemoveAll(w => w.Key.Equals(schluessel, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(name))
                werte.Add(new(schluessel, Sauber(name))); // zuletzt Gewähltes steht hinten
        }
        string Text() => string.Join("; ", werte.Select(w => $"{w.Key} = {w.Value}"));
        while (werte.Count > 0 && Text().Length > maxLaenge)
            werte.RemoveAt(0);
        return Text();
    }
}
