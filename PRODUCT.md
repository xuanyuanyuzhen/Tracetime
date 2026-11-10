# TraceTime

<!-- impeccable:product-schema 1 -->

Current product: 0.8.0 (implemented, verified and published) · Updated 2026-11-10. GitHub Release v0.8.0 is Latest; its ZIP and SHA-256 were downloaded publicly and matched the local artifacts. The local 0.7.1 improvements are included in 0.8.0.

0.8 adds optional continuous-use break reminders with their own page, interval, intensity and sounds. The sixth navigation destination shares the existing Windows appearance; navigation scrolls independently while process resources and the theme control stay at the bottom. The included 0.7.1 work adds an application-share pie chart with both duration metrics and exact-second chart details. Mouse clicks no longer leave a navigation focus outline; keyboard navigation retains its focus visual. The sidebar shows this process's CPU percentage and resident memory every two seconds, suspending the sampler while hidden or minimized. These counters stay in memory and add no network or database writes. Current validation and release state are recorded in WORKLOG.md and docs/VALIDATION.md.

## Platform

Windows native desktop (WPF). The skill's web/iOS/Android platform enum does not describe this application; preserve Windows conventions.

## Stack

Delegated by user and approved in PLAN.md: C# / .NET, WPF, SQLite. Local SDK: .NET 10.0.402; Microsoft.Data.Sqlite 10.0.13. Limits, break reminders, charts, analysis, quick actions and history management use native WPF and the existing dependencies.

## Users

The computer's owner, reviewing application usage during their own daily Windows session.

## Product Purpose

Show daily foreground application duration and duration after excluding inferred input inactivity, with full-day charts, application-level daily limits, temporary foreground allowances and 7/30-day local analysis. Optional continuous-use reminders let the owner choose when to take a break. Optional AI review supports the owner's reflection without assigning productivity scores; optional email reports configured application reminder events.

## Operating Context

Runs in the system tray; the owner opens a compact desktop window to inspect today, a past date or a 7/30-day range. Usage records and analysis remain local by default. Optional AI analysis transmits displayed aggregates only after the owner generates a preview, explicitly consents and sends; there is no automatic AI polling. Email is a separate opt-in: after the owner configures SMTP and enables both the global and application-rule switches, daily-limit and temporary-expiry events can send automatically. Both email switches default off, and saving configuration sends no test message.

## Capabilities and Constraints

Confirmed in PLAN.md: two distinct duration metrics, application-level browser aggregation, idle threshold default 5 minutes, pause/resume, SQLite persistence, date-based summaries, timeline, export and deletion. Do not store titles, documents, URLs, keyboard contents or screenshots in usage records. Autostart defaults off.

Each recorded application can have one daily limit, 1–1440 minutes using either metric, with progress, editing, pause/resume, deletion and today's ignore action. Light reminders use a notification and the configured level's sound; medium reminders stay on top without requesting activation; severe reminders request TraceTime foreground focus, with taskbar flashing if Windows refuses. “知道了” acknowledges only the current reminder, “临时使用 N 分钟” starts a foreground allowance using the preferred duration (initially 10), and “今天不再提醒” suppresses that application's local and email reminders for the day. Limits never close or block the target application.

Local analysis offers daily trends with a numeric table, application share, application ranking and a text summary for 7/30 days ending on a chosen date. AI configuration and sending are optional; local analysis stays usable without them. AI previews contain dates, application summaries and both duration metrics, with application names anonymous by default. They exclude paths, raw time segments, titles, URLs, documents, keyboard contents and screenshots. The API key uses PasswordBox input in the 0.4 configuration dialog and current-user Windows DPAPI storage; it is excluded from previews and exports. Requests can be cancelled, results can be exported, and AI does not change limit rules.

## Product Principles

- Preserve time truth through switches, sleep, lock and crashes.
- Input inactivity is an estimate, never a productivity score.
- Quick daily overview first; details available on demand.
- Make reminders user-controlled and keep optional aggregate sharing explicit.

## Appearance commitments

Confirmed by the user for 0.2: first launch follows the system color mode and uses glass material. Subsequent launches restore the selected system/light/dark mode, glass/soft/minimal material and accent color. Settings apply immediately and persist independently of usage records. Palette presets and a custom color are supported.

Preserve readable Windows-native interaction and Simplified Chinese copy. Glass is an internal WPF translucent layered treatment; it does not capture the desktop. No productivity claims or seeded data in normal mode. System dialogs remain native. Small windows scroll to retain access rather than dropping controls.

These appearance commitments remain in 0.8. The sidebar provides six destinations: daily overview, application limits, break reminders, time analysis, appearance and themes, settings and data. New controls reuse the existing theme and native interaction patterns.

## 0.4 interaction commitments

Application icons and names appear together in rankings, limit selection/rules, reminders and ordinary/idle time slices. Shared local EXE-resource icons are cached separately from usage records, with bounded memory/disk storage; missing icons use consistent initials. Non-application gaps have no app icon.

