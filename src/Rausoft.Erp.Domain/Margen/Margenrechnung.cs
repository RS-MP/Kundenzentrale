namespace Rausoft.Erp.Domain.Margen;

/// <summary>
/// Rechenkern des Margenrechners (World-Check, Full File und Sanctions Only):
/// Verkaufspreis aus Kunden je Basket, Einkaufspreis nach „Variante B"
/// (Gebühr der Staffel je Bereich mal Volumenanteil des Bereichs), Gewinn,
/// Grenzwerte und kritische Verteilungen.
///
/// Reine Funktionen ohne Datenbank und ohne Preise im Code — alle Zahlen
/// kommen aus <see cref="MargenDaten"/>. Gerechnet wird bewusst mit double und
/// denselben Rundungen und Toleranzen wie in der Kalkulationsvorlage, damit
/// die Ergebnisse mit ihr übereinstimmen.
/// </summary>
public sealed class Margenrechnung
{
    /// <summary>Rechenhorizont der Grenzwerte: 100 Mio. Cases.</summary>
    public const double Horizont = 100_000_000;
    /// <summary>Bereich der Margenkurve (Gesamtvolumen, logarithmisch).</summary>
    public const double KurveVon = 10_000;
    public const double KurveBis = 50_000_000;
    public const int MaxKundenJeFeld = 1_000_000;
    public const double AenderungMin = -90;
    public const double AenderungMax = 300;

    /// <summary>Eine Stufe der Staffel: gilt bis <see cref="Bis"/> Cases, Monatsgebühr je Bereich.</summary>
    public readonly record struct Stufe(long Bis, double FullFile, double SanctionsOnly)
    {
        public double Gebuehr(MargenBereich bereich) => bereich == MargenBereich.FullFile ? FullFile : SanctionsOnly;
    }

    private readonly MargenDaten _d;
    private readonly int _nt;
    private readonly long _linVon;
    private readonly long _schritt;
    private readonly double _rateUnendlich;

    public Margenrechnung(MargenDaten daten)
    {
        if (daten.Pruefen() is { Count: > 0 } fehler)
            throw new ArgumentException("Die Margenrechner-Daten sind unvollständig: " + string.Join(" ", fehler), nameof(daten));
        _d = daten;
        _nt = daten.Staffel.Count;
        _linVon = daten.LinearAnker[0].Cases;
        _schritt = daten.LinearSchritt;
        var letzter = daten.LinearAnker[^1];
        _rateUnendlich = letzter.Gebuehr / letzter.Cases;
    }

    // --- Staffel -----------------------------------------------------------------

    /// <summary>Stufe Nr. i (ab 0): erst die Tabelle, danach lineare Stufen ohne Obergrenze.</summary>
    public Stufe StufeAn(int i)
    {
        if (i < _nt)
        {
            var s = _d.Staffel[i];
            return new Stufe(s.BisCases, s.GebuehrFullFile, s.GebuehrSanctionsOnly);
        }
        var bis = _linVon + (long)(i - _nt + 1) * _schritt;
        var gebuehr = Runden(LinearGebuehr(bis));
        return new Stufe(bis, gebuehr, gebuehr);
    }

    /// <summary>Stufe, in die ein Gesamtvolumen fällt (zwischen zwei Stufen gilt die höhere).</summary>
    public int StufenIndex(double volumen)
    {
        if (volumen > _linVon)
            return _nt - 1 + (int)Math.Ceiling((volumen - _linVon) / _schritt - 1e-9);
        for (var i = 0; i < _nt; i++)
            if (volumen <= _d.Staffel[i].BisCases)
                return i;
        return _nt - 1;
    }

    /// <summary>Untergrenze der Stufe (= Obergrenze der vorigen).</summary>
    public long StufeAb(int i) => i == 0 ? 0 : StufeAn(i - 1).Bis;

