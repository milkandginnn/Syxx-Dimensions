# Error Telemetry Design Report — Syxx

**Author:** error-coordinator (Phase 0 subagent)
**Date:** 2026-07-27
**Status:** For review by performance-monitor and User
**Scope:** Sentry event design, OTel error span design, GUI stability monitoring, dashboard recommendations, missing-instrumentation gap analysis

---

## 1. Current Error Handling Gaps

### 1.1 macOS (SyxxKit + SyxxApp) — Silent Error Consumption

The codebase has **6 bare `catch {}`** blocks in SyxxKit that silently discard errors without any telemetry:

| File | Line | Pattern | What Is Lost |
|---|---|---|---|
| `DNSCapture.swift` | 606 | `} catch {}` | Process launch failure for `pgrep` — pid attribution silently becomes `.unknown` |
| `TracerouteRunner.swift` | 24 | `do { try process.run() } catch { return [] }` | Traceroute launch failure — returns empty hops, indistinguishable from "no route" |
| `UnboundManager.swift` | 92 | `do { try p.run() } catch { return (..., -1) }` | unbound launch failure — error message discarded, exit code fabricated as -1 |
| `UnboundManager.swift` | 261 | same pattern | unbound-anchor failure |
| `UnboundManager.swift` | 346 | same pattern | unbound-control failure |
| `SynthesisEngine.swift` | 73-74 | `catch { return nil }` | **Engine evaluation failure** — the matrix engine threw, but the error is discarded. The caller gets `nil` with no explanation of WHY the engine couldn't evaluate. |

Additionally, **no `fatalError` / `preconditionFailure` / `assertionFailure` calls exist** in production code — only one `assert` in `SynthesisEngine.swift:141`. This is good for crash prevention but means unhandled exceptions are the only crash mechanism, and Sentry's crash capture is the only telemetry for them.

### 1.2 Windows (Syxx.Kit + SyxxUI) — Widespread Silent Catch

**53+ bare `catch { }` or `catch { return null; }`** patterns across the codebase. The most severe:

| File | Line | Pattern | What Is Lost |
|---|---|---|---|
| `LicenseManager.cs` | 158, 255, 302, 423 | `catch { }` | Config read failures, license validation failures, HTTP response parse failures — all silent |
| `MonitorReportAnalyzer.cs` | 31 | `catch { }` | NDJSON parse failure per-line — malformed capture records silently skipped |
| `DNSCapture.cs` | 609, 637, 642 | `catch { }` | Process snapshot failures, module-path retrieval failures, arg-parsing failures |
| `Program.cs` | 485, 562, 860, 878 | `catch { return null/false; }` | CLI operations silently fail |
| `FirstRunScan.cs` | 68, 87, 95 | `catch { /* leave null */ }` | DNSSEC measurement failures silently treated as "not measured" |
| `SyxxSettings.cs` | 46, 59 | `catch { return ... }` | Settings file corruption silently returns defaults |
| `EndpointLogAnalyzer.cs` | 89, 181, 259 | `catch { }` | JSON parse failures per-line silently skipped |
| `AppViewModel.cs` | 97, 441 | `catch { }` / `catch { return null; }` | Notification failures, HTTP failures |

**Zero SentrySdk.CaptureException calls exist anywhere in Windows code.** The Sentry SDK is initialized but never fed any explicit errors. Only unhandled exceptions reach it.

### 1.3 Server (Python) — Telemetry Gap in Error Paths

| File | Line | Pattern | What Is Lost |
|---|---|---|---|
| `app.py` | 54 | `except Exception: pass` | Sentry init failure — server starts without crash reporting, no log |
| `app.py` | 84 | `except Exception: pass` | OTel init failure — server starts without trace export, no log |
| `app.py` | 565-567 | `except Exception as e: ... self._json_error(500, ...)` | Last-resort guard — error IS logged to `logging_util.error()` but NOT to Sentry directly |
| `resolver.py` | 213, 221, 236, 246, 254, 266 | `except Exception:` (bare) | DNS resolution errors — caught but only the outer handler logs them |
| `licensing.py` | 198 | `except Exception:` | Keygen signature verification — `logging_util.warn()` but no Sentry event |
| `licensing.py` | 227 | `except Exception as e:` | Keygen validate-key failure — `logging_util.warn()` but no Sentry event |

