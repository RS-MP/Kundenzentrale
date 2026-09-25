using Microsoft.EntityFrameworkCore;
using Rausoft.Erp.Data;
using Rausoft.Erp.Domain.Shared;
// Hinweis: Die .NET-Builds von Telerik DPL nutzen das Namespace-Schema
// Telerik.Documents.* — die Paket-/Assemblynamen heißen weiter Telerik.Windows.*.
using Telerik.Documents.Fixed.Model;
using Telerik.Documents.Fixed.Model.DigitalSignatures;
using Telerik.Documents.Fixed.Model.InteractiveForms;

namespace Rausoft.Erp.Api.Dienste;

/// <summary>
/// PDF-Erzeugung für Verträge (FEEDBACK Nr. 14 Stufe 1b, Telerik Document
/// Processing): wandelt die gefüllte Word-Datei in ein PDF, schreibt
/// Metadaten (Absender rausoft, Vertragsnummer, Vorlagen-Stand) und signiert
/// optional digital — dann ist jede nachträgliche Änderung an der Datei
/// sofort erkennbar (Signatur wird ungültig). Die Signatur braucht ein
/// PFX-Zertifikat (Einstellung Vertragserzeugung.SignaturZertifikat) und
/// dessen Passwort als verschlüsseltes Geheimnis (Signatur.PfxPasswort).
/// </summary>
public class PdfDienst(ErpDbContext db, GeheimnisDienst geheimnisse)
{
    public async Task<byte[]> DocxZuPdfAsync(byte[] docx, string vertragsnummer, string vorlagenStand,
        string kunde, CancellationToken ct)
    {
        // 1. Word → PDF (Flow-Modell)
        var flowDokument = new Telerik.Documents.Flow.FormatProviders.Docx.DocxFormatProvider()
            .Import(docx, null);
        var pdfRoh = new Telerik.Documents.Flow.FormatProviders.Pdf.PdfFormatProvider()
            .Export(flowDokument, null);

        // 2. Metadaten + optionale Signatur (Fixed-Modell)
        var fixedProvider = new Telerik.Documents.Fixed.FormatProviders.Pdf.PdfFormatProvider();
        var pdf = fixedProvider.Import(pdfRoh, null);
        pdf.DocumentInfo.Author = "rausoft GmbH";
        pdf.DocumentInfo.Title = $"Vertrag {vertragsnummer} — {kunde}";
        pdf.DocumentInfo.Description = $"Erstellt aus Vorlage {vorlagenStand} am {DateTime.Now:dd.MM.yyyy HH:mm} (Kundenzentrale).";

        var zertifikat = await ZertifikatLadenAsync(ct);
        if (zertifikat is not null)
        {
            var feld = new SignatureField("RausoftSignatur")
            {
                Signature = new Signature(zertifikat)
                {
                    Properties =
                    {
                        Reason = $"Vertrag {vertragsnummer} aus Vorlage {vorlagenStand}",
                        Location = "rausoft GmbH, Leonberg",
                    },
                },
            };
            // Unsichtbares Signatur-Widget — die Signatur gilt fürs ganze Dokument
            var widget = feld.Widgets.AddWidget();
            widget.Rect = new System.Windows.Rect(0, 0, 1, 1);
            pdf.Pages[0].Annotations.Add(widget);
            pdf.AcroForm.FormFields.Add(feld);
        }

        return fixedProvider.Export(pdf, null);
    }

    /// <summary>PFX laden, wenn konfiguriert; null = unsigniert erzeugen.</summary>
    private async Task<System.Security.Cryptography.X509Certificates.X509Certificate2?> ZertifikatLadenAsync(
        CancellationToken ct)
    {
        var pfad = (await db.Einstellungen.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Schluessel == EinstellungsSchluessel.VertragserzeugungSignaturZertifikat, ct))
            ?.Wert;
        if (string.IsNullOrWhiteSpace(pfad))
            return null;
        if (!File.Exists(pfad))
            throw new InvalidOperationException($"Signatur-Zertifikat nicht gefunden: {pfad}");
        var passwort = await geheimnisse.HolenAsync(GeheimnisSchluessel.SignaturPfxPasswort, ct)
            ?? throw new InvalidOperationException(
                "Das PFX-Passwort ist nicht gesetzt (Administration → Verzeichnisse → Signatur).");
        return System.Security.Cryptography.X509Certificates.X509CertificateLoader
            .LoadPkcs12FromFile(pfad, passwort);
    }
}
