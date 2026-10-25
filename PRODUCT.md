# TraceTime

<!-- impeccable:product-schema 1 -->

## Platform

Windows native desktop (WPF). The skill's web/iOS/Android platform enum does not describe this application; preserve Windows conventions.

## Stack

Delegated by user and approved in PLAN.md: C# / .NET, WPF, SQLite. Local SDK: .NET 10.0.402; Microsoft.Data.Sqlite 10.0.13.

## Users

The computer's owner, reviewing application usage during their own daily Windows session.

## Product Purpose

Show daily foreground application duration and duration after excluding inferred input inactivity, with an expandable timeline.

## Operating Context

Runs in the system tray; the owner opens a compact desktop window to inspect today or a past date. All records remain local.

## Capabilities and Constraints

Confirmed in PLAN.md: two distinct duration metrics, application-level browser aggregation, idle threshold default 5 minutes, pause/resume, SQLite persistence, date-based summaries, timeline, export and deletion. Do not store titles, documents, URLs, keyboard contents or screenshots in usage records. Autostart defaults off.

## Product Principles

- Preserve time truth through switches, sleep, lock and crashes.
- Input inactivity is an estimate, never a productivity score.
- Quick daily overview first; details available on demand.

## Appearance commitments

Confirmed by the user for 0.2: first launch follows the system color mode and uses glass material. Subsequent launches restore the selected system/light/dark mode, glass/soft/minimal material and accent color. Settings apply immediately and persist independently of usage records. Palette presets and a custom color are supported.

Preserve readable Windows-native interaction and Simplified Chinese copy. Glass is an internal WPF translucent layered treatment; it does not capture the desktop. No productivity claims or seeded data in normal mode. System dialogs remain native. Small windows scroll to retain access rather than dropping controls.