The server's `logging_util.error()` does forward to OTel via `_otel_logger.emit()`, but `logging_util.warn()` does NOT forward to OTel. Many security-relevant failures (signature verification, key validation) are logged at `warn` level and thus never reach OTel.

### 1.4 Cross-Platform Gap — Zero Manual Sentry Capture

**Neither platform has a single explicit `SentrySDK.capture(error:)` or `SentrySdk.CaptureException()` call.** All error telemetry relies on:
- Unhandled exception capture (automatic via SDK)
- OTel span status codes (only when spans are created)

Since zero custom spans are created in production code, the OTel path is also idle.

---

## 2. Error Telemetry Naming Convention Proposal

### 2.1 Sentry Event Names

Every Sentry event must use a structured, hierarchical name following the pattern:

```
{component}.{category}.{specific_error}
```

**Component prefixes:**

| Prefix | Platform | Example |
|---|---|---|
| `syxx.macos` | macOS GUI/CLI | `syxx.macos.gui.view.crash` |
| `syxx.windows` | Windows GUI/CLI | `syxx.windows.license.validation_failed` |
| `syxx.linux` | Linux/Avalonia | `syxx.linux.dns.timeout` |
| `syxx.server` | Python server | `syxx.server.auth.signature_invalid` |

**Category hierarchy:**

| Level 1 | Level 2 | Level 3 |
|---|---|---|
| `gui` | `view`, `layout`, `render`, `hang` | `crash`, `drift`, `jank`, `timeout` |
| `dns` | `query`, `parse`, `dnssec`, `resolve` | `timeout`, `malformed`, `nxdomain`, `servfail` |
| `license` | `validate`, `activate`, `save`, `load` | `network_error`, `keychain_error`, `invalid_key`, `expired` |
| `witness` | `sign`, `verify`, `snapshot` | `key_missing`, `signature_mismatch`, `version_mismatch` |
| `capture` | `start`, `stop`, `parse`, `report` | `permission_denied`, `buffer_overflow`, `process_unknown` |
| `synthesis` | `engine`, `evidence`, `conclusion` | `engine_error`, `invalid_bundle`, `missing_indicator` |
| `network` | `reachability`, `ssrf`, `proxy` | `blocked`, `timeout`, `redirect_loop` |
| `server` | `auth`, `route`, `db`, `stripe` | `500`, `402`, `rate_limit`, `connection_leak` |

**Examples:**
- `syxx.macos.gui.view.drift` — a GUI element exceeded its position drift threshold
- `syxx.windows.dns.query.timeout` — a DNS query timed out with no response
- `syxx.server.license.signature_invalid` — Keygen webhook signature verification failed
- `syxx.macos.synthesis.engine_error` — the matrix engine threw during evaluation
- `syxx.windows.capture.permission_denied` — DNSCapture couldn't access a process

### 2.2 OTel Span Status Codes

OTel spans use the standard `StatusCode` enum:

| Span Outcome | `span.status` | `span.setAttribute` additions |
|---|---|---|
| Success | `.ok` | `result.verdict = "secure" / "tampered" / "inconclusive"` |
| Expected failure (e.g., NXDOMAIN) | `.ok` | `result.expected = true`, `result.rcode = "NXDOMAIN"` |
| Unexpected error | `.error` | `error.type = "TimeoutError"`, `error.message = "..."` |
| Inconclusive | `.ok` | `result.inconclusive = true`, `result.reason = "..."` |
| Cancelled | `.error` | `error.type = "Cancelled"` |

Tri-state operations that return `.inconclusive` are NOT errors — they set span status `.ok` with an `inconclusive` attribute. Only actual failures (timeouts, parse errors, exceptions) set `.error`.

### 2.3 OTel Span Names

```
{toolset}.{operation}.{sub_operation}
```

