using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.OdooImport;

namespace Rausoft.Erp.Tests.OdooImport;

/// <summary>
/// Filterregeln „nur das Wesentliche" — bewertet am Beispielkunden 25757
/// (Entscheidungen 22.10.2026). Analyse und Import nutzen dieselbe Klasse.
/// </summary>
public class ChatterFilterTests
{
    private const int Mitarbeiter = 10;
    private const int Kunde = 20;
    private const int Bot = 2;

    private static readonly ChatterFilter Filter = new(new HashSet<int> { Mitarbeiter }, Bot);

    private static ChatterNachricht Nachricht(
        string messageType = "comment",
        string? subtypeXmlId = null,
        int? autor = Mitarbeiter,
        string? betreff = null,
        string klartext = "Kunde kommt über Consys.",
        string modell = "res.partner",
        bool intern = false,
        int trackingAnzahl = 0,
        int anhaenge = 0,
        IReadOnlyList<(string Feld, string Alt, string Neu)>? aenderungen = null) =>
        new(Id: 1, Modell: modell, ResId: 1, RecordName: null, MessageType: messageType,
            SubtypeId: null, SubtypeXmlId: subtypeXmlId, SubtypeName: subtypeXmlId,
            AutorPartnerId: autor, AutorName: autor == Bot ? "OdooBot" : "Jemand", EmailFrom: null,
            Datum: new DateTime(2026, 9, 16, 9, 0, 0), Betreff: betreff, Klartext: klartext,
            IstIntern: intern, TrackingAnzahl: trackingAnzahl, AnhangAnzahl: anhaenge,
            Aenderungen: aenderungen ?? []);

    [Fact]
    public void Notiz_WirdBehalten_AlsInterneNotiz()
    {
        var b = Filter.Bewerten(Nachricht(subtypeXmlId: "mail.mt_note"));
        Assert.Equal(ChatterEntscheidung.Behalten, b.Entscheidung);
        Assert.Equal(AktivitaetsTyp.Notiz, b.Typ);
        Assert.Equal(AktivitaetsRichtung.Intern, b.Richtung);
    }

    [Fact]
    public void GesendeteNachricht_IstAusgehendeEMail()
    {
        var b = Filter.Bewerten(Nachricht(subtypeXmlId: "mail.mt_comment", klartext: "Anbei unser Angebot."));
        Assert.Equal(ChatterEntscheidung.Behalten, b.Entscheidung);
        Assert.Equal(AktivitaetsTyp.EMail, b.Typ);
        Assert.Equal(AktivitaetsRichtung.Ausgehend, b.Richtung);
    }

    [Fact]
    public void EingehendeEMailVomKunden_IstEingehend()
    {
        var b = Filter.Bewerten(Nachricht(messageType: "email", autor: Kunde,
            betreff: "AW: Auftragsbestätigung", klartext: "Im Anhang der unterschriebene Vertrag."));
        Assert.Equal(ChatterEntscheidung.Behalten, b.Entscheidung);
        Assert.Equal(AktivitaetsRichtung.Eingehend, b.Richtung);
    }

    [Fact]
    public void Abwesenheitsnotiz_WirdVerworfen()
    {
        var b = Filter.Bewerten(Nachricht(messageType: "email", autor: Kunde,
            betreff: "Automatische Antwort: Ihre Bestellung", klartext: "Thank you for your message."));
        Assert.Equal(ChatterEntscheidung.Verwerfen, b.Entscheidung);
    }

    [Fact]
    public void Feldaenderung_Odoo19_Tracking_WirdVerworfen()
    {
        var b = Filter.Bewerten(Nachricht(messageType: "tracking", trackingAnzahl: 1,
            aenderungen: [("KundenNr (Contact)", "0", "25757")]));
        Assert.Equal(ChatterEntscheidung.Verwerfen, b.Entscheidung);
        Assert.Equal("Feldänderung", b.Grund);
    }

    [Fact]
    public void WartungsHaken_WirdVerdichtet()
    {
        var b = Filter.Bewerten(Nachricht(messageType: "tracking", trackingAnzahl: 2,
            aenderungen: [("Wartung (Contact)", "0", "1"), ("Sanktionslisten (Contact)", "0", "1")]));
        Assert.Equal(ChatterEntscheidung.Verdichten, b.Entscheidung);
        Assert.Equal(AktivitaetsTyp.System, b.Typ);
    }

