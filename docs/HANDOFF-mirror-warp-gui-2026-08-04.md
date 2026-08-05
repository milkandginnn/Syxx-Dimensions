# KEEP-OUT NOTICE — GUI files reserved by the Mirror/Warp work streams

- **Date:** 2026-08-04
- **For:** the team/model doing GUI work in this repo
- **Purpose:** an active multi-stream build (Mirror module redesign + Warp signed-manifest integrity feature) has in-flight and queued edits across specific GUI and GUI-adjacent files. Editing them will collide with that work. This document lists exactly what to leave alone, why, and what is safely yours.

---

## 0.1 RESERVED — do not edit ProphetView (macOS)-- Gold Standard


### 0.1.0 macOS first

Finish the GUI for macOS first. Then begin translating to Windows and Linux. Only translate to Windows and Linux after i've applroved what has been done so far. Do not continue this work unless you have a solid plan for reaching full parity, and a design that will enable full parity. That means: consider what content/captions/data need to be displayued, and what's the best way to display it. Then apply the color treatments to the captions and content that should get them (not everythoing).

### 0.1.1 ProphetView on macOS and associated files represent the gold standard to which parity should be reached.

ProphetView is as of now th§e gold standard aesthetic. It is incomplete, including having a result that is clipped on the lower end of the window, a couple incorrect fonts, and some spacing issues, but is still the aesthetic gold standard. To the extent practical, the panels should be style after it, except to use the highlight color assigned to the pane you're working on.

When creating an input, body copy, button, etc., feel free to copy directly from the ProphetView.swift code, translate to the language you're using, adapt for your UI framework, but do so in a way that preserves the style. Then change the highlight color (leaving the gradient alpha channels and styles), change the copy, resize objects, and adapt to the presentation needs of the pane you're currently working in.


## 0.1.2 Color Palette

Prophet: Blue

Mirror: Orange

Portal: Green/Teal

Glutton: Pink

Cage: Gray/Silver

Witness: Red

The color scheme goes cool, warm, cool, warm, cool, warm. When selecting a specific hex code/rgba for your highlight color, once again, style after ProphetView.swift. Prophet uses a particular value for blue that is bright, energetic, almost neon; Portal already has a highlight color that matches the intensity, it just isnt fully applied. Choose an orange that matches that intensity, a red that is just as bright, a pink that catches your attention. 

Match how ProphetView applies the highlight color to its ocntents: what text on the pane is colored in the highlight color> Matchg it on yours. How is the highlight color applied to containers, and which containers receive the treatment. Try to match.  Make it look consistent on panes within macOS, and then duplicate for Windows and Linux.

---

## 0.2 



## 1. RESERVED — do not edit these files

### 1.1 Mirror views, all three platforms — queued for imminent rewrite

The Mirror module is being rebuilt (duplicate DNS column removed, new conclusion banner, per-asset integrity table, side-by-side diff overlay). A build agent will rewrite these wholesale; any edit you make will be clobbered or cause merge conflicts:

```
macOS/Sources/SyxxApp/MirrorView.swift
Windows/dotNET/SyxxUI/Views/MirrorView.xaml
Windows/dotNET/SyxxUI/Views/MirrorView.xaml.cs
Linux/Ubuntu/SyxxUI.Avalonia/Views/MirrorView.axaml
Linux/Ubuntu/SyxxUI.Avalonia/Views/MirrorView.axaml.cs
Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/MirrorViewModel.cs
```

### 1.2 Prophet views + consensus presentation — pending migration to the shared engine

Prophet and Mirror now share one `ResolverConsensusEngine` + last-run store. The macOS Prophet resolver roster still lives in the App layer and is queued for migration into the shared engine; the presentation layer may shift with it:

```
macOS/Sources/SyxxApp/ProphetView.swift
Windows/dotNET/SyxxUI/Views/ProphetView.xaml
Windows/dotNET/SyxxUI/Views/ProphetView.xaml.cs
Linux/Ubuntu/SyxxUI.Avalonia/Views/ProphetView.axaml
Linux/Ubuntu/SyxxUI.Avalonia/Views/ProphetView.axaml.cs
Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/ProphetViewModel.cs
Windows/dotNET/Syxx.Kit/ProphetPresentation.cs
Windows/dotNET/Syxx.Kit/ProphetConsensus.cs
```

### 1.3 Witness views — uncommitted changes already sitting in the working tree

These carry live, uncommitted modifications from a separate Witness-context session. Touching them risks destroying work that exists nowhere else:

```
macOS/Sources/SyxxApp/WitnessView.swift
Windows/dotNET/SyxxUI/Views/WitnessView.xaml
Windows/dotNET/SyxxUI/Views/WitnessView.xaml.cs
```