| Span Name | Kind | Description |
|---|---|---|
| `prophet.run` | INTERNAL | Full Prophet run |
| `prophet.dns.query` | CLIENT | Single DNS query |
| `prophet.dns.parse` | INTERNAL | DNS response parsing |
| `prophet.dnssec.verify` | INTERNAL | DNSSEC chain validation |
| `mirror.run` | INTERNAL | Full Mirror run |
| `mirror.vantage.compare` | INTERNAL | Cross-vantage comparison |
| `mirror.warp.fetch` | CLIENT | ContentWarp proxy fetch |
| `portal.run` | INTERNAL | Full Portal run |
| `portal.traceroute.execute` | CLIENT | Traceroute subprocess |
| `portal.lan.discover` | CLIENT | LAN cartography |
| `glutton.run` | INTERNAL | Full Glutton run |
| `glutton.leak.collect` | CLIENT | Leak collector query |
| `glutton.leak.verify` | INTERNAL | Attestation verification |
| `cage.run` | INTERNAL | Full Cage run |
| `cage.capture.start` | INTERNAL | Capture session start |
| `cage.capture.packet` | INTERNAL | Per-packet processing |
| `cage.report.generate` | INTERNAL | Report generation |
| `witness.run` | INTERNAL | Full Witness run |
| `witness.sign` | INTERNAL | Snapshot signing |
| `witness.verify` | INTERNAL | Snapshot verification |
| `synthesis.evaluate` | INTERNAL | Matrix engine evaluation |
| `gui.view.appear` | INTERNAL | View lifecycle — onAppear |
| `gui.view.disappear` | INTERNAL | View lifecycle — onDisappear |
| `gui.stability.check` | INTERNAL | Layout stability monitor tick |

---

## 3. Structured Error Attribute Schema

Every error event (Sentry or OTel) must carry these attributes. The schema is platform-agnostic — the same keys appear in Sentry `tags` / `extra` and OTel `span.attributes`.

### 3.1 Required Attributes (every error event)

| Attribute | Type | Description | Example |
|---|---|---|---|
| `error.type` | string | Exception class or error code | `TimeoutError`, `DNSError`, `KeychainError` |
| `error.message` | string | Human-readable (not PII) | `DNS query timed out after 5s` |
| `error.severity` | string | `fatal`, `error`, `warning` | `error` |
| `platform` | string | `macos`, `windows`, `linux`, `server` | `macos` |
| `component` | string | The module/file that caught the error | `DNSResolver`, `LicenseManager`, `SynthesisEngine` |
| `release` | string | `syxx-{platform}@{version}` | `syxx-macos@2.0.0` |
| `error.handled` | bool | Was the error caught and handled, or did it crash? | `true` |

### 3.2 Context Attributes (when available)

| Attribute | Type | Description | Example |
|---|---|---|---|
| `dns.hostname` | string | Hostname being resolved | `example.com` |
| `dns.query_type` | string | DNS record type | `A`, `AAAA`, `TXT` |
| `dns.resolver` | string | Resolver endpoint | `1.1.1.1:53`, `https://cloudflare-dns.com/dns-query` |
| `http.url` | string | URL being fetched (path only, no query) | `/api/warp` |
| `http.status_code` | int | HTTP response status | `500`, `402`, `200` |
| `http.method` | string | HTTP method | `GET`, `POST` |
| `license.key_prefix` | string | First 8 chars of license key | `SYXX-ABC` |
| `license.operation` | string | What was being done | `activate`, `validate`, `save` |
| `gui.view` | string | Active view name | `Prophet`, `Mirror`, `Conclusions` |
| `gui.element` | string | Element identifier | `prophet.result.card`, `conclusions.verdict` |
| `gui.drift_px` | float | How far an element drifted (pixels) | `12.5` |
| `gui.drift_threshold_px` | float | The configured threshold | `5.0` |
| `gui.drift_direction` | string | `x`, `y`, `both` | `y` |
| `synthesis.diagnosis_count` | int | Number of diagnoses evaluated | `6` |
| `synthesis.indicator_count` | int | Number of indicators present | `24` |
| `synthesis.result` | string | Verdict, if one was reached | `inconclusive` |
| `server.route` | string | API route | `/api/diff` |
| `server.client_ip_hash` | string | SHA-256 of client IP (not raw IP) | `sha256:abc123...` |

