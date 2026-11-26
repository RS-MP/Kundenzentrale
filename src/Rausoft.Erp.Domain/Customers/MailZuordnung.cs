using System.Security;

namespace Rausoft.Erp.Domain.Customers;

/// <summary>
/// Zuordnung einer E-Mail aus Outlook zum Kunden (ANFORDERUNGEN.md, „Erfassung aus Outlook"):
/// zuerst die exakte Adresse eines Ansprechpartners, dann die Domäne des Kunden — aber nie über
/// Freemail-Domänen und nie über die eigenen Adressen. Mehrere Kunden = ein Mensch entscheidet.
/// Reine Regeln ohne Datenbank; der Durchstich (26.11.2026) speichert noch nichts.
/// </summary>
public static class MailZuordnung
{
    public const string Adresse = "adresse";
    public const string Domaene = "domaene";
    public const string Mehrdeutig = "mehrdeutig";
    public const string Keine = "keine";
    public const string NurEigene = "intern";

    /// <summary>Postfächer bei diesen Anbietern sagen nichts über die Firma des Absenders.</summary>
    private static readonly HashSet<string> Freemail = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "googlemail.com", "gmx.de", "gmx.net", "gmx.at", "gmx.ch", "web.de", "t-online.de", "outlook.com",
        "outlook.de", "hotmail.com", "hotmail.de", "live.com", "live.de", "msn.com", "yahoo.com", "yahoo.de", "icloud.com",
        "me.com", "mac.com", "aol.com", "freenet.de", "arcor.de", "online.de", "posteo.de", "mailbox.org", "proton.me",
        "protonmail.com", "vodafone.de", "kabelmail.de", "unitybox.de", "mail.de", "email.de",
    };

    /// <summary>Adresse in Kleinbuchstaben; null, wenn der Text keine einzelne E-Mail-Adresse ist.</summary>
    public static string? Normalisieren(string? text)
    {
        var t = text?.Trim().Trim('<', '>').ToLowerInvariant();
        if (string.IsNullOrEmpty(t) || t.Length > 254 || t.Any(char.IsWhiteSpace))
            return null;
        var at = t.IndexOf('@');
        return at > 0 && at == t.LastIndexOf('@') && t.IndexOf('.', at) > at + 1 && !t.EndsWith('.') ? t : null;
    }

    public static string DomaeneVon(string adresse) => adresse[(adresse.IndexOf('@') + 1)..];

    /// <summary>Alle Beteiligten ohne die eigenen Adressen — bei gesendeten Mails zählen so die Empfänger.</summary>
    public static List<string> Fremde(IEnumerable<string?> adressen, IReadOnlyCollection<string> eigeneDomaenen) =>
        adressen.Select(Normalisieren).OfType<string>()
            .Where(a => !eigeneDomaenen.Contains(DomaeneVon(a), StringComparer.OrdinalIgnoreCase))
            .Distinct().ToList();

    /// <summary>Domänen, über die ein Kunde gesucht werden darf (ohne Freemail).</summary>
    public static List<string> SuchDomaenen(IEnumerable<string> fremde) =>
        fremde.Select(DomaeneVon).Where(d => !Freemail.Contains(d)).Distinct().ToList();

    /// <summary>Ergebnis aus den Treffern: Adresse schlägt Domäne; mehr als ein Kunde ist nie eindeutig.</summary>
    public static (string Art, IReadOnlyList<int> Kunden) Entscheiden(
        bool hatFremde, IReadOnlyCollection<int> kundenPerAdresse, IReadOnlyCollection<int> kundenPerDomaene)
    {
        if (!hatFremde)
            return (NurEigene, []);
        var adresse = kundenPerAdresse.Distinct().ToList();
        if (adresse.Count == 1)
            return (Adresse, adresse);
        if (adresse.Count > 1)
            return (Mehrdeutig, adresse);
        var domaene = kundenPerDomaene.Distinct().ToList();
        return domaene.Count switch
        {
            1 => (Domaene, domaene),
            0 => (Keine, []),
            _ => (Mehrdeutig, domaene),
        };
    }
}

/// <summary>
/// Manifest des Outlook-Add-ins „In Kundenakte übernehmen". Es wird je Instanz erzeugt, weil
/// alle Adressen auf die eigene Kundenzentrale zeigen müssen; Test- und Produktivsystem haben
/// verschiedene Kennungen und Namen, damit beide nebeneinander in Outlook stehen können.
/// Das Add-in liest nur die geöffnete Mail (ReadItem) — kein Zugriff auf das Postfach.
/// </summary>
public static class OutlookManifest
{
    public const string KennungProduktiv = "5b7f3c2e-9a41-4d6b-8e15-3c2a7d90b1f4";
    public const string KennungTest = "5b7f3c2e-9a41-4d6b-8e15-3c2a7d90b1f5";
    public const string Version = "0.1.0.0";

