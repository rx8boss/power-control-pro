# Sicherheitsrichtlinie / Security Policy

## Unterstützte Versionen / Supported Versions

Sicherheitsmeldungen nehmen wir für die aktuelle Windows-WPF-Version von Power Control PRO entgegen. Die archivierte Electron-Version wird nicht mehr für Sicherheitsupdates gepflegt.

We accept security reports for the current Windows WPF version of Power Control PRO. The archived Electron version is no longer maintained for security updates.

## Sicherheitslücken melden / Reporting a Vulnerability

Bitte nutze unter „Security and quality → Advisories“ die Funktion „Report a vulnerability“. Veröffentliche Sicherheitslücken nicht als öffentliches Issue. Nenne die betroffene Version, Windows-Version, Schritte zum Nachstellen und die möglichen Auswirkungen. Veröffentliche keine Passwörter oder persönlichen Protokolldaten.

Please use “Security and quality → Advisories → Report a vulnerability”. Do not disclose vulnerabilities in a public issue. Include the affected app and Windows versions, reproduction steps, and potential impact. Do not include passwords or personal log data.

## Sicherheitsrelevanter Bereich / Security Scope

Die Anwendung führt lokale Systemaktionen aus, verwaltet Timer, zeigt Benutzerkonten an und kann DISM/SFC mit Administratorrechten starten. Sicherheitsrelevant sind insbesondere ungewollte Systemaktionen, eine Umgehung von Berechtigungen oder Bestätigungen sowie die Offenlegung vertraulicher Daten aus Einstellungen oder Protokollen.

The app performs local system actions, manages timers, displays user accounts, and can launch DISM/SFC with administrator privileges. Relevant findings include unintended system actions, bypassed permissions or confirmations, and exposure of sensitive settings or log data.

Reine Darstellungsfehler ohne Sicherheitsauswirkung gehören in ein normales Issue. / Visual defects without security impact belong in a regular issue.