### 3.3 Tags (Sentry-specific, indexed for search)

| Tag | Value | Purpose |
|---|---|---|
| `platform` | `macos`, `windows`, `linux`, `server` | Filter by platform |
| `component` | `DNSResolver`, `LicenseManager`, etc. | Filter by module |
| `error.type` | Exception class name | Group similar errors |
| `toolset` | `prophet`, `mirror`, `portal`, `glutton`, `cage`, `witness` | Filter by functional area |
| `severity` | `fatal`, `error`, `warning` | Alert routing |
| `environment` | `production`, `staging`, `development` | Environment filtering |

---

## 4. GUI Stability Monitor Error Events

### 4.1 Element Drift Detection

When the stability monitor detects an element has moved from its expected position by more than the threshold, it fires:

**Sentry Event:**
```
syxx.{platform}.gui.layout.drift_detected
```

**Attributes:**
```
error.type = "LayoutDrift"
error.severity = "warning"   // not an error unless it causes clipping
error.message = "Element 'prophet.result.card' drifted 12.5px on Y axis (threshold: 5.0px)"
gui.view = "Prophet"
gui.element = "prophet.result.card"
gui.drift_px = 12.5
gui.drift_threshold_px = 5.0
gui.drift_direction = "y"
gui.expected_frame = "{{0, 200}, {600, 300}}"
gui.actual_frame = "{{0, 212.5}, {600, 300}}"
gui.drift_duration_ms = 350   // how long the drift persisted before detection
```

**OTel Span:**
```
gui.stability.check
  ├── status: .ok
  ├── attribute: stability.drift_count = 1
  ├── attribute: stability.element = "prophet.result.card"
  └── attribute: stability.drift_px_max = 12.5
```

### 4.2 Layout Jump (Content Swap)

When a view's `if let` block swaps from placeholder to full content, causing a vertical jump >20pt:

**Sentry Event:**
```
syxx.{platform}.gui.layout.jump_detected
```

**Attributes:**
```
error.type = "LayoutJump"
error.severity = "info"
error.message = "View 'ConclusionsView' content swap caused 342pt vertical jump"
gui.view = "Conclusions"
gui.jump_px = 342
gui.jump_duration_ms = 16    // single frame — instant jump
gui.from_content = "empty_state"
gui.to_content = "full_verdict"
```

### 4.3 Frame Drop / Hang

When the main thread is blocked for >100ms (not yet 2s Sentry hang threshold):

**Sentry Event:**
```
syxx.{platform}.gui.render.frame_drop
```

**Attributes:**
```
error.type = "FrameDrop"
error.severity = "warning"
error.message = "Main thread blocked for 450ms during 'MirrorView' rendering"
gui.view = "Mirror"
gui.block_duration_ms = 450
gui.drop_frame_count = 27   // at 60fps, ~27 frames dropped
```

### 4.4 Unresponsive Element

When a user interaction (click, type) doesn't produce a response within 2s:

**Sentry Event:**
```
syxx.{platform}.gui.interaction.unresponsive
```

**Attributes:**
```
error.type = "UnresponsiveElement"
error.severity = "error"
error.message = "Button 'Generate Result' did not respond within 2s"
gui.view = "Conclusions"
gui.element = "generate_result_button"
gui.interaction_type = "click"
gui.wait_duration_ms = 2100
```

### 4.5 Sentry Breadcrumbs for GUI State

These breadcrumbs are non-error events that provide context for crashes:

| Breadcrumb Category | Message Pattern | When |
|---|---|---|
| `gui.navigation` | `Navigated to {view}` | View appears |
| `gui.navigation` | `Left {view}` | View disappears |
| `gui.state` | `{toolset} completed: {verdict}` | Toolset run finishes |
| `gui.state` | `{toolset} started` | Toolset run begins |
| `gui.state` | `Synthesis completed: {verdict} (confidence: {score})` | Conclusions generated |
| `gui.state` | `License state changed: {old} → {new}` | License activation/deactivation |
| `gui.render` | `View {view} rendered in {ms}ms` | View render completes |
| `gui.render` | `Layout shift detected: {element} jumped {px}px` | Layout jump detected |
| `gui.error` | `{toolset} failed: {error_message}` | Toolset operation fails |
| `gui.error` | `View {view} crashed: {error_message}` | View crashes |

