using Rausoft.Erp.Domain.Customers;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Dtos;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>Teilnehmer eines Verlaufseintrags, wie EF ihn aus der Datenbank liest.</summary>
public record TeilnehmerRoh(
    TeilnehmerSeite Seite,
    int? MitarbeiterId,
    string? MitarbeiterName,
    string? Bereich,
    int? AnsprechpartnerId,
    Anrede? ApAnrede,
    string? ApVorname,
    string? ApNachname,
    string? ApPosition,
    string? Name);

/// <summary>
/// Bildet aus Autor-Feldern und Teilnehmer-Zeilen die beiden Seiten eines Eintrags —
/// eine Stelle für Verlauf, Überblick („Wer mit wem") und Vereinbarungen.
/// </summary>
public static class AkteTeilnehmer
{
    /// <summary>„Frau Berger" mit Anrede, sonst „Anja Berger".</summary>
    public static string AnsprechpartnerName(Anrede? anrede, string? vorname, string? nachname) =>
        anrede is Anrede.Herr or Anrede.Frau
            ? $"{anrede} {nachname}"
            : string.Join(' ', new[] { vorname, nachname }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public static (List<AktePerson> Rausoft, List<AktePerson> Kunde) Seiten(
        TeilnehmerRoh? autorMitarbeiter, TeilnehmerRoh? autorAnsprechpartner, string? autorName,
        AktivitaetsRichtung? richtung, IEnumerable<TeilnehmerRoh> teilnehmer)
    {
        var rausoft = new List<AktePerson>();
        var kunde = new List<AktePerson>();
        void Hinzu(TeilnehmerRoh t)
        {
            var person = Person(t);
            var liste = t.Seite == TeilnehmerSeite.Rausoft ? rausoft : kunde;
            if (person is not null && liste.All(p => p.Schluessel != person.Schluessel))
                liste.Add(person);
        }

        if (autorMitarbeiter is not null) Hinzu(autorMitarbeiter);
        if (autorAnsprechpartner is not null) Hinzu(autorAnsprechpartner);
        foreach (var t in teilnehmer)
            Hinzu(t);
        // Externer Absender ohne Zuordnung (eingehende Mail) — Kundenseite mit Namen
        if (kunde.Count == 0 && richtung == AktivitaetsRichtung.Eingehend
            && Absender.Anzeigename(autorName) is { } name)
            kunde.Add(new AktePerson($"n:{name}", name, null));
        return (rausoft, kunde);
    }

    private static AktePerson? Person(TeilnehmerRoh t)
    {
        if (t.MitarbeiterId is { } m && t.MitarbeiterName is not null)
            return new AktePerson($"m:{m}", t.MitarbeiterName, t.Bereich);
        if (t.AnsprechpartnerId is { } a && t.ApNachname is not null)
            return new AktePerson($"a:{a}", AnsprechpartnerName(t.ApAnrede, t.ApVorname, t.ApNachname),
                string.IsNullOrWhiteSpace(t.ApPosition) ? null : t.ApPosition);
        return string.IsNullOrWhiteSpace(t.Name) ? null : new AktePerson($"n:{t.Name}", t.Name, null);
    }
}