    [Fact]
    public void Auftragsstatus_WirdVerdichtet()
    {
        var b = Filter.Bewerten(Nachricht(messageType: "tracking", modell: "sale.order", trackingAnzahl: 1,
            aenderungen: [("Status (Sales Order)", "Angebot gesendet", "Verkaufsauftrag")]));
        Assert.Equal(ChatterEntscheidung.Verdichten, b.Entscheidung);
        Assert.Equal(AktivitaetsTyp.Angebot, b.Typ);
    }

    [Fact]
    public void RechnungBezahlt_AnDerRechnung_WirdVerdichtet()
    {
        var b = Filter.Bewerten(Nachricht(messageType: "tracking", modell: "account.move",
            subtypeXmlId: "account.mt_invoice_paid", trackingAnzahl: 1,
            aenderungen: [("Payment Status (Journal Entry)", "Nicht bezahlt", "Bezahlt")]));
        Assert.Equal(ChatterEntscheidung.Verdichten, b.Entscheidung);
        Assert.Equal(AktivitaetsTyp.Rechnung, b.Typ);
    }

    [Fact]
    public void RechnungBezahlt_MeldungAmAuftrag_IstDuplikat_WirdVerworfen()
    {
        var b = Filter.Bewerten(Nachricht(messageType: "notification", modell: "sale.order",
            klartext: "Rechnung RE/2026/00774 bezahlt"));
        Assert.Equal(ChatterEntscheidung.Verwerfen, b.Entscheidung);
    }

    [Fact]
    public void RechnungsEntwurf_WirdVerworfen()
    {
        var b = Filter.Bewerten(Nachricht(messageType: "notification", modell: "account.move",
            subtypeXmlId: "account.mt_invoice_created", klartext: ""));
        Assert.Equal(ChatterEntscheidung.Verwerfen, b.Entscheidung);
    }

    [Fact]
    public void ArchivierterVorgang_MeilensteinWeg_NotizBleibt()
    {
        var archiviert = Nachricht(modell: "crm.lead",
            klartext: "Dieser Lead wurde aus einem Ticket erstellt: Automatische Antwort #(00333)",
            aenderungen: null) with { QuelleArchiviert = true };
        Assert.Equal(ChatterEntscheidung.Verwerfen, Filter.Bewerten(archiviert).Entscheidung);

        var notiz = Nachricht(modell: "crm.lead", subtypeXmlId: "mail.mt_note", klartext: "Echte Notiz") with { QuelleArchiviert = true };
        Assert.Equal(ChatterEntscheidung.Behalten, Filter.Bewerten(notiz).Entscheidung);
    }

    [Fact]
    public void GesendeteNachricht_VonFirmenpostfach_IstTrotzdemAusgehend()
    {
        var b = Filter.Bewerten(Nachricht(subtypeXmlId: "mail.mt_comment", autor: 99,
            betreff: "Ihre Rechnung: RE/2026/00774", klartext: "Anbei unsere Rechnung."));
        Assert.Equal(AktivitaetsRichtung.Ausgehend, b.Richtung);
    }

    [Fact]
    public void Signaturbild_WirdErkannt()
    {
        Assert.True(ChatterFilter.IstSignaturbild("image001.jpg", "image/jpeg", 72 * 1024));
        Assert.True(ChatterFilter.IstSignaturbild("Outlook-abc123.png", "image/png", 10 * 1024));
        Assert.False(ChatterFilter.IstSignaturbild("Angebot - S01194.pdf", "application/pdf", 149 * 1024));
        Assert.False(ChatterFilter.IstSignaturbild("Screenshot Fehler.png", "image/png", 50 * 1024));
        Assert.False(ChatterFilter.IstSignaturbild("image001.jpg", "image/jpeg", 2 * 1024 * 1024));
    }

    [Fact]
    public void KontaktErstellt_WirdVerworfen()
    {
        var b = Filter.Bewerten(Nachricht(messageType: "notification", klartext: "Kontakt erstellt"));
        Assert.Equal(ChatterEntscheidung.Verwerfen, b.Entscheidung);
    }

