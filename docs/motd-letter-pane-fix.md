# MOTD Letter Pane — Display Fix Report

**Date:** July 29, 2026
**Branch:** `matrix-cutover-phase3`
**Component:** macOS What's New / MOTD

---

## Summary

The MOTD letter pane (What's New view) was missing the Syxx branding — the logo, "THE SIX WARDENS" eyebrow, and "Welcome to Syxx" heading. The CSS for `.masthead` existed in the stylesheet but the masthead HTML was never added to the body.

## Root Cause

The server's `whats-new-mac.html` had the masthead CSS defined but the `<div class="masthead">` HTML was never added to the `<main class="pane">` section. The page body started directly with `<div class="letter">`, skipping the branding entirely.

## Fix

Added the masthead block to `Server/whats-new-mac.html` before the letter div:

```html
<div class="masthead">
  <div class="logo">
    <svg viewBox="0 0 1200 800" ...>
      <!-- Syxx logo mark: hexagon, circle, line, dot -->
    </svg>
  </div>
  <div class="eyebrow">THE SIX WARDENS</div>
  <h1>Welcome to Syxx</h1>
</div>
```

The same fix was applied to the local fallback page (`macOS/Sources/SyxxApp/Resources/fallback-mac.html`).

## Deployment

The updated `whats-new-mac.html` must be deployed to the production server at `/opt/SyxxServer/whats-new-mac.html` via `scripts/deploy.sh`. The local fallback is already bundled in the macOS app.