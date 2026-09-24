using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Rausoft.Erp.Domain.Contracts;
using Rausoft.Erp.Domain.Customers;
using Rausoft.Erp.Domain.Shared;
using Rausoft.Erp.Domain.Subscriptions;

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
    public DbSet<AuditLogEintrag> AuditLog => Set<AuditLogEintrag>();
    public DbSet<Einstellung> Einstellungen => Set<Einstellung>();

    // Kontakte
    public DbSet<Kunde> Kunden => Set<Kunde>();
    public DbSet<Partner> Partner => Set<Partner>();
    public DbSet<Ansprechpartner> Ansprechpartner => Set<Ansprechpartner>();
    public DbSet<Adresse> Adressen => Set<Adresse>();
    public DbSet<Merkmal> Merkmale => Set<Merkmal>();

    // Verträge
    public DbSet<Vertrag> Vertraege => Set<Vertrag>();
    public DbSet<Vertragsbestandteil> Vertragsbestandteile => Set<Vertragsbestandteil>();
    public DbSet<Vertragsdokument> Vertragsdokumente => Set<Vertragsdokument>();

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
            e.HasOne(m => m.Bereich).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Bereich>(e =>
        {
            e.HasIndex(b => b.Name).IsUnique();
            e.Property(b => b.Name).HasMaxLength(100);
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
                },
                new
                {
                    Id = 7,
                    Schluessel = EinstellungsSchluessel.OdooUrl,
                    Wert = "",
                    Beschreibung = "Basis-URL der Odoo-Instanz für den Import, z. B. https://rausoft.odoo.com.",
                },
                new
                {
                    Id = 8,
                    Schluessel = EinstellungsSchluessel.OdooDatenbank,
                    Wert = "",
                    Beschreibung = "Name der Odoo-Datenbank.",
                },
                new
                {
                    Id = 9,
                    Schluessel = EinstellungsSchluessel.OdooBenutzer,
                    Wert = "",
                    Beschreibung = "Login (E-Mail) des Odoo-API-Benutzers — idealerweise ein eigener, " +
                                   "nur lesend berechtigter Benutzer. Der API-Key wird als Geheimnis " +
                                   "verschlüsselt gespeichert (nie im Klartext).",
                });
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
            e.Property(v => v.Notiz).HasColumnType("nvarchar(max)");
            e.Property(v => v.RowVersion).IsRowVersion();
            e.HasOne(v => v.Kunde).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasMany(v => v.Bestandteile).WithOne(b => b.Vertrag!)
                .HasForeignKey(b => b.VertragId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Vertragsbestandteil>(e =>
        {
            e.Property(b => b.Titel).HasMaxLength(200);
            e.Property(b => b.Beschreibung).HasColumnType("nvarchar(max)");
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
        [typeof(AuditLogEintrag), typeof(Aktivitaet), typeof(Nummernkreis),
         typeof(GeheimeEinstellung), typeof(Domain.Servers.Server)];

    private List<(EntityEntry Entry, AuditLogEintrag Eintrag)> SammleAuditEintraege()
    {
        var jetzt = DateTime.UtcNow;
        var mitarbeiterId = ErmittleMitarbeiterId();
        var liste = new List<(EntityEntry, AuditLogEintrag)>();

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;
            if (NichtAuditieren.Contains(entry.Entity.GetType()))
                continue;

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
                AenderungenJson = SerialisiereAenderungen(entry),
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
            if (prop.Metadata.Name == "RowVersion")
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