    [Fact]
    public void OdooBot_WirdVerworfen()
    {
        var b = Filter.Bewerten(Nachricht(messageType: "auto_comment", autor: Bot, klartext: "Sehr geehrte Damen und Herren"));
        Assert.Equal(ChatterEntscheidung.Verwerfen, b.Entscheidung);
    }

    [Fact]
    public void CrmGewonnen_WirdVerdichtet()
    {
        var b = Filter.Bewerten(Nachricht(messageType: "tracking", modell: "crm.lead", subtypeXmlId: "crm.mt_lead_won"));
        Assert.Equal(ChatterEntscheidung.Verdichten, b.Entscheidung);
    }

    [Fact]
    public void LeadAusTicket_WirdVerdichtet()
    {
        var b = Filter.Bewerten(Nachricht(modell: "crm.lead",
            klartext: "Dieser Lead wurde aus einem Ticket erstellt: Automatische Antwort #(00333)"));
        Assert.Equal(ChatterEntscheidung.Verdichten, b.Entscheidung);
    }

    [Fact]
    public void LeereNotizOhneAnhang_WirdVerworfen_MitAnhangBehalten()
    {
        Assert.Equal(ChatterEntscheidung.Verwerfen, Filter.Bewerten(Nachricht(klartext: "")).Entscheidung);
        Assert.Equal(ChatterEntscheidung.Behalten, Filter.Bewerten(Nachricht(klartext: "", anhaenge: 1)).Entscheidung);
    }

    /// <summary>
    /// Mailvorlagen füllen den Vorschautext mit unsichtbaren Zeichen auf (je Zeile ein
    /// U+034F, dazu Null-Breite-Zeichen). Ohne Bereinigung blieben daraus über hundert
    /// scheinbar leere Zeilen in der Akte stehen (Kundenantwort auf RE/2026/00826, 11.11.2026).
    /// </summary>
    [Fact]
    public void Klartext_EntferntUnsichtbareFuellzeichen_UndFasstLeerzeilenZusammen()
    {
        var fuellung = string.Concat(Enumerable.Repeat("<div>&#847; &zwnj; &nbsp;</div>", 140));
        var html = "<p>Der Rechnungsbetrag [...]</p>" + fuellung + "<p>Rechnung ansehen</p>"
                   + "<p>&shy;&shy;&shy;____</p><p>Dermapharm&#8203; AG&#65279;</p>";
        var text = ChatterFilter.Klartext(html);
        // Die 140 Füllzeilen schrumpfen auf eine einzige Leerzeile
        Assert.Equal("Der Rechnungsbetrag [...]\n\nRechnung ansehen\n____\nDermapharm AG", text);
        Assert.DoesNotContain('͏', text);
        Assert.DoesNotContain('­', text);
    }

    [Fact]
    public void Klartext_UnsichtbareZeichen_WerdenAuchRohEntfernt_EmojiBleibenGanz()
    {
        // Zeichen direkt im Text (nicht als Entity) und ein zusammengesetztes Emoji mit U+200D
        var familie = "\U0001F468‍\U0001F469‍\U0001F467";
        var text = ChatterFilter.Klartext($"<p>Hallo​ Welt {familie}</p><p>͏</p><p>‍</p><p>‎⁠</p><p>Ende</p>");
        Assert.Equal($"Hallo Welt {familie}\nEnde", text);
    }