    /// <summary>Gebühr einer linearen Stufe: zwischen den Ankern interpoliert, darüber mit dem Preis je Case des letzten Ankers.</summary>
    private double LinearGebuehr(double bis)
    {
        var a = _d.LinearAnker;
        var n = a.Count;
        if (bis >= a[n - 1].Cases)
            return _rateUnendlich * bis;
        for (var j = 1; j < n; j++)
            if (bis <= a[j].Cases)
                return a[j - 1].Gebuehr + (bis - a[j - 1].Cases) * (a[j].Gebuehr - a[j - 1].Gebuehr) / (a[j].Cases - a[j - 1].Cases);
        return _rateUnendlich * bis;
    }

    /// <summary>Kaufmännisch auf volle Euro, halbe aufwärts — wie in der Vorlage (nicht „Banker's Rounding").</summary>
    private static double Runden(double wert) => Math.Floor(wert + 0.5);

    /// <summary>Einkaufspreis pro Monat für ein Volumen je Bereich (Variante B).</summary>
    public double Einkauf(double volumenFullFile, double volumenSanctionsOnly)
    {
        var v = volumenFullFile + volumenSanctionsOnly;
        if (v <= 0)
            return 0;
        var t = StufeAn(StufenIndex(v));
        return t.FullFile * volumenFullFile / v + t.SanctionsOnly * volumenSanctionsOnly / v;
    }

    // --- Bestand -----------------------------------------------------------------

    private sealed record Bestand(
        double Vs, double Vf, double V, int NS, int NF,
        double VkS, double VkF, int Index, Stufe T,
        double EkF, double EkS, double CS, double CF)
    {
        public int N => NS + NF;
        public double Vk => VkS + VkF;
        public double Ek => EkF + EkS;
        public double Gewinn => VkS + VkF - EkF - EkS;
    }

    private Bestand Rechne(IReadOnlyList<int> nS, IReadOnlyList<int> nF, double[] pS, double[] pF)
    {
        double vs = 0, vf = 0, vkS = 0, vkF = 0;
        int sS = 0, sF = 0;
        for (var i = 0; i < _d.Baskets.Count; i++)
        {
            double b = _d.Baskets[i].Cases;
            vs += nS[i] * b;
            vf += nF[i] * b;
            vkS += nS[i] * pS[i];
            vkF += nF[i] * pF[i];
            sS += nS[i];
            sF += nF[i];
        }
        var v = vs + vf;
        var index = StufenIndex(v);
        var t = StufeAn(index);
        return new Bestand(vs, vf, v, sS, sF, vkS, vkF, index, t,
            v > 0 ? t.FullFile * vf / v : 0, v > 0 ? t.SanctionsOnly * vs / v : 0,
            v > 0 ? t.SanctionsOnly / v : 0, v > 0 ? t.FullFile / v : 0);
    }

    private static int[] Kunden(IReadOnlyList<int>? eingabe, int anzahl)
    {
        var n = new int[anzahl];
        for (var i = 0; i < anzahl && i < (eingabe?.Count ?? 0); i++)
            n[i] = Math.Clamp(eingabe![i], 0, MaxKundenJeFeld);
        return n;
    }

    private static double Aenderung(double prozent) =>
        double.IsFinite(prozent) ? Math.Clamp(prozent, AenderungMin, AenderungMax) : 0;

    // --- Ergebnis ----------------------------------------------------------------

