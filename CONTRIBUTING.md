# Contributing

Patches welcome. A few things worth knowing before you spend an evening on one.

## Licence

LedBalloon is **GPL-3.0**. By opening a pull request you agree your contribution
is offered under that licence — inbound matches outbound, no separate agreement
to sign.

It is worth saying why, since the obvious choice for a hobby app is MIT: this
project reproduces WLED's lighting effects so it can show you on a photograph
what your house is about to do, and WLED is EUPL-1.2. `NOTICE` sets out which
parts of the tree that touches.

One consequence to be aware of: once a change of yours is merged, the licence
can no longer be changed without your agreement. That is the point of it.

## Measurement beats reading

This is the house rule and it is not a slogan. Twice, the table of which
controls each effect reads was built by reading code, and twice it was wrong —
the second time for 71 of 118 effects. Static reading cannot tell which branch a
caller takes, cannot follow a colour through a helper that uses it on one path
and drops it on the other, and missed an entire function that quietly becomes
the palette the moment one is selected.

So: if you are claiming something about what a controller does, drive it and
look. `WledAudioSync` will even play a controller invented music, so the
sound-reactive effects can be measured on hardware that has no microphone.

If you cannot measure it, say so in the comment rather than writing the claim
as though you had.

## Hardware

Development is against two Gledopto GL-C-616WL controllers on WLED 0.15.3, but
nothing in the code is allowed to assume that. Every list, limit and capability
is read from the device: effect names, palette counts, segment maximums,
filesystem space. Two controllers in one house can run different firmware, and
the app has to be right about both.

If you only have one controller, that is fine — most of the tests run against a
stand-in (`FakeController`) that serves the same endpoints.

## How a change gets in

Anyone may open an issue or a pull request. You do not need to be invited, and
you do not need to ask first — though for anything large, an issue describing
the house you are trying to light will usually save you an evening.

Every change reaches `main` through a pull request, including the maintainer's.
A pull request needs one approving review before it can be merged, and today
that review comes from Chris Cooper. If the project outgrows one person, that
list will grow and this paragraph will say so.

What a review is looking for, in roughly this order:

1. **Does it work, and how do you know?** See below. This is the one that gets
   changes sent back.
2. **Does it keep working?** `dotnet test` passes, and new behaviour has a test
   that would fail without it.
3. **Will the next person understand why?** Comments say what was measured and
   what surprised you, not what the code already says.
4. **Does it fit the house?** Nothing may assume two Gledopto controllers, or
   any particular firmware, or a house shaped like this one.

Build and tests run automatically on every pull request — including a check that
the vendored WLED and FastLED sources still match the upstream tags they claim to
come from, since the licence rests on that being true.

A change that is right but arrives without a test, or without a word about how it
was verified, is not refused. It is asked about, which is slower for everybody,
so it is worth getting in first.

## Tests

    dotnet test

Everything should pass before and after your change. Tests here are named as
sentences and carry the reason the behaviour exists, because a test that only
says *what* breaks tells the next person nothing about whether they are allowed
to change it.

## Comments

Write down what you measured and what surprised you, not what the code says. A
comment explaining that `SegmentPalettes.Clear()` clears the palettes is noise;
a comment explaining that clearing it mid-rebuild is what made the dropdown
revert a beat after it was set is the reason the next person does not reintroduce
the bug.
