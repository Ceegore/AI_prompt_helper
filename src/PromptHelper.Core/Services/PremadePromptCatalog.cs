using PromptHelper.Models;

namespace PromptHelper.Services;

public sealed record PremadeCategoryDefinition(
    Guid Id,
    Guid? ParentId,
    string Name,
    long SortOrder);

public sealed record PremadePromptDefinition(
    Guid Id,
    Guid CategoryId,
    string Title,
    long SortOrder,
    string Content);

public static class PremadePromptCatalog
{
    public const int CurrentPackVersion = 1;

    public static readonly Guid PremadesCategoryId = Id("30000000-0000-0000-0000-000000000001");
    public static readonly Guid UniversalCategoryId = Id("30000000-0000-0000-0000-000000000002");
    public static readonly Guid GamesCategoryId = Id("30000000-0000-0000-0000-000000000010");
    public static readonly Guid GamesPlanningCategoryId = Id("30000000-0000-0000-0000-000000000011");
    public static readonly Guid GamesImplementationCategoryId = Id("30000000-0000-0000-0000-000000000012");
    public static readonly Guid GamesTestingCategoryId = Id("30000000-0000-0000-0000-000000000013");
    public static readonly Guid GamesGeneralTestingCategoryId = Id("30000000-0000-0000-0000-000000000014");
    public static readonly Guid GamesUnityTestingCategoryId = Id("30000000-0000-0000-0000-000000000015");
    public static readonly Guid ToolsCategoryId = Id("30000000-0000-0000-0000-000000000020");
    public static readonly Guid ToolsPlanningCategoryId = Id("30000000-0000-0000-0000-000000000021");
    public static readonly Guid ToolsImplementationCategoryId = Id("30000000-0000-0000-0000-000000000022");
    public static readonly Guid ToolsTestingCategoryId = Id("30000000-0000-0000-0000-000000000023");
    public static readonly Guid WindowsTestingCategoryId = Id("30000000-0000-0000-0000-000000000024");
    public static readonly Guid WebTestingCategoryId = Id("30000000-0000-0000-0000-000000000025");
    public static readonly Guid CapacitorTestingCategoryId = Id("30000000-0000-0000-0000-000000000026");
    public static readonly Guid CliTestingCategoryId = Id("30000000-0000-0000-0000-000000000027");

    public static IReadOnlyList<PremadeCategoryDefinition> Categories { get; } =
    [
        C(PremadesCategoryId, null, "Premades", 30),
        C(UniversalCategoryId, PremadesCategoryId, "Universal Testing", 10),
        C(GamesCategoryId, PremadesCategoryId, "Games", 20),
        C(GamesPlanningCategoryId, GamesCategoryId, "Planning", 10),
        C(GamesImplementationCategoryId, GamesCategoryId, "Implementation", 20),
        C(GamesTestingCategoryId, GamesCategoryId, "Testing", 30),
        C(GamesGeneralTestingCategoryId, GamesTestingCategoryId, "General", 10),
        C(GamesUnityTestingCategoryId, GamesTestingCategoryId, "Unity", 20),
        C(ToolsCategoryId, PremadesCategoryId, "Tools", 30),
        C(ToolsPlanningCategoryId, ToolsCategoryId, "Planning", 10),
        C(ToolsImplementationCategoryId, ToolsCategoryId, "Implementation", 20),
        C(ToolsTestingCategoryId, ToolsCategoryId, "Testing", 30),
        C(WindowsTestingCategoryId, ToolsTestingCategoryId, "Windows Desktop", 10),
        C(WebTestingCategoryId, ToolsTestingCategoryId, "Web & HTML", 20),
        C(CapacitorTestingCategoryId, ToolsTestingCategoryId, "Capacitor & Mobile", 30),
        C(CliTestingCategoryId, ToolsTestingCategoryId, "CLI & Automation", 40)
    ];

