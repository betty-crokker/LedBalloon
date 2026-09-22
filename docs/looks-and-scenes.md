# Looks, scenes and presets

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

The mechanism worked exactly as written. The result is nonsense: a preset called *Stairs white* now
lights the porch and the whole roofline warm white, because position 1 on North happens to be the
stairs and position 1 on South happens to be the porch. Position is not a relationship. Garage is
not Porchline.

No pairing rule fixes this, because there is nothing to pair. Two controllers wire different parts
of a house. The answer is to stop asking one controller's preset what another controller should do.

## Three words, each doing one job

| Word | Means | Example |
| --- | --- | --- |
| **Look** | a named appearance for **one** segment: effect, palette, speed, intensity, colours | "Warm white", "Colorwaves brisk" |
| **Scene** | which look each segment wears, across the whole house | "Christmas", "Stairs white" |
| **Preset** | one controller's own recallable state — WLED's meaning, unchanged | what the 23:30 timer fires |

WLED has no collective noun for effect + palette + speed; in its UI that bundle is just "the
segment's settings". The nearest formal term in the wider lighting world is ETC's *palette* — a
named, reusable set of parameter values that cues reference — but "palette" is already spoken for
here, so the word is unavailable.

The point of the table is that **preset** keeps meaning what it already means everywhere else in
this app: what the schedule recalls, what "Check the presets" checks, what the WLED phone app shows.
The whole-house thing is a **scene**. Saying "in preset X, apply look Y to segment Z" would give
"preset" two meanings, which is the mistake this app has already made three times and undone three
times (aim-the-other-way against uplights, output reversal against run direction, "The house" as
both a tab and a panel).

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

Scenes are keyed by `Segment.Id`, which is a stable identifier that survives renaming, re-ordering
and length corrections. No LED indices appear anywhere in a look or a scene; geometry is resolved
from the layout at the moment it is applied. That property already exists and is already tested, and
it is the reason this app's saved appearances do not rot the way WLED presets do.

### The cost of references

Editing a named look reaches backwards into every scene using it. That is the entire point —
"Under stairs warm white" is defined once, and when it turns out too orange you fix it in one place
and Stairs white, Twinkle both and Winter both all follow. It is also the one thing people are
surprised by, so the editor has to say **"used in 3 scenes"** while you are changing it.

## What already exists

More than it sounds:

- `Look` and `SegmentLook` in `src/LedBalloon.Core/Layout/Look.cs`
- `LookResolver.Resolve` (scene → per-controller `WledState`), `.Capture` (current state → scene),
  `.ResolveGeometry` (the segment-bounds push that Save already uses)
- `LedBalloonProject.Looks`, stored under the `"looks"` JSON key, sliced per controller and merged
  back on load, so it syncs like the rest of the layout
- Five tests covering resolve, capture, no-indices, whole-house span, and unlisted-segments-off

What is missing is mostly naming, references and UI — not the hard part.

## What changes

1. Today's `Look` becomes **`Scene`**. Today's `SegmentLook` becomes **`Look`**, gains `Id` and
   `Name`, and becomes storable in its own right.
2. `LedBalloonProject` gains `Scenes`; `Looks` comes to mean the named per-segment looks.
3. A scene's segment entry becomes a reference-or-inline union.

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
this holds before the change lands: if a look gets made in the meantime, the rule is that a `"looks"`
entry carrying a `segments` dictionary is an old whole-house look and belongs in `scenes`.

## Publishing a scene to the controllers

**Presets do not go away, and cannot.** The timers run on the controllers with no PC involved —
South runs Bpm at 17:30 and Off at 23:30, North runs Stairs white at 23:30 and All off at sunrise.
A scene is resolved by the app, so a scene alone can never fire at 23:30.

So LedBalloon **publishes** a scene: it writes a preset on each controller the scene touches,
derived from the scene's own per-segment truth. Same file-level write as today
(`PresetCopier.StoreAsync`, which already backs up `presets.json` first), but the input is the scene
rather than another controller's preset. There is no pairing step, so there is nothing to get wrong.
Garage cannot inherit Porchline because nothing ever asks what Porchline is doing.

Two things publishing has to get right:

- **Slot stability.** The schedule recalls presets by *slot number*. `ChooseSlot` already reuses a
  slot holding a preset of the same name, so republishing is stable as long as the name is. This
  needs a test before anything writes to real hardware; a scene that moved slots would silently
  repoint a timer.
- **Custom palettes travel with it.** A look storing `palette: 254` is meaningless on a controller
  without that palette file. Publishing must drive `CustomPaletteCopier`, and remap the id to
  whatever slot it lands in on the target. Both halves of this already exist and both have now run
  against real hardware — South carries a `palette0.json` because of it.

## Gaps to close

- **`SegmentLook` has no custom sliders.** It carries on, brightness, three colours, effect,
  palette, speed and intensity — but not `c1`/`c2`/`c3`, which `WledSegment` and `PresetCopier` do
  carry. Some effects use them. Capturing a scene today would silently drop them.
- **The README calls the whole-house thing a "look"**, in the "Why this exists" section and again
  under Layout. It needs to say scene.

## Migration for what is already on the controllers

South currently holds three presets written by the position-pairing copier — `July4th`,
`Twinkle both` and `Stairs white` — all wrong in the same way. `presets.bak.json` on South holds the
file as it was before the copy that produced it.

None of them is scheduled: South's timers fire `Bpm` and `Off`, in slots 1 and 2, which the copier
never touched. So this is not urgent, and it only misbehaves if one of the three is picked by hand.

Options, in order of how much they throw away:

1. Leave them. They are wrong but harmless, and they will be replaced the first time a scene is
   published under the same name.
2. Delete the three. South keeps only presets it made itself.
3. Restore `presets.bak.json`. Reverts everything since that copy, not just the three — read it
   first and show what is in it before choosing this.

## Open decisions

- **One name across controllers?** If a published scene uses the same preset name on every
  controller, one schedule entry name means the same thing on both boxes. Probably yes, but it
  constrains `ChooseSlot`.
- **Do scenes replace the preset list in the UI, or sit beside it?** Replacing is cleaner. Sitting
  beside it keeps the WLED presets already made by hand visible where they are expected.
- Which of the three migration options above.

## Non-goals

- Changing what a WLED preset is, or how the controllers recall one.
- Storing anything on the PC. Scenes and looks live in the project, which lives on the controllers.
- Keeping cross-controller preset copying as a user-facing idea. Publishing a scene replaces it.
