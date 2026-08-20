# Prompt Helper – vollständiger Nutzungsguide für Einsteiger

**Version des Guides:** 2.0 – vollständig geprüft und überarbeitet  
**Gültig für die veröffentlichte Anwendung:** Prompt Helper v0.1.0  
**Release-Ziel:** Windows x64, primär Windows 11  
**Zielgruppe:** Anwender ohne technische Vorkenntnisse  
**Sprache der Anwendung:** Englisch  
**Sprache dieses Guides:** Deutsch

---

## 0. Wozu dieser Guide dient

Dieser Guide erklärt Prompt Helper vom ersten Download bis zu Backup, Recovery und Fehleranalyse.

Er ist bewusst so geschrieben, dass du ihn auch dann verwenden kannst, wenn du:

- noch nie mit einer Prompt-Bibliothek gearbeitet hast,
- nicht weißt, was `LocalAppData` ist,
- keine Erfahrung mit JSON-, Markdown- oder GUID-Dateien hast,
- bei Fehlermeldungen nicht selbst in Programmdateien eingreifen möchtest.

Für wichtige Abläufe findest du jeweils:

- **Ziel**
- **Schritt für Schritt**
- **Erwartetes Ergebnis**
- **Wenn etwas anderes passiert**

Außerdem enthält der Guide reproduzierbare Testabläufe, die sich direkt für Support und Bug Reports verwenden lassen.

---

# Inhaltsverzeichnis

