// Wiki-Editor: Texte formatieren wie in Word — Werkzeugleiste statt Formatierungszeichen.
// Gespeichert wird weiterhin das einfache Textformat (WikiFormat, Markdown-ähnlich): Die
// Blazor-Komponente WikiEditor übergibt das daraus erzeugte (HTML-kodierte) HTML, der Editor
// wandelt bei jeder Änderung zurück und meldet den Text an .NET. Beim Einfügen aus Word,
// Outlook oder Webseiten bleiben nur Überschriften, Absätze, Listen, fett/kursiv, Code,
// Tabellen und http(s)-Links übrig — alles andere wird entfernt.
//
// Absatzformat, Listen, Tabellen und Rückgängig sind selbst umgesetzt (die Browser-Befehle
// dafür erzeugen je nach Lage verschachtelte Absätze); fett, kursiv und Links nutzen die
// Browser-Befehle. Nach jeder Änderung räumt aufraeumen() die Struktur auf.
(function () {
    'use strict';

    const BLOCK = new Set(['P', 'DIV', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'UL', 'OL', 'LI', 'PRE', 'TABLE', 'BLOCKQUOTE', 'TR', 'TD', 'TH', 'THEAD', 'TBODY']);
    const ABSATZ = new Set(['P', 'DIV', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'BLOCKQUOTE']);
    const OBEN = new Set(['P', 'H3', 'H4', 'H5', 'UL', 'OL', 'PRE', 'TABLE']);   // erlaubt auf oberster Ebene
    const FORMATE = [['p', 'Text'], ['h3', 'Überschrift'], ['h4', 'Zwischenüberschrift'], ['h5', 'Kleine Überschrift'], ['pre', 'Code / Konfiguration']];

    try { document.execCommand('defaultParagraphSeparator', false, 'p'); } catch (e) { /* ältere Browser */ }

    // ================================================================ Aufbau

    function erstellen(container, html, dotnet, platzhalter, zeilen, beschriftung) {
        entfernen(container);
        container.textContent = '';

        const leiste = el('div', 'wiki-editor-leiste');
        leiste.setAttribute('role', 'toolbar');
        leiste.setAttribute('aria-label', 'Formatierung');

        const format = el('select', 'wiki-editor-format');
        format.title = 'Absatzformat';
        format.setAttribute('aria-label', 'Absatzformat');
        for (const [wert, text] of FORMATE) {
            const o = el('option'); o.value = wert; o.textContent = text; format.appendChild(o);
        }

        const flaeche = el('div', 'wiki-editor-flaeche wiki-text');
        flaeche.contentEditable = 'true';
        flaeche.spellcheck = true;
        flaeche.setAttribute('role', 'textbox');
        flaeche.setAttribute('aria-multiline', 'true');
        if (beschriftung) flaeche.setAttribute('aria-label', beschriftung);
        flaeche.dataset.platzhalter = platzhalter || '';
        flaeche.style.minHeight = (Math.max(2, zeilen || 6) * 1.55 + 1.2) + 'em';

        const z = { container, flaeche, dotnet, format, knoepfe: {}, auswahl: null, letzter: null, zurueck: [], vor: [], letzterStand: 0 };

        // Werkzeug ausführen: Markierung herstellen, Stand merken, ausführen, aufräumen, melden
        const ausfuehren = aktion => {
            auswahlHerstellen(z);
            merken(z);
            aktion();
            aufraeumen(z);
            aenderung(z); zustand(z);
        };
        const knopf = (schluessel, text, titel, aktion, klasse, ohneStand) => {
            const b = el('button', 'wiki-editor-knopf' + (klasse ? ' ' + klasse : ''));
            b.type = 'button'; b.textContent = text; b.title = titel; b.setAttribute('aria-label', titel);
            // mousedown verhindern: sonst verliert die Textfläche Fokus und Markierung
            b.addEventListener('mousedown', e => e.preventDefault());
            b.addEventListener('click', () => ohneStand ? (auswahlHerstellen(z), aktion()) : ausfuehren(aktion));
            z.knoepfe[schluessel] = b;
            return b;
        };
        const trenner = () => el('span', 'wiki-editor-trenner');

        format.addEventListener('change', () => ausfuehren(() => formatSetzen(z, format.value)));

        leiste.append(format, trenner(),
            knopf('bold', 'F', 'Fett (Strg+B)', () => befehl('bold'), 'fett'),
            knopf('italic', 'K', 'Kursiv (Strg+I)', () => befehl('italic'), 'kursiv'),
            knopf('code', 'Code', 'Als Code markieren (Befehle, Pfade, Werte)', () => codeUmschalten(z)),
            trenner(),
            knopf('ul', '• Liste', 'Aufzählung', () => listeUmschalten(z, 'UL')),
            knopf('ol', '1. Liste', 'Nummerierte Liste', () => listeUmschalten(z, 'OL')),
            trenner(),
            knopf('link', 'Link', 'Link einfügen oder ändern (Strg+K)', () => linkOeffnen(z), null, true),
            knopf('tabelle', 'Tabelle', 'Tabelle einfügen', () => tabelleEinfuegen(z)),
            trenner(),
            knopf('undo', '↶', 'Rückgängig (Strg+Z)', () => rueckgaengig(z), null, true),
            knopf('redo', '↷', 'Wiederholen (Strg+Y)', () => wiederholen(z), null, true),
            knopf('clear', 'Format entfernen', 'Fett, kursiv, Code und Links der Markierung entfernen', () => formatEntfernen(z), 'leise'));

        // Tabellenwerkzeuge — nur sichtbar, wenn der Cursor in einer Tabelle steht
        const tabelle = el('span', 'wiki-editor-tabelle');
        tabelle.append(el('span', 'wiki-editor-trenner'),
            knopf('zeile+', '+ Zeile', 'Zeile darunter einfügen (Tab in der letzten Zelle)', () => zeileEinfuegen(z)),
            knopf('spalte+', '+ Spalte', 'Spalte rechts einfügen', () => spalteEinfuegen(z)),
            knopf('zeile-', '− Zeile', 'Zeile löschen', () => zeileLoeschen(z)),
            knopf('spalte-', '− Spalte', 'Spalte löschen', () => spalteLoeschen(z)),
            knopf('tabelle-', 'Tabelle löschen', 'Ganze Tabelle löschen', () => { const c = zelle(z); if (c) tabelleEntfernen(c.closest('table')); }, 'leise'));
        leiste.appendChild(tabelle);
        z.tabelle = tabelle;

        // Link-Eingabe als Zeile unter der Leiste (statt Browser-Dialog)
        const linkZeile = el('div', 'wiki-editor-linkzeile');
        const linkFeld = el('input'); linkFeld.type = 'url'; linkFeld.placeholder = 'https://…'; linkFeld.setAttribute('aria-label', 'Linkadresse');
        const linkOk = el('button', 'knopf klein'); linkOk.type = 'button'; linkOk.textContent = 'Übernehmen';
        const linkWeg = el('button', 'mehr'); linkWeg.type = 'button'; linkWeg.textContent = 'Link entfernen';
        const linkAbbruch = el('button', 'mehr'); linkAbbruch.type = 'button'; linkAbbruch.textContent = 'Abbrechen';
        const linkHinweis = el('span', 'wiki-editor-hinweis');
        linkZeile.append(document.createTextNode('Linkadresse'), linkFeld, linkOk, linkWeg, linkAbbruch, linkHinweis);
        linkZeile.hidden = true;
        z.link = { zeile: linkZeile, feld: linkFeld, hinweis: linkHinweis, weg: linkWeg };
        linkOk.addEventListener('click', () => linkUebernehmen(z));
        linkWeg.addEventListener('click', () => {
            z.link.zeile.hidden = true;
            ausfuehren(() => { const a = linkAnker(z); if (a) a.replaceWith(...a.childNodes); else befehl('unlink'); });
        });
        linkAbbruch.addEventListener('click', () => linkSchliessen(z));
        linkFeld.addEventListener('keydown', e => {
            if (e.key === 'Enter') { e.preventDefault(); linkUebernehmen(z); }
            if (e.key === 'Escape') { e.preventDefault(); linkSchliessen(z); }
        });
        z.ausfuehren = ausfuehren;

        container.append(leiste, linkZeile, flaeche);
        inhaltSetzen(z, html);

        // Ereignisse der Textfläche
        flaeche.addEventListener('beforeinput', e => vorEingabe(z, e));
        flaeche.addEventListener('input', () => { aufraeumen(z); aenderung(z); zustand(z); });
        flaeche.addEventListener('paste', e => einfuegen(z, e));
        flaeche.addEventListener('drop', e => ablegen(z, e));
        flaeche.addEventListener('keydown', e => taste(z, e));
        flaeche.addEventListener('click', e => {
            const a = e.target.closest && e.target.closest('a');
            if (a && (e.ctrlKey || e.metaKey)) window.open(a.href, '_blank', 'noopener');
        });
        z.beiAuswahl = () => {
            const s = document.getSelection();
            if (s && s.rangeCount && flaeche.contains(s.getRangeAt(0).commonAncestorContainer)) {
                z.auswahl = s.getRangeAt(0).cloneRange();
                zustand(z);
            }
        };
        document.addEventListener('selectionchange', z.beiAuswahl);

        container.__wikiEditor = z;
        zustand(z);
    }

    function setzen(container, html) {
        const z = container.__wikiEditor;
        if (!z) return;
        inhaltSetzen(z, html);
        z.zurueck = []; z.vor = [];
        zustand(z);
    }

    function entfernen(container) {
        const z = container.__wikiEditor;
        if (!z) return;
        document.removeEventListener('selectionchange', z.beiAuswahl);
        container.__wikiEditor = null;
    }

    function inhaltSetzen(z, html) {
        z.flaeche.innerHTML = html || '';
        z.auswahl = null;
        aufraeumen(z);
        z.letzter = zuText(z.flaeche);
    }

    // ================================================================ Auswahl, Blöcke

    function befehl(name, wert) {
        try { document.execCommand(name, false, wert === undefined ? null : wert); } catch (e) { /* nicht unterstützt */ }
    }

    function auswahlHerstellen(z) {
        const s = document.getSelection();
        // Cursor steht schon in der Fläche (Tastenkürzel, Knopf ohne Fokuswechsel): so lassen
        if (document.activeElement === z.flaeche && s.rangeCount && z.flaeche.contains(s.getRangeAt(0).commonAncestorContainer))
            return;
        z.flaeche.focus();
        if (z.auswahl && z.flaeche.contains(z.auswahl.commonAncestorContainer)) {
            s.removeAllRanges();
            s.addRange(z.auswahl);
        } else if (!s.rangeCount || !z.flaeche.contains(s.getRangeAt(0).commonAncestorContainer)) {
            cursorIn(z.flaeche.lastElementChild || z.flaeche, true);   // noch nie ins Feld geklickt: ans Ende
        }
    }

    function bereich(z) {
        const s = document.getSelection();
        return s && s.rangeCount && z.flaeche.contains(s.getRangeAt(0).commonAncestorContainer) ? s.getRangeAt(0) : null;
    }

    function aktuellerKnoten(z) {
        const r = bereich(z);
        if (!r) return null;
        const k = r.startContainer;
        return k.nodeType === 1 ? k : k.parentElement;
    }

    /** Oberster Block (direktes Kind der Fläche), in dem der Knoten liegt. */
    function obersterBlock(z, k) {
        while (k && k.parentNode !== z.flaeche) k = k.parentNode;
        return k;
    }

    /** Betroffene Einheiten der Markierung: Listenpunkte einzeln, sonst die obersten Blöcke. */
    function einheiten(z) {
        const r = bereich(z);
        if (!r) return [];
        const aus = [];
        for (const b of Array.from(z.flaeche.children)) {
            if (!r.intersectsNode(b)) continue;
            if (b.tagName === 'UL' || b.tagName === 'OL') {
                for (const li of Array.from(b.querySelectorAll('li'))) if (r.intersectsNode(li) && !li.querySelector('li')) aus.push(li);
            } else aus.push(b);
        }
        return aus;
    }

    // ================================================================ Absatzformat, Listen

    function formatSetzen(z, tag) {
        const ziel = tag.toUpperCase();
        let letzter = null;
        for (const e of einheiten(z)) {
            if (e.tagName === 'TABLE') continue;
            const block = e.tagName === 'LI' ? ausListeLoesen(e) : e;
            letzter = blockUmwandeln(block, ziel) || letzter;
        }
        if (letzter) cursorIn(letzter, true);
    }

    /** Block in anderes Format: pre ↔ Absatz mit Zeilenumbrüchen. */
    function blockUmwandeln(block, ziel) {
        if (block.tagName === ziel) return block;
        const neu = document.createElement(ziel);
        if (ziel === 'PRE') {
            neu.textContent = preText(block).replace(/\n+$/, '');
        } else if (block.tagName === 'PRE') {
            const zeilen = block.textContent.split('\n');
            zeilen.forEach((t, i) => { if (i) neu.appendChild(document.createElement('br')); if (t) neu.appendChild(document.createTextNode(t)); });
        } else {
            neu.append(...block.childNodes);
        }
        if (!neu.firstChild) neu.appendChild(document.createElement('br'));
        block.replaceWith(neu);
        return neu;
    }

    /** Listenpunkt aus seiner Liste herauslösen → Absatz an derselben Stelle (Liste wird geteilt). */
    function ausListeLoesen(li) {
        const liste = li.parentElement;
        const p = document.createElement('p');
        for (const c of Array.from(li.childNodes)) {
            if (c.nodeType === 1 && (c.tagName === 'UL' || c.tagName === 'OL')) continue;
            p.appendChild(c);
        }
        if (!p.firstChild) p.appendChild(document.createElement('br'));
        const rest = document.createElement(liste.tagName);
        while (li.nextElementSibling) rest.appendChild(li.nextElementSibling);
        // Unterlisten des Punkts bleiben als eigene Liste nach dem Absatz erhalten
        const unter = Array.from(li.children).filter(c => c.tagName === 'UL' || c.tagName === 'OL');
        li.remove();
        let nach = liste;
        // Liste bis zur obersten Ebene teilen (verschachtelte Listen)
        while (nach.parentElement && nach.parentElement.tagName === 'LI') nach = nach.parentElement.parentElement;
        nach.after(p);
        let danach = p;
        for (const u of unter) { danach.after(u); danach = u; }
        if (rest.children.length) danach.after(rest);
        if (!liste.querySelector('li')) liste.remove();
        return p;
    }

    function listeUmschalten(z, typ) {
        const einh = einheiten(z).filter(e => e.tagName !== 'TABLE');
        if (!einh.length) return;
        const alleSchonListe = einh.every(e => e.tagName === 'LI' && e.parentElement.tagName === typ);
        let letzter = null;
        if (alleSchonListe) {
            for (const li of einh) letzter = ausListeLoesen(li);
        } else {
            for (const e of einh) {
                if (e.tagName === 'LI') {
                    const liste = e.parentElement;
                    if (liste.tagName !== typ) letzter = listeTypAendern(liste, typ);
                    continue;
                }
                const li = document.createElement('li');
                if (e.tagName === 'PRE') li.textContent = e.textContent.replace(/\n/g, ' ');
                else li.append(...e.childNodes);
                if (!li.firstChild) li.appendChild(document.createElement('br'));
                const vorher = e.previousElementSibling;
                if (vorher && vorher.tagName === typ) { vorher.appendChild(li); e.remove(); }
                else { const liste = document.createElement(typ); liste.appendChild(li); e.replaceWith(liste); }
                letzter = li;
            }
        }
        if (letzter) cursorIn(letzter, true);
    }

    function listeTypAendern(liste, typ) {
        const neu = document.createElement(typ);
        neu.append(...liste.childNodes);
        liste.replaceWith(neu);
        return neu.lastElementChild;
    }

    // ================================================================ Inline

    function codeUmschalten(z) {
        const k = aktuellerKnoten(z);
        const code = k && k.closest('code');
        if (code && z.flaeche.contains(code) && !code.closest('pre')) {
            code.replaceWith(document.createTextNode(code.textContent));
            return;
        }
        const s = document.getSelection();
        if (!s.rangeCount || s.isCollapsed) return;
        befehl('insertHTML', '<code>' + kodieren(s.toString()) + '</code>');
    }

    function formatEntfernen(z) {
        befehl('removeFormat');
        befehl('unlink');
        const r = bereich(z);
        if (!r) return;
        for (const c of Array.from(z.flaeche.querySelectorAll('code, b, strong, i, em')))
            if (r.intersectsNode(c) && !c.closest('pre')) c.replaceWith(...c.childNodes);
    }

    // ================================================================ Tabellen

    function tabelleEinfuegen(z) {
        const r = bereich(z);
        if (r && zelle(z)) return;   // keine Tabelle in der Tabelle
        const t = document.createElement('table');
        const tb = document.createElement('tbody');
        t.appendChild(tb);
        const kopf = document.createElement('tr');
        for (const name of ['Spalte 1', 'Spalte 2']) { const th = document.createElement('th'); th.textContent = name; kopf.appendChild(th); }
        tb.appendChild(kopf);
        for (let i = 0; i < 2; i++) { const tr = document.createElement('tr'); tr.append(leereZelle('td'), leereZelle('td')); tb.appendChild(tr); }
        const block = r ? obersterBlock(z, r.startContainer) : z.flaeche.lastElementChild;
        if (block && block.tagName === 'P' && !block.textContent.trim()) block.replaceWith(t);
        else if (block) block.after(t);
        else z.flaeche.appendChild(t);
        const p = document.createElement('p'); p.appendChild(document.createElement('br'));
        if (!t.nextElementSibling) t.after(p);
        // erste Kopfzelle markieren: Tippen überschreibt „Spalte 1"
        const sel = document.getSelection(); const rg = document.createRange();
        rg.selectNodeContents(kopf.firstChild); sel.removeAllRanges(); sel.addRange(rg);
    }

    function zelle(z) {
        const k = aktuellerKnoten(z);
        const c = k && k.closest('td, th');
        return c && z.flaeche.contains(c) ? c : null;
    }

    function zeileEinfuegen(z) {
        const c = zelle(z); if (!c) return;
        const tr = c.parentElement;
        const neu = document.createElement('tr');
        for (let i = 0; i < tr.children.length; i++) neu.appendChild(leereZelle('td'));
        tr.after(neu);
        cursorIn(neu.children[c.cellIndex] || neu.firstChild);
    }

    function spalteEinfuegen(z) {
        const c = zelle(z); if (!c) return;
        const index = c.cellIndex;
        c.closest('table').querySelectorAll('tr').forEach((tr, i) => {
            const neu = leereZelle(i === 0 && tr.firstElementChild && tr.firstElementChild.tagName === 'TH' ? 'th' : 'td');
            const ref = tr.children[index];
            if (ref) ref.after(neu); else tr.appendChild(neu);
        });
        const nachbar = c.nextElementSibling;
        if (nachbar) cursorIn(nachbar);
    }

    function zeileLoeschen(z) {
        const c = zelle(z); if (!c) return;
        const tr = c.parentElement, tabelle = c.closest('table');
        const naechste = tr.nextElementSibling || tr.previousElementSibling;
        tr.remove();
        if (!tabelle.querySelector('tr')) { tabelleEntfernen(tabelle); return; }
        if (naechste) cursorIn(naechste.children[Math.min(c.cellIndex, naechste.children.length - 1)]);
    }

    function spalteLoeschen(z) {
        const c = zelle(z); if (!c) return;
        const index = c.cellIndex, tabelle = c.closest('table');
        tabelle.querySelectorAll('tr').forEach(tr => { if (tr.children[index]) tr.children[index].remove(); });
        tabelle.querySelectorAll('tr').forEach(tr => { if (!tr.children.length) tr.remove(); });
        if (!tabelle.querySelector('td, th')) { tabelleEntfernen(tabelle); return; }
        const zeile = tabelle.querySelector('tr');
        cursorIn(zeile.children[Math.min(index, zeile.children.length - 1)]);
    }

    function tabelleEntfernen(tabelle) {
        const p = document.createElement('p'); p.appendChild(document.createElement('br'));
        tabelle.replaceWith(p);
        cursorIn(p);
    }

    function leereZelle(tag) {
        const c = document.createElement(tag); c.appendChild(document.createElement('br')); return c;
    }

    function cursorIn(knoten, ende) {
        if (!knoten) return;
        const r = document.createRange();
        r.selectNodeContents(knoten); r.collapse(!ende);
        const s = document.getSelection(); s.removeAllRanges(); s.addRange(r);
    }

    // ================================================================ Links

    function linkAnker(z) {
        const k = aktuellerKnoten(z);
        const a = k && k.closest('a');
        return a && z.flaeche.contains(a) ? a : null;
    }

    function linkOeffnen(z) {
        const a = linkAnker(z);
        z.link.feld.value = a ? a.getAttribute('href') : '';
        z.link.hinweis.textContent = '';
        z.link.weg.hidden = !a;
        z.link.zeile.hidden = false;
        z.link.feld.focus();
    }

    function linkSchliessen(z) {
        z.link.zeile.hidden = true;
        auswahlHerstellen(z);
    }

    function linkUebernehmen(z) {
        let url = z.link.feld.value.trim();
        if (/^www\./i.test(url)) url = 'https://' + url;
        if (!/^https?:\/\/[^\s]+$/i.test(url)) {
            z.link.hinweis.textContent = 'Bitte eine Adresse mit https:// angeben.';
            z.link.feld.focus();
            return;
        }
        z.link.zeile.hidden = true;
        z.ausfuehren(() => {
            const a = linkAnker(z);
            const s = document.getSelection();
            if (a) a.setAttribute('href', url);
            else if (s.rangeCount && !s.isCollapsed) befehl('createLink', url);
            else befehl('insertHTML', '<a href="' + kodieren(url) + '">' + kodieren(url) + '</a>&nbsp;');
        });
    }

    // ================================================================ Tastatur, Einfügen, Rückgängig

    function taste(z, e) {
        const strg = e.ctrlKey || e.metaKey;
        const t = e.key.toLowerCase();
        if (strg && !e.shiftKey && t === 'k') { e.preventDefault(); linkOeffnen(z); return; }
        if (strg && !e.shiftKey && t === 'z') { e.preventDefault(); rueckgaengig(z); return; }
        if (strg && (t === 'y' || (e.shiftKey && t === 'z'))) { e.preventDefault(); wiederholen(z); return; }
        if (strg && (t === 'b' || t === 'i')) { e.preventDefault(); z.ausfuehren(() => befehl(t === 'b' ? 'bold' : 'italic')); return; }

        const k = aktuellerKnoten(z);
        if (!k) return;
        // Enter im leeren Listenpunkt: Liste verlassen (wie in Word)
        const li = k.closest('li');
        if (e.key === 'Enter' && !e.shiftKey && li && z.flaeche.contains(li) && !li.textContent.trim() && !li.querySelector('ul, ol')) {
            e.preventDefault();
            z.ausfuehren(() => cursorIn(ausListeLoesen(li)));
            return;
        }
        // Enter im Codeblock: neue Zeile im Block (Shift+Enter verlässt ihn nicht — dafür Format „Text")
        const pre = k.closest('pre');
        if (e.key === 'Enter' && pre && z.flaeche.contains(pre)) {
            e.preventDefault();
            merken(z);
            const r = bereich(z);
            r.deleteContents();
            // am Ende braucht der Browser zwei Umbrüche, damit die neue Zeile sichtbar wird
            const amEnde = r.endContainer === pre ? r.endOffset >= pre.childNodes.length
                : (r.endContainer.nodeType === 3 && r.endOffset === r.endContainer.length && !textNach(pre, r.endContainer));
            const knoten = document.createTextNode(amEnde && !pre.textContent.endsWith('\n') ? '\n\n' : '\n');
            r.insertNode(knoten);
            const nach = document.createRange(); nach.setStart(knoten, 1); nach.collapse(true);
            const s = document.getSelection(); s.removeAllRanges(); s.addRange(nach);
            pre.normalize();
            aenderung(z);
            return;
        }
        // Tab in Tabellen: nächste Zelle, in der letzten eine neue Zeile
        if (e.key === 'Tab' && !strg) {
            const c = zelle(z);
            if (!c) return;
            e.preventDefault();
            const alle = Array.from(c.closest('table').querySelectorAll('td, th'));
            const i = alle.indexOf(c) + (e.shiftKey ? -1 : 1);
            if (i >= alle.length) z.ausfuehren(() => zeileEinfuegen(z));
            else if (i >= 0) { const ziel = alle[i]; const r = document.createRange(); r.selectNodeContents(ziel); const s = document.getSelection(); s.removeAllRanges(); s.addRange(r); }
        }
    }

    function textNach(pre, knoten) {
        const w = document.createTreeWalker(pre, NodeFilter.SHOW_TEXT);
        w.currentNode = knoten;
        let n;
        while ((n = w.nextNode())) if (n.nodeValue.length) return true;
        return false;
    }

    /** Vor jeder Eingabe: Rückgängig-Stand merken (beim Tippen höchstens alle 1,5 s ein Stand). */
    function vorEingabe(z, e) {
        if (e.inputType === 'historyUndo') { e.preventDefault(); rueckgaengig(z); return; }
        if (e.inputType === 'historyRedo') { e.preventDefault(); wiederholen(z); return; }
        const tippen = e.inputType === 'insertText' || e.inputType === 'deleteContentBackward' || e.inputType === 'deleteContentForward';
        if (!tippen || Date.now() - z.letzterStand > 1500) merken(z);
    }

    function merken(z) {
        const stand = { html: z.flaeche.innerHTML, cursor: cursorPfad(z) };
        const oben = z.zurueck[z.zurueck.length - 1];
        if (!oben || oben.html !== stand.html) {
            z.zurueck.push(stand);
            if (z.zurueck.length > 100) z.zurueck.shift();
        }
        z.vor = [];
        z.letzterStand = Date.now();
    }

    function rueckgaengig(z) { umschalten(z, z.zurueck, z.vor); }
    function wiederholen(z) { umschalten(z, z.vor, z.zurueck); }

    function umschalten(z, von, nach) {
        const stand = von.pop();
        if (!stand) { z.flaeche.focus(); return; }
        nach.push({ html: z.flaeche.innerHTML, cursor: cursorPfad(z) });
        z.flaeche.innerHTML = stand.html;
        z.flaeche.focus();
        cursorSetzen(z, stand.cursor);
        z.letzterStand = 0;
        aufraeumen(z);
        aenderung(z); zustand(z);
    }

    function cursorPfad(z) {
        const r = bereich(z);
        if (!r) return null;
        const pfad = [];
        for (let k = r.startContainer; k && k !== z.flaeche; k = k.parentNode) pfad.unshift(Array.prototype.indexOf.call(k.parentNode.childNodes, k));
        return { pfad, offset: r.startOffset };
    }

    function cursorSetzen(z, c) {
        if (!c) { cursorIn(z.flaeche.lastElementChild || z.flaeche, true); return; }
        let k = z.flaeche;
        for (const i of c.pfad) { if (!k.childNodes[i]) { cursorIn(k, true); return; } k = k.childNodes[i]; }
        const r = document.createRange();
        try { r.setStart(k, Math.min(c.offset, k.nodeType === 3 ? k.length : k.childNodes.length)); }
        catch (e) { cursorIn(k, true); return; }
        r.collapse(true);
        const s = document.getSelection(); s.removeAllRanges(); s.addRange(r);
    }

    function einfuegen(z, e) {
        const daten = e.clipboardData;
        if (!daten) return;
        e.preventDefault();
        merken(z);
        const k = aktuellerKnoten(z);
        const html = daten.getData('text/html');
        const text = daten.getData('text/plain');
        if (k && k.closest('pre')) befehl('insertText', text);
        else if (html) befehl('insertHTML', saeubern(html));
        else befehl('insertText', text);
    }

    function ablegen(z, e) {
        // Dateien und fremdes HTML nicht direkt übernehmen — nur Text
        e.preventDefault();
        const text = e.dataTransfer && e.dataTransfer.getData('text/plain');
        if (text) { merken(z); befehl('insertText', text); }
    }

    /** Fremdes HTML auf die erlaubten Elemente reduzieren (Word, Outlook, Webseiten). */
    function saeubern(html) {
        const doc = new DOMParser().parseFromString(html, 'text/html');
        const ziel = document.createElement('div');
        const weg = new Set(['SCRIPT', 'STYLE', 'META', 'LINK', 'TITLE', 'HEAD', 'IMG', 'SVG', 'IFRAME', 'OBJECT', 'EMBED',
            'VIDEO', 'AUDIO', 'CANVAS', 'TEMPLATE', 'NOSCRIPT', 'INPUT', 'BUTTON', 'SELECT', 'TEXTAREA', 'FORM', 'XML']);
        const kopieren = (quelle, nach) => {
            for (const n of quelle.childNodes) {
                if (n.nodeType === 3) { nach.appendChild(document.createTextNode(n.nodeValue)); continue; }
                if (n.nodeType !== 1 || weg.has(n.tagName.toUpperCase())) continue;
                const stil = (n.getAttribute('style') || '').toLowerCase().replace(/\s/g, '');
                if (stil.includes('mso-list:ignore')) continue;   // Word-Aufzählungszeichen
                const neu = abbilden(n, stil);
                if (neu) { nach.appendChild(neu); kopieren(n, neu); } else kopieren(n, nach);
            }
        };
        kopieren(doc.body, ziel);
        // Word-Listenabsätze (li ohne Liste) zu einer Aufzählung zusammenfassen
        let liste = null;
        for (const n of Array.from(ziel.childNodes)) {
            if (n.nodeType === 1 && n.tagName === 'LI') {
                if (!liste) { liste = document.createElement('ul'); n.before(liste); }
                liste.appendChild(n);
            } else if (!(n.nodeType === 3 && !n.nodeValue.trim())) liste = null;
        }
        return ziel.innerHTML;
    }

    function abbilden(n, stil) {
        const tag = n.tagName.toUpperCase();
        const neu = name => document.createElement(name);
        if (/^MsoListParagraph/i.test(n.className || '')) return neu('li');
        switch (tag) {
            case 'H1': case 'H2': case 'H3': return neu('h3');
            case 'H4': return neu('h4');
            case 'H5': case 'H6': return neu('h5');
            case 'P': case 'DIV': case 'BLOCKQUOTE': return neu('p');
            case 'UL': case 'OL': case 'LI': case 'TABLE': case 'TR': case 'PRE': case 'BR': case 'CODE': case 'TD': case 'TH':
                return neu(tag.toLowerCase());
            case 'B': case 'STRONG': return neu('strong');
            case 'I': case 'EM': return neu('em');
            case 'A': {
                const href = n.getAttribute('href') || '';
                if (!/^https?:\/\//i.test(href)) return null;
                const a = neu('a'); a.setAttribute('href', href); return a;
            }
            case 'SPAN': case 'FONT':
                if (/font-weight:(bold|[6-9]00)/.test(stil)) return neu('strong');
                if (stil.includes('font-style:italic')) return neu('em');
                return null;
            default: return null;   // auspacken: Inhalt bleibt, Element fällt weg
        }
    }

    // ================================================================ Aufräumen, Zustand

    /**
     * Struktur in Ordnung halten — nach jeder Änderung: oberste Ebene nur Absätze,
     * Überschriften, Listen, Codeblöcke, Tabellen; keine Blöcke in Absätzen oder
     * Listenpunkten; immer ein Absatz am Ende zum Weiterschreiben. Der Cursor bleibt stehen.
     */
    function aufraeumen(z) {
        const f = z.flaeche;
        const r = bereich(z);
        const merk = r ? { k: r.startContainer, o: r.startOffset } : null;
        let geaendert = false;

        // Absätze/Überschriften mit Blöcken darin auspacken
        for (const a of Array.from(f.querySelectorAll('p, div, h1, h2, h3, h4, h5, h6, blockquote'))) {
            if (Array.from(a.children).some(c => BLOCK.has(c.tagName) && c.tagName !== 'BR') && !a.closest('li, td, th')) {
                a.replaceWith(...a.childNodes); geaendert = true;
            }
        }
        // Absätze in Listenpunkten und Zellen → Zeilenumbrüche
        for (const a of Array.from(f.querySelectorAll('li > p, li > div, li > h1, li > h2, li > h3, li > h4, li > h5, li > h6, td > p, td > div, th > p, th > div'))) {
            if (a.previousSibling && !(a.previousSibling.nodeType === 1 && a.previousSibling.tagName === 'BR') && a.previousSibling.textContent)
                a.before(document.createElement('br'));
            a.replaceWith(...a.childNodes); geaendert = true;
        }
        // Oberste Ebene: lose Inline-Inhalte in Absätze, div → p, h1/h2 → h3, h6 → h5
        let p = null;
        for (const n of Array.from(f.childNodes)) {
            const name = n.nodeType === 1 ? n.tagName : null;
            if (name && OBEN.has(name)) { p = null; continue; }
            if (name && (ABSATZ.has(name) || name === 'BLOCKQUOTE')) {
                const ziel = name === 'H1' || name === 'H2' ? 'H3' : name === 'H6' ? 'H5' : 'P';
                const neu = document.createElement(ziel); neu.append(...n.childNodes); n.replaceWith(neu);
                if (!neu.firstChild) neu.appendChild(document.createElement('br'));
                p = null; geaendert = true; continue;
            }
            if (name === 'LI') {   // loser Listenpunkt
                const ul = document.createElement('ul'); n.replaceWith(ul); ul.appendChild(n); p = null; geaendert = true; continue;
            }
            if (n.nodeType === 3 && !n.nodeValue.trim() && !p) { if (f.childNodes.length > 1) { n.remove(); geaendert = true; } continue; }
            if (!p) { p = document.createElement('p'); n.before(p); }
            p.appendChild(n); geaendert = true;
        }
        if (!f.firstElementChild) { f.innerHTML = '<p><br></p>'; geaendert = true; }
        const letztes = f.lastElementChild;
        if (letztes && (letztes.tagName === 'PRE' || letztes.tagName === 'TABLE' || letztes.tagName === 'UL' || letztes.tagName === 'OL')) {
            const neu = document.createElement('p'); neu.appendChild(document.createElement('br')); f.appendChild(neu);
        }
        f.classList.toggle('leer', !f.textContent.trim() && !f.querySelector('table, pre, li'));

        // Cursor wiederherstellen, falls er durch das Umhängen verrutscht ist
        if (geaendert && merk && merk.k.isConnected && f.contains(merk.k) && document.activeElement === f) {
            const neu = document.createRange();
            try {
                neu.setStart(merk.k, Math.min(merk.o, merk.k.nodeType === 3 ? merk.k.length : merk.k.childNodes.length));
                neu.collapse(true);
                const s = document.getSelection(); s.removeAllRanges(); s.addRange(neu);
            } catch (e) { /* Cursor bleibt, wo der Browser ihn hingesetzt hat */ }
        }
    }

    function aenderung(z) {
        const text = zuText(z.flaeche);
        if (text === z.letzter) return;
        z.letzter = text;
        z.dotnet.invokeMethodAsync('Geaendert', text).catch(() => { /* Komponente bereits entfernt */ });
    }

    function zustand(z) {
        const k = aktuellerKnoten(z);
        const block = k && k.closest('h3, h4, h5, pre, p, li, td, th');
        const t = block && z.flaeche.contains(block) ? block.tagName.toLowerCase() : 'p';
        z.format.value = ['h3', 'h4', 'h5', 'pre'].includes(t) ? t : 'p';
        const aktiv = (name, an) => z.knoepfe[name] && z.knoepfe[name].classList.toggle('an', !!an);
        let fett = false, kursiv = false;
        try { fett = document.queryCommandState('bold'); kursiv = document.queryCommandState('italic'); } catch (e) { }
        const ueberschrift = block && /^H\d$/.test(block.tagName);
        aktiv('bold', k && (ueberschrift ? !!k.closest('b, strong') : fett));
        aktiv('italic', k && kursiv);
        aktiv('code', k && k.closest('code') && !k.closest('pre'));
        const liste = k && k.closest('ul, ol');
        aktiv('ul', liste && liste.tagName === 'UL');
        aktiv('ol', liste && liste.tagName === 'OL');
        aktiv('link', k && k.closest('a'));
        z.tabelle.hidden = !zelle(z);
    }

    // ================================================================ HTML → Text (WikiFormat)

    function zuText(wurzel) {
        return bloecke(wurzel).join('\n\n').replace(/\n{3,}/g, '\n\n').trim();
    }

    function bloecke(wurzel) {
        const aus = [];
        let inline = [];
        const inlineFertig = () => {
            if (inline.length) { const t = absatz(inlineText(inline)); if (t) aus.push(t); }
            inline = [];
        };
        for (const n of wurzel.childNodes) {
            if (n.nodeType === 1 && BLOCK.has(n.tagName)) {
                inlineFertig();
                const b = block(n);
                if (b) aus.push(b);
            } else inline.push(n);
        }
        inlineFertig();
        return aus;
    }

    function block(n) {
        switch (n.tagName) {
            case 'H1': case 'H2': case 'H3': case 'H4': case 'H5': case 'H6': {
                const ebene = { H1: 1, H2: 1, H3: 1, H4: 2, H5: 3, H6: 3 }[n.tagName];
                const t = einzeilig(inlineText(n.childNodes));
                return t ? '#'.repeat(ebene) + ' ' + t : '';
            }
            case 'UL': case 'OL': return liste(n, n.tagName === 'OL').join('\n');
            case 'LI': return liste({ children: [n] }, false).join('\n');
            case 'PRE': {
                const code = preText(n).replace(/​/g, '').replace(/\n+$/, '');
                return code.trim() ? '```\n' + code.replace(/```/g, "'''") + '\n```' : '';
            }
            case 'TABLE': return tabelle(n);
            default:
                // Container mit Blöcken darin (z. B. verschachtelte div) — wie die Wurzel behandeln
                if (Array.from(n.children).some(c => BLOCK.has(c.tagName)))
                    return bloecke(n).join('\n\n');
                return absatz(inlineText(n.childNodes));
        }
    }

    function absatz(text) {
        return text.split('\n').map(z => z.replace(/\s+$/, '')).join('\n').replace(/^\n+|\n+$/g, '');
    }

    function einzeilig(text) { return text.replace(/\s*\n\s*/g, ' ').trim(); }

    function liste(n, nummeriert) {
        const zeilen = [];
        let nr = 1;
        for (const li of Array.from(n.children)) {
            if (li.tagName !== 'LI') continue;
            const eigen = [], unter = [];
            for (const c of li.childNodes) (c.nodeType === 1 && (c.tagName === 'UL' || c.tagName === 'OL') ? unter : eigen).push(c);
            const t = einzeilig(inlineText(eigen));
            if (t) zeilen.push((nummeriert ? (nr++) + '. ' : '- ') + t);
            for (const u of unter) zeilen.push(...liste(u, u.tagName === 'OL'));   // Unterlisten flach anhängen
        }
        return zeilen;
    }

    function tabelle(n) {
        const zeilen = Array.from(n.querySelectorAll('tr')).filter(tr => tr.closest('table') === n);
        const aus = [];
        zeilen.forEach(tr => {
            const zellen = Array.from(tr.children).filter(c => c.tagName === 'TD' || c.tagName === 'TH')
                .map(c => einzeilig(inlineText(c.childNodes)).replace(/\|/g, '/'));
            if (!zellen.length || !zellen.some(x => x)) return;   // leere Zeilen fallen weg
            aus.push('| ' + zellen.join(' | ') + ' |');
            if (aus.length === 1) aus.push('| ' + zellen.map(() => '---').join(' | ') + ' |');
        });
        return aus.join('\n');
    }

    function preText(n) {
        let t = '';
        for (const c of n.childNodes) {
            if (c.nodeType === 3) t += c.nodeValue;
            else if (c.nodeType === 1 && c.tagName === 'BR') t += '\n';
            else if (c.nodeType === 1) {
                if ((c.tagName === 'DIV' || c.tagName === 'P') && t && !t.endsWith('\n')) t += '\n';
                t += preText(c);
            }
        }
        return t;
    }

    /** Inline-Inhalt → Text mit **fett**, *kursiv*, `Code` und [Text](Link). */
    function inlineText(knoten) {
        const laeufe = [];
        const gehen = (n, st) => {
            if (n.nodeType === 3) {
                const t = n.nodeValue.replace(/ /g, ' ').replace(/​/g, '').replace(/[\r\n\t]+/g, ' ');
                if (t) laeufe.push({ t, f: st.f, k: st.k, c: st.c, a: st.a });
                return;
            }
            if (n.nodeType !== 1) return;
            const tag = n.tagName;
            if (tag === 'BR') { laeufe.push({ t: '\n', umbruch: true, a: st.a }); return; }
            if (tag === 'SCRIPT' || tag === 'STYLE') return;
            const neu = { ...st };
            if (tag === 'B' || tag === 'STRONG') neu.f = true;
            if (tag === 'I' || tag === 'EM') neu.k = true;
            if (tag === 'CODE') neu.c = true;
            if (tag === 'A' && /^https?:\/\//i.test(n.getAttribute('href') || '')) neu.a = n.getAttribute('href');
            const s = n.style;
            if (s && (s.fontWeight === 'bold' || parseInt(s.fontWeight, 10) >= 600)) neu.f = true;
            if (s && (s.fontWeight === 'normal' || s.fontWeight === '400')) neu.f = false;
            if (s && s.fontStyle === 'italic') neu.k = true;
            if (BLOCK.has(tag) && laeufe.length) laeufe.push({ t: '\n', umbruch: true });
            for (const c of n.childNodes) gehen(c, neu);
        };
        for (const n of knoten) gehen(n, {});

        // nach Link gruppieren, jede Gruppe für sich auszeichnen
        let aus = '';
        for (let i = 0; i < laeufe.length;) {
            const a = laeufe[i].a;
            let j = i;
            while (j < laeufe.length && laeufe[j].a === a) j++;
            const gruppe = laeufe.slice(i, j);
            const text = auszeichnen(gruppe);
            if (!a) aus += text;
            else {
                const roh = gruppe.map(l => l.t).join('').trim();
                const href = a.replace(/\(/g, '%28').replace(/\)/g, '%29').replace(/ /g, '%20');
                aus += roh === a ? href : '[' + text.replace(/[[\]]/g, '').replace(/\n/g, ' ').trim() + '](' + href + ')';
            }
            i = j;
        }
        return aus;
    }

    /** Läufe mit Fett/Kursiv/Code zu Text — Markierungen eng am Wort, kursiv innen. */
    function auszeichnen(laeufe) {
        let aus = '', f = false, k = false;
        const schliessen = zeichen => {
            const rest = aus.match(/\s*$/)[0];
            aus = aus.slice(0, aus.length - rest.length) + zeichen + rest;
        };
        const allesZu = () => { if (k) schliessen('*'); if (f) schliessen('**'); f = k = false; };
        for (const l of laeufe) {
            if (l.umbruch) { allesZu(); aus += '\n'; continue; }
            if (l.c) { allesZu(); if (l.t.trim()) aus += '`' + l.t.replace(/`/g, "'") + '`'; else aus += l.t; continue; }
            const lf = !!l.f, lk = !!l.k;
            if (!l.t.trim()) { aus += l.t; continue; }   // reiner Leerraum ändert die Auszeichnung nicht
            if (lf !== f) { if (k) schliessen('*'); if (f) schliessen('**'); f = false; k = false; }
            else if (lk !== k && k) { schliessen('*'); k = false; }
            const vorne = l.t.match(/^\s*/)[0];
            aus += vorne;
            if (lf && !f) { aus += '**'; f = true; }
            if (lk && !k) { aus += '*'; k = true; }
            aus += l.t.slice(vorne.length);
        }
        allesZu();
        return aus;
    }

    // ================================================================ Hilfen

    function el(tag, klasse) { const e = document.createElement(tag); if (klasse) e.className = klasse; return e; }

    function kodieren(t) {
        return String(t).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    /**
     * Angezeigten Wiki-Text als Klartext (z. B. „Lösung kopieren"): Listen mit „1." bzw. „–",
     * Links als „Text (Adresse)", Tabellenzellen mit Tab getrennt.
     */
    function klartext(selektor) {
        const wurzel = document.querySelector(selektor);
        if (!wurzel) return null;
        const zeilen = [];
        const inline = n => {
            let t = '';
            for (const c of n.childNodes) {
                if (c.nodeType === 3) t += c.nodeValue;
                else if (c.nodeType === 1 && c.tagName === 'BR') t += '\n';
                else if (c.nodeType === 1 && c.tagName === 'A') t += c.textContent === c.getAttribute('href') ? c.textContent : c.textContent + ' (' + c.getAttribute('href') + ')';
                else if (c.nodeType === 1) t += inline(c);
            }
            return t;
        };
        for (const b of wurzel.children) {
            if (b.tagName === 'UL' || b.tagName === 'OL')
                Array.from(b.children).forEach((li, i) => zeilen.push((b.tagName === 'OL' ? (i + 1) + '. ' : '– ') + inline(li).trim()));
            else if (b.tagName === 'TABLE')
                b.querySelectorAll('tr').forEach(tr => zeilen.push(Array.from(tr.children).map(c => inline(c).trim()).join('\t')));
            else if (b.tagName === 'PRE') zeilen.push(b.textContent);
            else zeilen.push(inline(b).trim());
            zeilen.push('');
        }
        return zeilen.join('\n').trim();
    }

    window.wikiEditor = { erstellen, setzen, entfernen, zuText, klartext };
})();
