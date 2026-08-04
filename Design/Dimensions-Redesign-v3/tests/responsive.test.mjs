import test from "node:test";
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";
import { activeRuleHasDeclaration } from "./structural.mjs";

const root = resolve(import.meta.dirname, "..");

function stylesheet(name) {
  const path = resolve(root, `assets/css/${name}`);
  assert.equal(existsSync(path), true, `assets/css/${name}`);
  return readFileSync(path, "utf8");
}

function activeMediaBlock(css, condition) {
  const source = css.replace(/\/\*[\s\S]*?\*\//g, "");
  const start = source.search(condition);
  assert.notEqual(start, -1, condition.source);
  const openingBrace = source.indexOf("{", start);
  assert.notEqual(openingBrace, -1, `${condition.source} opening brace`);

  let depth = 1;
  let quote = null;
  let escaped = false;
  for (let index = openingBrace + 1; index < source.length; index += 1) {
    const character = source[index];
    if (quote) {
      if (!escaped && character === quote) quote = null;
      escaped = !escaped && character === "\\";
      continue;
    }
    if (character === "'" || character === '"') {
      quote = character;
      escaped = false;
    } else if (character === "{") {
      depth += 1;
    } else if (character === "}") {
      depth -= 1;
      if (depth === 0) return source.slice(openingBrace + 1, index);
    }
  }

  assert.fail(`${condition.source} closing brace`);
}

function sha256(path) {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}

test("token stylesheet defines the approved desktop window bounds", () => {
  const tokens = resolve(root, "assets/css/tokens.css");
  assert.equal(existsSync(tokens), true, "assets/css/tokens.css");
  const css = readFileSync(tokens, "utf8");
  assert.match(css, /--window-max-width:\s*1040px\s*;/);
  assert.match(css, /--window-max-height:\s*650px\s*;/);
});

test("token stylesheet carries each approved responsive breakpoint", () => {
  const tokens = resolve(root, "assets/css/tokens.css");
  assert.equal(existsSync(tokens), true, "assets/css/tokens.css");
  const css = readFileSync(tokens, "utf8");
  for (const width of [1008, 641, 401, 345]) {
    assert.match(css, new RegExp(`@media\\s*\\([^)]*${width}px`), `${width}px`);
  }
});

test("shell prevents horizontal stage overflow while grid children can shrink", () => {
  const css = stylesheet("shell.css");
  assert.equal(activeRuleHasDeclaration(css, ".app-stage", "overflow-x", "clip"), true);
  assert.equal(activeRuleHasDeclaration(css, ".app-main", "min-width", "0"), true);
  assert.equal(activeRuleHasDeclaration(css, ".app-drawer", "display", "none"), true);
});

test("application window enforces the approved maximum geometry", () => {
  const css = stylesheet("shell.css");
  assert.equal(activeRuleHasDeclaration(css, ".app-window", "max-inline-size", "var(--window-max-width)"), true);
  assert.equal(activeRuleHasDeclaration(css, ".app-window", "max-block-size", "var(--window-max-height)"), true);
});

test("main content scrolls independently from fixed navigation", () => {
  const css = stylesheet("shell.css");
  assert.equal(activeRuleHasDeclaration(css, ".app-window", "overflow", "hidden"), true);
  assert.equal(activeRuleHasDeclaration(css, ".app-main", "overflow-y", "auto"), true);
  assert.equal(activeRuleHasDeclaration(css, ".app-sidebar", "overflow-y", "auto"), true);
});

test("responsive shell exposes full sidebar navigation at 1008 pixels", () => {
  const css = stylesheet("shell.css");
  const media = activeMediaBlock(css, /@media\s*\(\s*min-width\s*:\s*1008px\s*\)/);
  assert.equal(activeRuleHasDeclaration(media, ".app-sidebar", "display", "flex"), true);
  assert.equal(activeRuleHasDeclaration(media, ".app-rail", "display", "none"), true);
  assert.equal(activeRuleHasDeclaration(media, ".compact-title-row", "display", "none"), true);
  assert.equal(activeRuleHasDeclaration(media, ".app-drawer", "display", "none"), true);
});

test("responsive shell exposes icon rail navigation from 641 through 1007 pixels", () => {
  const css = stylesheet("shell.css");
  const media = activeMediaBlock(
    css,
    /@media\s*\(\s*min-width\s*:\s*641px\s*\)\s*and\s*\(\s*max-width\s*:\s*1007px\s*\)/
  );
  assert.equal(activeRuleHasDeclaration(media, ".app-sidebar", "display", "none"), true);
  assert.equal(activeRuleHasDeclaration(media, ".app-rail", "display", "flex"), true);
  assert.equal(activeRuleHasDeclaration(media, ".compact-title-row", "display", "none"), true);
  assert.equal(activeRuleHasDeclaration(media, ".app-drawer", "display", "none"), true);
});

test("responsive shell exposes an overlay drawer from 401 through 640 pixels", () => {
  const css = stylesheet("shell.css");
  const media = activeMediaBlock(
    css,
    /@media\s*\(\s*min-width\s*:\s*401px\s*\)\s*and\s*\(\s*max-width\s*:\s*640px\s*\)/
  );
  assert.equal(activeRuleHasDeclaration(media, ".app-sidebar", "display", "none"), true);
  assert.equal(activeRuleHasDeclaration(media, ".app-rail", "display", "none"), true);
  assert.equal(activeRuleHasDeclaration(media, ".compact-title-row", "display", "flex"), true);
  assert.equal(activeRuleHasDeclaration(media, ".app-drawer", "display", "flex"), true);
  assert.equal(activeRuleHasDeclaration(media, ".app-drawer", "position", "fixed"), true);
});

test("responsive shell exposes a full-height compact drawer from 345 through 400 pixels", () => {
  const css = stylesheet("shell.css");
  const media = activeMediaBlock(
    css,
    /@media\s*\(\s*min-width\s*:\s*345px\s*\)\s*and\s*\(\s*max-width\s*:\s*400px\s*\)/
  );
  assert.equal(activeRuleHasDeclaration(media, ".compact-title-row", "display", "flex"), true);
  assert.equal(activeRuleHasDeclaration(media, ".app-drawer", "display", "flex"), true);
  assert.equal(activeRuleHasDeclaration(media, ".app-drawer", "inline-size", "100%"), true);
  assert.equal(activeRuleHasDeclaration(media, ".app-drawer", "block-size", "100dvh"), true);
  assert.equal(activeRuleHasDeclaration(media, ".app-main", "padding-inline", "12px"), true);
  assert.equal(activeRuleHasDeclaration(media, ".app-main", "--narrow-content-width", "321px"), true);
  assert.equal(
    activeRuleHasDeclaration(media, ".app-content", "inline-size", "max(var(--narrow-content-width), 100%)"),
    true
  );
  assert.equal(activeRuleHasDeclaration(media, ".app-content", "max-inline-size", "100%"), true);
});

test("reduced motion removes transforms and animated traces", () => {
  const condition = /@media\s*\(\s*prefers-reduced-motion\s*:\s*reduce\s*\)/;
  const shellMedia = activeMediaBlock(stylesheet("shell.css"), condition);
  const componentMedia = activeMediaBlock(stylesheet("components.css"), condition);
  assert.equal(activeRuleHasDeclaration(shellMedia, ".wayline::after", "animation", "none"), true);
  assert.equal(activeRuleHasDeclaration(shellMedia, ".wayline::after", "transform", "none"), true);
  assert.equal(activeRuleHasDeclaration(componentMedia, ".button:hover", "transform", "none"), true);
  assert.equal(activeRuleHasDeclaration(componentMedia, ".switch__track::after", "transition", "none"), true);
  assert.equal(
    activeRuleHasDeclaration(
      componentMedia,
      ".switch input:checked + .switch__track::after",
      "transform",
      "translateX(16px)"
    ),
    true
  );
});

test("canonical brand assets are copied without raster recompression", () => {
  const image = resolve(root, "assets/img/syxx-logo.png");
  const company = resolve(root, "assets/img/milkandgin-logo.svg");
  assert.equal(existsSync(image), true, "assets/img/syxx-logo.png");
  assert.equal(existsSync(company), true, "assets/img/milkandgin-logo.svg");
  assert.equal(sha256(image), "07e5f705bb45cb18ec4c8733143d15e576ae12482f12acbda36375bf309e8841");
  assert.equal(sha256(company), "155acbbff7a53af96b5b64b1ffd72fd2a85df21f2d6ba6f52a6ed459d78ffc19");
});

test("status template remains monochrome and legible as a current-color glyph", () => {
  const icon = resolve(root, "assets/img/syxx-status-template.svg");
  assert.equal(existsSync(icon), true, "assets/img/syxx-status-template.svg");
  const svg = readFileSync(icon, "utf8");
  assert.match(svg, /viewBox="0 0 24 24"/);
  assert.match(svg, /<title>Syxx<\/title>/);
  assert.match(svg, /fill="currentColor"/);
  assert.doesNotMatch(svg, /(?:linear|radial)Gradient|url\(#/i);
});

test("CSS rule helper rejects comment-only and string-only declarations", () => {
  const commentOnly = "/* .app-stage { overflow-x: clip; } */";
  const stringOnly = '.app-stage { content: "overflow-x: clip"; }';

  assert.equal(activeRuleHasDeclaration(commentOnly, ".app-stage", "overflow-x", "clip"), false);
  assert.equal(activeRuleHasDeclaration(stringOnly, ".app-stage", "overflow-x", "clip"), false);
});
