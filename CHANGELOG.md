# Changelog

All notable changes to this project are documented here.

## [Unreleased]

### Added

- First cut: one lava lamp on the desktop, lit from the Greenlight running on the machine. Green
  wax while everything passes, amber while a pull request wants you, and a switched-off lamp -
  dark glass, cold wax on the floor of the bottle - when there is no Greenlight to ask.
- A rolling boil for red: a hotter bulb and faster wax, eased in over a couple of seconds and
  eased back out when the pipeline is fixed. The colour is the news; the boil is what the lamp
  goes on doing for as long as it is broken.
- A warm-up. The wax takes a few seconds to leave the bottom of the bottle when the lamp comes
  on, and settles back down when Greenlight goes away - a lamp whose wax was already climbing
  the instant it was switched on would not read as a lava lamp at all.
- A build pulse. Everything drawn breathes while a build is running, mostly in the glow rather
  than in the wax's own brightness, because "dimming" is uncomfortably close to "going out" and
  that already means something else here.
- Seven blobs of wax, each on its own cycle, each clamped to the inside of a bottle that tapers
  - so a big blob stops short of the neck, which is what real wax does.
- Edit mode: drag the lamp anywhere, drag a corner or roll the wheel to resize it at its own
  proportions, and keep it with the tick or put it back with the cross. Enter, Escape, the arrow
  keys and a click on bare desktop all do what they look like they should. The file is written
  once, when the mode ends.
- A tray menu for everything the running overlay can absorb - size, glow, opacity, how fast the
  wax runs, which part of the screen it may stand on, the panel behind it - each written
  straight back to `lavalamp.json`.
- "Start with Windows" in the tray menu, registering the stable shim beside the install rather
  than the versioned copy an update would move.