AI connection settings live in a separate modal with provider presets, base/full address modes, fetched model selection or manual model entry, optional display name and DPAPI-protected credentials. Save commits changes; cancel preserves the existing configuration. Changing service authority does not automatically reuse the old key. Fetching models is explicit and sends no usage records. The analysis prompt is internal and fixed; no end-user edit/reset/copy entry remains. The preview and copied content contain user aggregates only.

All four date pickers highlight recorded foreground dates with a dot, including idle foreground periods. Unrecorded days are subdued but selectable; selected state and today's outline remain distinct. New or deleted usage refreshes the markers.

## 0.5 interaction commitments

Daily charts cover the full local day with active use, inferred input inactivity, known interruptions, unrecorded gaps and future time. Unknown gaps may include shutdown or periods without TraceTime; never invent a cause or charge them to an app. Users switch between hourly stacked bars, a status timeline and a duration ring, then choose an hour for dual-metric app totals. The ring excludes future time. Application filtering is explicitly scoped to detailed slices and timeline emphasis.

Reminder settings provide separate light/medium/severe sounds, listen/stop and effect previews, with optional short PCM WAV imports copied to the local data folder. Drafts save explicitly; cancelling preserves settings. Previews never alter limit rules or today's reminder state. Native notification and audio-device behavior remains subject to Windows settings.

The overview owns mouse wheel scrolling, including over rankings and slices; long slice lists retain their scrollbar and keyboard navigation. The sidebar switches between 200 DIP labels and 72 DIP icons with tooltips and accessible names. Navigation width and chart type persist separately from themes and usage records.

## 0.6 reminder and email commitments

Returning to an over-limit application can remind at the same configured level. Repeat-on-entry defaults on and can be disabled per rule; one foreground visit is deduplicated, and TraceTime's own windows do not create another visit. With repeat disabled, the daily limit reminds once, while temporary expiry remains a separate event. Pausing the rule disables it. Existing saved day-dismissal states remain respected; saving an edited rule resets today's state.

Enabled rules offer temporary use before the daily limit is reached: 5/10/15/30-minute shortcuts or an integer from 1–120. It consumes only the target application's recorded foreground duration, including inferred input inactivity. Background, pause, lock, sleep and periods without TraceTime consume none; the daily limit retains its chosen metric. Expiry reminds at the original level even below the daily limit. State survives same-day restart and ends at the next local day. Adjustment grants the selected minutes from the current foreground total; cancellation resumes the daily rule. Explicitly starting temporary use clears today's ignore state. The 480-DIP-wide native dialog sizes to its content, caps height at the work area, and scrolls when necessary.

SMTP configuration is optional and independent of AI. The native dialog is 600 × 740 DIP, minimum 480 × 460, with fixed save/cancel actions and a scrolling form. It requires a receiving address plus the owner's sending service, sender address, login and authorization code/application password. Only STARTTLS is supported, usually on port 587; port 465 implicit TLS is rejected, with no plaintext-authentication fallback. Credentials use PasswordBox and current-user DPAPI in a separate local settings file. Changing host, port, login or sender requires entering credentials again; cancel preserves settings.

Email contains the date, application name, event type and cumulative duration for the selected metric; daily-limit notices also include the configured daily allowance. It excludes executable paths, raw time slices, titles, URLs, document names, keystrokes and screenshots. Daily-limit and each temporary-expiry event deduplicate independently; repeat entry does not send again. Temporary use defers local reminders but does not defer the daily-limit email. The SMTP provider and recipient receive this content after opt-in.

Sending uses one asynchronous queue with a network timeout. The page shows today's latest 12 statuses and a manual retry action; failures do not retry automatically and old-day notices are not sent later. Pausing tracking prevents the next send from starting, though an active send can finish. Interrupted or timed-out delivery is marked unknown, so retry guidance asks the owner to check the inbox. “Accepted by the sending service” is not a delivery guarantee. Disabling configuration/rules or dismissing today cancels unsent work; already submitted mail cannot be recalled. Date-range deletion also removes the corresponding local temporary state and email summaries while retaining rule settings.

Historical 0.6 validation: 65/65 deterministic scenarios and the formal package workflow passed, with evidence at `artifacts/qa/release-0.6.0-620b41eb`. The independent finish review returned ship for seven screenshots and sampled source only; it does not establish real SMTP delivery, physical audio, system notification/focus, lock or sleep behavior. Earlier version sections retain their historical scope. Detailed usage and the unimplemented Flutter feasibility assessment are in `docs/REMINDERS-AND-ROADMAP.md`; this remains a Windows WPF product.

## 0.7 search, quick actions and history commitments

Overview name search filters application rows without changing totals, original ranks, percentage denominators or charts. Limit-page search filters both the recorded-app picker and rules, with clear/no-result states. Previous/next/latest recorded-day actions count foreground periods including idle; the calendar still permits empty dates.

