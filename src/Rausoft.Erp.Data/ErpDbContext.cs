using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Rausoft.Erp.Domain.Contracts;
using Rausoft.Erp.Domain.Customers;
using Rausoft.Erp.Domain.Servers;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Domain.Subscriptions;
using Rausoft.Erp.Domain.Vorgaenge;
using Rausoft.Erp.Domain.Wiki;

namespace Rausoft.Erp.Data;

public class ErpDbContext(
    DbContextOptions<ErpDbContext> options,
    IAktuellerBenutzer? aktuellerBenutzer = null) : DbContext(options)
{
    private bool _mitarbeiterIdErmittelt;
    private int? _mitarbeiterId;

    // Querschnitt
    public DbSet<Mitarbeiter> Mitarbeiter => Set<Mitarbeiter>();
    public DbSet<Bereich> Bereiche => Set<Bereich>();
    public DbSet<Nummernkreis> Nummernkreise => Set<Nummernkreis>();
    public DbSet<Aktivitaet> Aktivitaeten => Set<Aktivitaet>();
    public DbSet<AktivitaetAnhang> Aktivitaetsanhaenge => Set<AktivitaetAnhang>();
    public DbSet<AktivitaetTeilnehmer> AktivitaetTeilnehmer => Set<AktivitaetTeilnehmer>();
    public DbSet<Vereinbarung> Vereinbarungen => Set<Vereinbarung>();
    public DbSet<Aufgabe> Aufgaben => Set<Aufgabe>();
    public DbSet<AuditLogEintrag> AuditLog => Set<AuditLogEintrag>();
    public DbSet<Einstellung> Einstellungen => Set<Einstellung>();

    // Vorgänge
    public DbSet<Vorgang> Vorgaenge => Set<Vorgang>();
    public DbSet<Vorgangstyp> Vorgangstypen => Set<Vorgangstyp>();
    public DbSet<VorgangstypSchritt> VorgangstypSchritte => Set<VorgangstypSchritt>();

    // Kontakte
    public DbSet<Kunde> Kunden => Set<Kunde>();
    public DbSet<Partner> Partner => Set<Partner>();
    public DbSet<Ansprechpartner> Ansprechpartner => Set<Ansprechpartner>();
    public DbSet<Adresse> Adressen => Set<Adresse>();
    public DbSet<Merkmal> Merkmale => Set<Merkmal>();
    public DbSet<Anfrage> Anfragen => Set<Anfrage>();
    public DbSet<AnfrageOdooAnlage> AnfrageOdooAnlagen => Set<AnfrageOdooAnlage>();

    // Verträge
    public DbSet<Vertrag> Vertraege => Set<Vertrag>();
    public DbSet<Vertragsbestandteil> Vertragsbestandteile => Set<Vertragsbestandteil>();
    public DbSet<Vertragsdokument> Vertragsdokumente => Set<Vertragsdokument>();
    public DbSet<Vorlage> Vorlagen => Set<Vorlage>();
    public DbSet<VorlagenVersion> VorlagenVersionen => Set<VorlagenVersion>();

    // Wiki
    public DbSet<Sanktionsliste> Sanktionslisten => Set<Sanktionsliste>();
    public DbSet<SanktionslistenStandardprozess> SanktionslistenStandardprozesse => Set<SanktionslistenStandardprozess>();
    public DbSet<PreislistenVersion> PreislistenVersionen => Set<PreislistenVersion>();
    public DbSet<WikiThemaInfo> WikiThemaInfos => Set<WikiThemaInfo>();
    public DbSet<WikiNeuerung> WikiNeuerungen => Set<WikiNeuerung>();
    public DbSet<Zertifikat> Zertifikate => Set<Zertifikat>();
    public DbSet<ZertifikatEreignis> ZertifikatEreignisse => Set<ZertifikatEreignis>();
    public DbSet<WikiNeuerungLesung> WikiNeuerungLesungen => Set<WikiNeuerungLesung>();
    public DbSet<WikiMeldung> WikiMeldungen => Set<WikiMeldung>();
    public DbSet<FaqEintrag> FaqEintraege => Set<FaqEintrag>();
    public DbSet<Sprachregel> Sprachregeln => Set<Sprachregel>();

    // Abos
    public DbSet<Abo> Abos => Set<Abo>();
    public DbSet<AboPosition> AboPositionen => Set<AboPosition>();

    // Server & Geheimnisse
    public DbSet<Domain.Servers.Server> Server => Set<Domain.Servers.Server>();
    public DbSet<GeheimeEinstellung> GeheimeEinstellungen => Set<GeheimeEinstellung>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Konservativer Default; längere Felder werden gezielt konfiguriert.
        configurationBuilder.Properties<string>().HaveMaxLength(200);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // --- Querschnitt -----------------------------------------------------
        modelBuilder.Entity<Mitarbeiter>(e =>
        {
            e.HasIndex(m => m.EntraObjectId).IsUnique().HasFilter("[EntraObjectId] IS NOT NULL");
            e.HasIndex(m => m.WindowsKonto).IsUnique().HasFilter("[WindowsKonto] IS NOT NULL");
            e.Property(m => m.WindowsKonto).HasMaxLength(100);
            e.HasIndex(m => m.Email).IsUnique();
            e.Property(m => m.Anzeigename).HasMaxLength(100);
            e.Property(m => m.WikiThemen).HasMaxLength(500);
            e.HasOne(m => m.Bereich).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Bereich>(e =>
        {
            e.HasIndex(b => b.Name).IsUnique();
            e.Property(b => b.Name).HasMaxLength(100);
            e.HasOne(b => b.Leiter).WithMany().HasForeignKey(b => b.LeiterId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Nummernkreis>(e =>
        {
            e.HasIndex(n => n.Schluessel).IsUnique();
            e.Property(n => n.Schluessel).HasMaxLength(50);
            e.Property(n => n.Praefix).HasMaxLength(10);
            e.Property(n => n.Format).HasMaxLength(20);
            e.Property(n => n.RowVersion).IsRowVersion();
            // Anonyme Typen, damit die rowversion-Spalte nicht mitgeseedet wird.
            e.HasData(
                new { Id = 1, Schluessel = "Kunde", Praefix = "K", NaechsterWert = 1, Format = "D5" },
                new { Id = 2, Schluessel = "Vertrag", Praefix = "V", NaechsterWert = 1, Format = "D5" });
        });

        modelBuilder.Entity<Aktivitaet>(e =>
        {
            e.HasIndex(a => new { a.KundeId, a.Zeitpunkt }).IsDescending(false, true);
            e.Property(a => a.Betreff).HasMaxLength(300);
            e.Property(a => a.Inhalt).HasColumnType("nvarchar(max)");
            e.HasOne(a => a.Kunde).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.Mitarbeiter).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.Ansprechpartner).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.Property(a => a.AutorName).HasMaxLength(200);
            e.Property(a => a.Bezug).HasMaxLength(300);
            e.Property(a => a.FilterGrund).HasMaxLength(100);
            e.Property(a => a.Empfaenger).HasMaxLength(300);
            e.Property(a => a.StrangSchluessel).HasMaxLength(60);
            e.Property(a => a.StrangName).HasMaxLength(300);
            e.HasIndex(a => new { a.KundeId, a.StrangSchluessel });
            e.Property(a => a.OdooModell).HasMaxLength(50);
            e.HasIndex(a => new { a.KundeId, a.OdooModell, a.OdooResId });
            // Odoo-Nachricht nur einmal übernehmen (gefilterter Unique-Index: NULL mehrfach erlaubt)
            e.HasIndex(a => a.OdooId).IsUnique().HasFilter("[OdooId] IS NOT NULL");
            e.HasIndex(a => new { a.KundeId, a.Quelle });
            // Vorgangs-Detail, Akte-Aufgaben, Strang umhängen und Vertrag löschen filtern auf den
            // Verweis — ohne Index scannt jeder Aufruf die gesamte (Chatter-)Tabelle (SICHERHEIT.md Q1)
            e.HasIndex(a => new { a.VerweisTyp, a.VerweisId }).HasFilter("[VerweisId] IS NOT NULL");
        });

        modelBuilder.Entity<AktivitaetTeilnehmer>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(200);
            e.HasOne(t => t.Aktivitaet).WithMany(a => a.Teilnehmer)
                .HasForeignKey(t => t.AktivitaetId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.Mitarbeiter).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.Ansprechpartner).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Vereinbarung>(e =>
        {
            e.Property(v => v.Text).HasMaxLength(500);
            e.HasIndex(v => new { v.KundeId, v.Erledigt });
            e.HasOne(v => v.Kunde).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(v => v.Aktivitaet).WithMany().OnDelete(DeleteBehavior.SetNull);
            e.HasOne(v => v.ErstelltVon).WithMany().HasForeignKey(v => v.ErstelltVonId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(v => v.ErledigtVon).WithMany().HasForeignKey(v => v.ErledigtVonId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Aufgabe>(e =>
        {
            e.Property(a => a.Titel).HasMaxLength(300);
            e.Property(a => a.Art).HasMaxLength(50);
            e.Property(a => a.Beschreibung).HasColumnType("nvarchar(max)");
            e.Property(a => a.ZustaendigName).HasMaxLength(200);
            e.Property(a => a.Bezug).HasMaxLength(300);
            e.Property(a => a.StrangSchluessel).HasMaxLength(60);
            e.HasIndex(a => new { a.KundeId, a.Status });
            e.HasIndex(a => a.OdooId).IsUnique().HasFilter("[OdooId] IS NOT NULL");
            e.HasOne(a => a.Kunde).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.Zustaendig).WithMany().HasForeignKey(a => a.ZustaendigId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.ZustaendigBereich).WithMany().HasForeignKey(a => a.ZustaendigBereichId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.ErstelltVon).WithMany().HasForeignKey(a => a.ErstelltVonId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.ErledigtVon).WithMany().HasForeignKey(a => a.ErledigtVonId).OnDelete(DeleteBehavior.Restrict);
            // Vorgangsschritte
            e.Property(a => a.EntfallenGrund).HasMaxLength(20);
            e.Property(a => a.Pflichtfeld).HasMaxLength(100);
            e.Property(a => a.PflichtfeldWert).HasMaxLength(300);
            e.HasIndex(a => new { a.ZustaendigBereichId, a.Status });
            e.HasIndex(a => new { a.ZustaendigId, a.Status });
            e.HasOne(a => a.WartetAuf).WithMany().HasForeignKey(a => a.WartetAufId).OnDelete(DeleteBehavior.NoAction);
        });

        // --- Vorgänge (Bestellung → Teilaufgaben je Bereich) -----------------
        modelBuilder.Entity<Vorgangstyp>(e =>
        {
            e.HasIndex(t => t.Name).IsUnique();
            e.Property(t => t.Name).HasMaxLength(100);
            e.Property(t => t.Beschreibung).HasMaxLength(300);
            e.HasOne(t => t.GeaendertVon).WithMany().HasForeignKey(t => t.GeaendertVonId).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(t => t.Schritte).WithOne(s => s.Vorgangstyp!)
                .HasForeignKey(s => s.VorgangstypId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VorgangstypSchritt>(e =>
        {
            e.HasIndex(s => new { s.VorgangstypId, s.Nr }).IsUnique();
            e.Property(s => s.Titel).HasMaxLength(300);
            e.Property(s => s.Bedingung).HasMaxLength(100);
            e.Property(s => s.Pflichtfeld).HasMaxLength(100);
            e.HasOne(s => s.Bereich).WithMany().HasForeignKey(s => s.BereichId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Vorgang>(e =>
        {
            e.Property(v => v.Titel).HasMaxLength(200);
            e.Property(v => v.OdooAuftrag).HasMaxLength(30);
            e.Property(v => v.Hinweis).HasMaxLength(1000);
            e.Property(v => v.StrangSchluessel).HasMaxLength(60);
            e.Property(v => v.StrangName).HasMaxLength(300);
            e.HasIndex(v => v.AbgeschlossenAm);
            e.HasIndex(v => v.OdooAuftrag).HasFilter("[OdooAuftrag] IS NOT NULL");
            e.Property(v => v.RowVersion).IsRowVersion();
            e.HasOne(v => v.Kunde).WithMany().HasForeignKey(v => v.KundeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(v => v.Vorgangstyp).WithMany().HasForeignKey(v => v.VorgangstypId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(v => v.ErstelltVon).WithMany().HasForeignKey(v => v.ErstelltVonId).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(v => v.Schritte).WithOne(a => a.Vorgang)
                .HasForeignKey(a => a.VorgangId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AktivitaetAnhang>(e =>
        {
            e.Property(a => a.Dateiname).HasMaxLength(300);
            e.Property(a => a.MimeType).HasMaxLength(100);
            e.Property(a => a.RelativerPfad).HasMaxLength(500);
            e.HasIndex(a => a.OdooId).IsUnique().HasFilter("[OdooId] IS NOT NULL");
            e.HasOne(a => a.Aktivitaet).WithMany(a => a.Anhaenge)
                .HasForeignKey(a => a.AktivitaetId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditLogEintrag>(e =>
        {
            e.HasIndex(a => new { a.Entitaet, a.EntitaetId });
            e.Property(a => a.Entitaet).HasMaxLength(100);
            e.Property(a => a.AenderungenJson).HasColumnType("nvarchar(max)");
        });

        modelBuilder.Entity<Einstellung>(e =>
        {
            e.HasIndex(x => x.Schluessel).IsUnique();
            e.Property(x => x.Schluessel).HasMaxLength(100);
            e.Property(x => x.Wert).HasMaxLength(1000);
            e.Property(x => x.Beschreibung).HasMaxLength(500);
            e.HasData(
                new
                {
                    Id = 1,
                    Schluessel = EinstellungsSchluessel.VertragsablageVerzeichnis,
                    Wert = "",
                    Beschreibung = "Basisverzeichnis der Vertragsablage (UNC- oder lokaler Pfad). " +
                                   "Vertragsdokumente werden hier referenziert, Dateien bleiben im Verzeichnis.",
                },
                new
                {
                    Id = 2,
                    Schluessel = EinstellungsSchluessel.DokumentablageVerzeichnis,
                    Wert = "",
                    Beschreibung = "Basisverzeichnis für sonstige Dokumente (z. B. E-Mail-Anhänge ab Phase 2).",
                },
                new
                {
                    Id = 3,
                    Schluessel = EinstellungsSchluessel.VorlagenVerzeichnis,
                    Wert = @"\\RS-W2019-01\Kunden Archiv\0_Vorlagen",
                    Beschreibung = "Verzeichnis der Word-Vorlagen für die Vertragserzeugung. " +
                                   "Nur Dateien direkt im Verzeichnis gelten als aktive Vorlagen (keine Unterordner).",
                },
                new
                {
                    Id = 4,
                    Schluessel = EinstellungsSchluessel.VertragserzeugungZielverzeichnis,
                    Wert = @"\\RS-W2019-01\Kunden Archiv\2_Wartungsverträge",
                    Beschreibung = "Zielverzeichnis für aus Vorlagen erzeugte Verträge — muss innerhalb " +
                                   "des Vertragsablage-Verzeichnisses liegen.",
                },
                new
                {
                    Id = 5,
                    Schluessel = EinstellungsSchluessel.ServicePortalUrl,
                    Wert = "https://serviceportal.idprove.de",
                    Beschreibung = "Basis-URL des ID.prove Service Portals (Push-Ziel der Server-Status-Meldung).",
                },
                new
                {
                    Id = 6,
                    Schluessel = EinstellungsSchluessel.ServicePortalIntervallMinuten,
                    Wert = "0",
                    Beschreibung = "Automatische Server-Status-Meldung ans Portal alle n Minuten " +
                                   "(0 = aus; empfohlen 60 oder 120, sobald die Anwendung auf dem Server läuft).",
                });
            // ACHTUNG: KEINE weiteren HasData-Seeds mit festen IDs für Einstellungen!
            // Die Laufzeit legt selbst Zeilen an (z. B. ServicePortal.LetzterPush) und
            // belegt dabei die Identity-Werte — feste Seed-IDs kollidieren dann beim
            // Migrieren (PK-Verletzung, passiert 09/2026 mit Id 7 auf der Produktiv-DB).
            // Neue Einstellungen stattdessen per SQL-Migration mit IF NOT EXISTS auf
            // den Schlüssel einfügen (Beispiel: Migration OdooEinstellungen).
        });

        // --- Kontakte --------------------------------------------------------
        modelBuilder.Entity<Kunde>(e =>
        {
            e.HasIndex(k => k.Kundennummer).IsUnique();
            e.HasIndex(k => k.OdooId).IsUnique().HasFilter("[OdooId] IS NOT NULL");
            e.Property(k => k.Kundennummer).HasMaxLength(20);
            e.Property(k => k.UStIdNr).HasMaxLength(20);
            e.Property(k => k.Merkmale).HasMaxLength(500);
            e.Property(k => k.Individualentwicklung).HasMaxLength(2000);
            e.Property(k => k.MandantenKennung).HasMaxLength(100);
            e.HasOne(k => k.Server).WithMany()
                .HasForeignKey(k => k.ServerId).OnDelete(DeleteBehavior.SetNull);
            e.Property(k => k.Notiz).HasColumnType("nvarchar(max)");
            e.Property(k => k.RowVersion).IsRowVersion();
            e.HasOne(k => k.VermitteltDurchPartner).WithMany()
                .HasForeignKey(k => k.VermitteltDurchPartnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(k => k.RechnungsempfaengerPartner).WithMany()
                .HasForeignKey(k => k.RechnungsempfaengerPartnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(k => k.Ansprechpartner).WithOne(a => a.Kunde!)
                .HasForeignKey(a => a.KundeId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(k => k.Adressen).WithOne(a => a.Kunde!)
                .HasForeignKey(a => a.KundeId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Partner>(e =>
        {
            e.HasIndex(p => p.OdooId).IsUnique().HasFilter("[OdooId] IS NOT NULL");
            e.Property(p => p.Land).HasMaxLength(2).IsFixedLength();
            e.Property(p => p.ProvisionProzent).HasPrecision(5, 2);
            e.Property(p => p.RowVersion).IsRowVersion();
            // „Partner ist auch Kunde": höchstens ein Partner je Kunde
            e.HasIndex(p => p.KundeId).IsUnique().HasFilter("[KundeId] IS NOT NULL");
            e.HasOne(p => p.Kunde).WithMany().HasForeignKey(p => p.KundeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Ansprechpartner>(e =>
        {
            e.HasIndex(a => a.Email);
            e.Property(a => a.Titel).HasMaxLength(50);
            e.Property(a => a.Vorname).HasMaxLength(100);
            e.Property(a => a.Nachname).HasMaxLength(100);
            e.Property(a => a.TelefonFestnetz).HasMaxLength(50);
            e.Property(a => a.TelefonMobil).HasMaxLength(50);
            e.Property(a => a.Notiz).HasColumnType("nvarchar(max)");
            e.Property(a => a.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<Anfrage>(e =>
        {
            e.HasOne(a => a.Kunde).WithMany().HasForeignKey(a => a.KundeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.ErfasstVon).WithMany().HasForeignKey(a => a.ErfasstVonId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(a => a.EingegangenAm);
            e.HasIndex(a => a.Email);
            e.Property(a => a.Formular).HasMaxLength(100);
            e.Property(a => a.Website).HasMaxLength(200);
            e.Property(a => a.Firma).HasMaxLength(300);
            e.Property(a => a.Vorname).HasMaxLength(100);
            e.Property(a => a.Nachname).HasMaxLength(100);
            e.Property(a => a.Email).HasMaxLength(320);
            e.Property(a => a.Telefon).HasMaxLength(50);
            e.Property(a => a.Nachricht).HasMaxLength(500);
            e.Property(a => a.Quelle).HasMaxLength(100);
            e.Property(a => a.Medium).HasMaxLength(100);
            e.Property(a => a.Kampagne).HasMaxLength(200);
            e.Property(a => a.KampagnenNr).HasMaxLength(50);
            e.Property(a => a.Suchbegriff).HasMaxLength(200);
            e.Property(a => a.Anzeigenvariante).HasMaxLength(200);
            e.Property(a => a.Verweis).HasMaxLength(500);
            e.Property(a => a.Einstiegsseite).HasMaxLength(500);
            e.Property(a => a.LetzteSeite).HasMaxLength(500);
            e.Property(a => a.Gclid).HasMaxLength(500);
            e.Property(a => a.Gbraid).HasMaxLength(500);
            e.Property(a => a.Wbraid).HasMaxLength(500);
            e.Property(a => a.Msclkid).HasMaxLength(500);
            e.Property(a => a.VerlustGrund).HasMaxLength(200);
            e.HasIndex(a => a.OdooLeadId).IsUnique().HasFilter("[OdooLeadId] IS NOT NULL");
            e.Property(a => a.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<AnfrageOdooAnlage>(e =>
        {
            e.ToTable("AnfrageOdooAnlagen");
            // Mit der Anfrage verschwindet auch ihr Anlage-Stand
            e.HasOne(x => x.Anfrage).WithMany().HasForeignKey(x => x.AnfrageId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.BegonnenVon).WithMany().HasForeignKey(x => x.BegonnenVonId).OnDelete(DeleteBehavior.Restrict);
            // Je Anfrage und Odoo-Datenbank genau ein Stand — das verhindert auch zwei gleichzeitige Läufe
            e.HasIndex(x => new { x.AnfrageId, x.Datenbank }).IsUnique();
            e.Property(x => x.Datenbank).HasMaxLength(300);
            e.Property(x => x.LetzterFehler).HasMaxLength(300);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<Adresse>(e =>
        {
            e.Property(a => a.Plz).HasMaxLength(10);
            e.Property(a => a.Land).HasMaxLength(2).IsFixedLength();
            e.HasIndex(a => a.OdooId).IsUnique().HasFilter("[OdooId] IS NOT NULL");
        });

        modelBuilder.Entity<Domain.Servers.Server>(e =>
        {
            e.HasIndex(s => s.Alias).IsUnique();
            e.Property(s => s.Alias).HasMaxLength(100);
            e.Property(s => s.Url).HasMaxLength(300);
            e.Property(s => s.PruefMandant).HasMaxLength(100);
            e.Property(s => s.SanktionslistenStand).HasMaxLength(50);
            e.Property(s => s.Fehlertext).HasMaxLength(500);
            e.Property(s => s.ReleaseStand).HasMaxLength(50);
        });

        modelBuilder.Entity<GeheimeEinstellung>(e =>
        {
            e.HasIndex(g => g.Schluessel).IsUnique();
            e.Property(g => g.Schluessel).HasMaxLength(100);
            e.Property(g => g.WertVerschluesselt).HasMaxLength(2000);
        });

        modelBuilder.Entity<Merkmal>(e =>
        {
            e.HasIndex(m => m.Name).IsUnique();
            e.Property(m => m.Name).HasMaxLength(100);
            e.HasData(
                new { Id = 1, Name = "Insolvenz", Aktiv = true },
                new { Id = 2, Name = "Einmalkauf", Aktiv = true });
        });

        // --- Verträge --------------------------------------------------------
        modelBuilder.Entity<Vertrag>(e =>
        {
            e.HasIndex(v => v.Vertragsnummer).IsUnique();
            e.HasIndex(v => v.OdooId).IsUnique().HasFilter("[OdooId] IS NOT NULL");
            e.Property(v => v.Vertragsnummer).HasMaxLength(20);
            e.Property(v => v.Bezeichnung).HasMaxLength(VertragsGruppe.BezeichnungMaxLaenge);
            e.Property(v => v.Notiz).HasColumnType("nvarchar(max)");
            e.Property(v => v.RowVersion).IsRowVersion();
            e.HasOne(v => v.Kunde).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(v => v.Partner).WithMany().OnDelete(DeleteBehavior.Restrict);
            // Genau eine Vertragspartei: Kunde ODER Partner
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Vertraege_Partei",
                    "([KundeId] IS NULL AND [PartnerId] IS NOT NULL) OR ([KundeId] IS NOT NULL AND [PartnerId] IS NULL)");
                // Kein Vertrag ist sein eigener Anhang (übrige Regeln: VertragsGruppe)
                t.HasCheckConstraint("CK_Vertraege_KeinSelbstanhang",
                    "[HauptvertragId] IS NULL OR [HauptvertragId] <> [Id]");
            });
            // Zusammenfassen: Anhang → Hauptvertrag. Kein Kaskadieren — beim Löschen eines
            // Hauptvertrags werden die Anhänge vorher gelöst und stehen wieder für sich.
            e.HasOne(v => v.Hauptvertrag).WithMany(v => v.Anhaenge)
                .HasForeignKey(v => v.HauptvertragId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(v => v.Bestandteile).WithOne(b => b.Vertrag!)
                .HasForeignKey(b => b.VertragId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Vertragsbestandteil>(e =>
        {
            e.Property(b => b.Titel).HasMaxLength(200);
            e.Property(b => b.Beschreibung).HasColumnType("nvarchar(max)");
            e.Property(b => b.Kurzinfo).HasMaxLength(300);
            // Anlage → Hauptdokument innerhalb derselben Akte (Regeln: VertragsGruppe.AnlageFehler)
            e.HasOne(b => b.GehoertZu).WithMany(b => b.Anlagen)
                .HasForeignKey(b => b.GehoertZuId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_Vertragsbestandteile_KeineSelbstanlage",
                "[GehoertZuId] IS NULL OR [GehoertZuId] <> [Id]"));
            // Bestandteile mit Dokumenten sind nicht löschbar (Unveränderlichkeit).
            e.HasMany(b => b.Dokumente).WithOne(d => d.Vertragsbestandteil!)
                .HasForeignKey(d => d.VertragsbestandteilId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Abo>(e =>
        {
            e.HasIndex(a => a.OdooId).IsUnique().HasFilter("[OdooId] IS NOT NULL");
            e.Property(a => a.Nummer).HasMaxLength(20);
            e.HasOne(a => a.Kunde).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasMany(a => a.Positionen).WithOne(p => p.Abo!)
                .HasForeignKey(p => p.AboId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AboPosition>(e =>
        {
            e.Property(p => p.Produkt).HasMaxLength(300);
            e.Property(p => p.Menge).HasPrecision(9, 2);
        });

        modelBuilder.Entity<Vertragsdokument>(e =>
        {
            e.HasIndex(d => new { d.VertragsbestandteilId, d.Version }).IsUnique();
            e.Property(d => d.Dateipfad).HasMaxLength(500);
            e.Property(d => d.DateinameOriginal).HasMaxLength(260);
            e.Property(d => d.Sha256).HasMaxLength(64).IsFixedLength();
            e.HasOne(d => d.ErstelltVon).WithMany()
                .HasForeignKey(d => d.ErstelltVonId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(d => d.VorlagenVersion).WithMany()
                .HasForeignKey(d => d.VorlagenVersionId).OnDelete(DeleteBehavior.Restrict);
        });

        // --- Vertragsvorlagen (Freigabe-Versionierung) -----------------------
        modelBuilder.Entity<Vorlage>(e =>
        {
            e.HasIndex(v => v.Name).IsUnique();
            e.Property(v => v.Name).HasMaxLength(200);
        });
        // --- Wiki: Sanktionslisten ------------------------------------------
        modelBuilder.Entity<Sanktionsliste>(e =>
        {
            e.HasIndex(s => new { s.Region, s.Name }).IsUnique();
            e.Property(s => s.Region).HasMaxLength(10);
            e.Property(s => s.Kuerzel).HasMaxLength(30);
            e.Property(s => s.Name).HasMaxLength(250);
            e.Property(s => s.BezeichnungIntern).HasMaxLength(250);
            e.Property(s => s.Aliase).HasMaxLength(1000);
            e.Property(s => s.Beschreibung).HasColumnType("nvarchar(max)");
            e.Property(s => s.Relevanz).HasColumnType("nvarchar(max)");
            e.Property(s => s.Art).HasMaxLength(100);
            e.Property(s => s.Herausgeber).HasMaxLength(250);
            e.Property(s => s.Geltungsbereich).HasMaxLength(100);
            e.Property(s => s.DownloadUrl).HasMaxLength(500);
            e.Property(s => s.ImportHinweis).HasMaxLength(1000);
            e.Property(s => s.Prozess).HasColumnType("nvarchar(max)");
            e.Property(s => s.RowVersion).IsRowVersion();
            // Mitarbeiter werden i. d. R. deaktiviert; falls doch gelöscht: Zuständigkeit leeren
            e.HasOne(s => s.Verantwortlich).WithMany().HasForeignKey(s => s.VerantwortlichId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(s => s.Vertreter).WithMany().HasForeignKey(s => s.VertreterId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(s => s.GeaendertVon).WithMany().HasForeignKey(s => s.GeaendertVonId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<SanktionslistenStandardprozess>(e =>
        {
            e.HasKey(p => p.Import);
            e.Property(p => p.Import).ValueGeneratedNever();
            e.Property(p => p.Text).HasColumnType("nvarchar(max)");
            e.HasOne(p => p.GeaendertVon).WithMany().HasForeignKey(p => p.GeaendertVonId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<PreislistenVersion>(e =>
        {
            e.HasIndex(p => p.Version).IsUnique();
            e.Property(p => p.Stand).HasMaxLength(10);
            e.Property(p => p.Produkt).HasMaxLength(100);
            e.Property(p => p.Schema).HasMaxLength(50);
            e.Property(p => p.Daten).HasColumnType("nvarchar(max)");
            e.Property(p => p.Sha256).HasMaxLength(64).IsFixedLength();
            e.Property(p => p.DateiIntern).HasMaxLength(260);
            e.Property(p => p.DateiExtern).HasMaxLength(260);
            e.Property(p => p.Notiz).HasMaxLength(500);
            e.HasOne(p => p.ImportiertVon).WithMany().HasForeignKey(p => p.ImportiertVonId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<WikiThemaInfo>(e =>
        {
            e.HasKey(i => i.Thema);
            e.Property(i => i.Thema).HasMaxLength(50);
            e.Property(i => i.Prozess).HasColumnType("nvarchar(max)");
            e.Property(i => i.Abschnitte).HasMaxLength(500);
            e.Property(i => i.AbschnitteOffen).HasMaxLength(500);
            e.HasOne(i => i.Verantwortlich).WithMany().HasForeignKey(i => i.VerantwortlichId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(i => i.Vertreter).WithMany().HasForeignKey(i => i.VertreterId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(i => i.GeaendertVon).WithMany().HasForeignKey(i => i.GeaendertVonId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Zertifikat>(e =>
        {
            e.Property(z => z.Bezeichnung).HasMaxLength(200);
            e.Property(z => z.Hostname).HasMaxLength(255);
            e.Property(z => z.Anbieter).HasMaxLength(100);
            e.Property(z => z.Empfaenger).HasMaxLength(1000);
            e.Property(z => z.Notiz).HasMaxLength(2000);
            e.Property(z => z.LetzterMailFehler).HasMaxLength(1000);
            e.Property(z => z.LiveAussteller).HasMaxLength(300);
            e.Property(z => z.LiveFehler).HasMaxLength(500);
            e.Property(z => z.LiveProtokoll).HasMaxLength(20);
            e.Property(z => z.LiveWarnungen).HasMaxLength(1000);
            e.HasOne(z => z.Server).WithMany().HasForeignKey(z => z.ServerId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(z => z.ErneuertVon).WithMany().HasForeignKey(z => z.ErneuertVonId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<ZertifikatEreignis>(e =>
        {
            e.HasIndex(x => new { x.ZertifikatId, x.Zeitpunkt });
            e.Property(x => x.Art).HasMaxLength(30);
            e.Property(x => x.Text).HasMaxLength(2000);
            e.HasOne(x => x.Zertifikat).WithMany(z => z.Ereignisse).HasForeignKey(x => x.ZertifikatId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Von).WithMany().HasForeignKey(x => x.VonId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<WikiNeuerung>(e =>
        {
            e.HasIndex(n => n.Thema);
            e.Property(n => n.Thema).HasMaxLength(50);
            e.Property(n => n.Titel).HasMaxLength(200);
            e.Property(n => n.Text).HasColumnType("nvarchar(max)");
            e.HasOne(n => n.PreislistenVersion).WithMany().HasForeignKey(n => n.PreislistenVersionId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(n => n.ErstelltVon).WithMany().HasForeignKey(n => n.ErstelltVonId).OnDelete(DeleteBehavior.NoAction);
            // Wird die (zurückgezogene) Leitlinie gelöscht, bleibt die Neuerung mit ihren Lesebestätigungen als Nachweis stehen
            e.HasOne(n => n.Sprachregel).WithMany().HasForeignKey(n => n.SprachregelId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<WikiNeuerungLesung>(e =>
        {
            e.HasIndex(l => new { l.NeuerungId, l.MitarbeiterId }).IsUnique();
            e.HasOne(l => l.Neuerung).WithMany(n => n.Lesungen).HasForeignKey(l => l.NeuerungId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.Mitarbeiter).WithMany().HasForeignKey(l => l.MitarbeiterId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<WikiMeldung>(e =>
        {
            e.HasIndex(m => new { m.Thema, m.ErledigtAm });
            e.Property(m => m.Thema).HasMaxLength(50);
            e.Property(m => m.Bezug).HasMaxLength(200);
            e.Property(m => m.Text).HasColumnType("nvarchar(max)");
            e.Property(m => m.Antwort).HasColumnType("nvarchar(max)");
            e.HasOne(m => m.ErstelltVon).WithMany().HasForeignKey(m => m.ErstelltVonId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(m => m.ErledigtVon).WithMany().HasForeignKey(m => m.ErledigtVonId).OnDelete(DeleteBehavior.NoAction);
        });

        // --- Wiki: FAQ-Datenbank für den Support -----------------------------
        modelBuilder.Entity<FaqEintrag>(e =>
        {
            e.HasIndex(f => new { f.Produkt, f.Kategorie });
            e.HasIndex(f => f.Status);
            e.Property(f => f.Titel).HasMaxLength(200);
            e.Property(f => f.Produkt).HasMaxLength(50);
            e.Property(f => f.Kategorie).HasMaxLength(50);
            e.Property(f => f.Symptom).HasColumnType("nvarchar(max)");
            e.Property(f => f.Ursache).HasColumnType("nvarchar(max)");
            e.Property(f => f.Loesung).HasColumnType("nvarchar(max)");
            e.Property(f => f.Schlagwoerter).HasMaxLength(500);
            e.Property(f => f.TicketId).HasMaxLength(50);
            e.Property(f => f.RowVersion).IsRowVersion();
            // Der Kunde ist nur Herkunftshinweis — ein gelöschter Kunde nimmt den Fall nicht mit
            e.HasOne(f => f.Kunde).WithMany().HasForeignKey(f => f.KundeId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(f => f.ErstelltVon).WithMany().HasForeignKey(f => f.ErstelltVonId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(f => f.GeaendertVon).WithMany().HasForeignKey(f => f.GeaendertVonId).OnDelete(DeleteBehavior.NoAction);
        });

        // --- Wiki: Kommunikationsleitlinien (Sprachregeln) ---------------------
        modelBuilder.Entity<Sprachregel>(e =>
        {
            e.ToTable("Sprachregeln");
            e.HasIndex(r => new { r.Status, r.Art });
            e.Property(r => r.Aussage).HasMaxLength(500);
            e.Property(r => r.Stattdessen).HasMaxLength(500);
            e.Property(r => r.Anlass).HasMaxLength(500);
            e.Property(r => r.Begruendung).HasColumnType("nvarchar(max)");
            e.Property(r => r.BeispielGut).HasMaxLength(1000);
            e.Property(r => r.BeispielSchlecht).HasMaxLength(1000);
            e.Property(r => r.Varianten).HasMaxLength(500);
            e.Property(r => r.Thema).HasMaxLength(50);
            e.Property(r => r.Produkt).HasMaxLength(50);
            e.Property(r => r.KiHinweis).HasMaxLength(500);
            e.Property(r => r.RowVersion).IsRowVersion();
            e.HasOne(r => r.ErstelltVon).WithMany().HasForeignKey(r => r.ErstelltVonId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.GeaendertVon).WithMany().HasForeignKey(r => r.GeaendertVonId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.FreigegebenVon).WithMany().HasForeignKey(r => r.FreigegebenVonId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(r => r.ZurueckgezogenVon).WithMany().HasForeignKey(r => r.ZurueckgezogenVonId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<VorlagenVersion>(e =>
        {
            e.HasIndex(v => new { v.VorlageId, v.Nummer }).IsUnique();
            e.Property(v => v.Dateipfad).HasMaxLength(500);
            e.Property(v => v.Sha256).HasMaxLength(64).IsFixedLength();
            e.Property(v => v.Notiz).HasMaxLength(500);
            e.Property(v => v.QuellDateiname).HasMaxLength(260);
            e.HasOne(v => v.Vorlage).WithMany(v => v.Versionen)
                .HasForeignKey(v => v.VorlageId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(v => v.FreigegebenVon).WithMany()
                .HasForeignKey(v => v.FreigegebenVonId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PruefeDokumentUnveraenderlichkeit();
        var geplant = SammleAuditEintraege();
        var result = base.SaveChanges(acceptAllChangesOnSuccess);
        if (geplant.Count > 0)
        {
            FinalisiereAuditEintraege(geplant);
            base.SaveChanges(acceptAllChangesOnSuccess);
        }
        return result;
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PruefeDokumentUnveraenderlichkeit();
        var geplant = SammleAuditEintraege();
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (geplant.Count > 0)
        {
            FinalisiereAuditEintraege(geplant);
            await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        return result;
    }

    /// <summary>
    /// Einziger legitimer Weg an der Dokument-Unveränderlichkeit vorbei:
    /// die explizite Admin-Löschung einer kompletten Vertragsakte
    /// (z. B. Testdaten). Muss unmittelbar vor dem Löschvorgang gesetzt werden.
    /// </summary>
    public bool ErlaubeVertragsdokumentLoeschung { get; set; }

    /// <summary>
    /// Leitplanke „Dokumente unveränderlich versionieren": Vertragsdokumente
    /// dürfen nur eingefügt, nie geändert oder gelöscht werden.
    /// </summary>
    private void PruefeDokumentUnveraenderlichkeit()
    {
        var verstoss = ChangeTracker.Entries<Vertragsdokument>()
            .FirstOrDefault(e => e.State == EntityState.Modified
                || (e.State == EntityState.Deleted && !ErlaubeVertragsdokumentLoeschung));
        if (verstoss is not null)
            throw new InvalidOperationException(
                "Vertragsdokumente sind unveränderlich — statt Ändern/Löschen eine neue Version anlegen.");
    }

    // Entitäten, die selbst Protokollcharakter haben, werden nicht auditiert.
    // GeheimeEinstellung: NIE auditieren — sonst landet der (verschlüsselte)
    // Wert im Audit-JSON. Server: der Healthcheck-Takt würde das Log fluten.
    private static readonly HashSet<Type> NichtAuditieren =
        [typeof(AuditLogEintrag), typeof(Aktivitaet), typeof(AktivitaetAnhang), typeof(AktivitaetTeilnehmer), typeof(Nummernkreis),
         typeof(GeheimeEinstellung), typeof(Domain.Servers.Server), typeof(Domain.Servers.ZertifikatEreignis)];

    // Eigenschaften, die Hintergrundläufe im Takt pflegen oder die nur Nutzdaten tragen —
    // sie würden das Audit-Log fluten, ohne etwas Nachvollziehbares zu sagen (SICHERHEIT.md N4).
    // Ändert ein Speichervorgang NUR solche Eigenschaften, entsteht kein Eintrag.
    private static readonly Dictionary<Type, HashSet<string>> NichtAuditierteEigenschaften = new()
    {
        [typeof(Domain.Servers.Zertifikat)] =
            [nameof(Domain.Servers.Zertifikat.LiveGeprueftAm), nameof(Domain.Servers.Zertifikat.LiveGueltigBis),
             nameof(Domain.Servers.Zertifikat.LiveAussteller), nameof(Domain.Servers.Zertifikat.LiveFehler),
             nameof(Domain.Servers.Zertifikat.LiveFehlerSeit), nameof(Domain.Servers.Zertifikat.LiveFehlerGemeldetAm),
             nameof(Domain.Servers.Zertifikat.LiveProtokoll), nameof(Domain.Servers.Zertifikat.LiveWarnungen),
             nameof(Domain.Servers.Zertifikat.LetzteErinnerungAm), nameof(Domain.Servers.Zertifikat.ErinnerungenGesendet),
             nameof(Domain.Servers.Zertifikat.LetzterMailFehler)],
        // Der Preislisten-JSON (bis 5 MB) liegt in der Tabelle selbst; Version, Stand und Hash reichen im Audit.
        [typeof(Domain.Wiki.PreislistenVersion)] = [nameof(Domain.Wiki.PreislistenVersion.Daten)],
        // Klick-Kennungen werden nach 90 Tagen gelöscht — im Audit-Log dürfen sie nicht überleben.
        [typeof(Anfrage)] =
            [nameof(Anfrage.Gclid), nameof(Anfrage.Gbraid), nameof(Anfrage.Wbraid), nameof(Anfrage.Msclkid), nameof(Anfrage.KlickKennungenGeloeschtAm)],
        // Die Lauf-Sperre wird je Anlegen gesetzt und wieder gelöscht — die Schritte selbst stehen im Audit.
        [typeof(AnfrageOdooAnlage)] = [nameof(AnfrageOdooAnlage.LaeuftSeit)],
    };

    // Einstellungen, die Hintergrundläufe als Zeitstempel fortschreiben (kein Admin-Handeln),
    // und die gemerkte letzte Auswahl beim Anlegen in Odoo
    private static readonly HashSet<string> AutomatischGepflegteEinstellungen =
        [EinstellungsSchluessel.ServicePortalLetzterPush, EinstellungsSchluessel.OdooLetzterImport,
         EinstellungsSchluessel.ZertifikateLetzterWochenbericht, EinstellungsSchluessel.AnfragenOdooVorbelegung];

    private static bool Auditieren(EntityEntry entry) =>
        !NichtAuditieren.Contains(entry.Entity.GetType())
        && !(entry.Entity is Einstellung { Schluessel: var schluessel } && AutomatischGepflegteEinstellungen.Contains(schluessel));

    private static bool EigenschaftAuditieren(EntityEntry entry, string name) =>
        name != "RowVersion"
        && !(NichtAuditierteEigenschaften.TryGetValue(entry.Entity.GetType(), out var ausgenommen) && ausgenommen.Contains(name));

    private List<(EntityEntry Entry, AuditLogEintrag Eintrag)> SammleAuditEintraege()
    {
        var jetzt = DateTime.UtcNow;
        var mitarbeiterId = ErmittleMitarbeiterId();
        var liste = new List<(EntityEntry, AuditLogEintrag)>();

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;
            if (!Auditieren(entry))
                continue;
            var aenderungen = SerialisiereAenderungen(entry);
            if (entry.State == EntityState.Modified && aenderungen is null)
                continue;   // nur ausgenommene Eigenschaften geändert (Takt-Felder)

            var eintrag = new AuditLogEintrag
            {
                Zeitpunkt = jetzt,
                MitarbeiterId = mitarbeiterId,
                Entitaet = entry.Entity.GetType().Name,
                Aktion = entry.State switch
                {
                    EntityState.Added => AuditAktion.Created,
                    EntityState.Deleted => AuditAktion.Deleted,
                    _ => AuditAktion.Updated,
                },
                AenderungenJson = aenderungen,
            };
            liste.Add((entry, eintrag));
        }

        return liste;
    }

    private void FinalisiereAuditEintraege(List<(EntityEntry Entry, AuditLogEintrag Eintrag)> geplant)
    {
        foreach (var (entry, eintrag) in geplant)
        {
            // Bei Added steht die Id erst nach dem Speichern fest.
            if (entry.Metadata.FindProperty("Id") is not null &&
                entry.Property("Id").CurrentValue is int id)
            {
                eintrag.EntitaetId = id;
            }
            AuditLog.Add(eintrag);
        }
    }

    private static string? SerialisiereAenderungen(EntityEntry entry)
    {
        var aenderungen = new Dictionary<string, object?>();

        foreach (var prop in entry.Properties)
        {
            if (!EigenschaftAuditieren(entry, prop.Metadata.Name))
                continue;

            switch (entry.State)
            {
                case EntityState.Added:
                    aenderungen[prop.Metadata.Name] = new { neu = prop.CurrentValue };
                    break;
                case EntityState.Deleted:
                    aenderungen[prop.Metadata.Name] = new { alt = prop.OriginalValue };
                    break;
                case EntityState.Modified when prop.IsModified &&
                        !Equals(prop.OriginalValue, prop.CurrentValue):
                    aenderungen[prop.Metadata.Name] = new { alt = prop.OriginalValue, neu = prop.CurrentValue };
                    break;
            }
        }

        return aenderungen.Count == 0 ? null : JsonSerializer.Serialize(aenderungen);
    }

    private int? ErmittleMitarbeiterId()
    {
        if (_mitarbeiterIdErmittelt)
            return _mitarbeiterId;

        _mitarbeiterIdErmittelt = true;
        if (aktuellerBenutzer is { } b && (b.EntraObjectId is not null || b.WindowsKonto is not null))
        {
            _mitarbeiterId = Mitarbeiter
                .Where(Domain.Shared.Mitarbeiter.KontoFilter(b.EntraObjectId, b.WindowsKonto))
                .Select(m => (int?)m.Id)
                .FirstOrDefault();
        }
        return _mitarbeiterId;
    }
}