    /// <param name="basisUrl">Adresse der Kundenzentrale mit abschließendem Schrägstrich, nur https.</param>
    public static string Erzeugen(string basisUrl, bool testsystem)
    {
        if (!Uri.TryCreate(basisUrl, UriKind.Absolute, out var basis) || basis.Scheme != Uri.UriSchemeHttps || !basisUrl.EndsWith('/'))
            throw new ArgumentException("Das Add-in braucht eine https-Adresse mit abschließendem Schrägstrich.", nameof(basisUrl));
        string X(string text) => SecurityElement.Escape(text);
        var name = testsystem ? "Kundenzentrale (Test)" : "Kundenzentrale";
        var seite = X(basisUrl + "outlook-addin/aufgabenbereich.html");
        var bild = X(basisUrl + "icon-192.png");
        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <OfficeApp xmlns="http://schemas.microsoft.com/office/appforoffice/1.1"
              xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
              xmlns:bt="http://schemas.microsoft.com/office/officeappbasictypes/1.0" xsi:type="MailApp">
              <Id>{(testsystem ? KennungTest : KennungProduktiv)}</Id>
              <Version>{Version}</Version>
              <ProviderName>rausoft GmbH</ProviderName>
              <DefaultLocale>de-DE</DefaultLocale>
              <DisplayName DefaultValue="{X(name)}"/>
              <Description DefaultValue="Die geöffnete E-Mail in die Kundenakte der Kundenzentrale übernehmen."/>
              <IconUrl DefaultValue="{bild}"/>
              <HighResolutionIconUrl DefaultValue="{bild}"/>
              <SupportUrl DefaultValue="{X(basisUrl)}"/>
              <AppDomains>
                <AppDomain>{X(basis.GetLeftPart(UriPartial.Authority))}</AppDomain>
              </AppDomains>
              <Hosts>
                <Host Name="Mailbox"/>
              </Hosts>
              <Requirements>
                <Sets>
                  <Set Name="Mailbox" MinVersion="1.1"/>
                </Sets>
              </Requirements>
              <FormSettings>
                <Form xsi:type="ItemRead">
                  <DesktopSettings>
                    <SourceLocation DefaultValue="{seite}"/>
                    <RequestedHeight>320</RequestedHeight>
                  </DesktopSettings>
                </Form>
              </FormSettings>
              <Permissions>ReadItem</Permissions>
              <Rule xsi:type="RuleCollection" Mode="Or">
                <Rule xsi:type="ItemIs" ItemType="Message" FormType="Read"/>
              </Rule>
              <DisableEntityHighlighting>false</DisableEntityHighlighting>
              <VersionOverrides xmlns="http://schemas.microsoft.com/office/mailappversionoverrides" xsi:type="VersionOverridesV1_0">
                <Requirements>
                  <bt:Sets DefaultMinVersion="1.3">
                    <bt:Set Name="Mailbox"/>
                  </bt:Sets>
                </Requirements>
                <Hosts>
                  <Host xsi:type="MailHost">
                    <DesktopFormFactor>
                      <ExtensionPoint xsi:type="MessageReadCommandSurface">
                        <OfficeTab id="TabDefault">
                          <Group id="kundenzentraleGruppe">
                            <Label resid="Gruppe.Name"/>
                            <Control xsi:type="Button" id="kundenzentraleUebernehmen">
                              <Label resid="Knopf.Name"/>
                              <Supertip>
                                <Title resid="Knopf.Name"/>
                                <Description resid="Knopf.Hinweis"/>
                              </Supertip>
                              <Icon>
                                <bt:Image size="16" resid="Bild.16"/>
                                <bt:Image size="32" resid="Bild.32"/>
                                <bt:Image size="80" resid="Bild.80"/>
                              </Icon>
                              <Action xsi:type="ShowTaskpane">
                                <SourceLocation resid="Aufgabenbereich.Url"/>
                              </Action>
                            </Control>
                          </Group>
                        </OfficeTab>
                      </ExtensionPoint>
                    </DesktopFormFactor>
                  </Host>
                </Hosts>
                <Resources>
                  <bt:Images>
                    <bt:Image id="Bild.16" DefaultValue="{bild}"/>
                    <bt:Image id="Bild.32" DefaultValue="{bild}"/>
                    <bt:Image id="Bild.80" DefaultValue="{bild}"/>
                  </bt:Images>
                  <bt:Urls>
                    <bt:Url id="Aufgabenbereich.Url" DefaultValue="{seite}"/>
                  </bt:Urls>
                  <bt:ShortStrings>
                    <bt:String id="Gruppe.Name" DefaultValue="{X(name)}"/>
                    <bt:String id="Knopf.Name" DefaultValue="In Kundenakte"/>
                  </bt:ShortStrings>
                  <bt:LongStrings>
                    <bt:String id="Knopf.Hinweis" DefaultValue="Zeigt, zu welchem Kunden der Kundenzentrale diese E-Mail gehört."/>
                  </bt:LongStrings>
                </Resources>
              </VersionOverrides>
            </OfficeApp>
            """;
    }
}