    /// <summary>
    /// Standardmail aus dem CRM (15.07.2026): Der Quelltext ist eingerückt und umbrochen,
    /// fette Wörter und Links stehen auf eigenen Quelltextzeilen. Im Browser sind das
    /// keine Umbrüche — früher zerfiel der Satz an jedem fetten Wort (11.11.2026).
    /// </summary>
    [Fact]
    public void Klartext_QuelltextUmbruecheSindKeineZeilenumbrueche_AbsaetzeUndListenBleiben()
    {
        var html = """
            <div>
              <p>Sehr geehrte Frau Muster,</p>
              <p><br></p>
              <p>Anbei erhalten Sie wie gewünscht unsere
                 <strong>Infomappe</strong>
                 sowie unsere
                 <strong>Broschüre</strong>
                 mit kompakten Informationen.</p>
              <p><br></p>
              <p><strong>Ihre Vorteile auf einen Blick:</strong></p>
              <ul>
                <li>Beweissichere Dokumentation</li>
                <li><p>Keine Limitierung</p></li>
              </ul>
              <p>Die
                 <strong>Rausoft GmbH</strong>
                 ist spezialisiert.</p>
              <p><br></p>
              <p>Möchten Sie ID.prove live sehen?<br>
                 Gerne im Rahmen einer kurzen
                 <strong>Online-Präsentation</strong>
                 durch unsere Experten vor:
                 <a href="https://example.invalid/termin">Jetzt Präsentation buchen</a></p>
              <p>&nbsp;</p>
              <p>Mit freundlichen Grüßen<br></p>
            </div>
            """;
        var text = ChatterFilter.Klartext(html);
        Assert.Equal(
            "Sehr geehrte Frau Muster,\n\n" +
            "Anbei erhalten Sie wie gewünscht unsere Infomappe sowie unsere Broschüre mit kompakten Informationen.\n\n" +
            "Ihre Vorteile auf einen Blick:\n" +
            "• Beweissichere Dokumentation\n" +
            "• Keine Limitierung\n\n" +
            "Die Rausoft GmbH ist spezialisiert.\n\n" +
            "Möchten Sie ID.prove live sehen?\n" +
            "Gerne im Rahmen einer kurzen Online-Präsentation durch unsere Experten vor: Jetzt Präsentation buchen\n\n" +
            "Mit freundlichen Grüßen",
            text);
    }

    [Fact]
    public void Klartext_Listen_NummeriertUndVerschachtelt_LeererPunktFaerbtNichtAb()
    {
        var text = ChatterFilter.Klartext(
            "<ol><li>Eins<ul><li>Unterpunkt</li></ul></li><li>Zwei</li><li><br></li></ol><p>Danach</p>");
        Assert.Equal("1. Eins\n  • Unterpunkt\n2. Zwei\n\nDanach", text);
    }

    [Fact]
    public void Klartext_OutlookAbsaetze_BleibenEinzeilig_LeererAbsatzIstLeerzeile()
    {
        // Outlook: jede Zeile ein Absatz, Leerzeile als Absatz mit geschütztem Leerzeichen,
        // der Quelltext ist mitten im Satz umbrochen
        var html = "<html><head><style>p.MsoNormal {margin:0cm;}</style></head><body>" +
                   "<p class=MsoNormal>Hallo Herr Muster,<o:p></o:p></p>" +
                   "<p class=MsoNormal><o:p>&nbsp;</o:p></p>" +
                   "<p class=MsoNormal>vielen Dank für Ihre\r\nNachricht.<o:p></o:p></p>" +
                   "<p class=MsoNormal><o:p>&nbsp;</o:p></p><p class=MsoNormal><o:p>&nbsp;</o:p></p>" +
                   "<p class=MsoNormal>Viele Grüße<o:p></o:p></p></body></html>";
        var text = ChatterFilter.Klartext(html);
        Assert.Equal("Hallo Herr Muster,\n\nvielen Dank für Ihre Nachricht.\n\nViele Grüße", text);
    }

    [Fact]
    public void Klartext_Tabellenzellen_Vorformatiertes_UndReinerText()
    {
        Assert.Equal("Artikel Menge\nLizenz 5",
            ChatterFilter.Klartext("<table><tr><th>Artikel</th><th>Menge</th></tr><tr><td>Lizenz</td><td>5</td></tr></table>"));
        Assert.Equal("Zeile 1\nZeile 2", ChatterFilter.Klartext("<pre>Zeile 1\nZeile 2</pre>"));
        // Ohne HTML bleiben die Umbrüche, wie sie sind (höchstens eine Leerzeile)
        Assert.Equal("Zeile 1\nZeile 2\n\nZeile 3", ChatterFilter.Klartext("Zeile 1\r\nZeile 2\r\n\r\n\r\n\r\nZeile 3"));
    }