### 1.4 Cage views — a different agent's in-flight architecture redesign

Not part of the Mirror/Warp streams, but reserved for the same reason: an agent is mid-transition of the Cage/query-capture architecture to an XPC-helper-based system and ran out of usage partway. Its state is unknowable from the tree:

```
macOS/Sources/SyxxApp/CageView.swift
Windows/dotNET/SyxxUI/Views/CageView.xaml
Windows/dotNET/SyxxUI/Views/CageView.xaml.cs
Linux/Ubuntu/SyxxUI.Avalonia/Views/CageView.axaml
Linux/Ubuntu/SyxxUI.Avalonia/Views/CageView.axaml.cs
```

### 1.5 The shared view-model — additive edits only, no restructuring

```
Windows/dotNET/SyxxUI/ViewModels/AppViewModel.cs
```

The Mirror UI stream will be wiring new Warp state into this file. If your work must touch it, keep changes strictly additive (new members, no renames, no reordering, no refactors), and expect Mirror-related members to appear underneath you.

### 1.6 The output catalogs — append only, never reorder or rewrite

```
outputs-catalog-general-mode.csv
outputs-catalog-story-mode.csv
```

The Mirror stream will be appending new rows (columns: `file:line,trigger,module,task,output,change`). You may append your own rows; do not sort, deduplicate, reformat, or edit existing rows — the owner edits copy through these files and concurrent rewrites will destroy entries.

### 1.7 Non-GUI files backing this work — off-limits without exception

You shouldn't need these for GUI work; listed so nobody "helpfully" adjusts them:

```
Server/**                                        # server manifest pipeline (418 tests green)
warp-manifest-vectors/**                         # cross-language fixture corpus
macOS/Sources/SyxxKit/WarpEngine.swift           # Swift verifier engine
macOS/Sources/SyxxKit/WarpManifestPinning.swift
macOS/Tests/SyxxKitTests/WarpEngineTests.swift   # being fixed by a build agent right now
Windows/dotNET/Syxx.Kit/WarpEngine.cs            # C# verifier engine (19/19 green)
Windows/dotNET/Syxx.Kit/WarpManifestPinning.cs
Windows/dotNET/Syxx.Tests/WarpEngineTests.cs
Windows/dotNET/Syxx.Kit/TextDiff.cs              # will be surfaced by the Mirror diff overlay
macOS/Sources/SyxxKit/TextDiff.swift
docs/warp-manifest-v1-spec.md                    # frozen protocol contract
docs/PROPOSAL-mirror-warp-reimagining-2026-08-03.md
macOS/Sources/DeepSyxx/**                        # privileged helper — currently fails to link;
                                                 # that is expected and owned by the Cage agent
review-receipts/**  receipts/**                  # signed audit chains — never hand-edit
```

---

## 2. KNOWN BUILD QUIRKS — so you don't "fix" something reserved

- **`swift test` / `swift build` at package scope fails on macOS** with `ld: ... DeepSyxx-Info.plist`. That is the Cage agent's lane and is expected. Do not fix it. Scope your builds instead: `swift build --target <YourTarget>` from `/Users/masterrabbit/Software/Syxx/macOS` works.
- **Run .NET tests from the test-project directory** (`Windows/dotNET/Syxx.Tests`), not the repo root. The solution is `Windows/Syxx.sln`.
- **Two suites must stay green:** the C# Warp engine tests (19/19) and the Python server suite (418). If a change of yours turns either red, back it out — you've crossed into reserved territory.

---

## 3. HOUSE RULES THAT APPLY TO ALL GUI WORK (yours included)

1. **The word "verdict" is banned** by owner directive — `result` in code identifiers, `conclusion` in UI/docs. The only permitted occurrence is the pre-existing signed wire field `evidence.comparisonResult.verdict`; never rename it, never introduce the word anywhere new.
2. **Every new user-facing string** must be registered (appended) in both output catalogs (§1.6) so the owner can edit copy.
3. **Honesty protocol:** panels respect `Idle / Running / Completed / Failed`; failed ≠ never-ran ≠ match; never render a "clean"/positive state for something that didn't run.

---

## 4. WHAT IS SAFELY YOURS

Any GUI surface not listed above — e.g. Leak, Synthesis, license/settings/about surfaces, shell/navigation chrome — subject to the house rules in §3 and the additive-only rule for `AppViewModel.cs`. If a task genuinely requires touching a §1 file, stop and coordinate with the owner first rather than editing; three build streams and one paused agent are live in this tree, and the collision cost is higher than the wait.
