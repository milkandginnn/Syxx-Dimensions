# Syxx GUI — Implementation Guide

A dark, fantasy-themed "security console" for the Syxx DNS investigation app.
Everything below is lifted from the built artboard (`Syxx Artboard.dc.html` +
`WardenSidebar.dc.html`) so a UI coder can reproduce it 1:1. All styling is
inline; there is **no external CSS framework and no design-system bundle** (the
build is intentionally non-bundled).

---

## 1. Foundations

### 1.1 Color palette

| Token | Hex | Where it's used |
|---|---|---|
| **Canvas / page** | `#0b0518` | Artboard background base |
| **Window base (top)** | `#170c2e` | Window gradient start |
| **Window base (bottom)** | `#0f0720` | Window gradient end |
| **Sidebar top** | `#1d1240` | Sidebar gradient start |
| **Sidebar bottom** | `#160c30` | Sidebar gradient end |
| **Input / inset field** | `rgba(10,6,20,.6)` | Text inputs, signature blocks |
| **Violet — primary** | `#7c5cff` | Primary buttons, active nav, selection |
| **Violet — deep** | `#5b3fd6` | Primary button gradient end |
| **Violet — bright/text** | `#a78bff` | Links, Witness accent, icon glyphs |
| **Violet — pale text** | `#c4b3ff` | Emphasis inside body copy |
| **Cyan / teal — brand** | `#2dd4bf` | Healthy state, Prophet/Portal, success |
| **Teal — deep** | `#14a89a` | Teal button gradient end |
| **Gold / amber** | `#d9a441` → text `#e0b45a` | Sidebar dividers, Dimensions item, "current" tag |
| **Orange — warning** | `#f7931e` | Verdict warnings, Do53, ambiguous |
| **Red — critical** | `#ff4d6d` | Glutton, failed checks, live-capture dot |
| **Red — deep** | `#d62a4c` | Red button gradient end |
| **Text — primary** | `#efeafe` | Headings, key values |
| **Text — secondary** | `#c9c1e6` / `#b3a7d6` | Body copy |
| **Text — muted** | `#9a8fc0` / `#8f83b8` | Captions, labels |
| **Text — faint** | `#6f6597` / `#7d7399` | Timestamps, empty states |
| **Logo gradient** | `#ff9a3d → #ff2d78 → #9a5cff` | Wordmark + app icon |

**Standard tints (semantic color at low alpha):** fill `rgba(C,.06–.12)`,
border `rgba(C,.24–.4)`, glow `rgba(C,.30–.35)`. Neutral surface fill is
`rgba(255,255,255,.03)`; neutral border is `rgba(148,120,246,.14)`.

### 1.2 Gradients

- **Primary button:** `linear-gradient(120deg,#7c5cff,#5b3fd6)`
- **Teal button:** `linear-gradient(120deg,#2dd4bf,#14a89a)`
- **Red button:** `linear-gradient(120deg,#ff4d6d,#d62a4c)`
- **Active nav item:** `linear-gradient(90deg,#7c5cff,#6a49ef)`
- **Wordmark / logo text:** `linear-gradient(90deg,#ff9a3d,#ff2d78,#9a5cff)` + `background-clip:text`
- **Trust ring stroke:** `linear-gradient(0→1,0→1)` `#2dd4bf → #7c5cff`
- **Health meter fill:** `linear-gradient(90deg,#2dd4bf,#7c5cff)`
- **Pro Covenant panel:** `linear-gradient(120deg,rgba(124,92,255,.16),rgba(45,212,191,.06))`
- **Window fill:** `linear-gradient(180deg,#170c2e,#0f0720)`
- **Sidebar fill:** `linear-gradient(180deg,#1d1240,#160c30)`
- **Content glow (per pane):** `radial-gradient(120% 70% at ~70% -10%, rgba(ACCENT,.12–.18), transparent 55%)` layered on the window fill. Accent = violet on most panes, teal on Prophet/Portal, red on Glutton, orange on Conclusions.
- **Gold divider:** solid `#d9a441` at `opacity:.32`, `height:1px`.

### 1.3 Typography

Load once (Google Fonts): **Cinzel** (500/600/700) + **EB Garamond**
(400/500 + italics).