---

## 5. Dashboard Design Recommendations

### 5.1 Overview Dashboard

**Panel 1: Error Rate by Platform (last 24h)**
- Bar chart: macOS / Windows / Linux / Server
- Y-axis: errors per hour
- Color: green (<10/hr), yellow (10-50/hr), red (>50/hr)
- Drill-down: click bar → filtered issue list

**Panel 2: Top 10 Error Types (last 7d)**
- Table: event name, count, trend arrow
- Sortable by count, first seen, last seen
- Each row links to Sentry issue detail

**Panel 3: Crash-Free Session Rate (last 7d)**
- Gauge: 95%+ = green, 90-95% = yellow, <90% = red
- Per-platform breakdown below
- Trend line over 30 days

**Panel 4: Unhandled vs Handled Errors (last 24h)**
- Stacked bar: unhandled (red) vs handled (yellow)
- Unhandled = crash about to happen
- Goal: drive unhandled to zero

### 5.2 GUI Stability Dashboard

**Panel 1: Layout Drift Events (last 1h)**
- Timeline scatter plot: each dot = one drift event
- X-axis: time, Y-axis: drift magnitude (px)
- Color: by view (Prophet=blue, Mirror=green, etc.)
- Horizontal line: threshold (5px)

**Panel 2: View Render Times P50/P95/P99 (last 24h)**
- Per-view table: min, p50, p95, p99, max
- Color-coding: p95 > 100ms = yellow, p95 > 500ms = red
- Sparkline per row

**Panel 3: Frame Drop Frequency (last 1h)**
- Heatmap: X=time bucket (1min), Y=view
- Cell color: green (0 drops), yellow (1-5), red (5+)

**Panel 4: Most-Jumpy Views (last 7d)**
- Bar chart: total jump events per view
- Drill-down: click bar → jump event list with durations

**Panel 5: Element Drift Leaderboard (last 24h)**
- Table: element name, max drift px, drift count, last drift time
- Sort by max drift descending

### 5.3 Error Traceability Dashboard

**Panel 1: Error → Span Correlation**
- When an error event fires, show the full OTel trace it belongs to
- Highlight the erroring span in red
- Show parent spans for context (what operation was running)

**Panel 2: Breadcrumb Timeline**
- For any crash/hang: show the 50 breadcrumbs leading up to it
- Timestamped, color-coded by category
- Click breadcrumb → jump to that point in the trace

**Panel 3: Error Attribution**
- For each error: which release introduced it? (first seen)
- Which release fixed it? (last seen)
- Regression detection: previously resolved error reappeared

### 5.4 Alerting Rules

| Rule | Condition | Channel | Priority |
|---|---|---|---|
| New crash type | First occurrence of new issue | Sentry alert → Slack | P0 |
| Crash rate spike | >5 crashes/hour any platform | Sentry alert → Slack | P0 |
| Regression | Resolved issue reappears | Sentry alert → email | P1 |
| GUI drift storm | >10 drift events in 1 minute | Sentry alert → Slack | P2 |
| Frame drop spike | >50 frame drops in 1 minute | Sentry alert → Slack | P2 |
| Server 5xx rate | >1% of requests over 5min | Sentry alert → Slack | P1 |
| Probe timeout spike | >20% of DNS probes timing out | Sentry alert → Slack | P2 |
| License validation failure rate | >5% of validations failing | Sentry alert → email | P1 |

---

## 6. Specific File:Line Locations Where Error Telemetry Is Missing

### 6.1 macOS — Critical Gaps