    public MargenErgebnis Berechnen(MargenEingabe eingabe)
    {
        var baskets = _d.Baskets;
        var anzahl = baskets.Count;
        var nS = Kunden(eingabe.KundenSanctionsOnly, anzahl);
        var nF = Kunden(eingabe.KundenFullFile, anzahl);
        var aS = Aenderung(eingabe.AenderungSanctionsOnly);
        var aF = Aenderung(eingabe.AenderungFullFile);
        var pS = baskets.Select(b => b.VkSanctionsOnly * (1 + aS / 100)).ToArray();
        var pF = baskets.Select(b => b.VkFullFile * (1 + aF / 100)).ToArray();

        var r = Rechne(nS, nF, pS, pF);
        var ok = r.V > 0;

        // Eingabetabelle
        MargenZelle Zelle(int i, bool fullFile)
        {
            double b = baskets[i].Cases;
            var p = fullFile ? pF[i] : pS[i];
            double? ek = ok ? b * (fullFile ? r.CF : r.CS) : null;
            var mehr = Einkauf(r.Vf + (fullFile ? b : 0), r.Vs + (fullFile ? 0 : b)) - r.Ek;
            return new MargenZelle(fullFile ? nF[i] : nS[i], p, ek, ek is null ? null : p - ek, p - mehr);
        }
        var zeilen = new List<MargenZeile>(anzahl);
        for (var i = 0; i < anzahl; i++)
            zeilen.Add(new MargenZeile(baskets[i].Cases, Zelle(i, false), Zelle(i, true)));

        return new MargenErgebnis(
            ok,
            new MargenKennzahlen(r.Vk, r.Ek, r.Gewinn, r.Vk > 0 ? r.Gewinn / r.Vk : null, r.N, ok ? r.T.Bis : null),
            zeilen,
            new MargenSummen(r.NS, r.NF, r.VkS, r.VkF, ok ? r.EkS : null, ok ? r.EkF : null),
            new MargenVolumen(r.V, ok ? r.Vf / r.V : 0, ok ? r.Vs / r.V : 0, ok && r.Index >= _nt,
                ok ? r.T.FullFile : null, ok ? r.T.SanctionsOnly : null),
            ok ? Grenzwerte(r, nS, nF, pS, pF) : null,
            Kurve(r),
            Befunde(r, nS, nF, pS, pF),
            Verteilungen(pS, pF),
            BasketGrenzen(r, pS, pF),
            Rechenweg());
    }

    private MargenGrenzwerte Grenzwerte(Bestand r, int[] nS, int[] nF, double[] pS, double[] pF)
    {
        // Mindestbestand bei gleicher Mischung
        var zonen = Verlustzonen(r);
        var kMax = Horizont / r.V;
        var imVerlust = zonen.Any(q => q[0] < 1 && 1 < q[1]);
        MargenMindestbestand mindest;
        if (zonen.Count > 0 && zonen[0][0] == 0)
        {
            var k = zonen[0][1];
            if (k >= kMax - 1e-9)
                mindest = new MargenMindestbestand(MargenMindestbestandArt.Nie, null, null, true, []);
            else
                mindest = new MargenMindestbestand(MargenMindestbestandArt.AbKunden,
                    (long)Math.Ceiling(k * r.N - 1e-9), k * r.V, imVerlust,
                    zonen.Skip(1).Select(q => new MargenVerlustzone(q[0] * r.V, q[1] >= kMax - 1e-9 ? null : q[1] * r.V)).ToList());
        }
        else
            mindest = new MargenMindestbestand(MargenMindestbestandArt.KeineVerlustzone, null, null, false, []);

        // Nächste Staffel
        var n = StufeAn(r.Index + 1);
        var mehrkosten = (n.FullFile - r.T.FullFile) * r.Vf / r.V + (n.SanctionsOnly - r.T.SanctionsOnly) * r.Vs / r.V;

        // Kunden unter Einstand
        var kunden = 0;
        double luecke = 0;
        for (var i = 0; i < _d.Baskets.Count; i++)
        {
            double b = _d.Baskets[i].Cases;
            var ds = pS[i] - b * r.CS;
            var df = pF[i] - b * r.CF;
            if (ds < 0 && nS[i] > 0) { kunden += nS[i]; luecke += -ds * nS[i]; }
            if (df < 0 && nF[i] > 0) { kunden += nF[i]; luecke += -df * nF[i]; }
        }

        return new MargenGrenzwerte(
            r.Vk > 0 ? r.Ek / r.Vk - 1 : null,
            mindest,
            new MargenNaechsteStaffel(mehrkosten, r.T.Bis - r.V, n.Bis),
            kunden, luecke);
    }