| Role | Family | Size / weight |
|---|---|---|
| Fantasy headings, warden names, wordmark, big numbers | `'Cinzel', serif` | 40 / 33 / 28 / 26 / 20 / 16 (600–700) |
| Lore / flavor captions (always italic) | `'EB Garamond', serif` italic | 12–17 |
| UI labels, body, buttons | `system-ui, -apple-system, sans-serif` | 13–15 (500–600) |
| Eyebrows / micro-labels (UPPERCASE) | `system-ui` | 10–10.5, weight 700, `letter-spacing:.2–.28em` |
| Paths, hashes, IPs, keys, timestamps | `ui-monospace, monospace` | 10–14 |

Casing: Title Case for titles/section headers; **UPPERCASE** tracked for
eyebrows/badges; sentence case for body.

### 1.4 Corner radii

badge/tag `4–6` · chip `6–7` · button/nav-item/toggle `8–12` · card `12–14` ·
window/glass panel `16` · pills & ring stroke ends fully round (`999px`).

### 1.5 Shadows & the glow

- **Window:** `0 40px 120px rgba(0,0,0,.55), 0 0 0 1px rgba(148,120,246,.14)`
- **Primary button:** `0 10px 26px rgba(124,92,255,.4)` (dashboard action `0 12px 30px`)
- **Active nav item:** `0 8px 20px rgba(124,92,255,.4)`
- **Signature glow (rune tiles, ring, dots):** `box-shadow:0 0 12–22px rgba(ACCENT,.30–.35)` or `filter:drop-shadow(0 0 6–12px …)`
- **Logo:** `filter:drop-shadow(0 0 10px rgba(255,45,120,.3))`

### 1.6 Motion

- Hover lift: `transform:translateY(-2 to -4px)` on cards/buttons; `filter:brightness(1.06–1.1)` on filled buttons; faint `rgba(124,92,255,.10)` fill on hover rows.
- Transition: `all .12–.16s`.
- Live pulse (capture dot): `@keyframes syxpulse{0%,100%{opacity:.5;transform:scale(1)}50%{opacity:1;transform:scale(1.25)}}` at `1.2s infinite`.
- No infinite decorative motion on content.

---

## 2. Layout & element sizes

### 2.1 Artboard

- Panes laid out in a CSS grid: `grid-template-columns:repeat(4,1050px); gap:78px 56px;` container `width:max-content`.
- Page padding `52px 60px 130px`; page background = the canvas radial gradient.
- A fixed top-center **toast** pill appears on actions (see 3.7).

### 2.2 Window (every pane)

- **Size: 1050 × 700 px**, `border-radius:16px`, `overflow:hidden`, `display:flex`, `position:relative`.
- **Traffic lights:** three 12px dots (`#ff5f57 / #febc2e / #28c840`), `gap:8px`, absolutely positioned `top:17px left:20px`.
- **Content area:** `flex:1; overflow-y:auto;` padding **`22px 34px 20px`** (warden/letter panes) or **`18px 30px 10px`** (Dashboard). Most panes fit 700px with no scroll; the **Conclusion** pane is intentionally taller and scrolls, with generous section spacing so its sections breathe.

### 2.1a First-run Guided Setup (modal)

- A centered modal over a dimmed backdrop that **appears on first run only, before any other view is rendered.** Persist a "setup complete" flag (e.g. localStorage) so it never shows again unless reset.
- **Step 1 — Choose experience:** two selectable cards, **Story Mode** (wardens/lore, violet-selected) vs **General Mode** (technical, teal-selected). This choice determines which mode the whole app renders in afterward.
- **Step 2 — Confirm defaults** (all editable, pre-filled): Resolver = Cloudflare (1.1.1.1), Transport = DoT (TLS), Encrypt all DNS = on, Notify on trust changes = on.
- Footer: **Skip — use defaults** and **Begin →**. On completion, store the mode + settings and route to the selected mode's **Dashboard**.
- Modal chrome: 640px card, `rgba(28,17,52,.85)` + `backdrop-filter:blur(24px)`, border `rgba(160,140,255,.24)`, radius 18; backdrop = radial violet glow over near-opaque canvas.

### 2.2a Dashboard controls

The Dashboard's action area has, in order: the primary pair **Conduct All Scans** / **Flush DNS Cache**, then a secondary pair **Enable Unbound DNS** (active) and **Enable Encrypted DNS**.

