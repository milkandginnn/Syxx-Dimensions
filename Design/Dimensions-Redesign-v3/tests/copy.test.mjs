import test from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";
import { hasElementWithAttribute, hasVisibleElementText, illustrativeDocuments, isTextAsset, textPackageFiles } from "./structural.mjs";

const root = resolve(import.meta.dirname, "..");
const watermark = "ILLUSTRATIVE STATE. NOT LIVE DATA.";
const approvedCopy = [
  "Set up Syxx",
  "Confirm a few defaults before your first scan. You can change them later in Settings.",
  "Choose how Syxx speaks",
  "High-fantasy dialogue with plain labels and exact technical details.",
  "Plain, direct English with no story language.",
  "Choose DNS defaults",
  "System resolver",
  "Preferred encrypted protocol",
  "Let Syxx manage encrypted DNS",
  "Use Unbound as the local validating resolver",
  "Syxx reports a change only after it confirms the setting took effect.",
  "Privacy and reminders",
  "Remind me after seven days without a complete scan",
  "Check for updates and critical notices when Syxx opens",
  "Send anonymous usage data",
  "Mask network addresses in saved files",
  "Nothing leaves this device unless a setting says it will.",
  "Ready for your first scan",
  "Review these choices, then take the short tutorial or open What's New.",
  "Back",
  "Next",
  "Use defaults",
  "Finish setup",
  "Start tutorial",
  "I have read and agree to the terms above.",
  "Decline and quit",
  "Agree and continue",
  "Close",
  "Take a quick tour",
  "This strip shows DNS encryption, protocol, Unbound, and scan freshness. A question mark means Syxx could not confirm the state.",
  "Each module answers one network question. Use Dashboard for a complete scan, or open one module for a focused check.",
  "Running a complete scan gathers the evidence needed for a conclusion. Missing or interrupted checks stay incomplete.",
  "Conclusion explains what the completed checks support, what is missing, and what to do next. Save it as a plain-text file when you need a record.",
  "Back",
  "Next",
  "Skip tutorial",
  "Finish",
  "DNS encryption: On",
  "DNS encryption: Off",
  "DNS encryption: Unknown",
  "Protocol: DoH",
  "Protocol: DoT",
  "Protocol: DoQ",
  "Protocol: Plain DNS",
  "Protocol: Unknown",
  "Unbound: Active",
  "Unbound: Inactive",
  "Unbound: Unknown",
  "Scan: Never run",
  "Scan: In progress",
  "Scan: Incomplete",
  "Scan: More than a week old",
  "Last complete scan: {date}",
  "Quick actions",
  "Syxx is ready",
  "Collecting evidence",
  "A complete scan has not run yet",
  "The latest scan is incomplete",
  "The last complete scan is more than a week old",
  "Run a quick scan",
  "Run selected checks",
  "Open What's New",
  "Open Settings",
  "Save latest conclusion",
  "Quit Syxx",
  "General",
  "DNS",
  "Scans & reminders",
  "Privacy",
  "Files",
  "Advanced",
  "Presentation",
  "Replay tutorial",
  "Resolver",
  "Protocol",
  "Let Syxx manage encrypted DNS",
  "Use Unbound",
  "Changes that need administrator permission will ask when you apply them.",
  "Run a quick scan when Syxx opens",
  "Remind me after seven days without a complete scan",
  "Send anonymous usage data",
  "Mask network addresses in saved files",
  "Remove stored Syxx data",
  "Cancel",
  "Apply changes",
  "Save conclusion",
  "File name",
  "Location",
  "Format",
  "Plain text (.txt)",
  "Cancel",
  "Save",
  "Add existing license key",
  "Paste your license key below. Syxx checks it with the licensing service, then stores it on this device. It is never attached to DNS results.",
  "License key",
  "Checking key…",
  "License added",
  "This key was not accepted.",
  "Syxx could not contact the licensing service. Your current access has not changed.",
  "Cancel",
  "Check key",
  "Send feedback",
  "Tell us what worked, what did not, or what you would like Syxx to do.",
  "Topic",
  "Message",
  "Reply email (optional)",
  "Include app version and operating system",
  "No DNS results or captured addresses are included unless you paste them here.",
  "Cancel",
  "Open email",
  "Syxx Dimensions",
  "DNS investigation and protection",
  "Version {version}",
  "Build {build}",
  "Created by MilkandGin",
  "Website",
  "License Agreement",
  "Send Feedback",
  "Close",
  "Warp",
  "Compare what syxx.app retrieves with what your device retrieves.",
  "Domain or web address",
  "Compare content",
  "Remote fetch",
  "From syxx.app",
  "Local fetch",
  "From this device",
  "Waiting for a domain",
  "Waiting for the Syxx desktop helper",
  "Fetching",
  "Complete",
  "Could not complete",
  "Review differences",
  "Only you can decide whether a difference is expected. Syxx highlights changed, added, and missing lines.",
  "Added locally",
  "Missing locally",
  "Changed",
  "Open in Syxx",
  "Paste local source",
  "Compare again",
  watermark
];

