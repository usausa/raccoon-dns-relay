# NativeAOT Migration Plan

> Status: planned (not started). Created 2026-06-16.
> Goal: ship a fully **NativeAOT** build of Raccoon.DnsRelay by removing the
> reflection-heavy dependencies (OpenTelemetry, Serilog) and replacing logging
> with a minimal, AOT-safe **custom logger**.

## 1. Goal

Produce a `PublishAot=true` build with:

- **No Serilog** packages.
- **No OpenTelemetry** packages.
- A custom `ILoggerProvider` (console + rolling file) as the only logging backend.
- Zero (or a small, reviewed) set of trim/AOT warnings.
- Maximum performance: native code, fast startup, low memory.

## 2. Baseline (current state)

- `net10.0` Worker service; hosted as Windows Service and systemd unit.
- **Logging**: `Microsoft.Extensions.Logging` with source-generated
  `[LoggerMessage]` methods in `Raccoon.DnsRelay/Log.cs`. The backend is
  Serilog (`Serilog.Extensions.Hosting` + `Serilog.Settings.Configuration`
  + `Serilog.Sinks.Console` / `Serilog.Sinks.File`), configured from the
  `Serilog` section of `appsettings.json`.
- **Interim fix already applied (Approach A)**: `ConfigurationReaderOptions`
  with explicit sink assemblies in `Program.cs` + `TrimmerRootAssembly`
  entries in the csproj, so the current **trimmed single-file (R2R)** build
  works. This is the starting point; it is removed in Phase 1.
- **Metrics/tracing**: `DnsRelayMetrics` uses BCL `System.Diagnostics.Metrics.Meter`;
  `DnsRelayTelemetry` uses a BCL `ActivitySource`. They are exported via
  OpenTelemetry wiring in `Telemetry/OpenTelemetryExtensions.cs`
  (`AddDnsRelayTelemetry`).
- **Config binding**: `Microsoft.Extensions.Options` `.Bind()` / `.Get<T>()`
  (reflection-based unless the source-gen binder is enabled).

## 3. Guiding principles

- Keep the `ILogger` abstraction and the `[LoggerMessage]` source generators —
  they are already AOT-safe. **Only swap the backend.** No call site in the
  codebase needs to change for logging.
- Prefer source-generated / statically-reachable code paths; eliminate every
  reflection / dynamic-code dependency.
- Migrate in small, independently shippable phases; keep the build green and
  the app runnable after each phase.

## 4. Prerequisites

- NativeAOT on Windows needs the **MSVC toolchain** (Visual Studio
  "Desktop development with C++" workload) on the build/CI machine.
- For the systemd target, build on/with the matching `linux-x64` toolchain.
- Pin the SDK via `global.json` to keep CI reproducible.

## 5. Phases

### Phase 0 — Enable source-generated config binding (low risk, do first)

1. Add to the csproj: `<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>`.
2. This makes the existing `.Bind()` / `.Get<ServerSetting|UpstreamSetting|CacheSetting|TelemetrySetting>()`
   calls AOT-safe (no reflection binder).
3. Verify the app still starts and `ValidateOnStart` still works with the
   current (Serilog/OTel) stack.

### Phase 1 — Replace Serilog with a custom `ILoggerProvider`

1. Add custom logging under `Raccoon.DnsRelay/Logging/`:
   - `LoggingSetting` (strongly typed): `MinimumLevel`, per-category
     `Override`, `Console { Enabled, OutputTemplate }`,
     `File { Enabled, Path, RollingInterval, RetainedFileCountLimit, OutputTemplate }`.
     Bound from `appsettings.json` via the source-gen binder — this keeps the
     **log destination (path) and retention limit changeable from config**.
   - A custom `ILogger` + `ILoggerProvider` writing to console and/or a
     rolling file.
2. Wire in `Program.cs`:
   - Keep `builder.Logging.ClearProviders()`.
   - Bind `LoggingSetting` and register the custom provider
     (`builder.Logging.AddProvider(...)` or a DI registration of
     `ILoggerProvider`).
   - Remove the `AddSerilog(...)` call.
3. Remove packages: `Serilog.Extensions.Hosting`,
   `Serilog.Settings.Configuration`, `Serilog.Sinks.Console`,
   `Serilog.Sinks.File`.
4. Remove the **Approach A** artifacts: the `ConfigurationReaderOptions` code
   in `Program.cs` and the `TrimmerRootAssembly` entries in the csproj — no
   longer needed once Serilog is gone.
5. Replace the `Serilog` section in `appsettings.json` (and
   `appsettings.Development.json`) with the new `LoggingSetting` schema.
6. Verify: console + file output, date rolling, retention cleanup, level
   filtering, category overrides, and that **every `[LoggerMessage]` call site
   still emits**.

**Custom provider design notes (performance & correctness):**

- Hot path: `Log<TState>` does an `IsEnabled` level check, then enqueues a
  record into a **bounded `Channel<T>`**; a dedicated background task formats
  and writes. Keep the calling thread allocation-free where practical
  (pooled buffers / reused `StringBuilder`, invariant-culture timestamps).
- Console: write through a buffered `StreamWriter` over
  `Console.OpenStandardOutput()`; flush on batch drain.
- File: date-based file name (e.g. `Log/Raccoon.DnsRelay_yyyyMMdd.log`),
  roll at the day boundary, enforce `RetainedFileCountLimit` by deleting the
  oldest matching files. Mirror the current Serilog behavior (Day rolling,
  retain 7).
- Lifecycle: implement `IAsyncDisposable`/`IDisposable` on the provider and
  **flush on host shutdown** so no log lines are lost.