Temporary use can remember a preferred 1–120 minutes, initially 10, independently of usage and appearance. Cancelling changes no preference; later reminder buttons use the saved duration. “今日全部静默” sits in the existing limits heading and changes to “恢复今日提醒”, with a persistent footer marker. It covers all local and email reminders, including hidden and newly created rules, while recording and temporary allowances continue. It expires next local day. Restoring reminders preserves each rule and its own day-dismissal state; cancelled mail is not automatically resent.

The settings page shows database/WAL size and slice count, then full-history backup, month archive and restore actions. A `.ttarchive` contains only historical app identity, name, executable path and usage/idle/interruption intervals plus archive metadata; no rules, appearance, preferences or credentials. It is unencrypted. Creation uses a read-only transaction for a consistent snapshot including committed WAL data and permits continued recording. Originals remain by default. Only an ended month may be removed after archiving, through a separate confirmation; the archive is revalidated and held open before deletion. There is no automatic retention cleanup, and removing rows need not shrink the file immediately.

Restore first previews the exact range and interval counts, binds confirmation to the file's SHA, and creates a safety backup. It transactionally replaces that range without adding duplicate time, preserving outside history and existing rules, appearance and credentials. Success makes today's reminders globally quiet. Conflicting operations are disabled during maintenance; removal/restoration suspend collection and restore its prior paused/running state. A committed restore remains successful if temporary-file cleanup fails: the UI adds a cleanup notice and path. It must not claim the original history remained after a successful replacement.

0.7 validation: 80/80 scenarios in `artifacts/qa/build-0.7-reviewed.txt`; eight application flows and single-instance checks in `artifacts/qa/release-0.7.0-495c6765`; final captures, mocks and new-process preference checks in `artifacts/qa/release-0.7.0-86d9b8f3` and `artifacts/qa/verify-0.7-capture-final.txt`. The latter release directory's independent review resolved its sole P2 cleanup-feedback issue, with remaining clear / ship limited to that issue. No real user history was removed/restored and no large-scale performance result is claimed. See `docs/HISTORY-AND-QUICK-ACTIONS.md` for usage.

## 0.8 continuous-use break commitments

Break reminders default off, with 30 minutes and medium intensity selected initially. The owner chooses 20/30/45/60/90-minute presets or an integer from 5–240, then saves explicitly. Saving the configuration starts a fresh round; reopening the app restores configuration and today's break-dismissal date but starts elapsed use at zero. Configuration is local in `break-reminder.json`; the clock reuses existing collector events and a monotonic clock, without a new sampler, database query or network service.

Continuous use accumulates across application switches and includes reading or watching without input. It is separate from both historical application metrics. Tracking pause and data maintenance pause accumulation without proving rest; unknown long sampling gaps neither count as use nor reset the round. Manual rest or a known lock/sleep interval of at least five minutes resets the round. A shorter rest retains elapsed use; manual rest pauses only the break clock, while usage recording continues normally. No offline time is added after restart.

The page shows the saved round's elapsed use or rest duration, its next reminder/status and manual-rest/today-quiet actions below the configuration. Light notifications repeat after the selected additional use interval. Medium and severe use the existing topmost/foreground reminder behavior with “开始休息”, “5 分钟后提醒” and “今天不再提醒”; snooze requires five further minutes of accumulated use. Rest-only quiet continues counting and expires next local day; global quiet also covers break reminders, and its restore action remains on the application-limits page. Application and break popups do not overlap; preview, maintenance and unavailable-session states defer presentation. Turning off, silencing or beginning rest stops active and pending break audio, including the light path with no popup.

The independent break audio store reuses per-level sounds, volume, short PCM WAV import, listen/stop and effect previews. It does not change application-limit sounds. Preview actions alter neither the break clock nor enablement or today's reminder state. Copy suggests looking away, blinking and moving; the application makes no treatment/prevention claim, locks no screen and blocks no application. The 200/72 DIP sidebar retains tooltips and accessible names, with a scrolling navigation area and fixed bottom resources/theme controls.

0.8 validation: 97/97 core scenarios in `artifacts/qa/build-breaks-final.txt`; the full formal package workflow passed in `artifacts/qa/release-0.8.0-ac433f47`. The final sound-cancellation correction was rebuilt in `artifacts/qa/build-0.8-reviewed.txt` and passed focused WPF and fake-audio checks in `artifacts/qa/breaks-reviewed`. The same reviewer's `independent-review.md` marks the sole P2 resolved, remaining clear / ship only for that fix; five final light/dark/compact captures were inspected. This does not establish real sound-device, notification/focus, lock/sleep, full DPI/multi-monitor or long-running behavior. GitHub Release v0.8.0 is published; the public ZIP and checksum downloads match the local artifacts. See WORKLOG.md for the exact tagged-build and publication evidence.