| Priority | File | Lines | Current Behavior | Required Change |
|---|---|---|---|---|
| **P0** | `SynthesisEngine.swift` | 73-74 | `catch { return nil }` — engine error silently discarded | Add `SentrySDK.capture(error:)` + OTel span `.error` |
| **P0** | `DNSCapture.swift` | 606 | `catch {}` — process launch failure silent | Add `SentrySDK.capture(error:)` with `component=DNSCapture` |
| **P0** | `TracerouteRunner.swift` | 24 | `catch { return [] }` — silent failure | Add `SentrySDK.capture(error:)` + OTel span `.error` |
| **P1** | `UnboundManager.swift` | 92, 261, 346 | `catch { return (..., -1) }` — silent | Add `SentrySDK.capture(error:)` |
| **P1** | `AppModel.swift` | 458-459 | `catch { output = "..." }` — error captured but not sent to Sentry | Add `SentrySDK.capture(error:)` |
| **P1** | `AppModel.swift` | 570-572 | `catch { }` — silent | Add `SentrySDK.capture(error:)` |
| **P1** | `AppModel.swift` | 1474-1478 | Keychain save error — shown to user but not sent to Sentry | Add `SentrySDK.capture(error:)` |
| **P1** | `AppModel.swift` | 1530-1532 | Witness snapshot error — shown to user but not sent to Sentry | Add `SentrySDK.capture(error:)` |
| **P2** | `LeakDetector.swift` | 182, 257, 295, 336, 525 | Multiple `catch` blocks — errors converted to tri-state but not logged | Add Sentry breadcrumb for inconclusive results |
| **P2** | `ContentWarp.swift` | 120, 141, 344 | Network errors caught and mapped to result types | Add `SentrySDK.capture(error:)` for unexpected errors |
| **P2** | `LicenseManager.swift` | 419-421 | `catch let error as URLError` + `catch {}` — network error logged, generic error silent | Add Sentry capture for the generic catch |

### 6.2 Windows — Critical Gaps

| Priority | File | Lines | Current Behavior | Required Change |
|---|---|---|---|---|
| **P0** | `LicenseManager.cs` | 158, 255, 302, 423 | `catch { }` — 4 silent catch blocks | Add `SentrySdk.CaptureException()` to each |
| **P0** | `MonitorReportAnalyzer.cs` | 31 | `catch { }` — NDJSON parse failure per-line | Add `SentrySdk.CaptureException()` with `error.type=ParseError` |
| **P0** | `DNSCapture.cs` | 609, 637, 642 | `catch { }` — 3 silent catch blocks | Add `SentrySdk.CaptureException()` |
| **P1** | `SyxxSettings.cs` | 46, 59 | `catch { return ... }` — silent corruption | Add `SentrySdk.CaptureException()` with `error.type=SettingsCorrupt` |
| **P1** | `EndpointLogAnalyzer.cs` | 89, 181, 259 | `catch { }` — 3 silent JSON parse failures | Add `SentrySdk.CaptureException()` |
| **P1** | `FirstRunScan.cs` | 68, 87, 95 | `catch { /* leave null */ }` — DNSSEC measurement failures | Add `SentrySdk.CaptureException()` with `component=FirstRunScan` |
| **P1** | `AppViewModel.cs` | 97, 441 | `catch { }` / `catch { return null; }` | Add `SentrySdk.CaptureException()` |
| **P2** | `Program.cs` | 485, 562, 860, 878 | `catch { return null/false; }` — CLI failures | Add `SentrySdk.CaptureException()` |
| **P2** | `CageView.xaml.cs` | 106, 145, 204, 205, 230, 231, 245 | Multiple catch blocks — some expected, some not | Add Sentry breadcrumbs for expected; `CaptureException` for unexpected |
| **P2** | `PortalView.xaml.cs` | 141, 149 | `catch { }` / `catch (Exception ex)` | Add `SentrySdk.CaptureException()` |
| **P2** | `ConclusionsView.xaml.cs` | 202 | `catch (System.Exception ex)` — caught but not sent to Sentry | Add `SentrySdk.CaptureException(ex)` |

### 6.3 Server — Critical Gaps