test("copy catalogue preserves every approved string verbatim", () => {
  const catalogue = resolve(root, "copy-catalogue.html");
  assert.equal(existsSync(catalogue), true, "copy-catalogue.html");
  const html = readFileSync(catalogue, "utf8");
  for (const copy of approvedCopy) assert.ok(html.includes(copy), copy);
});

test("illustrative pages carry the required honesty watermark", () => {
  for (const document of illustrativeDocuments(root)) {
    assert.equal(hasVisibleElementText(document.html, "data-illustrative-watermark", watermark), true, document.path);
  }
});

test("illustrative discovery and watermark helper reject inert or invisible matches", () => {
  const commentOnly = "<!-- <section data-illustrative-state> -->";
  const hiddenWatermark = `<p data-illustrative-watermark hidden>${watermark}</p>`;
  const invisibleWatermarks = [
    `<p data-illustrative-watermark style="visibility: hidden">${watermark}</p>`,
    `<p data-illustrative-watermark style="visibility: collapse">${watermark}</p>`,
    `<p data-illustrative-watermark style="opacity: 0">${watermark}</p>`,
    `<p data-illustrative-watermark style="position: absolute; left: -9999px">${watermark}</p>`,
    `<p data-illustrative-watermark style="position: relative; left: -9999px">${watermark}</p>`,
    `<p data-illustrative-watermark style="position: absolute; left: -100vw">${watermark}</p>`,
    `<p data-illustrative-watermark style="transform: translateX(-100vw)">${watermark}</p>`,
    `<p data-illustrative-watermark style="transform: translate(-9999px, 0)">${watermark}</p>`,
    `<p data-illustrative-watermark style="transform: matrix(1, 0, 0, 1, -9999, 0)">${watermark}</p>`
  ];
  const visibleWatermarks = [
    `<p data-illustrative-watermark style="position: relative; left: -20px">${watermark}</p>`,
    `<p data-illustrative-watermark style="transform: translateX(-12px)">${watermark}</p>`
  ];

  assert.equal(hasElementWithAttribute(commentOnly, "data-illustrative-state"), false);
  assert.equal(hasVisibleElementText(hiddenWatermark, "data-illustrative-watermark", watermark), false);
  for (const html of invisibleWatermarks) {
    assert.equal(hasVisibleElementText(html, "data-illustrative-watermark", watermark), false, html);
  }
  for (const html of visibleWatermarks) {
    assert.equal(hasVisibleElementText(html, "data-illustrative-watermark", watermark), true, html);
  }
});

test("text asset decision scans textual assets and skips binary assets", () => {
  for (const file of ["screen.svg", "state.json", "notes.txt", "report.csv", "routes.tsv", "feed.xml", "script.map"]) {
    assert.equal(isTextAsset(file), true, file);
  }
  for (const file of ["capture.mp3", "recording.mp4", "preview.mov", "archive.zip", "brand.otf", "module.wasm"]) {
    assert.equal(isTextAsset(file), false, file);
  }
});

test("package files exclude the owner-prohibited word and Unicode em dash", () => {
  const ownerProhibited = ["War", "den"].join("");
  for (const file of textPackageFiles(root)) {
    const content = readFileSync(file, "utf8");
    assert.equal(content.includes(ownerProhibited), false, file);
    assert.equal(content.includes("\u2014"), false, file);
  }
});
