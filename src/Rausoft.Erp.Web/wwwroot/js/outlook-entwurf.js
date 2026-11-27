// Mails im eigenen Outlook-Postfach anlegen, öffnen und senden (Microsoft Graph).
//
// Die Kundenzentrale bleibt bei der Windows-Anmeldung. Für das Postfach meldet sich der Browser
// des Mitarbeiters selbst bei Microsoft an: öffentlicher Client, Autorisierungscode mit PKCE,
// Anmeldefenster. Das Zugriffstoken lebt nur im Arbeitsspeicher dieser Seite (nie im
// Browser-Speicher, nie auf dem Server) und gilt nur für das eigene Postfach: Mail.ReadWrite
// für Entwürfe, zusätzlich Mail.Send nur dann, wenn der Mitarbeiter direkt senden will.
(function () {
    const BEREICH = "https://graph.microsoft.com/Mail.ReadWrite";
    // Direkt senden braucht zusätzlich Mail.Send — nur angefordert, wenn der Mitarbeiter auf „Senden“ klickt
    const BEREICH_SENDEN = BEREICH + " https://graph.microsoft.com/Mail.Send";
    const GRAPH = "https://graph.microsoft.com/v1.0";
    const KENNUNG = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    const merker = {}; // je Berechtigungsstufe: { token, bis, client }

    const b64 = (bytes) => btoa(String.fromCharCode(...new Uint8Array(bytes))).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
    const zufall = (n) => b64(crypto.getRandomValues(new Uint8Array(n)));

    function ruecksprung() {
        return new URL("outlook-anmeldung.html", document.baseURI).href;
    }

    function aufAntwortWarten(fenster) {
        return new Promise((fertig, fehler) => {
            const aufraeumen = () => { window.removeEventListener("message", hoeren); clearInterval(takt); clearTimeout(frist); };
            const hoeren = (e) => {
                if (e.origin !== window.location.origin || e.source !== fenster || !e.data || e.data.art !== "outlook-anmeldung") return;
                aufraeumen();
                fertig(new URLSearchParams(e.data.antwort));
            };
            const takt = setInterval(() => {
                if (fenster.closed) { aufraeumen(); fehler(new Error("Das Anmeldefenster wurde geschlossen, bevor die Anmeldung fertig war.")); }
            }, 500);
            const frist = setTimeout(() => { aufraeumen(); try { fenster.close(); } catch { } fehler(new Error("Die Anmeldung bei Microsoft hat zu lange gedauert.")); }, 180000);
            window.addEventListener("message", hoeren);
        });
    }

    function gemerkt(k, bereich) {
        const m = merker[bereich];
        return m && m.client === k.clientId && m.bis > Date.now() + 60000 ? m.token : null;
    }

    async function tokenHolen(k, bereich, zielFenster) {
        bereich = bereich || BEREICH;
        if (!KENNUNG.test(k.tenantId || "") || !KENNUNG.test(k.clientId || ""))
            throw new Error("Die App-Registrierung ist nicht eingetragen (Verzeichnis-ID und Anwendungs-ID).");
        // Ein Token mit Senderecht darf auch alles, was das kleinere darf
        const vorhanden = gemerkt(k, bereich) || gemerkt(k, BEREICH_SENDEN);
        if (vorhanden)
            return vorhanden;
        if (!window.crypto || !crypto.subtle)
            throw new Error("Die Anmeldung braucht eine https-Adresse.");

        // Das Fenster muss noch im Klick geöffnet werden, sonst blockiert der Browser es
        const fenster = zielFenster || window.open("about:blank", "kundenzentrale-outlook", "width=520,height=660");
        if (!fenster)
            throw new Error("Der Browser hat das Anmeldefenster blockiert — bitte Pop-ups für die Kundenzentrale zulassen.");

        const pruefwert = zufall(48);
        const herausforderung = b64(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(pruefwert)));
        const zustand = zufall(16);
        const basis = `https://login.microsoftonline.com/${k.tenantId}/oauth2/v2.0`;
        const frage = new URLSearchParams({
            client_id: k.clientId, response_type: "code", redirect_uri: ruecksprung(), response_mode: "query",
            scope: bereich, state: zustand, code_challenge: herausforderung, code_challenge_method: "S256",
        });
        if (k.anmeldeHinweis) frage.set("login_hint", k.anmeldeHinweis);
        fenster.location.href = `${basis}/authorize?${frage}`;

        const antwort = await aufAntwortWarten(fenster);
        if (antwort.get("state") !== zustand)
            throw new Error("Die Antwort der Anmeldung passt nicht zur Anfrage — bitte noch einmal versuchen.");
        if (antwort.get("error"))
            throw new Error(`Microsoft hat die Anmeldung abgelehnt: ${antwort.get("error_description") || antwort.get("error")}`);

        const tausch = await fetch(`${basis}/token`, {
            method: "POST",
            headers: { "Content-Type": "application/x-www-form-urlencoded" },
            body: new URLSearchParams({
                client_id: k.clientId, grant_type: "authorization_code", code: antwort.get("code") || "",
                redirect_uri: ruecksprung(), code_verifier: pruefwert, scope: bereich,
            }),
        });
        const daten = await tausch.json().catch(() => ({}));
        if (!tausch.ok || !daten.access_token)
            throw new Error(`Microsoft hat kein Zugriffstoken ausgestellt: ${daten.error_description || daten.error || tausch.status}`);
        merker[bereich] = { token: daten.access_token, bis: Date.now() + (daten.expires_in || 3000) * 1000, client: k.clientId };
        return daten.access_token;
    }

    async function graph(k, methode, pfad, inhalt, bereich, zielFenster) {
        const token = await tokenHolen(k, bereich, zielFenster);
        const antwort = await fetch(GRAPH + pfad, {
            method: methode,
            headers: {
                Authorization: `Bearer ${token}`,
                "Content-Type": "application/json",
                // Unveränderliche Kennung: Der Entwurf behält sie, wenn er nach dem Senden in „Gesendete Elemente“ wandert
                Prefer: 'IdType="ImmutableId"',
            },
            body: inhalt ? JSON.stringify(inhalt) : undefined,
        });
        const daten = await antwort.json().catch(() => ({}));
        if (!antwort.ok) {
            const fehler = new Error((daten.error && daten.error.message) || `Graph antwortet mit ${antwort.status}`);
            fehler.status = antwort.status;
            throw fehler;
        }
        return daten;
    }

    // Legt den Entwurf an und liefert Kennung und Verweis; gesendet wird nichts.
    window.outlookEntwurfAnlegen = async (k, mail) => {
        const empfaenger = (mail.empfaenger || []).filter((a) => a).map((a) => ({ emailAddress: { address: a } }));
        const m = await graph(k, "POST", "/me/messages", {
            subject: mail.betreff || "",
            body: { contentType: "HTML", content: mail.html || "" },
            toRecipients: empfaenger,
        });
        return { id: m.id, verweis: m.webLink || null, nachrichtenKennung: m.internetMessageId || null };
    };

    // Was ist aus dem Entwurf geworden? "entwurf" · "gesendet" · "geloescht" — mit der wirklich gesendeten Fassung
    window.outlookEntwurfStand = async (k, id) => {
        const adressen = (liste) => (liste || []).map((e) => e && e.emailAddress && e.emailAddress.address).filter((a) => a);
        try {
            const m = await graph(k, "GET",
                `/me/messages/${encodeURIComponent(id)}?$select=isDraft,sentDateTime,subject,internetMessageId,toRecipients,ccRecipients,body`);
            return {
                stand: m.isDraft ? "entwurf" : "gesendet", gesendetAm: m.sentDateTime || null,
                betreff: m.subject || "", nachrichtenKennung: m.internetMessageId || null,
                html: m.body && m.body.contentType && m.body.contentType.toLowerCase() === "html" ? m.body.content || "" : null,
                an: adressen(m.toRecipients), cc: adressen(m.ccRecipients),
            };
        } catch (e) {
            if (e.status === 404) return { stand: "geloescht", gesendetAm: null, betreff: "", nachrichtenKennung: null, html: null, an: [], cc: [] };
            throw e;
        }
    };

    // --- Mail aus der Akte: anlegen, Anhänge hochladen, öffnen oder senden ---------------------

    const KLEIN = 3 * 1024 * 1024;       // bis hierher geht ein Anhang in einem Aufruf mit
    const STUECK = 10 * 327680;          // sonst in Stücken von 3,125 MiB (Vielfaches von 320 KiB)

    function base64(bytes) {
        const u = new Uint8Array(bytes);
        let s = "";
        for (let i = 0; i < u.length; i += 0x8000) s += String.fromCharCode.apply(null, u.subarray(i, i + 0x8000));
        return btoa(s);
    }

    // Datei eines Textbausteins von der Kundenzentrale holen — gleiche Adresse, also mit der Windows-Anmeldung des Mitarbeiters
    async function dateiLaden(anhang) {
        const antwort = await fetch(anhang.adresse, { headers: { "X-Requested-With": "Kundenzentrale" } });
        if (!antwort.ok) throw new Error(`Der Anhang „${anhang.name}“ konnte nicht aus der Kundenzentrale geladen werden (${antwort.status}).`);
        return await antwort.arrayBuffer();
    }

    async function anhaengen(k, id, anhang, bereich) {
        const bytes = await dateiLaden(anhang);
        const pfad = `/me/messages/${encodeURIComponent(id)}/attachments`;
        if (bytes.byteLength < KLEIN) {
            await graph(k, "POST", pfad, { "@odata.type": "#microsoft.graph.fileAttachment", name: anhang.name, contentBytes: base64(bytes) }, bereich);
            return;
        }
        // Große Datei: Upload-Sitzung, die Adresse der Sitzung trägt die Berechtigung selbst
        const sitzung = await graph(k, "POST", `${pfad}/createUploadSession`,
            { AttachmentItem: { attachmentType: "file", name: anhang.name, size: bytes.byteLength } }, bereich);
        if (!sitzung.uploadUrl) throw new Error(`Für den Anhang „${anhang.name}“ hat Outlook keine Upload-Adresse geliefert.`);
        for (let von = 0; von < bytes.byteLength; von += STUECK) {
            const bis = Math.min(von + STUECK, bytes.byteLength);
            const antwort = await fetch(sitzung.uploadUrl, {
                method: "PUT",
                headers: { "Content-Type": "application/octet-stream", "Content-Range": `bytes ${von}-${bis - 1}/${bytes.byteLength}` },
                body: bytes.slice(von, bis),
            });
            if (!antwort.ok) throw new Error(`Der Anhang „${anhang.name}“ ließ sich nicht hochladen (${antwort.status}).`);
        }
    }

    // mail: { an: [], cc: [], betreff, html, anhaenge: [{ name, adresse }] }
    // senden = false: Entwurf anlegen und in Outlook im Web öffnen · true: anlegen und sofort senden
    window.outlookMailAnlegen = async (k, mail, senden) => {
        const bereich = senden ? BEREICH_SENDEN : BEREICH;
        // Das Fenster für den Entwurf muss noch im Klick aufgehen — nach dem Anlegen blockiert der Browser es.
        // Braucht es eine Anmeldung, läuft sie in demselben Fenster.
        const ziel = senden ? null : window.open("about:blank", "kundenzentrale-entwurf");
        if (ziel) {
            try { ziel.document.title = "Entwurf wird vorbereitet …"; ziel.document.body.textContent = "Der Entwurf wird vorbereitet …"; } catch { }
        }
        let id = null;
        try {
            const adressen = (liste) => (liste || []).filter((a) => a).map((a) => ({ emailAddress: { address: a } }));
            const m = await graph(k, "POST", "/me/messages", {
                subject: mail.betreff || "",
                body: { contentType: "HTML", content: mail.html || "" },
                toRecipients: adressen(mail.an),
                ccRecipients: adressen(mail.cc),
            }, bereich, ziel);
            id = m.id;
            for (const anhang of mail.anhaenge || []) await anhaengen(k, id, anhang, bereich);
            if (senden) {
                await graph(k, "POST", `/me/messages/${encodeURIComponent(id)}/send`, null, bereich);
                return { id: id, verweis: null, nachrichtenKennung: m.internetMessageId || null, gesendet: true, geoeffnet: false };
            }
            let geoeffnet = false;
            if (ziel && !ziel.closed && m.webLink) {
                ziel.location.href = m.webLink;
                geoeffnet = true;
            }
            return { id: id, verweis: m.webLink || null, nachrichtenKennung: m.internetMessageId || null, gesendet: false, geoeffnet: geoeffnet };
        } catch (e) {
            if (ziel) { try { ziel.close(); } catch { } }
            // Kein halbfertiger Entwurf im Postfach: was angelegt wurde, wieder entfernen
            if (id) { try { await graph(k, "DELETE", `/me/messages/${encodeURIComponent(id)}`, null, bereich); } catch { } }
            throw e;
        }
    };

    // Ist diese Seite schon bei Microsoft angemeldet? Dann lässt sich der Stand vorbereiteter Mails
    // ohne Anmeldefenster prüfen — ein Fenster darf nur nach einem Klick aufgehen.
    window.outlookAngemeldet = (k) => !!(k && (gemerkt(k, BEREICH) || gemerkt(k, BEREICH_SENDEN)));

    // Meldet, wenn der Mitarbeiter in den Reiter der Kundenzentrale zurückkommt (z. B. nach dem Senden in Outlook).
    window.akteRueckkehrAnmelden = (empfaenger) => {
        const hoeren = () => {
            if (document.visibilityState === "visible") empfaenger.invokeMethodAsync("ZurueckImReiter").catch(() => { });
        };
        document.addEventListener("visibilitychange", hoeren);
        return { abmelden: () => document.removeEventListener("visibilitychange", hoeren) };
    };

    window.outlookRuecksprungAdresse = ruecksprung;
    window.outlookOeffnen = (verweis) => { window.open(verweis, "_blank", "noopener"); };

    // Signatur aus Outlook einfügen: Der Inhalt der Zwischenablage wird als Text (HTML) gelesen und
    // an die Seite gemeldet, nie selbst in die Seite gesetzt — angezeigt wird er nur im abgeschotteten Rahmen.
    window.signaturEinfuegenAnmelden = (element, empfaenger) => {
        if (!element || element.dataset.einfuegen) return;
        element.dataset.einfuegen = "1";
        element.addEventListener("paste", (e) => {
            e.preventDefault();
            const daten = e.clipboardData;
            if (!daten) return;
            let html = daten.getData("text/html");
            if (!html) {
                const text = daten.getData("text/plain") || "";
                const p = document.createElement("div");
                p.textContent = text;
                html = p.innerHTML.replace(/\r?\n/g, "<br>");
            }
            empfaenger.invokeMethodAsync("SignaturEingefuegt", html.slice(0, 400000));
        });
    };
})();