| Priority | File | Lines | Current Behavior | Required Change |
|---|---|---|---|---|
| **P0** | `app.py` | 54 | `except Exception: pass` — Sentry init failure silent | Log to stderr; add startup health check |
| **P0** | `app.py` | 84 | `except Exception: pass` — OTel init failure silent | Log to stderr; add startup health check |
| **P0** | `app.py` | 565-567 | `except Exception as e:` — error logged to logging_util but NOT to Sentry | Add `sentry_sdk.capture_exception(e)` |
| **P1** | `licensing.py` | 198, 227 | `except Exception:` — keygen failures at `warn` level | Change to `logging_util.error()` so it reaches OTel; add `sentry_sdk.capture_exception()` |
| **P1** | `logging_util.py` | 34 | Only `error` level forwards to OTel; `warn` does not | Add OTel forwarding for `warn` level with `severity_number=13` (WARN) |
| **P1** | `resolver.py` | 213, 221, 236, 246, 254, 266 | `except Exception:` (bare, no log) | Add `logging_util.warn("dns_resolution_error", ...)` |
| **P2** | `fetcher.py` | 23 | `logging_util.warn("fetcher_ssrf_disabled", ...)` — SSRF protection disabled | Elevate to `logging_util.error()` + Sentry capture |
| **P2** | `stripe_checkout.py` | 54, 59 | `except urllib.error.HTTPError` / `except OSError` — wraps in StripeError | Add `sentry_sdk.capture_exception()` before re-raise |

### 6.4 GUI Stability — Missing Instrumentation

| Priority | File | Lines | Gap | Required Change |
|---|---|---|---|---|
| **P0** | `ProphetView.swift` | 85-107 | H1: Result card swaps empty→full, no telemetry | Add `SentrySDK.addBreadcrumb()` on state change |
| **P0** | `MirrorView.swift` | 80-199 | H2: Both columns swap independently, no telemetry | Add breadcrumbs + add `SentrySDK.capture(error:)` if layout jump exceeds threshold |
| **P0** | `ConclusionsView.swift` | 60-162 | H4: Entire view hierarchy swaps, no telemetry | Add `.transition(.opacity)` + breadcrumb on stage change |
| **P1** | `WitnessView.swift` | 107-195 | H3: Two large conditional sections appear sequentially | Add breadcrumbs |
| **P1** | `OverallVerdictView.swift` | 84, 217, 253 | H5: Index-based ForEach risks row recycling | Add `Identifiable` conformance; add breadcrumb on row count mismatch |
| **P1** | `PortalView.swift` | 144-177 | M6: Traceroute has 5 different layout sizes | Add breadcrumb on state change |
| **P1** | `CageView.swift` | 100-153 | M7+M8: Terminal swaps placeholder→ScrollView | Add breadcrumb on capture state change |
| **P2** | `DashboardView.swift` | 262-266, 451-483 | M9+M12: Tile height mismatch, activity log swap | Add breadcrumb |
| **P2** | All toolset views | — | No view lifecycle spans | Add `SyxxTracer.span("gui.view.appear", ...)` in `onAppear` |
| **P2** | `AppModel.swift` | — | No state transition breadcrumbs | Add `SentrySDK.addBreadcrumb()` on `runningTool` changes, verdict changes, license changes |

### 6.5 OTel Span Instrumentation — Missing

| Priority | File | Lines | Gap | Required Change |
|---|---|---|---|---|
| **P0** | `DNSResolver.swift` | — | No spans for DNS queries | Wrap `query()` in `SyxxTracer.span("dns.query", ...)` |
| **P0** | `SynthesisEngine.swift` | — | No spans for evaluation | Wrap `evaluate()` in `SyxxTracer.span("synthesis.evaluate", ...)` |
| **P1** | `VantageComparer.swift` | — | No spans for comparison | Wrap `compareVantagePoints()` in `SyxxTracer.span("mirror.vantage.compare", ...)` |
| **P1** | `ContentWarp.swift` | — | No spans for warp fetch | Wrap `comparePageContent()` in `SyxxTracer.span("mirror.warp.fetch", ...)` |
| **P1** | `LeakDetector.swift` | — | No spans for leak detection | Wrap `runLeakTest()` in `SyxxTracer.span("glutton.run", ...)` |
| **P1** | `WitnessSigner.swift` | — | No spans for signing | Wrap `createSignedSnapshot()` in `SyxxTracer.span("witness.sign", ...)` |
| **P2** | `NetworkKeyState.swift` | — | No spans for key state | Wrap operations in `SyxxTracer.span("portal.key_state", ...)` |
| **P2** | `DNSCapture.swift` | — | No spans for capture | Wrap `startCapture()` in `SyxxTracer.span("cage.capture.start", ...)` |
| **P2** | `TracerouteRunner.swift` | — | No spans for traceroute | Wrap `run()` in `SyxxTracer.span("portal.traceroute.execute", ...)` |
| **P2** | `Server/syxx_server/leak.py` | — | No spans for collector operations | Wrap in `tracer.start_as_current_span("leak.collector.query")` |
| **P2** | `Server/syxx_server/warp.py` | — | No spans for warp operations | Wrap in `tracer.start_as_current_span("warp.content.compare")` |
| **P2** | `Server/syxx_server/investigate.py` | — | No spans for investigation | Wrap in `tracer.start_as_current_span("investigate.consensus")` |

