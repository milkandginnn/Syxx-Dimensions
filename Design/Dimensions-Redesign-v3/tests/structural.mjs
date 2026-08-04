import { basename, extname, join } from "node:path";
import { readdirSync, readFileSync } from "node:fs";

const inertElementPattern = /<(script|style|template|textarea)\b[^>]*>[\s\S]*?<\/\1\s*>/gi;

export function packageFiles(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    return entry.isDirectory() ? packageFiles(path) : [path];
  });
}

export function textPackageFiles(directory) {
  return packageFiles(directory).filter(isTextAsset);
}

const textExtensions = new Set([
  ".css", ".csv", ".cjs", ".html", ".htm", ".js", ".json", ".map", ".md", ".mjs", ".svg", ".tsv", ".txt", ".webmanifest", ".xml", ".yaml", ".yml"
]);
const intentionalExtensionlessTextAssets = new Set();

export function isTextAsset(path) {
  const extension = extname(path).toLowerCase();
  return textExtensions.has(extension) || (!extension && intentionalExtensionlessTextAssets.has(basename(path)));
}

function activeMarkup(html) {
  return html.replace(/<!--[\s\S]*?-->/g, "").replace(inertElementPattern, "");
}

function openingElements(html) {
  return [...activeMarkup(html).matchAll(/<([A-Za-z][\w:-]*)(\s[^<>]*?)?\s*\/?>/g)].map((match) => ({
    tag: match[1].toLowerCase(),
    attributes: match[2] ?? "",
    end: match.index + match[0].length,
    source: activeMarkup(html)
  }));
}

function escapePattern(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function hasAttribute(attributes, name, value) {
  const escapedName = escapePattern(name);
  if (value === undefined) {
    return new RegExp(`(?:^|\\s)${escapedName}(?=\\s|=|/|$)`, "i").test(attributes);
  }
  const escapedValue = escapePattern(value);
  return new RegExp(
    `(?:^|\\s)${escapedName}\\s*=\\s*(?:"${escapedValue}"|'${escapedValue}'|${escapedValue})(?=\\s|/|$)`,
    "i"
  ).test(attributes);
}

export function hasElementWithAttribute(html, name, value) {
  return openingElements(html).some((element) => hasAttribute(element.attributes, name, value));
}

function isHidden(attributes) {
  const styles = inlineStyles(attributes);
  const visibility = styles.get("visibility")?.toLowerCase();
  const opacity = styles.get("opacity");
  return hasAttribute(attributes, "hidden") ||
    hasAttribute(attributes, "aria-hidden", "true") ||
    styles.get("display")?.toLowerCase() === "none" ||
    visibility === "hidden" ||
    visibility === "collapse" ||
    isZeroOpacity(opacity) ||
    isClearlyOffscreen(styles);
}

function inlineStyles(attributes) {
  const match = attributes.match(/\bstyle\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))/i);
  if (!match) return new Map();
  return new Map(declarations(match[1] ?? match[2] ?? match[3]).map(([name, value]) => [name.toLowerCase(), normalizedStyleValue(value)]));
}

function normalizedStyleValue(value) {
  return value.replace(/\s*!important\s*$/i, "").trim();
}

function isZeroOpacity(value) {
  if (value === undefined) return false;
  const normalized = value.trim();
  return normalized === "0%" || (Number(normalized) === 0 && /^[-+]?\d*\.?\d+$/.test(normalized));
}

function isClearlyOffscreen(styles) {
  const positioned = new Set(["absolute", "fixed", "relative"]).has(styles.get("position")?.toLowerCase());
  const offset = positioned && ["left", "right", "top", "bottom"].some((property) => isLargeNegativeLength(styles.get(property)));
  return offset || hasLargeNegativeTranslation(styles.get("transform"));
}

function isLargeNegativeLength(value) {
  return value !== undefined && /^-\s*(?:(?:[1-9]\d{3,})(?:\.\d+)?px|(?:[1-9]\d{2,})(?:\.\d+)?v[wh])\b/i.test(value.trim());
}

function hasLargeNegativeTranslation(transform) {
  if (!transform) return false;
  const translation = transform.match(/translate(?:3d|x|y)?\(\s*([^,\s)]+)/i);
  if (translation && isLargeNegativeLength(translation[1])) return true;
  const matrix = transform.match(/matrix(?:3d)?\(([^)]+)\)/i);
  if (!matrix) return false;
  const values = matrix[1].split(",").map((value) => Number(value.trim()));
  const xIndex = transform.trim().toLowerCase().startsWith("matrix3d") ? 12 : 4;
  return Number.isFinite(values[xIndex]) && values[xIndex] <= -1000;
}

export function hasVisibleElementText(html, marker, text) {
  return openingElements(html).some((element) => {
    if (!hasAttribute(element.attributes, marker) || isHidden(element.attributes)) return false;
    const closing = new RegExp(`</${escapePattern(element.tag)}\\s*>`, "i");
    const body = element.source.slice(element.end);
    const closingMatch = body.match(closing);
    if (!closingMatch) return false;
    return body.slice(0, closingMatch.index).replace(/<[^>]*>/g, "").includes(text);
  });
}

export function illustrativeDocuments(root) {
  return packageFiles(root)
    .filter((path) => extname(path).toLowerCase() === ".html")
    .map((path) => ({ path, html: readFileSync(path, "utf8") }))
    .filter((document) => hasElementWithAttribute(document.html, "data-illustrative-state"));
}

function cssRuleBlocks(css) {
  const source = css.replace(/\/\*[\s\S]*?\*\//g, "");
  const blocks = [];
  const stack = [];
  let segmentStart = 0;
  let quote = null;
  let escaped = false;

  for (let index = 0; index < source.length; index += 1) {
    const character = source[index];
    if (quote) {
      if (!escaped && character === quote) quote = null;
      escaped = !escaped && character === "\\";
      continue;
    }
    if (character === "'" || character === '"') {
      quote = character;
      escaped = false;
      continue;
    }
    if (character === "{") {
      stack.push({ prelude: source.slice(segmentStart, index).trim(), bodyStart: index + 1 });
      segmentStart = index + 1;
    }
    if (character === "}") {
      const block = stack.pop();
      if (block && !block.prelude.startsWith("@")) {
        blocks.push({ selector: block.prelude, declarations: source.slice(block.bodyStart, index) });
      }
      segmentStart = index + 1;
    }
  }
  return blocks;
}

function declarations(body) {
  const entries = [];
  let start = 0;
  let depth = 0;
  let quote = null;
  let escaped = false;
  for (let index = 0; index <= body.length; index += 1) {
    const character = body[index];
    if (quote) {
      if (!escaped && character === quote) quote = null;
      escaped = !escaped && character === "\\";
      continue;
    }
    if (character === "'" || character === '"') {
      quote = character;
      escaped = false;
    } else if (character === "(") {
      depth += 1;
    } else if (character === ")") {
      depth -= 1;
    } else if ((character === ";" && depth === 0) || index === body.length) {
      const entry = body.slice(start, index).trim();
      const separator = entry.indexOf(":");
      if (separator > 0) entries.push([entry.slice(0, separator).trim(), entry.slice(separator + 1).trim()]);
      start = index + 1;
    }
  }
  return entries;
}

export function activeRuleHasDeclaration(css, selector, property, value) {
  return cssRuleBlocks(css).some((rule) =>
    rule.selector.split(",").some((candidate) => candidate.trim() === selector) &&
    declarations(rule.declarations).some(([name, actual]) => name === property && actual === value)
  );
}