    /// <summary>
    /// Verlustzonen, wenn der Bestand in gleicher Mischung skaliert wird: Faktor-Intervalle [von, bis).
    /// Reicht eine Zone bis zum Rechenhorizont, gilt sie als dauerhaft.
    /// </summary>
    private List<double[]> Verlustzonen(Bestand r)
    {
        var zonen = new List<double[]>();
        if (!(r.Vk > 0) || !(r.V > 0))
            return zonen;
        double sf = r.Vf / r.V, ss = r.Vs / r.V;
        for (var i = 0; StufeAb(i) < Horizont; i++)
        {
            var t = StufeAn(i);
            double lo = StufeAb(i) / r.V, hi = t.Bis / r.V, kb = (t.FullFile * sf + t.SanctionsOnly * ss) / r.Vk;
            if (kb > lo)
            {
                var b = Math.Min(hi, kb);
                if (zonen.Count > 0 && Math.Abs(zonen[^1][1] - lo) < 1e-12)
                    zonen[^1][1] = b;
                else
                    zonen.Add([lo, b]);
            }
        }
        return zonen;
    }

    /// <summary>Volumenbereiche, in denen ein Basket seinen EK-Anteil nicht deckt: [von, bis) in Cases.</summary>
    private List<MargenSpanne> Ungedeckt(long basket, double preis, MargenBereich bereich)
    {
        var bereiche = new List<MargenSpanne>();
        for (var i = 0; StufeAb(i) < Horizont; i++)
        {
            double lo = StufeAb(i);
            var t = StufeAn(i);
            var noetig = t.Gebuehr(bereich) * basket / preis;
            if (noetig > lo)
            {
                var ende = Math.Min(t.Bis, noetig);
                if (bereiche.Count > 0 && bereiche[^1].Bis == lo)
                    bereiche[^1] = bereiche[^1] with { Bis = ende };
                else
                    bereiche.Add(new MargenSpanne(lo, ende));
            }
        }
        return bereiche;
    }

    /// <summary>
    /// Bestand nur aus einem Basket: Kundenzahlen mit Verlust als Bereiche [von, bis].
    /// Die Vorlage zählt jede Kundenzahl einzeln durch; hier je Stufe, mit demselben
    /// Ergebnis — innerhalb einer Stufe ist die Gebühr fest, der Verlust also ein
    /// zusammenhängender Anfang des Kundenbereichs der Stufe.
    /// </summary>
    private (List<MargenKundenSpanne> Zonen, long NMax) AlleinVerlust(long basket, double preis, MargenBereich bereich)
    {
        var nMax = (long)Math.Floor(Horizont / basket);
        var zonen = new List<MargenKundenSpanne>();
        for (var i = 0; StufeAb(i) < Horizont; i++)
        {
            var t = StufeAn(i);
            long von = StufeAb(i) / basket + 1, bis = Math.Min(t.Bis / basket, nMax);
            if (von > bis)
                continue;
            var gebuehr = t.Gebuehr(bereich);
            bool Verlust(long n) => n * preis - gebuehr <= 0;
            if (!Verlust(von))
                continue;
            var ende = bis;
            if (!Verlust(bis))
            {
                // größte Kundenzahl der Stufe, die noch Verlust macht
                long a = von, b = bis;
                while (b - a > 1)
                {
                    var m = a + (b - a) / 2;
                    if (Verlust(m)) a = m; else b = m;
                }
                ende = a;
            }
            if (zonen.Count > 0 && zonen[^1].Bis == von - 1)
                zonen[^1] = zonen[^1] with { Bis = ende };
            else
                zonen.Add(new MargenKundenSpanne(von, ende));
        }
        return (zonen, nMax);
    }