    [Fact]
    public void Klartext_MaskiertPasswort_AuchNachLeerzeile()
    {
        var text = ChatterFilter.Klartext("<p>Ihr Passwort:</p><p><br></p><p>Geheim123!</p><p>Viele Grüße</p>");
        Assert.DoesNotContain("Geheim123", text);
        Assert.Contains("Viele Grüße", text);
    }

    [Fact]
    public void Klartext_EntferntHtml_UndMaskiertPasswoerter()
    {
        var html = "<p>Sehr geehrter Herr Quint,</p><p>Link: https://shared.idprove.cloud<br>" +
                   "Benutzername: kai.quint@rhb-industries.de<br>Passwort: Geheim123!</p><p>Viele Gr&uuml;&szlig;e</p>";
        var text = ChatterFilter.Klartext(html);
        Assert.Contains("Benutzername: kai.quint@rhb-industries.de", text);
        Assert.Contains("Passwort: •••", text);
        Assert.DoesNotContain("Geheim123", text);
        Assert.Contains("Viele Grüße", text);
        Assert.DoesNotContain("<p>", text);
    }

    [Fact]
    public void Klartext_MaskiertEinmalLink_AuchInFolgezeile()
    {
        var html = "<p>Benutzername: alexander.braun@rhb-industries.de<br>Passwort-Einmal-Link:<br>" +
                   "https://keepersecurity.eu/vault/share/#RgOwxDKW0aPotoThetm/lang/de_DE<br>" +
                   "Bitte beachten Sie, dass der Passwort-Link nur 90 Tage gültig ist.</p>";
        var text = ChatterFilter.Klartext(html);
        Assert.DoesNotContain("keepersecurity", text);
        Assert.DoesNotContain("RgOwxDKW0a", text);
        Assert.Contains("Passwort-Einmal-Link:", text);
        Assert.Contains("Benutzername: alexander.braun@rhb-industries.de", text);
        Assert.Contains("nur 90 Tage gültig", text);
    }

    [Fact]
    public void Klartext_MaskiertBekannteEinmalLinksUeberall()
    {
        var text = ChatterFilter.ZugangsdatenMaskieren("Hier der Link: https://onetimesecret.com/secret/abc123 bitte öffnen");
        Assert.DoesNotContain("abc123", text);
        Assert.Contains("Einmal-Link entfernt", text);
    }

    [Theory]
    [InlineData(ChatterEntscheidung.Behalten, "Notiz", AktivitaetsStufe.Wesentlich)]
    [InlineData(ChatterEntscheidung.Verdichten, "Rechnungs-Ereignis (Paid)", AktivitaetsStufe.Meilenstein)]
    [InlineData(ChatterEntscheidung.Verwerfen, "Feldänderung", AktivitaetsStufe.Feldaenderung)]
    [InlineData(ChatterEntscheidung.Verwerfen, "Benachrichtigung", AktivitaetsStufe.Systemmeldung)]
    [InlineData(ChatterEntscheidung.Verwerfen, "OdooBot", AktivitaetsStufe.Systemmeldung)]
    public void StufeVon_OrdnetEntscheidungZu(ChatterEntscheidung entscheidung, string grund, AktivitaetsStufe erwartet) =>
        Assert.Equal(erwartet, ChatterFilter.StufeVon(new ChatterBewertung(entscheidung, grund)));

    [Theory]
    [InlineData(AktivitaetsStufe.Wesentlich, AktivitaetsTyp.EMail, VerlaufKategorie.EMails)]
    [InlineData(AktivitaetsStufe.Wesentlich, AktivitaetsTyp.Notiz, VerlaufKategorie.Notizen)]
    [InlineData(AktivitaetsStufe.Wesentlich, AktivitaetsTyp.Vertrag, VerlaufKategorie.Meilensteine)]
    [InlineData(AktivitaetsStufe.Meilenstein, AktivitaetsTyp.Rechnung, VerlaufKategorie.Meilensteine)]
    [InlineData(AktivitaetsStufe.Systemmeldung, AktivitaetsTyp.EMail, VerlaufKategorie.Systemmeldungen)]
    [InlineData(AktivitaetsStufe.Feldaenderung, AktivitaetsTyp.System, VerlaufKategorie.Feldaenderungen)]
    public void Kategorie_AusStufeUndTyp(AktivitaetsStufe stufe, AktivitaetsTyp typ, VerlaufKategorie erwartet) =>
        Assert.Equal(erwartet, VerlaufKategorien.Von(stufe, typ));