- **Enable Encrypted DNS must stay disabled when DNS queries are already encrypted** — e.g. when a VPN, an existing DoH/DoT profile, or another secure resolver is already carrying the traffic. Render it at `opacity:.5; cursor:not-allowed` with a caption naming the reason ("Disabled — traffic already encrypted (VPN)"). Only enable it when the system's DNS is confirmed to be cleartext (Do53). This avoids double-encrypting or fighting an existing secure transport.

### 2.3 Sidebar (`WardenSidebar`)

- **Width 236px** (`min-width:236px`), full height, `padding:46px 15px 16px`, right border `1px rgba(148,120,246,.14)`.
- **Logo block:** 50×50 app icon (`assets/syxx-logo.png`) + wordmark "Syxx" (Cinzel 27, gradient) + eyebrow "THE SIX WARDENS" (10px, `.22em`).
- Hairline under logo: `1px rgba(148,120,246,.16)`, margin `15px 4px 11px`.
- **Nav item:** `display:flex; gap:13px; padding:8px 13px; border-radius:9px;` 14px/500 text `#b3a7d6`; 22px-wide glyph slot. Hover → `rgba(124,92,255,.10)` + `#efeafe`.
- **Active item:** violet gradient fill, `#fff` 600, `padding:9px 13px`, shadow `0 8px 20px rgba(124,92,255,.4)`. Hover → `brightness(1.06)` (keeps gradient).
- **Group dividers:** gold `1px` line, `opacity:.32`, `margin:11px 8px` — placed before Prophet, before Conclusion, before Dimensions.
- **Nav icons:** What's New = sparkles, Dashboard = bar chart, the six modules = Elder-Futhark runes, **Conclusion = report/file-check**, Dimensions = globe (gold). All are inline 16px SVGs with `stroke:currentColor` except the runes (text glyphs).
- **Dimensions** item is gold (`#e0b45a`, 600) with a trailing `↗`.
- **Footer:** "⟲ Start your story anew" pill — `border:1px rgba(217,164,65,.35)`, gold text, `border-radius:999px`, pinned bottom.
- Settings & License are **not** in the sidebar — they open from the menu-bar popover.
- Nav order: What's New · Dashboard — / — Prophet · Mirror · Portal · Glutton · Cage · Witness — / — Conclusions — / — Dimensions.
- Each item links to its pane: `href="#pane-<key>"`. The active view is passed as an `active` prop.

### 2.4 Common component sizes

- **Card:** `background:rgba(255,255,255,.03); border:1px rgba(148,120,246,.14); border-radius:14px; padding:18–20px` (compact panes 12–15px).
- **Warden header rune tile:** 54×54, radius 14, Cinzel 28, semantic fill+glow.
- **Dashboard warden tile:** radius 11, padding ~7–10px, 32px rune sub-tile.
- **Trust ring (dashboard):** SVG `viewBox 0 0 118 118`, rendered ~70px; stroke 10–13px; track `rgba(148,120,246,.15)`, fill = teal→violet gradient, `stroke-linecap:round`, rotated `-90°`, glow drop-shadow.
- **Confidence ring (conclusions):** `viewBox 0 0 94 94`, rendered ~76px, stroke 8px.
- **Button (primary):** `padding:11–12px 22px; border-radius:10–12px;` gradient + shadow, `font:600 14px`.
- **Toggle:** track 44×26 radius 999; knob 20px circle; ON = `#7c5cff` + glow + knob right; OFF = `rgba(148,120,246,.2)` + `#9a8fc0` knob left.
- **Pill / status chip:** `padding:5–7px 11–14px; border-radius:999px;` semantic tint fill + border, optional 7–9px leading dot.
- **Select:** `padding:9px 14px; border-radius:9px; background:rgba(10,6,20,.6); border:1px rgba(148,120,246,.2);` (`appearance:none`).

---

## 3. Component recipes

### 3.1 Section eyebrow
`font:700 10.5px system-ui; letter-spacing:.26em; text-transform:uppercase;` color = pane accent (violet `#8a6ef0`, teal `#2dd4bf`, red `#ff4d6d`, orange).

### 3.2 Pane title
`font:600 26–33px 'Cinzel',serif; color:#efeafe`. Follow with an EB Garamond italic lore subtitle in `#9a8fc0`.

### 3.3 Warden header (Prophet/Portal/Glutton/Cage/Witness/Mirror)
Row: 54px rune tile (semantic fill+glow) + [eyebrow (role, e.g. "Resolver accuracy") · Cinzel name · italic lore quote]. `gap:15px; margin-bottom:16px`.