    /// <summary>Marge über dem Gesamtvolumen bei gleicher Mischung — bis zur Tabellengrenze mit den Sprüngen an jeder Stufe.</summary>
    private List<MargenKurvenpunkt> Kurve(Bestand r)
    {
        var punkte = new List<MargenKurvenpunkt>();
        if (!(r.Vk > 0) || !(r.V > 0))
            return punkte;
        double a = r.Vk / r.V, sf = r.Vf / r.V, ss = r.Vs / r.V;
        for (var i = 0; StufeAb(i) < KurveBis; i++)
        {
            var t = StufeAn(i);
            double lo = Math.Max(KurveVon, StufeAb(i)), hi = Math.Min(KurveBis, t.Bis);
            if (hi <= KurveVon)
                continue;
            var kosten = t.FullFile * sf + t.SanctionsOnly * ss;
            var schritte = i == 0 || hi / lo > 1.3 ? 24 : i < _nt ? 3 : 1;
            for (var s = i < _nt ? 0 : 1; s <= schritte; s++)
            {
                var v = Math.Exp(Math.Log(lo) + (Math.Log(hi) - Math.Log(lo)) * s / schritte);
                punkte.Add(new MargenKurvenpunkt(v, Math.Max(-1.25, 1 - kosten / (a * v)), t.Bis, kosten));
            }
        }
        return punkte;
    }

    private MargenBefunde Befunde(Bestand r, int[] nS, int[] nF, double[] pS, double[] pF)
    {
        var baskets = _d.Baskets;
        var ok = r.V > 0;

        // Unabhängig vom Bestand: Baskets, deren VK je Case unter dem Einkauf je Case oberhalb des
        // letzten Ankers liegt, und Baskets, die sich erst bei großem Gesamtvolumen selbst tragen
        var dauerhaft = new List<MargenDauerhaftUnterEinkauf>();
        var spaet = new List<MargenSpaetKostendeckend>();
        var mit = new List<MargenBasketBereich>();
        var ohne = new List<MargenBasketBereich>();
        var senkt = new List<MargenWeitererKunde>();
        for (var i = 0; i < baskets.Count; i++)
        {
            var b = baskets[i].Cases;
            foreach (var (bereich, p, n, c) in new[]
                     {
                         (MargenBereich.SanctionsOnly, pS[i], nS[i], r.CS),
                         (MargenBereich.FullFile, pF[i], nF[i], r.CF),
                     })
            {
                if (p / b < _rateUnendlich)
                    dauerhaft.Add(new MargenDauerhaftUnterEinkauf(b, bereich, p, Math.Ceiling(_rateUnendlich * b)));
                else
                {
                    var u = Ungedeckt(b, p, bereich);
                    var ende = u.Count > 0 ? u[^1].Bis : 0;
                    if (ende > 1e6 && ende < Horizont)
                        spaet.Add(new MargenSpaetKostendeckend(b, bereich, ende));
                }
                if (!ok)
                    continue;
                // Baskets unter Einstand beim aktuellen Volumen
                if (p - b * c < 0)
                    (n > 0 ? mit : ohne).Add(new MargenBasketBereich(b, bereich));
                // Weitere Kunden, die den Gewinn senken
                var fullFile = bereich == MargenBereich.FullFile;
                var d = p - (Einkauf(r.Vf + (fullFile ? b : 0), r.Vs + (fullFile ? 0 : b)) - r.Ek);
                if (d < 0)
                    senkt.Add(new MargenWeitererKunde(b, bereich, d));
            }
        }

        // Marge bei großem Volumen in gleicher Mischung
        var anker = _d.LinearAnker;
        var margen = new List<MargenAnkerMarge>();
        double? margeUnendlich = null;
        if (ok && r.Vk > 0)
        {
            var a = r.Vk / r.V;
            for (var j = 1; j < anker.Count - 1; j++)
                margen.Add(new MargenAnkerMarge(anker[j].Cases, 1 - anker[j].Gebuehr / (a * anker[j].Cases)));
            margeUnendlich = 1 - _rateUnendlich / a;
        }

        return new MargenBefunde(dauerhaft, spaet, _rateUnendlich, anker[^1].Cases,
            mit, ohne, senkt.OrderBy(x => x.Aenderung).ToList(),
            margen, margeUnendlich, _linVon, MaxAnstiegJeStufe());
    }

    private double MaxAnstiegJeStufe()
    {
        var anker = _d.LinearAnker;
        var max = _rateUnendlich * _schritt;
        for (var j = 1; j < anker.Count; j++)
            max = Math.Max(max, AnstiegJeStufe(anker[j - 1], anker[j]));
        return max;
    }