### Phase 2 — Remove OpenTelemetry

**Decision point — metrics/tracing future:**

- **Option 2a (recommended): keep the BCL instrumentation.** Keep
  `DnsRelayMetrics` (`Meter`) and `DnsRelayTelemetry` (`ActivitySource`).
  They are BCL, AOT-safe, and become cheap no-ops with no listener attached;
  they can be observed later via `dotnet-counters`, an `EventListener`, or a
  custom in-process exporter. Only the OTel **exporter** layer is removed.
- **Option 2b: remove metrics/tracing entirely.** Delete `DnsRelayMetrics`,
  `DnsRelayTelemetry`, their call sites, and `TelemetrySetting`.

**Steps (common):**

1. Delete `Telemetry/OpenTelemetryExtensions.cs` and the
   `AddDnsRelayTelemetry(...)` call (plus the `TelemetrySetting` read) in
   `Program.cs`.
2. Remove packages: `OpenTelemetry.Exporter.Console`,
   `OpenTelemetry.Exporter.OpenTelemetryProtocol`,
   `OpenTelemetry.Exporter.Prometheus.HttpListener`,
   `OpenTelemetry.Extensions.Hosting`,
   `OpenTelemetry.Instrumentation.Runtime`.
3. Trim `TelemetrySetting` to whatever remains relevant (or delete it under 2b).
4. **If 2b**: remove the `DnsRelayMetrics` / `DnsRelayTelemetry` injections and
   call sites (`CachingDnsResolver`, `UpstreamDnsResolver`, `DnsRelayService`)
   and their DI registrations.
5. Verify build and runtime.

### Phase 3 — Enable NativeAOT

1. csproj:
   - `<PublishAot>true</PublishAot>`
   - `<OptimizationPreference>Speed</OptimizationPreference>`
   - `<InvariantGlobalization>true</InvariantGlobalization>` (confirm no
     culture-specific formatting is required)
   - Keep `ServerGarbageCollection` / `ConcurrentGarbageCollection`.
2. Drop the old publish flags — `PublishSingleFile`,
   `IncludeNativeLibrariesForSelfExtract`, `PublishReadyToRun`,
   `PublishTrimmed` — they are superseded by AOT (R2R is mutually exclusive
   with AOT).
3. Resolve remaining trim/AOT warnings (`IL2026` / `IL3050`). With Serilog and
   OpenTelemetry gone and the source-gen binder on, these should be minimal or
   zero. Fix at the source; **only suppress with explicit review** (per
   `AGENTS.md`, ask before suppressing warnings).
4. Validate Windows Service and systemd registration with the native binary.

## 6. Target build & publish options

```text
# Windows
dotnet publish Raccoon.DnsRelay\Raccoon.DnsRelay.csproj -c Release -r win-x64 -o Publish

# Linux (systemd)
dotnet publish Raccoon.DnsRelay/Raccoon.DnsRelay.csproj -c Release -r linux-x64 -o Publish
```

- `--self-contained` is implied by `-r` + `PublishAot`.
- Optional, advanced: `-p:StripSymbols=true` (smaller binary);
  `-p:IlcInstructionSet=native` (optimize for the deployment CPU — sacrifices
  portability, only for known hardware).

## 7. Validation checklist

- [ ] Console + rolling file output; day roll + retention cleanup work.
- [ ] Minimum level and per-category overrides honored.
- [ ] Config changes (file path, retention count, enable/disable a sink) take
      effect from `appsettings.json` without a rebuild.
- [ ] Graceful flush on shutdown — no lost log lines.
- [ ] DNS path works (cache hit/miss, upstream, timeout); throughput/latency
      at least matches the baseline.
- [ ] Runs as a Windows Service and as a systemd unit.
- [ ] Native publish has no unreviewed `IL2026` / `IL3050` warnings.
- [ ] Native binary starts faster and uses less memory than the JIT build.
- [ ] `ValidateOnStart` option validation still works under the source-gen binder.

## 8. Rollback

- Land each phase as its own commit/PR; revert the phase if a blocker appears.
- The Approach A interim fix keeps the **trimmed single-file** build working
  until Phase 3 lands, so `main` always has a shippable build.

## 9. Risks & notes

- The custom file logger must match Serilog's reliability (flush policy, day
  roll, retention, robust file handling). Budget time for focused tests.
- NativeAOT requires the native toolchain in CI.
- `Microsoft.Extensions.Hosting.WindowsServices` and `...Systemd` are
  AOT-compatible.
- If a remaining dependency blocks AOT, the fallback is the trimmed
  single-file build — the custom logging work is still a net win there.

## 10. Affected files (reference)

- `Raccoon.DnsRelay/Program.cs` — logging wiring, telemetry wiring, config reads.
- `Raccoon.DnsRelay/Log.cs` — `[LoggerMessage]` definitions (unchanged).
- `Raccoon.DnsRelay/Telemetry/OpenTelemetryExtensions.cs` — deleted in Phase 2.
- `Raccoon.DnsRelay/Telemetry/DnsRelayMetrics.cs`,
  `Raccoon.DnsRelay/Telemetry/DnsRelayTelemetry.cs` — kept (2a) or deleted (2b).
- `Raccoon.DnsRelay/Settings/TelemetrySetting.cs` — trimmed or deleted.
- `Raccoon.DnsRelay/appsettings.json`,
  `Raccoon.DnsRelay/appsettings.Development.json` — logging section reworked.
- `Raccoon.DnsRelay/Raccoon.DnsRelay.csproj` — packages and publish properties.
- New: `Raccoon.DnsRelay/Logging/` — custom logger, provider, `LoggingSetting`.