    [Theory]
    [InlineData("\"Daniel Stauch\" <stauch.daniel@example.de>", "Daniel Stauch")]
    [InlineData("Anja Berger <a.berger@example.de>", "Anja Berger")]
    [InlineData("<info@example.de>", "info@example.de")]
    [InlineData("info@example.de", "info@example.de")]
    [InlineData("  ", null)]
    public void Absender_ZeigtNurDenNamen(string eingabe, string? erwartet) =>
        Assert.Equal(erwartet, Absender.Anzeigename(eingabe));

    [Theory]
    [InlineData("Verkaufschance", "Verkaufschance von RHB-Industries GmbH", "Verkaufschance von RHB-Industries GmbH")]
    [InlineData("Verkaufschance", "Muster Industries", "Verkaufschance Muster Industries")]
    [InlineData("Auftrag", "S01194", "Auftrag S01194")]
    [InlineData("Rechnung", null, "Rechnung")]
    public void VorgangsName_KeinDoppelterPraefix(string praefix, string? name, string erwartet) =>
        Assert.Equal(erwartet, VorgangsName.MitPraefix(praefix, name));

    [Fact]
    public void VorgangsName_HerkunftMehrererAuftraege() =>
        Assert.Equal(["S01194", "S01195"], VorgangsName.Herkunft("S01194, S01195").ToArray());

    [Fact]
    public void ErledigteOdooAktivitaet_IstAufgabeImVerlauf()
    {
        var b = Filter.Bewerten(Nachricht(messageType: "notification", subtypeXmlId: "mail.mt_activities",
            klartext: "Anruf erledigt\nRückmeldung: Kunde will 24 Monate"));
        Assert.Equal(ChatterEntscheidung.Behalten, b.Entscheidung);
        Assert.Equal(AktivitaetsTyp.Aufgabe, b.Typ);
        Assert.Equal(VerlaufKategorie.Notizen, VerlaufKategorien.Von(ChatterFilter.StufeVon(b), b.Typ!.Value));
    }

    [Theory]
    [InlineData(AktivitaetsTyp.Termin)]
    [InlineData(AktivitaetsTyp.Aufgabe)]
    [InlineData(AktivitaetsTyp.Anruf)]
    public void GespraecheUndAufgaben_ZaehlenZurKommunikation(AktivitaetsTyp typ) =>
        Assert.Equal(VerlaufKategorie.Notizen, VerlaufKategorien.Von(AktivitaetsStufe.Wesentlich, typ));

    [Fact]
    public void Standard_SindNurNotizenUndEMails() =>
        Assert.Equal([VerlaufKategorie.Notizen, VerlaufKategorie.EMails], VerlaufKategorien.Standard);

    [Fact]
    public void KategorienFilter_StimmtMitVonUeberein()
    {
        // Der EF-Ausdruck muss dieselbe Zuordnung treffen wie Von() — alle Kombinationen prüfen
        foreach (var gewaehlt in Enum.GetValues<VerlaufKategorie>())
        {
            var filter = VerlaufKategorien.Filter([gewaehlt]).Compile();
            foreach (var stufe in Enum.GetValues<AktivitaetsStufe>())
            foreach (var typ in Enum.GetValues<AktivitaetsTyp>())
            {
                var eintrag = new Aktivitaet { Betreff = "x", Stufe = stufe, Typ = typ };
                Assert.Equal(VerlaufKategorien.Von(stufe, typ) == gewaehlt, filter(eintrag));
            }
        }
    }

    [Fact]
    public void Klartext_MaskiertAuchKennwortUndEnglisch()
    {
        var text = ChatterFilter.Klartext("Kennwort = abc\nPassword: xyz\nPW: 123\nHinweis: nichts");
        Assert.Equal("Kennwort = •••\nPassword: •••\nPW: •••\nHinweis: nichts", text);
    }
}