    private double AnstiegJeStufe(MargenAnker von, MargenAnker bis) =>
        (bis.Gebuehr - von.Gebuehr) / ((double)(bis.Cases - von.Cases) / _schritt);

    private List<MargenVerteilung> Verteilungen(double[] pS, double[] pF)
    {
        var liste = new List<MargenVerteilung>();
        var baskets = _d.Baskets.Select(b => b.Cases).ToList();
        foreach (var v in MargenBeispiele.Verteilungen)
        {
            if (MargenBeispiele.Kunden(baskets, v.SanctionsOnly) is not { } nS
                || MargenBeispiele.Kunden(baskets, v.FullFile) is not { } nF)
                continue;   // Verteilung nutzt einen Basket, den es in den Daten nicht gibt
            var r = Rechne(nS, nF, pS, pF);
            liste.Add(new MargenVerteilung(v.Name, v.Beschreibung, r.V, r.T.Bis, r.Vk, r.Ek, r.Gewinn, [.. nS], [.. nF]));
        }
        return liste;
    }

    private List<MargenBasketGrenze> BasketGrenzen(Bestand r, double[] pS, double[] pF)
    {
        MargenBasketGrenzeBereich Grenze(long b, double p, MargenBereich bereich)
        {
            // trägt sich ab Gesamtvolumen
            var u = Ungedeckt(b, p, bereich);
            var jetzt = r.V > 0 && u.Any(z => z.Von < r.V && r.V < z.Bis);
            var offen = u.Count > 0 && u[^1].Bis >= Horizont;
            var geschlossen = offen ? u.Take(u.Count - 1).ToList() : u;
            MargenTraegtSich traegt;
            if (offen && u.Count == 1)
                traegt = new MargenTraegtSich(true, false, 0, [], false, null, jetzt);
            else
            {
                var abNull = geschlossen.Count > 0 && geschlossen[0].Von == 0;
                var erste = abNull ? geschlossen[0].Bis : 0;
                var luecken = abNull ? geschlossen.Skip(1).ToList() : geschlossen;
                traegt = new MargenTraegtSich(false, erste <= b, erste,
                    luecken.Take(2).ToList(), luecken.Count > 2, offen ? u[^1].Von : null, jetzt);
            }

            // allein Gewinn ab
            var (z, nMax) = AlleinVerlust(b, p, bereich);
            var zOffen = z.Count > 0 && z[^1].Bis >= nMax;
            MargenAlleinGewinn allein;
            if (z.Count == 0)
                allein = new MargenAlleinGewinn(false, 1, [], false, null);
            else if (z.Count == 1 && z[0].Von == 1 && zOffen)
                allein = new MargenAlleinGewinn(true, 0, [], false, null);
            else
            {
                var abEins = z[0].Von == 1;
                var rest = abEins ? z.Skip(1).ToList() : z;
                var restGeschlossen = zOffen ? rest.Take(rest.Count - 1).ToList() : rest;
                allein = new MargenAlleinGewinn(false, abEins ? z[0].Bis + 1 : 1,
                    restGeschlossen.Take(2).ToList(), restGeschlossen.Count > 2, zOffen ? z[^1].Von : null);
            }
            return new MargenBasketGrenzeBereich(p / b, traegt, allein);
        }

        var liste = new List<MargenBasketGrenze>();
        for (var i = 0; i < _d.Baskets.Count; i++)
        {
            var b = _d.Baskets[i].Cases;
            liste.Add(new MargenBasketGrenze(b, Grenze(b, pS[i], MargenBereich.SanctionsOnly), Grenze(b, pF[i], MargenBereich.FullFile)));
        }
        return liste;
    }