    public static IReadOnlyList<PremadePromptDefinition> Prompts { get; } =
    [
        P("40000000-0000-0000-0000-000000000001", UniversalCategoryId, "Universal Project Test Orchestrator", 10, """
# Role

Act as the senior test engineer for the supplied repository or project.

# Task

Build and execute the most valuable test campaign that is possible in the current environment. First inspect the product, its documentation, configuration, changed files, existing tests and release pipeline. Infer the real technology stack instead of relying only on the user's description.

# Required coverage

- happy paths and realistic end-to-end workflows
- invalid, empty, boundary and unusually large inputs
- persistence, restart, migration and recovery behaviour
- error handling, cancellation and partial failure
- platform-specific integration points
- regression risks around the changed code
- security, privacy and data-loss risks
- accessibility and usability where a user interface exists

Run existing tests before changing code. Add focused automated tests for confirmed gaps. Reproduce defects before repairing them, make the smallest safe fix, and rerun all affected suites. Do not hide skipped tests or environmental limitations.

# Result

Return: defects found, fixes made, tests executed with exact outcomes, remaining risks, and any manual checks that still require real hardware or credentials. Never claim that no bugs exist; state only that no further defect was found within the executed scope.
"""),
        P("40000000-0000-0000-0000-000000000002", UniversalCategoryId, "Focused Bug Hunt & Safe Repair", 20, """
# Task

Perform a deep, evidence-driven bug hunt on the supplied implementation and repair every confirmed defect that is within scope.

1. Read the requirements and map them to the implementation.
2. Inspect high-risk boundaries: file I/O, serialization, async work, lifecycle transitions, retries, cancellation, concurrency, external processes and user-controlled input.
3. Search for stale assumptions, swallowed exceptions, misleading success messages, partial writes, race conditions, path mistakes, resource leaks and inconsistent state.
4. Reproduce each suspected issue with a minimal deterministic test before changing production code.
5. Apply the narrowest durable fix and add a regression test that fails on the old behaviour.
6. Run the focused tests, the relevant integration tests and the complete available suite.

Preserve unrelated user changes. Do not weaken validation or tests just to make CI green. Clearly separate confirmed defects from hypotheses and environmental limitations.
"""),
        P("40000000-0000-0000-0000-000000000003", UniversalCategoryId, "Regression Tests for a Change", 30, """
# Task

Review the supplied change or diff and create a compact but strong regression suite for it.

- Identify the behaviour that changed and every caller or downstream consumer it can affect.
- Test the expected path, boundary values, invalid input, failure injection and state before/after restart where relevant.
- Prefer observable behaviour over implementation details.
- Include at least one test that would fail if the original bug or missing feature returned.
- Check compatibility with existing data and configuration.
- Avoid flaky timing, network dependence and order dependence.
- Use deterministic fixtures and clean up owned test data.

Run the new tests against the final implementation, then run the surrounding suite. Report the exact tests added, what each proves, commands executed and outcomes.
"""),
        P("40000000-0000-0000-0000-000000000004", UniversalCategoryId, "Release Readiness Gate", 40, """
# Task

Decide whether the current commit is safe to release. Treat this as a blocking release gate, not a general code review.

Verify version consistency, clean reproducible builds, full automated tests, production packaging, required assets, licenses, checksums, migrations from the previous release, first-run behaviour, upgrade behaviour, uninstall/portable expectations, configuration defaults and release notes. Inspect the actual packaged output rather than assuming that a successful build produced the right files.

For UI products, perform or specify launch, primary workflow, keyboard, scaling and accessibility smoke tests. For networked products, include offline, timeout and partial-response behaviour. For local-data products, include backup, recovery and data-loss checks.

Return PASS or BLOCKED. A PASS requires evidence for every applicable gate. List blockers first, then warnings, executed commands, artifact names and remaining manual checks.
"""),
        P("40000000-0000-0000-0000-000000000005", UniversalCategoryId, "Security, Privacy & Data-Loss Audit", 50, """
# Task

Audit the supplied project for practical security, privacy and data-loss defects appropriate to its threat model.

Check trust boundaries, secrets, logs, telemetry, permissions, path traversal, symlink/reparse-point handling, command injection, unsafe deserialization, archive extraction, web injection, credential storage, update mechanisms and dependency risks. Trace destructive actions and persistent writes through interruption, partial failure and concurrent modification. Verify that failure messages do not promise success after only part of an operation committed.

Prioritize exploitable or irreversible findings over theoretical hardening. Reproduce confirmed problems safely, add regression tests, and repair only within authorization. Do not print secrets or use real production credentials. Report severity, attack or failure path, evidence, fix and residual risk.
"""),

        P("40000000-0000-0000-0000-000000000101", GamesPlanningCategoryId, "Risk-Based Game Test Plan", 10, """
# Task

Create a risk-based test plan for the supplied game feature, milestone or build.

Derive test areas from the actual design, tickets and implementation. Cover core loop, progression, economy, save/load, input devices, camera, UI, audio, localization, performance, platform services and failure recovery where applicable. Identify affected systems and likely regressions. Separate smoke, focused feature, regression, exploratory, compatibility and soak testing.

For every test mission provide: purpose, setup, exact actions, expected observable result, evidence to capture, stop rules and priority. Include a small hardware/platform matrix and make assumptions explicit. Optimize for the available tester time instead of producing an unbounded checklist.
"""),
        P("40000000-0000-0000-0000-000000000102", GamesPlanningCategoryId, "Game Milestone Acceptance Matrix", 20, """
# Task

Turn the supplied milestone requirements into a traceable acceptance matrix.

For each requirement list its source, priority, preconditions, acceptance test, expected result, relevant platform/build configuration, automation candidate and evidence. Add negative and interruption cases for save data, progression blockers, purchases/economy, online dependencies and platform services when applicable. Flag requirements that are ambiguous, unverifiable or missing a measurable outcome. End with the minimum release-blocking smoke set.
"""),
        P("40000000-0000-0000-0000-000000000111", GamesImplementationCategoryId, "Implement a Testable Game Feature", 10, """
# Task

Implement the requested game feature while keeping it observable and testable.

Before coding, identify lifecycle, state ownership, persistence, input, scene/prefab dependencies, performance budget and failure behaviour. Separate deterministic domain logic from engine-facing behaviour where practical. Add focused unit or edit-mode tests, integration or play-mode coverage, and diagnostics that help reproduce failures without spamming release logs.

Preserve existing content and serialization compatibility. Validate scene transitions, pause/resume, restart, save/load and repeated activation. Run the relevant build and test commands and summarize implementation decisions, tests and remaining manual gameplay checks.
"""),
        P("40000000-0000-0000-0000-000000000120", GamesGeneralTestingCategoryId, "Full Game QA Audit", 10, """
# Task

Perform a broad QA audit of the supplied game build and repository.

Start with boot-to-gameplay smoke coverage, then inspect core loop, progression, save/load, fail/retry flows, menus, controls, camera, UI, audio, localization, performance and shutdown. Exercise fresh profile and existing profile paths. Test repeated transitions, rapid input, unusual order of operations, full inventories, empty states and interrupted operations.

Use logs, screenshots, video, save files and profiler captures as evidence where available. Reproduce each issue at least twice when practical and distinguish code defects from missing content or environment limitations. Fix confirmed repository defects when authorized, add regression coverage, rerun affected tests and provide a prioritized defect list.
"""),
        P("40000000-0000-0000-0000-000000000121", GamesGeneralTestingCategoryId, "Gameplay Regression Hunt", 20, """
# Task

Use the supplied change list to hunt specifically for gameplay regressions.

Map changed systems to direct behaviours, shared dependencies and adjacent features. Compare baseline and current behaviour where a prior build or test evidence exists. Stress repeated start/stop, cancel/retry, scene changes, death/respawn, checkpoint reload, save/load, pausing and switching input devices. Probe unexpected sequencing and interactions between two changed systems.

Produce minimal reproduction steps with build, save state, configuration, frequency, expected result and actual result. Add deterministic automated coverage for code-level regressions and identify the smallest manual regression set for engine-only behaviour.
"""),
        P("40000000-0000-0000-0000-000000000122", GamesGeneralTestingCategoryId, "Save, Load & Progression Torture Test", 30, """
# Task

Audit save data, loading and progression for corruption, loss and soft locks.

Test new game, multiple slots, overwrite, delete, autosave/manual save interaction, checkpoint restore, death/retry, scene changes, application termination during save, disk-full/write-denied behaviour, damaged or older-version saves, missing optional content and repeated load cycles. Verify atomicity: after interruption the result must be either the old valid state or the new valid state, never a misleading partial success.

Validate progression flags, inventory, currency, unlocked content, world state, timestamps and UI summaries after every round trip. Preserve evidence copies before destructive tests. Add serialization and migration regression tests where source access permits.
"""),
        P("40000000-0000-0000-0000-000000000123", GamesGeneralTestingCategoryId, "Input, Controller & Resolution Matrix", 40, """
# Task

Test the game across input methods and display configurations.

Cover keyboard/mouse, supported controllers, hot-plug, disconnect/reconnect, focus loss, remapping, conflicting bindings, held inputs during transitions and switching the active device. Check windowed, borderless and fullscreen modes; supported aspect ratios; common resolutions; DPI scaling; safe areas; UI scale; text clipping and cursor confinement.

Prioritize configurations relevant to the target platforms. Record exact device, resolution, refresh rate and settings for failures. Verify that prompts and glyphs match the active device and that settings persist correctly after restart.
"""),
        P("40000000-0000-0000-0000-000000000124", GamesGeneralTestingCategoryId, "Performance, Stutter & Memory Investigation", 50, """
# Task

Investigate the reported performance problem using measurements rather than visual guesses.

Establish a reproducible scene and baseline. Capture frame time rather than FPS alone, separating CPU, render thread, GPU, loading, garbage collection and shader/asset compilation where tools permit. Test cold and warm runs, repeated scene transitions, long sessions, spawn/despawn loops and increasing content counts. Track memory, allocations, handles and load times.

Identify the smallest correlation between a code/content event and the spike or leak. Make one controlled repair at a time, compare before/after captures, and verify that visual quality and gameplay behaviour are unchanged. Report hardware, build, settings, capture method and remaining uncertainty.
"""),
        P("40000000-0000-0000-0000-000000000125", GamesGeneralTestingCategoryId, "Localization & UI Overflow Audit", 60, """
# Task

Audit localized game UI for correctness and layout failures.

Use every available target language, with special attention to long German text, non-Latin scripts, right-to-left support if required, pluralization, variables and controller glyphs. Inspect menus, HUD, tutorials, subtitles, notifications, inventories, maps, loading screens and error dialogs at minimum and maximum supported resolutions/UI scales.

Find clipping, overlap, truncation, missing fonts, tofu glyphs, broken markup, untranslated strings, hard-coded concatenation and incorrect line breaks. Confirm that fixes do not damage other languages. Return screenshots or coordinates, string keys, language, resolution and a reusable regression matrix.
"""),
        P("40000000-0000-0000-0000-000000000126", GamesGeneralTestingCategoryId, "Long-Session Soak & State Leak Test", 70, """
# Task

Design and run the longest useful stability test possible for the game.

Cycle representative gameplay, menus, saves, loads, scene changes, spawning, combat or simulation systems and pause/resume. Monitor memory, handles, thread count, log growth, frame time and persistent object counts. Include idle periods and accelerated repetition. Define thresholds and stop conditions before starting.

When growth occurs, distinguish caches that plateau from leaks that continue. Capture checkpoints so the first divergent interval can be isolated. Report duration, loop count, telemetry trend, errors, reproducibility and the next diagnostic step.
"""),

        P("40000000-0000-0000-0000-000000000130", GamesUnityTestingCategoryId, "Unity EditMode & PlayMode Test Suite", 10, """
# Task

Create or improve Unity Test Framework coverage for the supplied feature.

Put deterministic domain and serialization checks in EditMode tests. Use PlayMode tests for MonoBehaviour lifecycle, scenes, prefabs, coroutines, physics timing and UI interaction. Avoid arbitrary waits; wait for explicit conditions with bounded timeouts. Isolate persistent data and restore global time, input and scene state after every test.

Cover enable/disable, destroy/recreate, domain reload assumptions, pause, scene reload and missing references. Run tests in batch mode where possible and report exact Unity version, platform, commands and results.
"""),
        P("40000000-0000-0000-0000-000000000131", GamesUnityTestingCategoryId, "Unity Scene, Prefab & Serialization Audit", 20, """
# Task

Audit Unity scenes, prefabs, ScriptableObjects and serialized fields affected by the change.

Search for missing scripts, broken object references, unintended prefab overrides, duplicate persistent objects, invalid execution-order assumptions, renamed serialized fields without migration attributes, assets excluded from builds and editor-only dependencies. Check additive loading, unload/reload and DontDestroyOnLoad behaviour.

Use Unity validation APIs or editor tests where practical. Do not rewrite scenes or prefabs merely for formatting. Report asset paths and object hierarchy for every finding and add a targeted validation test for recurring failures.
"""),
        P("40000000-0000-0000-0000-000000000132", GamesUnityTestingCategoryId, "Unity Build & Platform Smoke Test", 30, """
# Task

Validate that the Unity project produces and launches a real player build, not only an editor-success state.

Check scenes-in-build, scripting backend, architecture, stripping, addressable/bundled content, native plugins, permissions, product/version metadata and development-only flags. Build the relevant target, inspect warnings, launch from a clean user-data state, reach gameplay, save/reload and exit cleanly.

Identify editor APIs leaking into runtime code and case-sensitive asset path problems. Report build size, duration, executable location, smoke results and any platform step that could not be performed locally.
"""),
        P("40000000-0000-0000-0000-000000000133", GamesUnityTestingCategoryId, "Unity Asset & Addressables Failure Test", 40, """
# Task

Test Unity asset loading and Addressables or AssetBundle behaviour under success and failure.

Cover missing keys, duplicate addresses, unavailable catalogs, download interruption, checksum mismatch, stale cache, low storage, cancellation, release of handles, repeated load/unload and scene transitions. Verify visible fallback behaviour and that failures do not leave permanent loading states or leaked handles.

Use diagnostics to track reference counts and memory. Add automated tests around the loading abstraction and clearly separate cases that require a remote catalog or target device.
"""),
        P("40000000-0000-0000-0000-000000000134", GamesUnityTestingCategoryId, "Unity Lifecycle & Domain Reload Audit", 50, """
# Task

Audit Unity code for lifecycle and state bugs.

Inspect Awake/OnEnable/Start ordering, event subscription symmetry, static state, domain-reload-disabled behaviour, coroutines and cancellation, async continuations after destruction, application pause/focus, scene unloading and quitting. Reproduce duplicate callbacks, destroyed-object access, stale singletons and state that differs between editor and player.

Repair confirmed issues with explicit ownership and cleanup. Add PlayMode tests that repeat enable/disable and scene transitions, and verify behaviour with the project's actual Enter Play Mode settings.
"""),

        P("40000000-0000-0000-0000-000000000201", ToolsPlanningCategoryId, "Tool Test Strategy", 10, """
# Task

Create a proportionate test strategy for the supplied desktop, web, mobile or command-line tool.

Map user workflows, stored data, external integrations and platform constraints. Rank risks by probability and impact. Define unit, component, integration, UI, packaging, upgrade, recovery, accessibility, security and exploratory coverage. Include supported operating systems, browsers or devices without inventing unsupported targets.

Provide a small release smoke suite, a normal regression suite and deeper periodic checks. Each item needs an observable pass condition, required fixture and automation recommendation.
"""),
        P("40000000-0000-0000-0000-000000000211", ToolsImplementationCategoryId, "Implement a Tool Feature with Tests", 10, """
# Task

Implement the requested tool feature completely and leave it protected by tests.

Inspect architecture and conventions first. Define input validation, state ownership, persistence, cancellation, error messages and rollback behaviour before coding. Keep platform-specific operations behind narrow interfaces. Never report success before durable work actually commits.

Add tests for the normal path, boundaries, invalid input and injected failures. For UI work, include keyboard/accessibility behaviour and keep expensive work off the UI thread. Build and run the real production package or publish command in addition to unit tests. Summarize changes, evidence and manual checks.
"""),

        P("40000000-0000-0000-0000-000000000220", WindowsTestingCategoryId, "Windows Desktop Paranoid Test", 10, """
# Task

Perform a deep test of the Windows desktop application.

Cover first launch, normal launch, multiple instances, primary workflows, keyboard-only navigation, screen readers/automation names, high contrast, light/dark themes, DPI scaling, multiple monitors, minimized/restored state and clean shutdown. Exercise missing, locked, read-only and long paths; Unicode names; network/removable drives if supported; cancellation; low disk space; and abrupt process termination around persistent writes.

Inspect event-handler cleanup, UI-thread access, async exception paths and misleading dialogs. Run on a clean profile or isolated data directory. Add deterministic regression tests and report Windows version, architecture, scaling and exact outcomes.
"""),
        P("40000000-0000-0000-0000-000000000221", WindowsTestingCategoryId, "Install, Update, Uninstall & Portable Test", 20, """
# Task

Validate the actual Windows distribution format end to end.

For an installer, test clean install, repair, update, downgrade policy, per-user/per-machine permissions, shortcuts, running-process handling and uninstall with user-data preservation. For a portable ZIP, extract to normal, spaced, Unicode and read-only locations; launch without a development SDK; move the folder; and verify required runtime/assets are included.

Check version metadata, icon, signatures if configured, licenses, checksums, SmartScreen expectations and leftovers. Test upgrade from the immediately previous public release with existing data. Report exact artifact and hashes.
"""),
        P("40000000-0000-0000-0000-000000000222", WindowsTestingCategoryId, "Filesystem, Permissions & Atomicity Test", 30, """
# Task

Attack the Windows file-handling code without risking real user data.

Use an isolated temporary root. Test missing parents, existing targets, locked files, read-only directories, access denial, long and Unicode paths, case variants, junctions/symlinks/reparse points, concurrent modification, partial writes and process termination at durable boundaries. Verify path containment physically, not only lexically.

For every mutation prove the allowed end states and recovery behaviour. Never follow or delete foreign linked content. Add failure-injection tests and confirm that backups and success messages match the committed state.
"""),
        P("40000000-0000-0000-0000-000000000223", WindowsTestingCategoryId, "WPF UI, Keyboard & Accessibility Audit", 40, """
# Task

Audit the WPF interface for usability and accessibility regressions.

Test logical tab order, visible focus, access keys, default/cancel buttons, keyboard-only completion, AutomationProperties, labels, validation announcements, high contrast, 100–300% DPI, text scaling and narrow windows. Inspect dialogs for ownership, modality, focus restoration and accidental data loss on close.

Verify bindings and commands for stale state, cross-thread updates and swallowed exceptions. Use automated UI or WPF integration tests where reliable, then list manual screen-reader and scaling checks separately.
"""),
        P("40000000-0000-0000-0000-000000000224", WindowsTestingCategoryId, ".NET Async, Cancellation & Concurrency Audit", 50, """
# Task

Audit the .NET application for async and concurrency defects.

Trace async-void handlers, fire-and-forget tasks, cancellation token ownership, dispatcher transitions, locks, file sharing, disposal and shutdown. Test rapid repeated commands, cancel-before-start, cancel-mid-operation, timeout, exception after partial completion, two actors changing the same state and closing the app during work.

Use deterministic synchronization in tests instead of sleeps. Confirm exceptions are observed once, UI state recovers, resources are disposed and durable state cannot be silently overwritten.
"""),
        P("40000000-0000-0000-0000-000000000225", WindowsTestingCategoryId, "Windows Release Artifact Inspection", 60, """
# Task

Inspect the produced Windows release artifact as an adversarial user would receive it.

Verify architecture, self-contained/framework-dependent promise, executable version, product metadata, icon resources, DLL/config/content completeness, licenses, SBOM and checksums. Extract to a clean directory and launch without repository files or developer tools. Ensure debug symbols, secrets, test fixtures and stale files are not shipped unintentionally.

Open the archive and compare its contents against an explicit allowlist or required-file list. Run a smoke workflow from the packaged executable and report artifact name, size and SHA-256.
"""),

        P("40000000-0000-0000-0000-000000000230", WebTestingCategoryId, "Web App Functional & Responsive Audit", 10, """
# Task

Test the supplied web application across its real user workflows and responsive layouts.

Cover routing, navigation, forms, validation, loading/empty/error states, refresh, back/forward, deep links, multiple tabs, session expiry and slow/offline requests. Test supported viewport breakpoints with keyboard, mouse and touch assumptions. Look for overlap, overflow, layout shifts, unreachable controls and state lost on reload.

Use browser automation for stable critical paths and component/unit tests for logic. Avoid brittle selectors and arbitrary sleeps. Report browsers, viewport sizes, network conditions and evidence.
"""),
        P("40000000-0000-0000-0000-000000000231", WebTestingCategoryId, "HTML/CSS Visual Regression Audit", 20, """
# Task

Find visual regressions in the supplied HTML/CSS implementation.

Inspect semantic structure, stacking contexts, overflow, responsive grids, font loading, focus/hover/disabled states, dark mode, reduced motion, zoom to 200%, long localized text and empty/large datasets. Compare deterministic screenshots at representative breakpoints where a baseline exists.

Do not treat harmless antialiasing differences as defects. For each issue provide selector/component, viewport, state, screenshot evidence, root cause and minimal fix. Recheck adjacent breakpoints after changes.
"""),
        P("40000000-0000-0000-0000-000000000232", WebTestingCategoryId, "Web Accessibility WCAG Audit", 30, """
# Task

Audit the web UI against practical WCAG 2.2 AA expectations.

Combine automated scanning with manual keyboard and semantics checks. Verify headings, landmarks, labels, accessible names, focus order/visibility, dialogs, live regions, errors, contrast, zoom/reflow, target size, reduced motion and screen-reader-friendly dynamic updates. Test without a mouse and at 200% zoom.

Do not claim full WCAG conformance from an automated tool. Report each finding with affected element, user impact, applicable criterion, reproduction, fix and retest result.
"""),
        P("40000000-0000-0000-0000-000000000233", WebTestingCategoryId, "Browser Storage, Offline & Failure Test", 40, """
# Task

Test browser persistence and network failure handling.

Cover empty and existing local/session storage, IndexedDB migrations, corrupted values, quota exhaustion, private mode, cleared storage, multiple tabs, stale service workers, offline startup, mid-request disconnect, timeout, retry and partial API responses. Verify that cached data is not presented as freshly saved when a write failed.

Protect real accounts and data. Use isolated test profiles and deterministic network interception. Add tests for migrations and state reconciliation, then report supported recovery behaviour and unavoidable browser limitations.
"""),
        P("40000000-0000-0000-0000-000000000234", WebTestingCategoryId, "Web Security Quick Audit", 50, """
# Task

Perform a targeted security review of the web application.

Trace untrusted data into HTML, URLs, commands, storage and logs. Check XSS, unsafe HTML, open redirects, CSRF assumptions, authorization boundaries, secret exposure, CORS, clickjacking headers, dependency risks and insecure client-side trust. Test file uploads/downloads and URL parsing if present.

Use harmless payloads in an authorized local/test environment. Distinguish client-side observations from server-side proof. Add regression tests for confirmed defects and report exploit path, impact, fix and residual server configuration work.
"""),
        P("40000000-0000-0000-0000-000000000235", WebTestingCategoryId, "PWA, Cache & Update Audit", 60, """
# Task

Audit the progressive web app or service-worker update lifecycle.

Test first install, offline reload, new-version discovery, waiting/activation, open old tabs, cache invalidation, removed assets, interrupted downloads and recovery from a broken cached release. Verify manifest metadata, icons, start URL, display mode and installability where applicable.

Ensure users cannot remain silently trapped on incompatible mixed versions. Automate cache/version logic where possible and document the manual browser steps and exact build identifiers used.
"""),

        P("40000000-0000-0000-0000-000000000240", CapacitorTestingCategoryId, "Capacitor Android & iOS Test Matrix", 10, """
# Task

Create and execute a risk-based Capacitor test matrix for the supplied app.

Cover web build plus Android and iOS native shells, supported OS versions, phones/tablets, portrait/landscape, safe areas, dark mode, font scaling, hardware back, keyboard, resume after backgrounding, process recreation and cold/warm starts. Include offline and slow-network behaviour.

Verify that `cap sync` output, native configuration and bundled web assets match the tested commit. Separate emulator/simulator results from checks requiring physical devices. Report exact app version, native build, device/OS and limitations.
"""),
        P("40000000-0000-0000-0000-000000000241", CapacitorTestingCategoryId, "Capacitor Permissions & Lifecycle Audit", 20, """
# Task

Test Capacitor permissions and mobile lifecycle transitions.

For each used permission test first request, allow, deny, deny permanently, OS-settings recovery and permission removal after update. Exercise background/foreground, screen lock, rotation, low-memory process death, interrupted activity results and multiple rapid requests. Confirm UI state and pending operations recover without duplicate actions.

Inspect AndroidManifest.xml, Info.plist and runtime request code for mismatch or unnecessary permissions. Add tests around the app-facing permission abstraction and list physical-device checks separately.
"""),
        P("40000000-0000-0000-0000-000000000242", CapacitorTestingCategoryId, "Capacitor Native Bridge & Plugin Audit", 30, """
# Task

Audit every Capacitor plugin and native bridge call used by the feature.

Verify plugin availability, platform guards, argument validation, success/error/cancel paths, version compatibility, thread assumptions and behaviour when a plugin is missing or returns malformed data. Test repeated calls and lifecycle interruption. Check that web fallbacks are explicit and safe.

Inspect native dependency versions and sync output. Mock the bridge for deterministic web tests, then identify cases that require Android/iOS integration tests or physical hardware.
"""),
        P("40000000-0000-0000-0000-000000000243", CapacitorTestingCategoryId, "Deep Links, Back Button & Navigation Test", 40, """
# Task

Test navigation in the Capacitor app under native lifecycle conditions.

Cover cold and warm deep links, valid/invalid routes, authentication gates, duplicate link delivery, Android hardware back, modal dismissal, root exit policy, browser history, notification links and state restoration after process recreation. Confirm that external URLs use the intended safe handler.

Record initial state, incoming URL, expected destination and resulting back stack. Add automated route/state tests and perform native integration checks on both platforms where available.
"""),
        P("40000000-0000-0000-0000-000000000244", CapacitorTestingCategoryId, "Capacitor Build, Sync & Store Readiness", 50, """
# Task

Validate the Capacitor project from web build through native release artifacts.

Run the canonical web build and Capacitor sync. Detect stale copied assets, uncommitted native changes, missing icons/splash screens, wrong app ID/version, debug endpoints, clear-text traffic, signing/configuration mistakes and environment secrets. Build the available Android/iOS release variant and smoke-test the installed result.

Check privacy declarations, permission descriptions and store metadata inputs without inventing legal approval. Report generated artifact names, versions, hashes and all steps requiring store credentials or macOS hardware.
"""),

        P("40000000-0000-0000-0000-000000000250", CliTestingCategoryId, "CLI Contract & Exit-Code Test", 10, """
# Task

Test the command-line tool as a script or CI consumer would use it.

Cover help/version, no arguments, valid commands, unknown flags, missing values, mutually exclusive options, stdin, stdout/stderr separation, exit codes, Unicode, spaces, large input and non-interactive execution. Verify stable machine-readable output when promised and ensure errors do not emit partial success artifacts.

Run from directories other than the repository and from paths containing spaces. Add black-box process tests with bounded timeouts and exact assertions on exit code and essential output.
"""),
        P("40000000-0000-0000-0000-000000000251", CliTestingCategoryId, "Automation Script Safety Audit", 20, """
# Task

Audit the supplied PowerShell, shell or automation scripts for safety and determinism.

Check quoting, path resolution, working-directory assumptions, unset variables, glob expansion, command injection, error propagation, pipeline exit codes, idempotence, partial output and cleanup. Test dry-run behaviour if advertised. Ensure destructive targets are resolved and narrowly validated before mutation.

Use temporary fixtures and never point tests at a home directory, repository root or production resource. Add regression cases for spaces, Unicode, missing tools and interrupted execution.
"""),
        P("40000000-0000-0000-0000-000000000252", CliTestingCategoryId, "CI Workflow & Artifact Audit", 30, """
# Task

Audit the CI workflow as executable production code.

Check trigger coverage, permissions, pinned third-party actions, secret handling, concurrency, caching correctness, timeouts, failure propagation, test evidence, matrix gaps and artifact retention. Verify release jobs cannot publish when build, tests, packaging or integrity checks fail. Inspect the actual artifact paths and names.

Avoid changing security gates merely to make a run pass. Add static workflow regression checks where useful, run or observe the real pipeline, and report which guarantees are proven versus only inferred.
""")
    ];

    public static DefaultLibraryPackage CreatePackage()
    {
        var document = new LibraryDocument
        {
            PremadePackVersion = CurrentPackVersion,
            Categories = Categories.Select(x => new CategoryRecord
            {
                Id = x.Id,
                ParentId = x.ParentId,
                Name = x.Name,
                SortOrder = x.SortOrder
            }).ToList(),
            Prompts = Prompts.Select(x => new PromptRecord
            {
                Id = x.Id,
                CategoryId = x.CategoryId,
                Title = x.Title,
                SortOrder = x.SortOrder
            }).ToList()
        };

        return new DefaultLibraryPackage(
            document,
            Prompts.ToDictionary(x => x.Id, x => x.Content));
    }

    private static Guid Id(string value) => Guid.Parse(value);

    private static PremadeCategoryDefinition C(
        Guid id,
        Guid? parentId,
        string name,
        long sortOrder) => new(id, parentId, name, sortOrder);

    private static PremadePromptDefinition P(
        string id,
        Guid categoryId,
        string title,
        long sortOrder,
        string content) => new(Id(id), categoryId, title, sortOrder, content);
}
