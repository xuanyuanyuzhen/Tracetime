# TraceTime

<!-- impeccable:product-schema 1 -->

## Platform

Windows native desktop (WPF). The skill's web/iOS/Android platform enum does not describe this application; preserve Windows conventions.

## Stack

Delegated by user and approved in PLAN.md: C# / .NET, WPF, SQLite. Local SDK: .NET 10.0.402; Microsoft.Data.Sqlite 10.0.13. The 0.3 limits, charts and analysis UI use native WPF without new libraries.

## Users

The computer's owner, reviewing application usage during their own daily Windows session.

## Product Purpose

Show daily foreground application duration and duration after excluding inferred input inactivity, with an expandable timeline, application-level daily limits and 7/30-day local analysis. Optional AI review supports the owner's reflection without assigning productivity scores.

## Operating Context

Runs in the system tray; the owner opens a compact desktop window to inspect today, a past date or a 7/30-day range. Usage records and analysis remain local by default. Only after the owner generates a preview, explicitly consents and sends does optional AI analysis transmit the displayed aggregate data to the configured service. There is no automatic AI polling.

## Capabilities and Constraints

Confirmed in PLAN.md: two distinct duration metrics, application-level browser aggregation, idle threshold default 5 minutes, pause/resume, SQLite persistence, date-based summaries, timeline, export and deletion. Do not store titles, documents, URLs, keyboard contents or screenshots in usage records. Autostart defaults off.

0.3 adds one daily limit per recorded application, 1–1440 minutes using either metric, with progress, editing, pause/resume, deletion and today's ignore action. Light reminders use a notification and sound; medium reminders stay on top without requesting activation; severe reminders request TraceTime foreground focus, with taskbar flashing if Windows refuses. Medium and severe reminders require acknowledgement or a further 10 minutes. Limits never close or block the target application.

Local analysis offers daily trends with a numeric table, application ranking and a text summary for 7/30 days ending on a chosen date. AI configuration and sending are optional; local analysis stays usable without them. AI previews contain dates, application summaries and both duration metrics, with application names anonymous by default. They exclude paths, raw time segments, titles, URLs, documents, keyboard contents and screenshots. The API key uses PasswordBox input and current-user Windows DPAPI storage; it is excluded from previews and exports. Requests can be cancelled, results can be exported, and AI does not change limit rules.

## Product Principles

- Preserve time truth through switches, sleep, lock and crashes.
- Input inactivity is an estimate, never a productivity score.
- Quick daily overview first; details available on demand.
- Make reminders user-controlled and keep optional aggregate sharing explicit.

## Appearance commitments

Confirmed by the user for 0.2: first launch follows the system color mode and uses glass material. Subsequent launches restore the selected system/light/dark mode, glass/soft/minimal material and accent color. Settings apply immediately and persist independently of usage records. Palette presets and a custom color are supported.

Preserve readable Windows-native interaction and Simplified Chinese copy. Glass is an internal WPF translucent layered treatment; it does not capture the desktop. No productivity claims or seeded data in normal mode. System dialogs remain native. Small windows scroll to retain access rather than dropping controls.

0.3 retains these appearance commitments. The sidebar now provides five destinations: daily overview, application limits, time analysis, appearance and themes, settings and data. New controls reuse the existing theme and native interaction patterns.