    private MargenRechenweg Rechenweg()
    {
        var anker = _d.LinearAnker;
        return new MargenRechenweg(
            _nt, _d.Staffel[0].BisCases, _d.Staffel[^1].BisCases,
            _d.Staffel.Min(s => Math.Min(s.GebuehrFullFile, s.GebuehrSanctionsOnly)),
            _d.Staffel.Max(s => Math.Max(s.GebuehrFullFile, s.GebuehrSanctionsOnly)),
            _schritt,
            anker.Select((a, j) => new MargenRechenwegAnker(a.Cases, a.Gebuehr, j == 0 ? null : AnstiegJeStufe(anker[j - 1], a))).ToList(),
            _rateUnendlich * _schritt,
            _d.Stand, _d.Hinweis);
    }
}

/// <summary>
/// Beispielbestand und kritische Verteilungen der Kalkulationsvorlage — nur
/// Kundenzahlen je Basket, keine Preise. Baskets, die es in den gepflegten
/// Daten nicht gibt, fallen weg (Beispielbestand) bzw. lassen die Verteilung
/// entfallen.
/// </summary>
public static class MargenBeispiele
{
    public sealed record Verteilung(string Name, string Beschreibung,
        IReadOnlyDictionary<long, int> SanctionsOnly, IReadOnlyDictionary<long, int> FullFile);

    private static readonly Dictionary<long, int> Leer = [];

    public static readonly IReadOnlyDictionary<long, int> BeispielSanctionsOnly = new Dictionary<long, int>
    {
        [1_000] = 60, [2_500] = 40, [5_000] = 30, [10_000] = 20, [15_000] = 10, [20_000] = 8,
        [25_000] = 6, [30_000] = 4, [40_000] = 3, [50_000] = 2, [100_000] = 1,
    };

    public static readonly IReadOnlyDictionary<long, int> BeispielFullFile = new Dictionary<long, int>
    {
        [1_000] = 20, [2_500] = 15, [5_000] = 10, [10_000] = 8, [15_000] = 4, [20_000] = 3,
        [25_000] = 2, [30_000] = 1, [40_000] = 1, [50_000] = 1,
    };

    public static readonly IReadOnlyList<Verteilung> Verteilungen =
    [
        new("Wenige kleine Kunden", "50 Kunden mit 1.000er Basket Sanctions Only",
            new Dictionary<long, int> { [1_000] = 50 }, Leer),
        new("Wenige mittelgroße Full-File-Kunden", "4 Kunden mit 100.000er Basket Full File",
            Leer, new Dictionary<long, int> { [100_000] = 4 }),
        new("Wenige große Full-File-Kunden", "3 Kunden mit 500.000er Basket Full File",
            Leer, new Dictionary<long, int> { [500_000] = 3 }),
        new("Kleinkunden mit einigen Großkunden", "20 Kunden mit 1.000er und 5 Kunden mit 500.000er Basket Sanctions Only",
            new Dictionary<long, int> { [1_000] = 20, [500_000] = 5 }, Leer),
        new("Nur große Sanctions-Only-Baskets", "10 Kunden mit 500.000er Basket Sanctions Only",
            new Dictionary<long, int> { [500_000] = 10 }, Leer),
        new("Viele große Sanctions-Only-Baskets", "20 Kunden mit 500.000er Basket Sanctions Only",
            new Dictionary<long, int> { [500_000] = 20 }, Leer),
    ];

    /// <summary>Kundenzahlen in Basket-Reihenfolge; null, wenn ein benötigter Basket fehlt.</summary>
    public static int[]? Kunden(IReadOnlyList<long> baskets, IReadOnlyDictionary<long, int> jeBasket)
    {
        if (jeBasket.Keys.Any(k => !baskets.Contains(k)))
            return null;
        return baskets.Select(b => jeBasket.TryGetValue(b, out var n) ? n : 0).ToArray();
    }

    /// <summary>Beispielbestand in Basket-Reihenfolge; Baskets außerhalb der Daten bleiben weg.</summary>
    public static int[] Beispiel(IReadOnlyList<long> baskets, IReadOnlyDictionary<long, int> jeBasket) =>
        baskets.Select(b => jeBasket.TryGetValue(b, out var n) ? n : 0).ToArray();
}
