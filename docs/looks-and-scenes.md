# Looks and scenes

Status: **design note, nothing built.** Written 2026-09-22 after a cross-controller preset copy
produced a wrong result on real hardware.

## The defect this replaces

`PresetCopier.BuildFor` pairs runs **by position**. Its own comment is honest about it: *"the first
run here takes the first run's appearance there. A target with more runs repeats the last appearance
rather than leaving them dark."*

Applying North's "Stairs white" wrote this onto South:

| | North (source) | South (what was written) |
| --- | --- | --- |
| run 0 | Porchline — off, fx74, red | Garage — off, fx74, red |
| run 1 | Under stairs — Solid `255,212,172` | Porch — Solid `255,212,172` |
| run 2 | *(none)* | Roofline — Solid `255,212,172` |

The mechanism worked exactly as written. The result is nonsense: a preset called *Stairs white* lit
the porch and the whole roofline warm white, because position 1 on North happens to be the stairs
and position 1 on South happens to be the porch. Position is not a relationship. Garage is not
Porchline.

No pairing rule fixes this, because there is nothing to pair. Two controllers wire different parts
of a house. The answer is to stop asking one controller's preset what another controller should do.

## Two words, and one that stays under the floor

| Word | Means | Example |
| --- | --- | --- |
| **Look** | a named appearance for **one** segment: effect, palette, speed, intensity, colours | "Warm white", "Colorwaves brisk" |
| **Scene** | which look each segment wears, across the whole house | "Christmas", "Stairs white" |

**Preset** is not a third idea. It is what a WLED controller calls the thing LedBalloon writes onto
it so that a scene can be recalled without a PC. Saving a scene publishes it, so on the hardware a
scene *is* a preset, with the same name. Nobody using this app should have to know the word.

WLED has no collective noun for effect + palette + speed; in its UI that bundle is just "the
segment's settings". The nearest formal term in the wider lighting world is ETC's *palette* — a
named, reusable set of parameter values that cues reference — but "palette" is already spoken for
here, so the word is unavailable.

### Why there were nearly two words

A preset and a scene differ in exactly two ways, and only one of them is in the user's interest:

1. **A preset bakes in LED numbers; a scene does not.** North's "Stairs white" preset says *segment
   0 → LEDs 0–308, off; segment 1 → LEDs 308–356, warm white*. Discover later that the porchline is
   really 310 LEDs and the preset still says 308, so two stay dark on every recall. The scene says
   *Porchline off; Under stairs warm white*, and the numbers are worked out at apply time.
2. **A preset can be recalled by the controller alone** — the 23:30 timer, the wall button, the WLED
   phone app. A scene needs LedBalloon running.

The second is the only reason presets have to exist at all, and publishing on save gets it without
charging the user a second concept.

## The model

A look is named and reusable, or anonymous and used once. Both are the same shape; the difference is
whether it is stored in the project's own list and given a name.

```jsonc
// project.looks — named, reusable
{ "id": "a1b2c3d4", "name": "Warm white", "effect": 0, "palette": 0, "primary": "FFD4AC" }

// project.scenes — which look each segment wears
{
  "id": "9f8e7d6c",
  "name": "Stairs white",
  "unlistedSegmentsOff": true,
  "segments": {
    "e4d5c6b7": { "look": "a1b2c3d4" },                       // by reference
    "b8a9c0d1": { "on": false },                              // anonymous, one-off
    "c2d3e4f5": { "effect": 74, "speed": 128, "primary": "FF0000" }
  }
}
```

A segment entry carries **either** a `look` id **or** inline fields, never both. Anonymous entries
are the common case — most segments in most scenes are one-offs, and forcing a name on every one
would be tedious. Naming is a promotion: "use this elsewhere" turns an inline entry into a stored
look and leaves a reference behind.

Scenes are keyed by `Segment.Id`, a stable identifier that survives renaming, re-ordering and length
corrections. No LED indices appear anywhere in a look or a scene.

### The cost of references

Editing a named look reaches backwards into every scene using it. That is the entire point —
"Under stairs warm white" is defined once, and when it turns out too orange you fix it in one place
and Stairs white, Twinkle both and Winter both all follow. It is also the one thing people are
surprised by, so the editor has to say **"used in 3 scenes"** while you are changing it.

## Publishing, which the user does not do