1. [Was ist Prompt Helper?](#1-was-ist-prompt-helper)
2. [Das Wichtigste in 60 Sekunden](#2-das-wichtigste-in-60-sekunden)
3. [Installation](#3-installation)
4. [Erster Start](#4-erster-start)
5. [Programmordner und Datenordner verstehen](#5-programmordner-und-datenordner-verstehen)
6. [Das Hauptfenster](#6-das-hauptfenster)
7. [Kategorien verwenden](#7-kategorien-verwenden)
8. [Prompts erstellen und bearbeiten](#8-prompts-erstellen-und-bearbeiten)
9. [Copy – Prompt in die Zwischenablage kopieren](#9-copy--prompt-in-die-zwischenablage-kopieren)
10. [Move – Prompt verschieben](#10-move--prompt-verschieben)
11. [Copy instead of move – Prompt duplizieren](#11-copy-instead-of-move--prompt-duplizieren)
12. [Prompts und Kategorien löschen](#12-prompts-und-kategorien-löschen)
13. [Sortierung und Navigation](#13-sortierung-und-navigation)
14. [Datenablage im Detail](#14-datenablage-im-detail)
15. [Backups richtig erstellen](#15-backups-richtig-erstellen)
16. [Backup wiederherstellen](#16-backup-wiederherstellen)
17. [Automatische Sicherheitskopie und Recovery](#17-automatische-sicherheitskopie-und-recovery)
18. [Unavailable Prompts](#18-unavailable-prompts)
19. [Typische Fehlermeldungen](#19-typische-fehlermeldungen)
20. [Tastaturbedienung](#20-tastaturbedienung)
21. [Datenschutz und Sicherheit](#21-datenschutz-und-sicherheit)
22. [Update auf eine neue Version](#22-update-auf-eine-neue-version)
23. [Deinstallation](#23-deinstallation)
24. [Empfohlene Organisationsstruktur](#24-empfohlene-organisationsstruktur)
25. [Vollständiger Anfänger-Funktionstest](#25-vollständiger-anfänger-funktionstest)
26. [Fehler sauber melden](#26-fehler-sauber-melden)
27. [FAQ](#27-faq)
28. [Schnellreferenz](#28-schnellreferenz)

---

# 1. Was ist Prompt Helper?

Prompt Helper ist ein kleines lokales Windows-Programm zum Speichern, Organisieren, Bearbeiten und schnellen Kopieren wiederverwendbarer AI-Prompts.

Ein typischer Ablauf sieht so aus:

```text
Prompt Helper öffnen
→ Kategorie auswählen
→ Prompt finden
→ Copy klicken
→ zu ChatGPT / Claude / Codex / anderem Tool wechseln
→ Strg + V
```

Prompt Helper führt selbst **keine AI-Anfrage** aus.

Die Anwendung:

- ruft keine AI-API auf,
- schickt deine Prompts nicht selbst an ChatGPT oder andere Anbieter,
- besitzt kein Benutzerkonto,
- besitzt keine Cloud-Synchronisierung.

Die Aufgabe des Programms ist ausschließlich:

```text
Prompt-Texte lokal verwalten
+
Prompt-Texte in die Windows-Zwischenablage kopieren
```

---

# 2. Das Wichtigste in 60 Sekunden

Wenn du sofort loslegen möchtest:

1. ZIP vollständig entpacken.
2. `PromptHelper.exe` starten.
3. Mit **+ Add** eine Kategorie anlegen.
4. Kategorie öffnen.
5. Mit **+ Prompt** einen Prompt erstellen (mit optionaler Headline und Zeilenumbruch-Vorschau via **Wrap long lines**).
6. Im Editor auf **Save** klicken.
7. Beim Prompt auf **Copy** klicken (wird auch in die Quick-Bar der letzten Kopien übernommen).
8. In deinem AI-Tool `Strg + V` drücken.
9. Mit **Edit** änderst du einen Prompt.
10. Mit **Move** verschiebst du ihn.
11. Mit **Move → Copy instead of move** duplizierst du ihn.
12. Oben rechts über das Schraubenschlüssel-Symbol **🔧 Tools and settings** kannst du den Datenordner anpassen.

Die Prompt-Bibliothek liegt standardmäßig hier:

```text
%LOCALAPPDATA%\PromptHelper
```

Die Bootstrap-Konfiguration liegt dauerhaft unter:

```text
%LOCALAPPDATA%\PromptHelper\settings.json
%LOCALAPPDATA%\PromptHelper\settings.backup.json
```

Für ein echtes Backup immer den **gesamten aktiven Datenordner** sichern (Pfad siehe Tools & Settings).

---

# 3. Installation

## 3.1 Fertige Release-Version verwenden

Die fertige Windows-Version wird als ZIP bereitgestellt.

Der Dateiname sieht zum Beispiel so aus:

```text
PromptHelper-v0.1.0-win-x64.zip
```

## Schritt für Schritt

1. Lade die Release-ZIP aus der vertrauenswürdigen Projektquelle herunter.
2. Öffne den Download-Ordner.
3. Klicke die ZIP mit der rechten Maustaste an.
4. Wähle **Alle extrahieren...**
5. Wähle einen dauerhaften Ordner.

Einfacher benutzerbezogener Beispielpfad:

```text
C:\Users\<DEIN-NAME>\Apps\PromptHelper
```

Alternativ kannst du einen eigenen Tools-Ordner verwenden, auf den du Schreib-/Leserechte hast.

6. Öffne den entpackten Ordner.
7. Suche:

```text
PromptHelper.exe
```

8. Doppelklicke auf die EXE.

## Erwartetes Ergebnis

Das Fenster **Prompt Helper** öffnet sich.

---

## 3.2 Nicht direkt aus der ZIP arbeiten

Benutze die Anwendung nicht dauerhaft aus der noch gepackten ZIP.

Richtig:

```text
ZIP
→ vollständig entpacken
→ entpackten Ordner öffnen
→ PromptHelper.exe starten
```

---

## 3.3 Falls Windows eine Sicherheitswarnung zeigt

Je nach Windows-Einstellungen und Signaturstatus kann Windows beim ersten Start eine Sicherheits- oder SmartScreen-Warnung zeigen.

Dann gilt:

1. Prüfe, ob die Datei wirklich aus der vertrauenswürdigen Projekt-Releasequelle stammt.
2. Prüfe Dateiname und Version.
3. Fahre nur fort, wenn du der Quelle vertraust.

Nicht sinnvoll:

```text
jede beliebige EXE-Warnung blind übergehen
```

---

## 3.4 Muss .NET installiert werden?

Für die vorgesehene veröffentlichte `win-x64`-Version wurde Prompt Helper selbstständig veröffentlicht.

Das bedeutet:

```text
Normale Nutzung der fertigen Release-Version
→ keine separate .NET-Installation vorgesehen
```

Wenn du dagegen den Quellcode selbst bauen möchtest, brauchst du eine passende .NET-10-Entwicklungsumgebung.

---

# 4. Erster Start

Beim ersten sauberen Start legt Prompt Helper seine lokale Datenstruktur an.

Außerdem werden Beispielkategorien erstellt.

Typische Anfangsstruktur:

```text
Home
├── Games
│   ├── Planning
│   ├── Implementation
│   └── Testing
│
└── Tools
    ├── Planning
    ├── Implementation
    └── Testing
```

Zusätzlich existieren Beispiel-Prompts in:

```text
Games > Planning
```

und:

```text
Tools > Testing
```

Diese Beispiele dienen als Startpunkt.

Du kannst sie später:

- bearbeiten,
- verschieben,
- duplizieren,
- löschen.

---

# 5. Programmordner und Datenordner verstehen

Das ist einer der wichtigsten Punkte.

Prompt Helper hat getrennte Orte:

## Programmordner

Dort liegt zum Beispiel:

```text
PromptHelper.exe
```

Beispiel:

```text
C:\Users\Anna\Apps\PromptHelper
```

## Datenordner

Dort liegen deine Kategorien und Prompt-Dateien.

Standard:

```text
%LOCALAPPDATA%\PromptHelper
```

Über das Schraubenschlüssel-Symbol **Tools and settings** oben rechts kannst du einen beliebigen anderen Datenordner auf deiner Festplatte auswählen:

- **Bei Auswahl eines LEEREN Ordners:** Dein aktueller Datenbestand wird vollständig in den neuen Zielordner kopiert. Dein bisheriger Ordner bleibt als unberührte Sicherheitskopie erhalten. Prompt Helper schließt sich unmittelbar nach dem Speichern, damit der alte Stand nicht versehentlich weiterverändert wird. Öffne Prompt Helper danach einfach wieder, um mit dem neuen Ordner weiterzuarbeiten.
- **Bei Auswahl eines Ordners mit BESTEHENDER Bibliothek:** Dein bisheriger Bestand wird **weder kopiert, noch überschrieben oder zusammengeführt**. Nach einer expliziten Sicherheitsabfrage schließt sich Prompt Helper. Beim nächsten Öffnen lädt Prompt Helper die bereits vorhandene Bibliothek aus dem Zielordner.

Die Bootstrap-Konfiguration (wo sich der aktive Datenordner befindet) verbleibt immer fest unter:

```text
%LOCALAPPDATA%\PromptHelper\settings.json
%LOCALAPPDATA%\PromptHelper\settings.backup.json
```

Diese Trennung bedeutet:

```text
EXE löschen
≠
automatisch Prompt-Daten löschen
```

und:

```text
neue EXE-Version installieren
≠
Datenordner neu anlegen müssen
```

---

# 6. Das Hauptfenster

Das Hauptfenster besitzt folgende wesentliche Bereiche.

---

## 6.1 Kopfbereich

Oben links:

```text
Prompt Helper
```

Oben rechts:

```text
🔧
```

Der `🔧`-Button öffnet den Dialog **Tools & Settings**.

---

## 6.2 Tools & Settings Dialog

Klicke oben rechts auf:

```text
🔧
```

Der Dialog zeigt unter anderem:

- den aktuellen Datenordner (**Data folder**),
- einen **Browse…**-Button zur Auswahl eines neuen Speicherorts,
- die laufende Programmversion,
- praktische Bedienhinweise,
- die Autorenzeile `Made by CeeGore`.

Besonders hilfreich:

```text
Data folder:
```

Hier siehst du exakt, wo deine Prompts und Kategorien gespeichert sind. Wenn du einen neuen Ordner auswählst und auf **Save** klickst, wird dein Datenbestand dorthin migriert. Ein Neustart von Prompt Helper übernimmt den neuen Pfad.

---

## 6.3 Quick-Bar der letzten Kopien (Recent Prompts)

Direkt unter der Kopfzeile befindet sich eine praktische Schnellauswahlleiste:

- Zeigt bis zu **3** zuletzt erfolgreich kopierte Prompts nebeneinander an.
- Beginnt bei jedem Start von Prompt Helper leer (rein sitzungsbasiert, wird nicht auf Festplatte gespeichert).
- Neueste Kopie erscheint links (Position 1).
- Beim erneuten Kopieren eines bereits vorhandenen Prompts rückt dieser an die erste Stelle.
- Jede Kachel enthält die Headline, einen kurzen Textauszug und einen kleinen **Copy**-Button.
- Beim Löschen eines Prompts wird er automatisch auch aus der Schnellauswahlleiste entfernt.
- Beim Bearbeiten eines Prompts aktualisieren sich Headline und Auszug in der Leiste in Echtzeit.

---

## 6.4 Breadcrumb-Navigation

Beispiel:

```text
Home › Games › Planning
```

Die Breadcrumb-Leiste zeigt, wo du dich gerade befindest.

Übergeordnete Einträge können angeklickt werden.

Beispiel:

```text
Home › Games › Planning
```

Klick auf:

```text
Games
```

führt zu:

```text
Home › Games
```

Klick auf:

```text
Home
```

führt zur obersten Ebene.

---

## 6.5 Categories

Unter **Categories** siehst du die Unterkategorien der aktuellen Ebene.

Rechts:

```text
+ Add
```

Jede Kategorie besitzt:

```text
Kategoriename
🔧 (Aktionsmenü)
```

Bedeutung:

- Klick auf den Namen → Kategorie öffnen
- Klick auf `🔧` öffnet ein Kontextmenü:
  - `✎ Rename` → Kategorie umbenennen
  - `× Delete` → leere Kategorie löschen

---

## 6.6 Prompts (3-Spalten-Raster)

Unter **Prompts** siehst du die Prompts der aktuellen Ebene in einem übersichtlichen Raster von **3 Karten pro Zeile**.

Rechts:

```text
+ Prompt
```

Eine Prompt-Karte besitzt eine 4-spaltige Aktionsleiste:

```text
Delete | Edit | Move | Copy
```

Darunter befindet sich eine kurze, kompakte Textvorschau.

---

## 6.7 Vollständige Vorschau per Hover (Tooltip)

Wenn du mit dem Mauszeiger ca. **0,5 Sekunden** über einer Prompt-Karte verweilst:

- Öffnet sich automatisch ein scrollbares Vorschaufenster mit dem **vollständigen Prompt-Text**.
- Zeilenumbrüche und Formatierungen bleiben exakt erhalten.
- Schnelles Überfliegen mit der Maus löst keine störenden Tooltips aus.
- Unabhängig von der gekürzten Vorschau auf der Karte kopiert der **Copy**-Button immer den **vollständigen** Prompt in die Zwischenablage.

---

# 7. Kategorien verwenden

# 7.1 Kategorie erstellen

## Ziel

Eine neue Kategorie in der aktuellen Ebene erstellen.

## Schritt für Schritt

1. Navigiere zur gewünschten Ebene.
2. Klicke:

```text
+ Add
```

3. Der Dialog **Create Category** öffnet sich.
4. Gib einen Namen ein.

Beispiel:

```text
E-Mails
```

5. Klicke **Create**.

Alternativ:

```text
Enter
```

## Erwartetes Ergebnis

Die neue Kategorie erscheint im Bereich **Categories**.

---

# 7.2 Regeln für Kategorienamen

Ein Kategoriename:

- darf nicht leer sein,
- darf nicht nur aus Leerzeichen bestehen,
- darf keine Steuerzeichen enthalten,
- darf maximal 80 sichtbare Textelemente lang sein,
- darf auf derselben Ebene nicht bereits existieren.

Groß-/Kleinschreibung wird bei der Duplikatprüfung ignoriert.

Wenn vorhanden:

```text
Testing
```

ist auf derselben Ebene nicht zusätzlich erlaubt:

```text
testing
```

oder:

```text
TESTING
```

Auf verschiedenen Ebenen ist derselbe Name erlaubt:

```text
Games > Testing
Tools > Testing
```

---

# 7.3 Führende und folgende Leerzeichen

Prompt Helper trimmt den Kategorienamen.

Beispiel-Eingabe:

```text
   Reports   
```

wird gespeichert als:

```text
Reports
```

---

# 7.4 Doppelten Kategorienamen reproduzieren

## Steps to reproduce

1. Erstelle:

```text
Test
```

2. Klicke erneut **+ Add**.
3. Gib ein:

```text
test
```

4. Versuche zu erstellen.

## Erwartetes Ergebnis

Die Eingabe wird abgelehnt.

Sinngemäß erscheint:

```text
A category named 'test' already exists in this location.
```

Es entsteht keine zweite Kategorie.

---

# 7.5 Unterkategorie erstellen

Beispielziel:

```text
Arbeit
└── E-Mails
```

## Schritte

1. Auf `Home` **+ Add**.
2. `Arbeit` erstellen.
3. `Arbeit` öffnen.
4. Dort **+ Add**.
5. `E-Mails` erstellen.

## Erwartetes Ergebnis

Pfad:

```text
Home › Arbeit › E-Mails
```

---

# 7.6 Kategorie umbenennen

1. Suche die Kategorie.
2. Klicke auf das Schraubenschlüssel-Symbol:

```text
🔧
```

3. Wähle im Menü **✎ Rename**.
4. Der Dialog **Rename Category** öffnet sich.
5. Ändere den Namen.
6. Klicke **Save** oder drücke `Enter`.

Unterkategorien und Prompts bleiben erhalten.

---

# 7.7 Kategorie löschen

Eine Kategorie kann nur gelöscht werden, wenn sie leer ist.

Nicht leer bedeutet:

- mindestens ein Prompt,
- oder mindestens eine Unterkategorie.

## Steps to reproduce – Schutz testen

1. Erstelle `Arbeit`.
2. Öffne `Arbeit`.
3. Erstelle `E-Mails`.
4. Gehe zurück zu `Home`.
5. Klicke bei `Arbeit` auf `🔧` und wähle `× Delete`.

## Erwartetes Ergebnis

Die Kategorie bleibt erhalten.

Exakte Meldung:

```text
This category is not empty.

Move or delete its prompts and subcategories first.
```

---

# 7.8 Leere Kategorie löschen

1. Erstelle eine leere Kategorie `Alt`.
2. Klicke auf `🔧` und wähle `× Delete`.
3. **Delete Category** öffnet sich.
4. Klicke **Delete**.

`Cancel` oder `Escape` brechen ab.

---

# 8. Prompts erstellen und bearbeiten

# 8.1 Neuen Prompt erstellen

Prompts werden in der aktuell geöffneten Ebene angelegt.

Beispiel:

```text
Home › Arbeit › E-Mails
```

→ neuer Prompt landet in `E-Mails`.

## Schritte

1. Öffne die Zielkategorie.
2. Klicke:

```text
+ Prompt
```

3. Der Dialog **Create Prompt** öffnet sich.
4. **Headline (optional):** Du kannst eine eigene Überschrift für die Prompt-Karte vergeben. Wenn du das Feld leer lässt, wird die Überschrift automatisch aus der ersten Textzeile generiert.
5. **Wrap long lines:** Setze bei Bedarf den Haken, um lange Zeilen im Editor visuell umzubrechen. Dies dient nur der besseren Lesbarkeit im Editor und verändert den gespeicherten Prompt-Text niemals.
6. Gib deinen Prompt-Text ein.
7. Klicke:

```text
Save
```

## Beispiel

```text
Headline: E-Mail Assistent

Prompt-Text:
E-MAIL – Professionelle Antwort erstellen

Formuliere aus den folgenden Stichpunkten eine kurze professionelle E-Mail.

Anforderungen:
- freundlich
- sachlich
- maximal 150 Wörter
```

## Erwartetes Ergebnis

Der Editor schließt sich und eine neue Prompt-Karte im 3-Spalten-Raster erscheint.

---

# 8.2 Wie der Kartentitel entsteht (Headline und Automatik-Modus)

Prompt Helper bietet zwei Modi für den Kartentitel:

1. **Benutzerdefinierte Headline:** Wenn du im Feld `Headline` einen eigenen Titel eingegeben hast, wird dieser auf der Karte angezeigt.
2. **Automatischer Modus:** Wenn das Feld `Headline` leer gelassen wird, erzeugt Prompt Helper die Überschrift automatisch aus der **ersten nicht-leeren Zeile** des Prompt-Texts.

### Wichtiges Verhalten beim Bearbeiten:

- **Automatisch vorausgefüllter Titel:** Öffnest du einen Prompt im automatischen Modus zur Bearbeitung, wird die bisherige automatische Überschrift im Headline-Feld vorausgefüllt angezeigt. Wenn du dieses Feld **nicht veränderst**, bleibt der Prompt im automatischen Modus. Änderst du später die erste Zeile des Textes, passt sich die Überschrift weiterhin automatisch an!
- **Manuelles Festlegen:** Sobald du das Headline-Feld explizit abänderst und speicherst, wird deine Eingabe als feste benutzerdefinierte Headline gespeichert.
- **Zurück zum Automatik-Modus:** Löschst du das Headline-Feld komplett leer, wechselt der Prompt wieder in den automatischen Modus.

---

# 8.3 Leerer Prompt

Ein leerer Prompt ist technisch erlaubt.

## Steps to reproduce

1. **+ Prompt**
2. nichts eingeben
3. **Save**

## Erwartetes Ergebnis

Kartentitel:

```text
(Empty prompt)
```

---

# 8.4 Prompt bearbeiten

1. Beim Prompt **Edit** klicken.
2. Der vollständige Inhalt und die Headline öffnen sich im Editor.
3. Text oder Headline ändern.
4. Bei Bedarf **Wrap long lines** aktivieren, um überlange Zeilen lesbar umzubrechen.
5. **Save** klicken.

---

# 8.5 Bearbeiten abbrechen

Im Prompt-Editor:

```text
Cancel
```

oder:

```text
Escape
```

Der zuletzt gespeicherte Stand bleibt erhalten.

---

# 8.6 Enter und Tab im Prompt-Editor

Wichtig:

```text
Enter
→ neue Zeile
```

Enter speichert **nicht**.

Zum Speichern musst du **Save** anklicken.

Ebenfalls wichtig:

```text
Tab
→ Tabulatorzeichen in den Prompt einfügen
```

Das ist absichtlich so, weil der Editor `Tab` als Texteingabe akzeptiert.

Außerhalb des Editors wird `Tab` normal für die Fokusnavigation verwendet.

---

# 8.7 Sehr große Prompts

Prompt Helper besitzt im Prompt-Editor keine kleine feste Textlängenbegrenzung.

Auch große Texte können gespeichert werden.

Praktisch gilt trotzdem:

- extrem große Texte brauchen mehr Speicher,
- sehr große Inhalte können Scrollen/Bearbeiten langsamer machen,
- die Ziel-AI kann eigene Kontext-/Eingabelimits haben.

Prompt Helpers mögliche Textgröße ist also nicht automatisch das Limit des später verwendeten AI-Dienstes.

---

# 8.8 Was passiert bei einem Speicherfehler?

Wenn ein neuer oder bearbeiteter Prompt nicht gespeichert werden kann, zeigt Prompt Helper eine Fehlermeldung.

Bei Create/Edit hält der Dialogablauf den zuletzt eingegebenen Text erneut bereit, damit deine Arbeit nach einem Save-Fehler nicht sofort verloren ist.

Trotzdem empfohlen:

1. Fehlermeldung lesen.
2. `Strg + A`.
3. `Strg + C`.
4. Text vorsichtshalber in Notepad einfügen.
5. Ursache prüfen.
6. erneut speichern.

---

# 9. Copy – Prompt in die Zwischenablage kopieren

# 9.1 Normaler Ablauf

1. Prompt suchen.
2. **Copy** klicken.
3. Button zeigt kurz:

```text
Copied ✓
```

4. Zum Zielprogramm wechseln.
5. `Strg + V`.

## Erwartetes Ergebnis

Der **vollständige Prompt** wird eingefügt.

Nicht nur:

- Kartentitel,
- sichtbarer Ausschnitt,
- erste Zeile.

Nach ungefähr einer Sekunde wird der Button wieder zu:

```text
Copy
```

---

# 9.2 Copy mit Unicode und Markdown

Prompt Helper kopiert den gespeicherten Text.

Beispiel:

````text
Unicode: ä ö ü ß 日本語 🚀

```json
{
  "test": true
}
```
````

soll vollständig in die Zwischenablage gelangen.

---

# 10. Move – Prompt verschieben

# 10.1 Prompt in andere Kategorie verschieben

Beispiel:

```text
Arbeit > Allgemein
```

nach:

```text
Arbeit > E-Mails
```

## Schritte

1. Beim Prompt **Move** klicken.
2. **Move Prompt** öffnet sich.
3. Unter **Destination** Ziel auswählen.
4. **Copy instead of move** nicht aktivieren.
5. **Move** klicken oder `Enter`.

## Erwartetes Ergebnis

Der Prompt verschwindet aus der Quellkategorie und erscheint im Ziel.

---

# 10.2 Sehr wichtig: aktuelle Kategorie ist zunächst ausgewählt

Wenn der Move-Dialog öffnet, ist standardmäßig die **aktuelle Kategorie** ausgewählt.

Das bedeutet:

```text
Move öffnen
→ Ziel nicht ändern
→ Move
```

führt absichtlich zu:

```text
keine Änderung
```

Das ist kein Fehler.

Für ein echtes Verschieben musst du eine andere Destination auswählen.

---

# 10.3 Nach Home verschieben

Im Zielmenü steht:

```text
Home
```

an erster Stelle.

Wähle `Home`, um einen Prompt direkt auf die oberste Ebene zu verschieben.

---

# 10.4 Seltene disambiguierte Zielnamen

Kategorienamen dürfen Zeichen enthalten, durch die zwei dargestellte Pfade theoretisch gleich aussehen können.

In solchen seltenen Fällen kann Prompt Helper im Move-Dialog einen Zusatz anzeigen:

```text
[4a23c8f1]
```

oder einen längeren internen Suffix.

Beispiel:

```text
Home [4a23c8f1]
```

Das ist kein beschädigter Kategoriename.

Der Zusatz dient nur dazu, zwei sonst gleich aussehende Ziele eindeutig unterscheidbar zu machen.

Der echte Root-Eintrag:

```text
Home
```

bleibt ohne Suffix.

---

# 11. Copy instead of move – Prompt duplizieren

Prompt Helper besitzt keinen separaten großen `Duplicate`-Button.

Duplizieren erfolgt über **Move**.

## Schritte

1. Beim Prompt **Move**.
2. Destination auswählen.
3. Checkbox aktivieren:

```text
Copy instead of move
```

4. Der Aktionsbutton zeigt:

```text
Copy
```

5. Aktion bestätigen.

## Erwartetes Ergebnis

Original bleibt erhalten.

Zusätzlich entsteht eine unabhängige Kopie im Ziel.

---

# 11.1 In derselben Kategorie duplizieren

Da die aktuelle Kategorie vorausgewählt ist, kannst du:

```text
Move
→ Copy instead of move
→ Copy
```

verwenden, um eine Kopie direkt in derselben Kategorie anzulegen.

Das ist nützlich, wenn du anschließend eine Variante bearbeiten möchtest.

---

# 11.2 Unavailable Prompt kann nicht dupliziert werden

Wenn der Inhalt eines Prompts fehlt, ist:

```text
Copy instead of move
```

deaktiviert.

Im Dialog erscheint sinngemäß:

```text
Unavailable prompts can be moved but cannot be duplicated.
```

Grund:

Prompt Helper kann keine echte Kopie erzeugen, wenn die Quelldatei nicht gelesen werden kann.

---

# 12. Prompts und Kategorien löschen

# 12.1 Prompt löschen

1. **Delete** beim Prompt klicken.
2. Dialog **Delete Prompt** erscheint.
3. **Delete** bestätigen.

Es gibt keinen Papierkorb in der Oberfläche.

---

# 12.2 Kategorie löschen

Nur leere Kategorien können gelöscht werden.

Wenn eine Kategorie Inhalt besitzt, musst du zuerst:

- Prompts verschieben oder löschen,
- Unterkategorien leeren/löschen.

---

# 12.3 Sonderfall: Löschung logisch erfolgreich, Datei bleibt auf Platte

Prompt Helper schützt die Metadatenkonsistenz sehr vorsichtig.

Es kann selten passieren:

```text
Prompt ist aus der Bibliothek entfernt
aber
.md-Datei konnte nicht gelöscht werden
```

Dann erscheint eine Warnung.

Die Aktion kann trotzdem logisch erfolgreich gewesen sein.

Wichtig:

```text
Warnung genau lesen
→ Zustand prüfen
→ nicht sofort mehrfach Delete klicken
```

Die übrig gebliebene `.md` kann dann zu einer **Orphan-Datei** werden.

Prompt Helper löscht solche unbekannten Dateien nicht automatisch.

---

# 12.4 Sonderfall: Safety Backup konnte beim Delete nicht aktualisiert werden

Wenn `library.json` erfolgreich aktualisiert wurde, aber `library.backup.json` nicht, bewahrt Prompt Helper die `.md`-Datei absichtlich auf.

Grund:

Falls später aus dem älteren Backup recovered werden muss, soll der dort möglicherweise noch referenzierte Prompt-Inhalt nicht bereits zerstört sein.

Das ist ein Sicherheitsverhalten, kein „halb kaputtes Delete“.

Nach einer solchen Warnung:

1. Prompt Helper schließen.
2. vollständiges externes Backup des Datenordners erstellen.
3. Ursache für den Backup-Schreibfehler prüfen.

---

# 13. Sortierung und Navigation

# 13.1 Keine manuelle Reihenfolge

Die aktuelle Version besitzt keine:

- Drag-and-drop-Sortierung,
- Pfeile „nach oben/nach unten“,
- frei einstellbare Reihenfolge.

Neue Elemente erhalten intern eine Reihenfolge und werden normalerweise entsprechend angehängt.

Eine reine Umbenennung muss deshalb nicht dazu führen, dass die Kategorie an eine alphabetisch andere Position springt.

---

# 13.2 Kein globales Suchfeld

Die aktuelle Version besitzt keine globale Suche.

Deshalb empfehlen sich:

- wenige klare Hauptkategorien,
- aussagekräftige Prompt-Erstzeilen,
- nicht zu tiefe unnötige Strukturen.

---

# 13.3 Breadcrumbs bei tiefen Strukturen

Bei langen Pfaden kann die Breadcrumb-Leiste horizontal scrollen.

Damit kannst du auch bei tiefer Verschachtelung zu übergeordneten Kategorien zurückkehren.

---

# 14. Datenablage im Detail

Standardmäßiger Datenordner:

```text
%LOCALAPPDATA%\PromptHelper
```

Diesen Ordner kannst du über das Schraubenschlüssel-Symbol oben rechts (**🔧 Tools and settings**) einsehen oder auf einen benutzerdefinierten Pfad ändern.

Die feste Bootstrap-Konfiguration liegt immer unter:

```text
%LOCALAPPDATA%\PromptHelper\settings.json
%LOCALAPPDATA%\PromptHelper\settings.backup.json
```

Öffnen des Standardordners:

1. `Windows + R`
2. eingeben:

```text
%LOCALAPPDATA%\PromptHelper
```

3. `Enter`

---

# 14.1 Typische Struktur

```text
PromptHelper
├── .app.lock
├── library.json
├── library.backup.json
├── prompts
│   ├── <GUID>.md
│   └── ...
└── recovery
```

Während einer Initialisierung kann vorübergehend zusätzlich existieren:

```text
initializing.marker
```

---

# 14.2 `library.json`

Primäre Metadatendatei.

Sie enthält unter anderem:

- Kategorien,
- Parent-/Unterkategorie-Zuordnung,
- Prompt-IDs,
- Kategoriezuordnung der Prompts,
- interne Reihenfolge.

Sie enthält **nicht** die eigentlichen vollständigen Prompt-Texte.

---

# 14.3 `prompts\`

Hier liegen die eigentlichen Inhalte.

Jeder Prompt besitzt eine eigene Markdown-Datei.

Beispiel:

```text
d31ebf4ba2344120b81991e1cc3fd8a5.md
```

Der Dateiname ist eine interne ID.

Du musst diese IDs im normalen Betrieb nicht verstehen oder bearbeiten.

---

# 14.4 `library.backup.json`

Das ist eine automatische Sicherheitskopie der Metadaten.

Sehr wichtig:

```text
library.backup.json
≠
historisches Versionsarchiv
```

Sie wird normalerweise mit dem aktuellen gültigen Bibliotheksstand synchronisiert.

Sie ist für Recovery gedacht, nicht dafür, beliebig zu einem Stand von „vor drei Tagen“ zurückzugehen.

Für echte Versionen brauchst du **externe manuelle Backups**.

---

# 14.5 `recovery\`

Wenn die primäre `library.json` beschädigt ist und Recovery aus einem gültigen Backup möglich ist, versucht Prompt Helper best-effort, den beschädigten Primärinhalt in `recovery\` zu sichern.

Dateien sehen ungefähr so aus:

```text
library.corrupt-<Zeitstempel>-<GUID>.json
```

Wichtig:

```text
recovery\
```

ist **kein vollständiges automatisches Backup deiner Bibliothek**.

Es kann Kopien beschädigter Metadaten enthalten.

Die eigentlichen Prompts liegen weiterhin unter:

```text
prompts\
```

---

# 14.6 `.app.lock`

Diese Datei dient dem exklusiven Zugriff.

Wichtig:

```text
Existenz von .app.lock
≠
automatisch bedeutet, dass die App noch läuft
```

Die Sperre basiert darauf, dass ein laufender Prompt-Helper-Prozess die Datei exklusiv geöffnet hält.

Die Datei darf nach dem Beenden auf der Festplatte bestehen bleiben.

Deshalb:

```text
.app.lock nicht einfach löschen, um ein Startproblem zu "reparieren"
```

Prüfe stattdessen zuerst den Task-Manager.

---

# 14.7 `initializing.marker`

Diese Datei ist ein Sicherheitsmarker für eine begonnene Initialisierung.

Im normalen stabilen Betrieb solltest du sie nicht manuell verwalten müssen.

Wenn du bei einem Startproblem eine solche Datei findest:

```text
nicht sofort löschen
```

Stattdessen:

1. Prompt Helper schließen.
2. kompletten Datenordner sichern.
3. Fehler dokumentieren.
4. erst danach gezielt analysieren.

---

# 14.8 Datenordner wechseln und migrieren (Tools & Settings)

Über das Schraubenschlüssel-Symbol oben rechts (**🔧 Tools and settings**) kannst du den aktiven Datenordner einsehen und ändern.

## 1. Migration in einen neuen / leeren Ordner

Wenn du einen leeren Ordner auswählst:

- Prompt Helper kopiert deinen aktuellen Bibliotheksbestand (Metadaten und Prompt-Dateien) vollständig dorthin.
- Dein bisheriger Datenordner bleibt als unberührte Sicherheitskopie erhalten.
- Prompt Helper schließt sich nach dem Speichern sofort, damit keine Änderungen mehr im alten Ordner landen.
- **Nach dem Schließen:** Starte Prompt Helper einfach wieder. Das Programm öffnet nun den neuen Zielordner.

## 2. Wechsel zu einer bereits existierenden Bibliothek

Wenn der ausgewählte Ordner bereits eine Prompt-Helper-Bibliothek enthält:

- Dein aktueller Bestand wird **weder kopiert, noch überschrieben oder zusammengeführt**.
- Ein Sicherheitsdialog weist dich explizit darauf hin.
- Bestätigst du den Wechsel, wird die Einstellung aktualisiert und Prompt Helper schließt sich.
- Beim nächsten Start öffnet Prompt Helper die bereits im Zielordner vorhandene Bibliothek.

## 3. Gültigkeitsregeln für den Zielordner

Ein Zielordner muss folgenden Kriterien entsprechen:

- Es muss ein vollständiger, absoluter Pfad sein.
- Es darf kein Laufwerks-Hauptverzeichnis sein (z. B. `C:\` oder `D:\`).
- Er darf nicht innerhalb des aktuellen Datenordners liegen und ihn nicht umschließen.
- Er darf nicht innerhalb des Bootstrap-Ordners (`%LOCALAPPDATA%\PromptHelper`) liegen oder ihn umschließen (außer es ist exakt der Standard-Pfad).
- Er muss normale Schreib-, Ersetzungs- (`File.Replace`) und Löschzugriffe gestatten.
- Er darf nicht zeitgleich durch eine andere laufende Prompt-Helper-Instanz gesperrt sein.

---

# 15. Backups richtig erstellen

# 15.1 Wichtigster Grundsatz

Die interne:

```text
library.backup.json
```

ersetzt **kein externes Backup**.

Ein vollständiges Backup muss enthalten:

```text
Metadaten
+
Prompt-Dateien
```

Am einfachsten:

```text
gesamten PromptHelper-Datenordner kopieren
```

---

# 15.2 Sicheres manuelles Backup

## Schritt für Schritt

1. Prompt Helper normal schließen.
2. Prüfe im Task-Manager, dass kein `PromptHelper`-Prozess mehr läuft.
3. Öffne den **aktuellen Datenordner**.
   - Standard: `%LOCALAPPDATA%\PromptHelper`
   - Bei benutzerdefiniertem Speicherort: Den in **Tools & Settings** konfigurierten Ordner öffnen.
4. Den gesamten Prompt-Helper-Datenordner kopieren (enthält `library.json`, `library.backup.json` und den Unterordner `prompts\`).
5. An einem sicheren Ort einfügen.

Beispiel:

```text
Dokumente\PromptHelper-Backups\PromptHelper-2026-08-20
```

> **Hinweis zur Bootstrap-Einstellung:**
> Wenn du einen benutzerdefinierten Datenordner verwendest, liegt deine Prompt-Bibliothek in diesem Zielordner. Die kleine Datei `%LOCALAPPDATA%\PromptHelper\settings.json` (und `settings.backup.json`) speichert lediglich, wo sich dieser Datenordner befindet. Für dein inhaltliches Backup ist der konfigurierte Datenordner maßgeblich.

---

# 15.3 Gute Backup-Routine

Empfehlung:

- nach größeren Änderungen,
- vor Updates,
- vor manueller Dateiarbeit,
- regelmäßig, zum Beispiel wöchentlich.

Behalte mehrere Stände:

```text
PromptHelper-2026-08-01
PromptHelper-2026-08-08
PromptHelper-2026-08-15
```

Nicht immer dieselbe Sicherung überschreiben.

---

# 15.4 Gesundheitscheck vor einem wichtigen Backup

Bei einem normalen laufenden Bestand sollten typischerweise vorhanden sein:

```text
library.json
library.backup.json
prompts\
```

Wenn Prompt Helper unmittelbar zuvor:

- Startup Error,
- Recovery Notice,
- Safety-Backup-Warnung

angezeigt hat, kennzeichne die Sicherung entsprechend.

Beispiel:

```text
PromptHelper-2026-08-20-after-recovery
```

und behalte zusätzlich einen älteren bekannten guten Stand.

---

# 16. Backup wiederherstellen

Nur durchführen, wenn du bewusst zu einem gesicherten Stand zurückkehren möchtest.

## Sicherer Ablauf

1. Prompt Helper schließen.
2. Task-Manager prüfen.
3. Den **aktuellen** `%LOCALAPPDATA%\PromptHelper`-Ordner zuerst separat sichern.
4. Aktuellen Ordner umbenennen.

Beispiel:

```text
PromptHelper-before-restore-2026-08-20
```

5. Gewünschten Backup-Ordner nach `%LOCALAPPDATA%` kopieren.
6. Die aktive Kopie muss heißen:

```text
PromptHelper
```

7. Prompt Helper starten.
8. Mehrere wichtige Kategorien und Prompts prüfen.

---

# 16.1 Warum den aktuellen Stand vorher behalten?

Falls das alte Backup:

- zu alt,
- unvollständig,
- beschädigt

ist, kannst du jederzeit zum vorherigen Zustand zurückkehren.

---

# 16.2 Niemals nur `library.json` als vollständiges Restore betrachten

Wenn du nur `library.json` zurückspielst, können Metadaten und Prompt-Dateien auseinanderlaufen.

Beispiel:

```text
alte library.json
+
neue prompts\
```

kann zu:

- unavailable Prompts,
- Orphan-Dateien,
- älteren Zuordnungen

führen.

Für Anfänger ist der vollständige Ordner-Restore deutlich sicherer.

---

# 17. Automatische Sicherheitskopie und Recovery

# 17.1 Normalfall

Bei Metadatenänderungen wird zuerst:

```text
library.json
```

geschrieben.

Danach versucht Prompt Helper:

```text
library.backup.json
```

zu aktualisieren.

Wenn beides klappt:

```text
normaler Zustand
```

---

# 17.2 Primärdatei gespeichert, Safety Backup fehlgeschlagen

Mögliche Warnung:

```text
The library was saved, but its safety backup could not be updated.
Current data remains stored in library.json.
```

Bedeutung:

```text
aktuelle Primärdaten:
gespeichert

automatische Redundanz:
derzeit nicht zuverlässig aktualisiert
```

Was tun:

1. Nicht panisch dieselbe Aktion mehrfach wiederholen.
2. Prompt Helper schließen.
3. externes Backup des kompletten Datenordners erstellen.
4. Schreibrechte, Datenträger, Antivirus/Controlled Folder Access prüfen.
5. Prompt Helper neu starten.

---

# 17.3 Beim Start: gültige Primärdatei, Backup-Sync fehlgeschlagen

Mögliche Warnung:

```text
The library was loaded from library.json,
but its safety backup could not be synchronized.
```

Bedeutung:

Die Hauptbibliothek war lesbar und wurde verwendet.

Nur das automatische Backup konnte nicht aktualisiert werden.

Auch hier:

```text
externe Sicherung erstellen
+
Ursache prüfen
```

---

# 17.4 Recovery aus Safety Backup

Wenn die primäre Metadatendatei beschädigt oder fehlt, die Backup-Metadaten aber gültig sind, kann Prompt Helper die Struktur aus dem Backup wiederherstellen.

Dann erscheint eine **Prompt Helper Recovery Notice**.

Die interne Warnung weist ausdrücklich darauf hin, dass:

- die wiederhergestellte Struktur möglicherweise einen älteren gespeicherten Stand darstellt,
- vorhandene Prompt-Dateien nicht automatisch gelöscht wurden.

Das ist wichtig.

Nach Recovery:

1. **nichts aufräumen**.
2. kompletten Datenordner extern sichern.
3. wichtige Kategorien prüfen.
4. wichtige Prompts prüfen.
5. nach vermissten Prompts suchen.
6. erst danach weitere Änderungen vornehmen.

---

# 17.5 Warum nach Recovery ein Prompt „verschwunden“ wirken kann

Angenommen:

- die primäre Metadatenstruktur war neuer,
- das Safety Backup war älter,
- neuere `.md`-Dateien existieren noch.

Recovery kann dann eine ältere Metadatenstruktur laden.

Die neueren Dateien werden absichtlich nicht automatisch gelöscht.

Sie können dadurch als **Orphan-Dateien** auf der Festplatte verbleiben, obwohl sie in der UI nicht auftauchen.

Das ist Datenrettungsfreundlichkeit, kein automatisches Aufräumen.

---

# 17.6 Was ist eine Orphan-Datei?

Eine `.md`-Promptdatei, die existiert, aber von der aktuellen `library.json` nicht referenziert wird.

Prompt Helper:

```text
zeigt sie nicht in der UI
```

und:

```text
löscht sie nicht automatisch
```

Wenn du Orphans vermutest:

```text
nicht einfach löschen
```

Erst vollständiges Backup erstellen.

---

# 17.7 Future Schema

Wenn die Bibliothek von einer neueren Datenformat-Version stammt, stoppt die ältere Anwendung absichtlich.

Mögliche Meldung:

```text
The library file was created by a newer version of Prompt Helper
(schema version ...)
and cannot be opened.
```

Wichtig:

1. Nicht `library.json` löschen.
2. Nicht das ältere Backup erzwingen.
3. Datenordner vollständig sichern.
4. passende neuere Programmversion verwenden.

Dieses Verhalten schützt neuere Daten vor einer versehentlichen Rückstufung.

---

# 18. Unavailable Prompts

# 18.1 Bedeutung

Die Metadaten sagen:

```text
Prompt existiert
```

aber die dazugehörige `.md`-Datei kann nicht gelesen werden.

Kartentitel:

```text
(Unavailable prompt)
```

Inhalt:

```text
[Prompt file could not be loaded.]
```

---

# 18.2 Erlaubte Aktionen

```text
Delete → erlaubt
Move   → erlaubt
Edit   → deaktiviert
Copy   → deaktiviert
Duplicate → deaktiviert
```

---

# 18.3 Warum Move noch funktioniert

Move ändert nur die Kategoriezuordnung in den Metadaten.

Dafür muss der Prompt-Inhalt nicht gelesen werden.

---

# 18.4 Warum Duplicate nicht funktioniert

Duplizieren benötigt den eigentlichen Inhalt.

Ohne lesbare `.md` kann Prompt Helper keine echte Kopie erzeugen.

---

# 18.5 Was tun?

## Wenn Inhalt unwichtig ist

```text
Delete
```

## Wenn Backup vorhanden ist

1. App schließen.
2. aktuellen Datenordner sichern.
3. bekannten guten vollständigen Backup-Stand wiederherstellen.

## Wenn du nur organisieren möchtest

```text
Move
```

ist weiterhin möglich.

---

# 18.6 Reproduktion nur mit Testdaten

1. neuen Test-Prompt erstellen.
2. App schließen.
3. zugehörige neue `.md` identifizieren.
4. Datei aus `prompts\` heraus verschieben.
5. App starten.

Erwartung:

```text
(Unavailable prompt)
```

und:

```text
Edit / Copy deaktiviert
```

Führe das nicht mit wichtigen Daten durch.

---

# 19. Typische Fehlermeldungen

# 19.1 „Another instance ... is already running“

Meldung:

```text
Another instance of Prompt Helper is already running and using the library.
```

Bedeutung:

Ein anderer Prozess hält die Bibliothek exklusiv geöffnet.

## Lösung

1. Taskleiste prüfen.
2. `Strg + Umschalt + Esc`.
3. Im Task-Manager nach `PromptHelper` suchen.
4. vorhandene Instanz verwenden.

Wichtig:

```text
.app.lock nicht allein wegen seiner Existenz löschen
```

---

# 19.2 Prompt Helper Startup Error

Möglich:

```text
Failed to load or initialize Prompt Helper library:

...
```

## Sofortmaßnahmen

1. komplette Meldung notieren oder Screenshot erstellen.
2. App schließen.
3. Datenordner vollständig sichern.
4. keine JSON-/Promptdateien löschen.
5. Schreibrechte, Datenträger und Sicherheitssoftware prüfen.

---

# 19.3 Unbekannte Prompt-Dateien ohne Metadaten

Wenn beide Metadatendateien fehlen, aber Prompt-Dateien vorhanden sind, initialisiert Prompt Helper nicht einfach neu.

Stattdessen kann die Anwendung stoppen, um Datenverlust zu verhindern.

Sinngemäß:

```text
Unknown prompt files found in data folder
without library metadata ...
Initialization aborted to prevent data loss.
```

Das ist ein Schutz.

Nicht:

```text
prompts\ leeren
```

sondern:

1. App schließen.
2. kompletten Ordner sichern.
3. nach `library.json` / `library.backup.json` aus einem Backup suchen.
4. erst danach Recovery planen.

---

# 19.4 Interrupted initialization / initializing.marker

Wenn der erste Start während der Initialisierung abbricht, kann `initializing.marker` zurückbleiben.

Prompt Helper besitzt dafür einen konservativen Wiederaufnahmeweg.

Wenn dabei unbekannte oder veränderte Prompt-Dateien gefunden werden, wird aus Sicherheitsgründen abgebrochen.

Als Anfänger:

```text
Marker nicht löschen
Dateien nicht überschreiben
```

sondern zuerst vollständiges Backup.

---

# 19.5 Clipboard Copy Failed

Wenn **Copy** fehlschlägt:

1. erneut versuchen.
2. Windows-Zwischenablage mit Notepad testen.
3. Clipboard-Manager oder Programme prüfen, die Zwischenablage exklusiv verwenden.
4. Prompt Helper neu starten.
5. genaue Fehlermeldung dokumentieren.

---

# 19.6 Prompt Helper Notice

Eine Notice kann bedeuten:

```text
Hauptaktion erfolgreich
+
Sicherheits-/Cleanup-Nebenaktion fehlgeschlagen
```

Deshalb Warnungen niemals nur anhand des Titels interpretieren.

Immer den Text lesen.

Beispiel:

```text
Prompt aus Bibliothek entfernt
aber Datei konnte nicht gelöscht werden
```

ist etwas anderes als:

```text
Prompt konnte überhaupt nicht gespeichert werden
```

---

# 20. Tastaturbedienung

# 20.1 Kategorie-Name-Dialog

```text
Enter  → Create / Save
Escape → Cancel
```

---

# 20.2 Delete-Dialog

```text
Enter  → Delete
Escape → Cancel
```

---

# 20.3 Prompt-Editor

```text
Enter  → neue Zeile
Tab    → Tabulator in den Prompt
Escape → Cancel
```

**Save ist im Prompt-Editor absichtlich kein Default-Enter-Button.**

---

# 20.4 Move-Dialog

```text
Enter  → aktuelle Move-/Copy-Aktion ausführen
Escape → Cancel
```

---

# 20.5 Allgemeine Fokusnavigation

Außerhalb des Prompt-Texteditors:

```text
Tab
Shift + Tab
```

zum Wechseln zwischen fokussierbaren Bedienelementen.

---

# 21. Datenschutz und Sicherheit

Prompt Helper verwaltet seine Bibliothek lokal.

Die Anwendung selbst enthält keine AI-API-Funktion.

Wichtig:

```text
lokal gespeichert
≠
automatisch sicher für beliebige Geheimnisse
```

Andere Programme oder Benutzer mit Zugriff auf dein Windows-Profil können möglicherweise auf Dateien oder Zwischenablage zugreifen.

Deshalb nicht ohne Erlaubnis speichern:

- Passwörter,
- API-Keys,
- geheime Tokens,
- vertrauliche Kundendaten,
- Firmengeheimnisse.

Wenn du einen Prompt anschließend in einen Onlinedienst einfügst, gelten zusätzlich die Datenschutzregeln dieses externen Dienstes.

---

# 22. Update auf eine neue Version

Wenn später eine neue Prompt-Helper-Version erscheint:

## Sicherer Ablauf

1. Prompt Helper schließen.
2. Vollständiges Backup des aktuellen Datenordners erstellen (siehe Abschnitt 15).
3. Neue Release-ZIP herunterladen.
4. Neue ZIP in **neuen Programmordner** entpacken.

Beispiel:

```text
Apps\PromptHelper-0.1.1
```

5. Neue EXE starten.
6. Über das Schraubenschlüssel-Symbol **🔧 Tools and settings** die Version und den Datenpfad prüfen.
7. Wichtige Kategorien und Prompts testen.
8. Alten Programmordner erst löschen, wenn alles funktioniert.

---

# 22.1 Downgrade-Warnung (Wichtig bei Nutzung von Headlines)

> **Wichtiger Hinweis zur Abwärtskompatibilität:**
> Sobald du in deiner Bibliothek benutzerdefinierte Prompt-Headlines vergeben hast, bearbeite diese Bibliothek **nicht mehr mit älteren Versionen von Prompt Helper**, die noch keine Unterstützung für Headlines besitzen. Ältere Versionen könnten die neuen Titel beim erneuten Speichern der Metadaten verwerfen.

---

# 22.2 Daten nicht in den Programmordner kopieren

Die eigentliche Bibliothek bleibt unter:

```text
%LOCALAPPDATA%\PromptHelper
```

Du musst normalerweise nicht:

```text
library.json
prompts\
```

neben die neue EXE kopieren.

---

# 22.2 Bei „Unsupported Library Schema“

Nicht zur alten Version zurückspringen und Metadaten erzwingen.

Stattdessen:

1. Daten sichern.
2. passende Version verwenden.
3. bei Unsicherheit keine Daten manuell überschreiben.

---

# 23. Deinstallation

# 23.1 Nur Programm entfernen, Daten behalten

1. Prompt Helper schließen.
2. Programmordner mit `PromptHelper.exe` löschen.

Die Daten unter:

```text
%LOCALAPPDATA%\PromptHelper
```

bleiben bestehen.

Das ist sinnvoll, wenn du später neu installieren möchtest.

---

# 23.2 Programm und Daten vollständig entfernen

Nur wenn du wirklich alles löschen möchtest:

1. App schließen.
2. optional vollständiges Backup erstellen.
3. Programmordner löschen.
4. `%LOCALAPPDATA%\PromptHelper` löschen.

Danach sind lokale Bibliothek und Prompts entfernt.

---

# 24. Empfohlene Organisationsstruktur

Einsteiger sollten lieber wenige klare Ebenen verwenden.

Beispiel:

```text
Home
├── Arbeit
│   ├── E-Mails
│   ├── Übersetzungen
│   ├── Reports
│   └── Tickets
│
├── Coding
│   ├── Planung
│   ├── Implementierung
│   └── Testing
│
└── Privat
    ├── Schreiben
    └── Recherche
```

---

# 24.1 Gute erste Prompt-Zeile

Da sie als Titel dient:

```text
EMAIL – Professionelle Antwort erstellen
```

ist besser als:

```text
ROLE
```

wenn viele Prompts alle mit `ROLE` beginnen.

---

# 24.2 Empfehltes Prompt-Muster

```text
QA – Vollständigen Bug Audit durchführen

ROLE
Du bist ein QA-Agent.

TASK
...

INPUT
...
```

---

# 24.3 Varianten lieber duplizieren

Statt einen guten Prompt stark umzubauen:

```text
Move
→ Copy instead of move
→ Kopie bearbeiten
```

Beispiel:

```text
Bug Audit – Standard
Bug Audit – Spiele
Bug Audit – C# Tools
```

---

# 25. Vollständiger Anfänger-Funktionstest

Verwende nur Testdaten.

---

## Test A – Kategorie

1. `Home`.
2. **+ Add**.
3. `Testbereich`.
4. **Create**.
5. `Testbereich` öffnen.
6. **+ Add**.
7. `Unterordner`.
8. **Create**.

Erwartung:

```text
Home › Testbereich › Unterordner
```

ist erreichbar.

---

## Test B – Duplikatprüfung

1. In `Testbereich`.
2. `Unterordner` existiert.
3. **+ Add**.
4. `unterordner`.
5. Create versuchen.

Erwartung:

```text
abgelehnt
```

---

## Test C – Prompt

1. In `Testbereich`.
2. **+ Prompt**.
3. Eingeben:

```text
Mein erster Test-Prompt

Bitte antworte mit: Test erfolgreich.
```

4. **Save**.

Erwartung:

Kartentitel:

```text
Mein erster Test-Prompt
```

---

## Test D – Copy

1. **Copy**.
2. Notepad öffnen.
3. `Strg + V`.

Erwartung:

vollständiger Text exakt eingefügt.

---

## Test E – Edit

1. **Edit**.
2. ergänzen:

```text
Zusatzzeile.
```

3. **Save**.
4. erneut **Edit**.

Erwartung:

Zusatzzeile vorhanden.

---

## Test F – Move

1. In `Testbereich` Kategorie `Ziel` erstellen.
2. Beim Prompt **Move**.
3. Destination:

```text
Testbereich > Ziel
```

4. **Move**.

Erwartung:

Prompt nicht mehr in `Testbereich`, sondern in `Ziel`.

---

## Test G – Duplicate

1. In `Ziel` **Move**.
2. `Copy instead of move` aktivieren.
3. aktuelle Kategorie als Ziel lassen.
4. **Copy**.

Erwartung:

zwei unabhängige Prompt-Karten mit gleichem Inhalt.

---

## Test H – Restart Persistence

1. Prompt Helper normal schließen.
2. erneut starten.
3. zu `Testbereich > Ziel` navigieren.

Erwartung:

Testdaten weiterhin vorhanden.

---

## Test I – Delete-Schutz

1. zurück zu `Home`.
2. `Testbereich` über `×` löschen versuchen.

Erwartung:

nicht möglich, solange Unterkategorien/Prompts existieren.

---

## Test J – Aufräumen

1. alle Test-Prompts löschen.
2. Unterkategorien löschen.
3. zuletzt `Testbereich` löschen.

---

# 26. Fehler sauber melden

Ein guter Fehlerbericht braucht reproduzierbare Schritte.

Vorlage:

```text
Titel:
Kurze eindeutige Beschreibung

Prompt Helper Version:
z. B. v0.1.0

Windows:
z. B. Windows 11

Steps to reproduce:
1.
2.
3.
4.

Expected result:
Was sollte passieren?

Actual result:
Was passiert tatsächlich?

Frequency:
z. B. 3/3 Versuche

Error message:
Exakter Text

Additional information:
- nach Neustart weiterhin vorhanden?
- Daten manuell verändert?
- Screenshot vorhanden?
```

---

# 26.1 Gutes Beispiel

```text
Titel:
Prompt wird über Move nicht nach "Tools > Testing" verschoben

Version:
v0.1.0

Steps to reproduce:
1. Prompt Helper starten.
2. Games > Planning öffnen.
3. Prompt mit Text "MOVE TEST" erstellen.
4. Move klicken.
5. Destination "Tools > Testing" wählen.
6. Move klicken.
7. Tools > Testing öffnen.

Expected result:
"Move Test" befindet sich in Tools > Testing
und nicht mehr in Games > Planning.

Actual result:
Prompt bleibt in Games > Planning.

Frequency:
3/3 Versuche.
```

---

# 26.2 Bei Recovery-/Dateifehlern zusätzlich hilfreich

Wenn möglich sichern:

- Screenshot der Meldung,
- exakter Text,
- Datum/Uhrzeit,
- vollständige Kopie des Datenordners **privat** für Analyse.

Nicht öffentlich hochladen, wenn darin vertrauliche Prompts liegen.

---

# 27. FAQ

## Kann ich Prompts direkt auf Home speichern?

Ja.

Auf `Home`:

```text
+ Prompt
```

---

## Kann ich einen Prompt umbenennen?

Es gibt keinen separaten Prompt-Namen.

Ändere die erste nicht-leere Inhaltszeile über:

```text
Edit
```

---

## Kann ich einen Prompt duplizieren?

Ja:

```text
Move
→ Copy instead of move
```

---

## Kann ich in derselben Kategorie duplizieren?

Ja.

Move öffnen, aktuelle Destination behalten, `Copy instead of move` aktivieren.

---

## Warum passiert beim Move manchmal nichts?

Weil die aktuelle Kategorie standardmäßig vorausgewählt ist.

Wenn du das Ziel nicht änderst:

```text
Move innerhalb derselben Kategorie
→ no-op
```

---

## Kann ich Kategorien manuell sortieren?

Nein, nicht in v0.1.0.

---

## Kann ich Prompts manuell sortieren?

Nein, nicht in v0.1.0.

---

## Gibt es eine Suche?

Nein.

---

## Gibt es Tags oder Favoriten?

Nein.

---

## Gibt es einen Papierkorb?

Nein.

---

## Kann ich einen gelöschten Prompt per UI wiederherstellen?

Nein.

Nur ein passendes vorheriges vollständiges Backup kann einen früheren Stand zurückbringen.

---

## Ist `library.backup.json` ein tägliches Versionsbackup?

Nein.

Sie ist ein Safety Mirror für Recovery und wird normalerweise aktualisiert.

Für historische Stände brauchst du externe Backups.

---

## Was ist eine Orphan-Datei?

Eine Prompt-`.md`, die auf Platte existiert, aber nicht von der aktuellen Bibliotheksstruktur referenziert wird.

Prompt Helper zeigt sie nicht und löscht sie nicht automatisch.

---

## Soll ich `.app.lock` löschen, wenn Prompt Helper nicht startet?

Normalerweise nein.

Die Datei darf existieren.

Prüfe zuerst, ob ein PromptHelper-Prozess läuft.

---

## Kann ich den Datenordner auf einen USB-Stick neben die EXE legen?

Die normale v0.1.0 verwendet standardmäßig:

```text
%LOCALAPPDATA%\PromptHelper
```

Sie ist damit keine klassische vollständig portable „alles neben der EXE“-Anwendung.

---

## Brauche ich Internet?

Prompt Helper selbst verwaltet seine Bibliothek lokal.

Das externe AI-Tool, in das du den Prompt später einfügst, kann natürlich Internet benötigen.

---

## Sendet Prompt Helper meine Prompts automatisch irgendwohin?

Nein.

Copy bedeutet:

```text
Prompt Helper
→ Windows-Zwischenablage
```

Erst du entscheidest, wohin du den Text danach einfügst.

---

# 28. Schnellreferenz

| Element | Funktion |
|---|---|
| `?` | Hilfe, Datenpfad und Version |
| `Home` | oberste Bibliotheksebene |
| `+ Add` | Kategorie erstellen |
| `✎` | Kategorie umbenennen |
| `×` | leere Kategorie löschen |
| `+ Prompt` | Prompt erstellen |
| `Edit` | Prompt bearbeiten |
| `Delete` | Prompt löschen |
| `Move` | Prompt verschieben |
| `Copy` | Prompt in Windows-Zwischenablage |
| `Copy instead of move` | Prompt duplizieren |
| `(Empty prompt)` | Prompt besitzt keinen Inhalt |
| `(Unavailable prompt)` | referenzierte Prompt-Datei kann nicht geladen werden |

---

# 28.1 Die fünf wichtigsten Merksätze

```text
1. Home ist die oberste Ebene.

2. Die erste nicht-leere Prompt-Zeile wird zum Kartentitel.

3. Move verschiebt; Move + Copy instead of move dupliziert.

4. library.backup.json ist kein historisches Versionsarchiv.

5. Für ein echtes Backup immer den gesamten
   %LOCALAPPDATA%\PromptHelper-Ordner sichern.
```

---

# 28.2 Bei Problemen niemals als erste Maßnahme

Nicht sofort:

```text
library.json löschen
library.backup.json löschen
prompts\ leeren
recovery\ leeren
initializing.marker löschen
.app.lock löschen
kompletten Datenordner zurücksetzen
```

Stattdessen:

```text
App schließen
→ Datenordner komplett sichern
→ Fehlermeldung dokumentieren
→ Ursache gezielt prüfen
```

---

# Schluss

Der normale Alltag mit Prompt Helper ist bewusst einfach:

```text
organisieren
→ auswählen
→ Copy
→ im Zieltool einfügen
```

Für sichere langfristige Nutzung sind zusätzlich zwei Regeln besonders wichtig:

```text
regelmäßig vollständige externe Backups
+
bei Recovery-/Dateifehlern nicht vorschnell Dateien löschen
```

Damit bleibt die lokale Prompt-Bibliothek auch dann möglichst gut rettbar, wenn einmal ein Dateisystem- oder Metadatenproblem auftritt.
