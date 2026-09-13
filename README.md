# MeddlingIdiot.Greenlight.LavaLampClient

A lava lamp sitting on your desktop that turns the colour of your pipeline.

A runnable reference consumer of the [Greenlight](https://github.com/meddlingidiot/MeddlingIdiot.Greenlight)
SDK, and a demonstration of how little an app needs to do to use it. The wax runs **green**
while everything passes and **amber** while a pull request wants you. When a build breaks it
turns **red** and the lamp goes over to a rolling boil — hotter bulb, faster wax — and stays
there for as long as the pipeline is broken. While a build is running, all of it breathes. With
no Greenlight on the machine at all the lamp is simply switched off: dark glass, cold grey wax
pooled in the bottom, no light. Green wax on a four-minute-old snapshot would be lying to you.

It warms up, too. Wax that was already climbing the instant the lamp came on would not read as
a lava lamp at all, so it takes a few seconds to get going and settles back down when Greenlight
goes away.

The point of it is what it does **not** have. No Azure DevOps client, no GitHub client, no
token, no polling loop — everything it knows arrives through the SDK, from the Greenlight
already running on the machine. Strip out the drawing and the tray icon and the integration is
about twenty lines, all of them in [`App.cs`](Greenlight.LavaLampClient/App.cs).

## Running it

```bash
dotnet run --project Greenlight.LavaLampClient
```

Windows only: the click-through overlay and the work-area maths are Win32. The SDK itself is
not — it is plain .NET, and the same twenty lines work anywhere.

## Moving it about

The lamp ignores the mouse the rest of the time — a window you cannot click is a window that
never steals your caret — so moving it is a mode you turn on from the tray: **Move and resize
it…**. While it is on, the overlay answers the mouse and the lamp grows a dashed frame, four
corner handles and two buttons:

- **Drag the middle** to carry it anywhere on the desktop. It is clamped so the whole of it
  stays on screen; the arrow keys nudge it a pixel at a time, Shift ten.
- **Drag a corner** to resize. The opposite corner stays where it is and the lamp keeps its
  proportions — a bottle stretched to whatever shape the mouse was making stops being a lava
  lamp somewhere around the second pixel. The wheel over the lamp does the same thing about its
  middle.
- **✓** keeps the arrangement and writes it to the file. So do Enter, a click on bare desktop,
  and turning the mode off from the tray.
- **✕** puts it back exactly where it was when you turned the mode on. So does Escape.

Nothing is written to disk until the mode ends, so dragging the lamp across the desk costs one
write rather than a few hundred. The lamp is shown warmed up for the duration of the mode, so
nobody has to wait for the wax to come up before they can see what they are placing.

## The tray

Everything else lives on the mascot in the notification area:

- **Lamp on the desktop** — take it away and bring it back. Clicking the icon does the same.
- **Move and resize it…** — the mode above.
- **How big**, **How much glow**, **How solid** — the ordinary sizes, and how much light the
  bottle throws onto the wallpaper.
- **How lively** — how fast the wax runs. The only setting here that means nothing at all about
  the build: the pace that reads as calm to one person reads as stalled to the next.
- **Where it may stand** — the work area, or the whole screen including the taskbar.
- **Dark panel behind it** — the panel the lamp stands against. On by default: a glowing bottle
  laid straight over a photograph is unreadable, and the panel is what stops the wallpaper
  deciding whether the build is passing.
- **Leave it switched off when Greenlight is away** — an unlit lamp rather than nothing at all.
- **Start with Windows** — read from the registry every time it is shown, so it agrees with
  Task Manager's Startup tab rather than with what we last wrote there.
- **Edit the colours…** — opens `lavalamp.json`. **Reload the file** picks up hand edits without
  a restart.

Every setting is written straight back to the file, so the menu and the JSON are never two
different sets of settings.

## The file

`%AppData%\Greenlight.LavaLamp\lavalamp.json`, written with the defaults on first run. Each
state gets two colours — the wax, and the light the bottle throws — because a glow is not simply
the wax at a lower alpha:

```json
{
  "Lamp": { "AnchorX": 0.93, "AnchorY": 0.62, "Size": 0.34 },
  "WhenGreen": { "Wax": "#4BFF86", "Glow": "#14B546" },
  "WhenAmber": { "Wax": "#FFCE42", "Glow": "#E08C0C" },
  "WhenRed":   { "Wax": "#FF4E3C", "Glow": "#C11A0C" }
}
```

The fluid the wax floats in is not a setting: it is the glow colour taken down almost to black,
so a hand-picked pair of colours cannot accidentally produce a bottle the wax is invisible
inside. A file that cannot be parsed falls back to the defaults rather than refusing to start.

## How it is put together

| | |
|---|---|
| [`App.cs`](Greenlight.LavaLampClient/App.cs) | The whole Greenlight integration, and what to do when edit mode ends |
| [`LampScene.cs`](Greenlight.LavaLampClient/LampScene.cs) | Where the lamp is, how hot it has got, what the mouse is over. No Avalonia, so it is testable |
| [`LavaBlob.cs`](Greenlight.LavaLampClient/LavaBlob.cs) | One blob of wax: its own slow cycle, and where that puts it inside a bottle that tapers |
| [`LavaCanvas.cs`](Greenlight.LavaLampClient/LavaCanvas.cs) | The drawing: the glass, the metal, the wax and the edit chrome |
| [`LampWindow.cs`](Greenlight.LavaLampClient/LampWindow.cs) | The click-through overlay, and the mouse and keyboard in edit mode |
| [`LampTray.cs`](Greenlight.LavaLampClient/LampTray.cs) | The tray icon and its menu |
| [`ClickThroughNative.cs`](Greenlight.LavaLampClient/ClickThroughNative.cs) | The four window styles that make it furniture, and the one edit mode takes back |

The wax is not a fluid simulation, and deliberately not: it is seven blobs, each on its own
unhurried cycle, drawn as soft-edged shells that run together where they overlap. That is one
number per blob, it never goes wrong, and at seven blobs it is indistinguishable from the
expensive version.

The scene is free of Avalonia so the warm-up, the boil, the clamping and the edit-mode geometry
can be tested without a window — and so the one thing that would make the whole toy look broken,
a blob drifting out through the side of a bottle that tapers, is a test rather than something
you would have to watch a lamp for twenty minutes to catch.

```bash
dotnet test
```