### 3.4 Warden state vocabulary (literal, kept crisp)
`Passed` (teal) · `Failed` (red) · `Not run` (muted) · `Locked · Pro` (violet) · `Signed` (violet). Conclusions badges: `Untested` / `Negative` / `Invalidated` / `Sealed`.

### 3.5 Menu-bar popover (macOS status-bar dropdown)
- Thin menu bar strip (`height:28px`, `rgba(15,8,28,.9)`) with right-aligned highlighted `✶` icon + clock.
- Panel: width ~324px, `border-radius:14px`, `background:rgba(28,17,52,.82)`, `backdrop-filter:blur(24px)`, border `rgba(160,140,255,.24)`, shadow `0 30px 80px rgba(0,0,0,.6)`.
- Sections: **header** (icon + "Syxx · The Watch" + health dot) → **DNS Condition snapshot** (Resolver / Transport / Wells / Last verdict, mono values, teal wash) → **action rows**.
- Action rows (glyph + title + italic sub + `›`): Run a quick scan · Flush DNS cache · Open Settings · Copy diagnostic report · Open the main app (last row highlighted `rgba(124,92,255,.12)`). Rows link to panes and/or fire a toast.

### 3.6 Cards used per pane
- **Dashboard:** status banner (ring + health meter), action pair (Conduct All Scans / Flush DNS Cache), 6 warden tiles (2-col), Council's Verdict card, Chronicle (3 rows), Pro Covenant banner.
- **Conclusions:** verdict banner + confidence ring, hard-gate warning, execution-status chips (wrap), hypotheses distribution (single-line label + inline bar + %), recommended steps, ambiguous-verdict box with Copy Report.
- **Settings:** grouped cards (Encrypted DNS + resolver/transport selects, Scans, Privacy) with custom toggles.
- **License:** two tier cards (Freeborn = current, Covenant = Pro $2/moon) + license-key input + machine-ID mono line.

### 3.7 Toast
Fixed, top-center: `padding:12px 22px; border-radius:999px; background:rgba(28,17,52,.92); backdrop-filter:blur(20px); border:1px rgba(160,140,255,.35);` EB Garamond italic. Auto-dismiss ~2.8s. Copy is literal ("DNS cache flushed…", "Testimony signed…").

---

## 4. Iconography (arcane runes)

SF Symbols aren't on the web; this build uses **Elder-Futhark runes** as the
fantasy glyph set (rendered in `system-ui` so they display), plus a few
geometric symbols for non-warden items.

| Warden | Rune | Function | Accent |
|---|---|---|---|
| Prophet | `ᚨ` | Resolver accuracy | teal |
| Mirror | `ᛞ` | Remote vs local (Pro) | violet |
| Portal | `ᛜ` | LAN & route trace | teal |
| Glutton | `ᚦ` | DNS leak test | red |
| Cage | `ᚾ` | Live query capture | muted/violet |
| Witness | `ᛉ` | Environment trust (signing) | violet |

Nav symbols: What's New `✦` · Dashboard `◧` · Conclusions `⟐` · Dimensions `⊕` ·
(menu) Settings `⚙` · License `⚷`. Action glyphs: scan `⚔` · flush `≈` ·
copy `⧉` · open app `⌂` · pro `✧` · warning `⚠`.

For production on Apple platforms, swap runes/symbols back to the real SF
Symbols; the rune set is the web/fantasy stand-in.

---

## 5. The story (theme layer)

The six modules are **wardens keeping watch over the roads (DNS) between you and
the far cities (servers)**. Weave lightly — literal function copy stays primary,
lore is the flavor caption:

- **Prophet — The Seer:** many oracles asked for a name; agreement = genuine.
- **Mirror — The Reflection:** far city's face vs your own (Pro, "slumbers until the Covenant").
- **Portal — The Gatekeeper:** maps devices + the road out (route trace).
- **Glutton — The Devourer:** swallows leaked packets (leak test).
- **Cage — The Warden:** traps live messengers (query capture).
- **Witness — The Sealbearer:** signs tamper-proof testimony (Ed25519).
- **Conclusions — The Council:** weaves all testimony into one hard-gated verdict.
- **Dashboard = The Watchtower · Settings = The Sanctum · License = The Covenant · reset = "Start your story anew."**

Keep lore to short italic lines; never let it obscure what a control does.