---

## 7. Implementation Sequencing

### Immediate (Phase 0, this sprint)

1. **Add SentrySdk.CaptureException to all P0 silent catch blocks** — macOS: 4 sites, Windows: 7 sites, Server: 3 sites. ~30 lines total.
2. **Fix logging_util.py to forward `warn` to OTel** — 3 lines.
3. **Add Sentry breadcrumbs for GUI state transitions** — `AppModel.swift` `runningTool` changes, verdict changes, license changes. ~15 lines.
4. **Add OTel spans for `DNSResolver.query()` and `SynthesisEngine.evaluate()`** — the two most critical paths. ~30 lines each.

### Short-term (Phase 1-2)

5. Add Sentry capture to all P1 catch blocks (~40 sites across all platforms).
6. Instrument all 6 toolset run paths with OTel spans.
7. Add GUI view lifecycle spans (`onAppear`/`onDisappear`).
8. Build the GUI stability monitor (element drift detection, layout jump detection).

### Medium-term (Phase 3-4)

9. Instrument all server endpoints with OTel spans.
10. Build the dashboards (Overview, GUI Stability, Error Traceability).
11. Configure alerting rules in Sentry.
12. Add trace context propagation (W3C `traceparent` header) between client and server.

---

## 8. Tri-State Error Handling Contract

Per AGENTS.md rules, all failure modes must map to `.inconclusive`, never crash. The error telemetry must never interfere with this contract:

| Error Scenario | App Behavior | Telemetry |
|---|---|---|
| DNS query timeout | Return `.inconclusive(timeout)` | Sentry breadcrumb + OTel span `.error` |
| DNS parse failure | Return `.inconclusive(malformed)` | Sentry `CaptureException` + OTel span `.error` |
| Engine evaluation failure | Return `nil` determination | Sentry `CaptureException` + OTel span `.error` |
| Network unreachable | Return `.inconclusive(network)` | Sentry breadcrumb + OTel span `.error` |
| Keychain write failure | Show error to user, app continues | Sentry `CaptureException` |
| Permission denied (capture) | Return `.inconclusive(permission)` | Sentry `CaptureException` |
| SSRF block | Return `.inconclusive(blocked)` | Sentry breadcrumb (expected, not an error) |
| License validation failure | App works in free mode | Sentry `CaptureException` |

**Telemetry must never change the app's behavior.** It is always fire-and-forget, best-effort. A Sentry/OTel failure must never crash the app, block the UI, or change a verdict.

---

## 9. Summary of Findings

| Metric | Count |
|---|---|
| Silent `catch {}` blocks (macOS) | 6 |
| Silent `catch {}` blocks (Windows) | 53+ |
| Silent `except: pass` blocks (Server) | 2 (init) + ~10 bare except |
| Explicit `SentrySDK.capture(error:)` calls (all platforms) | **0** |
| Explicit `SentrySdk.CaptureException()` calls (all platforms) | **0** |
| Custom OTel spans created in production code (all platforms) | **0** |
| GUI stability telemetry (drift, jump, frame drop) | **0** |
| Sentry breadcrumbs for state transitions | **0** |
| P0 error telemetry gaps | **14** |
| P1 error telemetry gaps | **24** |
| P2 error telemetry gaps | **28** |
| Missing OTel span instrumentation sites | **14** |
| GUI views needing stability instrumentation | **10** |