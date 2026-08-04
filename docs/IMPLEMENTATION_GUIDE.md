# Syxx Cross-Platform GUI Implementation Guide

This guide details the technical specifications, architectural patterns, design token definitions, and platform-specific implementations for translating Syxx desktop and web server mockups into native applications on macOS (SwiftUI), Windows (WinUI 3 / C#), Ubuntu/Mint Linux (GTK4 / Libadwaita), and Web (Vanilla JS / CSS Grid).

---

## 1. Architecture & Design Tokens

Syxx utilizes a unified cross-platform design token specification combining dark glassmorphism (`#170c2e` to `#0f0720`), vibrant status colors, and formal typography.

---

## 2. Six Wardens Rotated Color Sequence Matrix

The Six Wardens follow an exact 6-color rotation sequence for their glyphs, cards, background glows, and execution buttons:

| # | Warden | Rune | Tier | Lock Icon | Color Theme | Primary Button Gradient | Secondary Button Style |
| :-: | :--- | :---: | :--- | :---: | :--- | :--- | :--- |
| **1** | **Prophet** | `ᚨ` | Standard | — | **Green** (`#22c55e`) | `linear-gradient(120deg, #22c55e, #16a34a)` (Dark `#042f14`) | `rgba(34,197,94,.12)` border `rgba(34,197,94,.3)` |
| **2** | **Mirror** | `ᛞ` | **PRO** | 🔒 | **White** (`#ffffff`) | `linear-gradient(120deg, #ffffff, #e5e7eb)` (Dark `#111827`) | `rgba(255,255,255,.12)` border `rgba(255,255,255,.35)` |
| **3** | **Portal** | `ᛜ` | Standard | — | **Blue** (`#3b82f6`) | `linear-gradient(120deg, #3b82f6, #2563eb)` (White text) | `rgba(59,130,246,.12)` border `rgba(59,130,246,.3)` |
| **4** | **Glutton** | `ᚦ` | **PRO** | 🔒 | **Yellow** (`#eab308`) | `linear-gradient(120deg, #eab308, #ca8a04)` (Dark `#2e1d02`) | `rgba(234,179,8,.12)` border `rgba(234,179,8,.3)` |
| **5** | **Cage** | `ᚾ` | Standard | — | **Pink** (`#ff2d78`) | `linear-gradient(120deg, #ff2d78, #ec4899)` (White text) | `rgba(255,45,120,.12)` border `rgba(255,45,120,.3)` |
| **6** | **Witness** | `ᛉ` | **PRO** | 🔒 | **Orange** (`#f97316`) | `linear-gradient(120deg, #f97316, #ea580c)` (White text) | `rgba(249,115,22,.12)` border `rgba(249,115,22,.3)` |

---

## 3. Sidebar Lock Badges for Pro Modules

Pro modules (**Mirror**, **Glutton**, **Witness**) display a gold lock SVG icon next to the `PRO` text badge in the navigation sidebar:

```html
<span class="lock-pro-badge">
  <svg width="10" height="11" viewBox="0 0 26 30" fill="none">
    <path d="M7 13 V10 a6 6 0 0 1 12 0 V13" stroke="#e0b45a" stroke-width="2.6" fill="none"></path>
    <rect x="5" y="13" width="16" height="12.5" rx="3" fill="#e0b45a"></rect>
  </svg>
  PRO
</span>
```

---

## 4. Web Server & Dimensions Application Architecture

The Web Server application ([Syxx Web Server GUI Redesign.html](file:///Users/masterrabbit/Software/Syxx/Syxx%20Web%20Server%20GUI%20Redesign.html)) matches the offline standalone bundle format (`Syxx Dimensions (offline).html`) served from `syxx.app` and provides:
1. **Offline Standalone Assets**: Embedded WOFF2 font glyphs for Cinzel and EB Garamond, data URI logo assets, and offline script unpacker.
2. **OS Preview Switcher**: Dynamic window chrome switching between macOS, Windows 11, and Ubuntu Linux.
3. **Cover & Product Landing**: Pricing tiers ($3.95/mo, $19.95/yr), OS download buttons.
4. **Warp Line-by-Line Diffing**: Side-by-side comparative quasi-terminals showing remote vs local served HTML/CSS/JS.
5. **Governed Remote Shell**: Terminal execution environment supporting restricted DNS commands (`prophet`, `mirror`, `warp`, `conclude`, `nslookup`, `dig`, `dnstwist`, `whois`, `dnssec`).
6. **Zero-Knowledge Account Store**: Device-side PII encryption (Argon2id + salted cipher) with blind indexing.