Saving a scene writes it to every controller it covers, as a preset of the same name. Not a button,
not a separate step — part of saving, the way segment boundaries and output lengths already are.
After that the scene is available to the timers, the wall button and the phone app, and the word
"preset" never has to appear in the UI.

The input is the scene's own per-segment truth, never another controller's preset, so there is no
pairing step and nothing to get wrong. Garage cannot inherit Porchline because nothing ever asks
what Porchline is doing.

What publishing has to get right:

- **One name everywhere.** A scene covering two controllers is one scene with one name, stored once
  per box because that is how WLED works. `PresetCatalog` already groups by trimmed name,
  case-insensitively, into one `HousePreset` with a `PresetPlacement(ControllerKey, Slot, Preset)`
  each, so the merged view already exists.
- **Slots differ per controller, and nobody is shown them.** North's "Stairs white" is slot 3; on
  South it would take whatever is free. `ChooseSlot` already reuses a slot holding a preset of the
  same name, so each box keeps its own stable slot across republishes. The schedule picks a scene by
  name and resolves the slot per controller behind the scenes.
- **Write nothing when nothing changed.** Flash has a finite write budget and saving a layout is not
  a reason to spend one. `SetLedOutputLengthsAsync` already works this way; publishing must too.
- **Custom palettes travel with it.** A look storing `palette: 254` is meaningless on a controller
  without that palette file. Publishing must drive `CustomPaletteCopier` and remap the id to the
  slot it lands in. Both halves exist and both have now run against real hardware — South carries a
  `palette0.json` because of it.

### Drift

The scene lives in `ledballoon.json`; its published copy lives in `presets.json` on the same box.
Two renderings of one fact, which can diverge if someone edits the preset in the WLED app. **The
project's scene wins**, and republishing overwrites. Worth saying in the UI the first time it
happens rather than silently clobbering someone's edit.

## Presets that already exist

South has `Bpm` and `Off`; North has ten. They were made by hand in the WLED app, they are what the
timers currently fire, and they cannot simply disappear.

They appear in the same list as scenes, because to the person looking at the list they *are* scenes —
named things that make the house look a certain way. Editing one adopts it: LedBalloon reads its
segments, matches them to the layout by LED range, and writes a real scene with per-segment identity
and no baked indices. From then on it behaves like any other scene.

The corner that cannot be hidden: a preset made in the WLED app may cover LED ranges that match no
segment in the layout — South's `Bpm` and `Off` both do, with bounds `0–20` and `20–305` from a
layout that no longer exists. Adoption would silently drop whatever does not match. So an
un-adopted preset is shown **read-only, with a note saying which parts of it the layout cannot
account for**, and adoption is a deliberate act rather than something that happens on first edit.

## What already exists in the code

More than it sounds:

- `Look` and `SegmentLook` in `src/LedBalloon.Core/Layout/Look.cs`
- `LookResolver.Resolve` (scene → per-controller `WledState`), `.Capture` (current state → scene),
  `.ResolveGeometry` (the segment-bounds push that Save already uses)
- `LedBalloonProject.Looks`, stored under the `"looks"` JSON key, sliced per controller and merged
  back on load, so it syncs like the rest of the layout
- `PresetCopier.StoreAsync`, which writes a preset into `presets.json` and backs up the previous file
- `PresetCatalog`, which already merges by name across controllers
- Five tests covering resolve, capture, no-indices, whole-house span, and unlisted-segments-off

What is missing is naming, references, adoption and UI — not the hard part.

## What changes

1. Today's `Look` becomes **`Scene`**. Today's `SegmentLook` becomes **`Look`**, gains `Id` and
   `Name`, and becomes storable in its own right.
2. `LedBalloonProject` gains `Scenes`; `Looks` comes to mean the named per-segment looks.
3. A scene's segment entry becomes a reference-or-inline union.
4. Saving publishes. `PresetCopier.BuildFor` — the position-pairing function — is deleted outright
   rather than fixed.

### The `"looks"` key is free

`"looks"` currently means whole-house looks, and after this it means named per-segment looks. That
would normally need a migration at `ProjectSerialization.FromUtf8`, where `RetireFlipAim` lives.

It does not, because nobody ever made one. Looks were modelled, resolved and tested but never
reachable from the UI, and the project file lives on the controllers rather than on any PC — so the
only copies that exist anywhere are the four on the hardware, and all four are empty:

| File | Revision | `looks` |
| --- | --- | --- |
| South `ledballoon.json` | 15 | `[]` |
| South `ledballoon.bak.json` | 14 | `[]` |
| North `ledballoon.json` | 15 | `[]` |
| North `ledballoon.bak.json` | 14 | `[]` |

So the key changes meaning outright, with no migration and no compatibility shim. Worth re-checking
before the change lands: if a look gets made in the meantime, the rule is that a `"looks"` entry
carrying a `segments` dictionary is an old whole-house look and belongs in `scenes`.

## Gaps to close

- **`SegmentLook` has no custom sliders.** It carries on, brightness, three colours, effect,
  palette, speed and intensity — but not `c1`/`c2`/`c3`, which `WledSegment` and `PresetCopier` do
  carry. Some effects use them. Capturing a scene today would silently drop them.
- **The README calls the whole-house thing a "look"**, in "Why this exists" and again under Layout.
  It needs to say scene.
- **The UI says "preset" in several places** — the Presets heading, "Check the presets", the
  Schedule tab's picker, and the status line after a save. All become scene, except the preset check
  itself, which is genuinely about what is on the hardware.

## What was already on the controllers — done

South had accumulated **four** position-mapped copies: `July4th`, `Twinkle both`, `Stairs white` and
`St pats`. The fourth appeared partway through writing this note, which is itself worth recording —
every time one of North's presets was applied, another copy landed.

All four are removed. South keeps only the two it made itself, `Bpm` and `Off` in slots 1 and 2,
which its timers fire at 17:30 and 23:30; both were verified still pointing at those slots
afterwards. The full six-preset file is on South as `/psold.json`, and `presets.bak.json` beside it
holds the five-preset state from one copy earlier.

`palette0.json` is left on South. It is the custom palette July4th's copy brought across, remapped
from North's id 254 to South's 255, and nothing references it now — 86 bytes, harmless, and wanted
again the first time a scene using that palette is published.

One practical note for whoever writes the publishing code: WLED's `/edit` handler **rejects an
upload whose part carries no explicit content type**, answering 500 with nothing written. It is not
the "500 on success" case already documented in `WledFileSystemClient` — the file genuinely does not
appear. `WledFileSystemClient.UploadAsync` sets the type and is fine; hand-rolled `curl` is not.

## Settled

**Deleting a scene is refused while a timer points at it.** The Schedule tab names the timer and the
controller; delete the timer first. Removing the published preset out from under a timer would leave
it firing nothing at 23:30 with no way to notice, and leaving the preset behind would keep a scene
alive that the app says is gone. Refusing is the only option that cannot surprise anyone in the
dark.

**Renaming a scene rewrites the published preset and deletes the old name.** Publishing matches by
name, so anything else orphans the old preset — and an orphan is worse than usual here, because a
timer pointing at its slot would go on firing the previous version of the scene forever. The rename
has to carry the timers with it, which is possible because they are recalled by slot and the slot is
being reused.

## Offline controllers — a display gap that exists today

A controller that has never answered **is not shown at all**, and its segments are unreachable:

- `RebuildSegmentRowsCore` builds `Coverage` from `Devices`, filtered to those with a `DeviceKey`.
  The key comes from the device's own info, so it only exists after a successful connect. No
  connect, no card.
- Its segments are still drawn on the photo — `HouseCanvas` iterates `project.Segments` and knows
  nothing about devices — so they can be clicked there but not edited anywhere.
- They are added to `SegmentRows` as orphans, under a comment saying they "would otherwise vanish
  rather than be fixable". That is no longer true. The panel renders segments only through
  `Coverage → Outputs → Runs`, so the orphan rows are not displayed anywhere. The comment describes
  the old panel; grouping the list by output broke its intent without anyone noticing.

A controller that answered and then dropped is fine: devices are only removed when a duplicate key
appears, so the card stays with a disconnected dot.

None of this is caused by scenes, and it should be fixed on its own — a segment on a box that is not
answering still belongs to the house, and the panel should say so rather than omit it. Once it is,
the scenes answer follows: publish to the controllers that answer, mark the scene as not fully
published, and say which box is missing.

## Non-goals

- Changing what a WLED preset is, or how the controllers recall one.
- Storing anything on the PC. Scenes and looks live in the project, which lives on the controllers.
- Keeping cross-controller preset copying as a user-facing idea, or as code.
