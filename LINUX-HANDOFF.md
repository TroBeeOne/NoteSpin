# Linux handoff: Hide the window frame

*Settings → Window → Hide the window frame* is done for Windows (1.5.0) and not yet for Linux. The page side is
shared and finished; Linux needs only the window side in `src/linux/`. Until then the Linux build works as before,
without the setting. Delete this file once Linux has it.

## What it does (README: "Hide the window frame")

No title bar or border, clear around the cards (see-through with no tint, and no shadow on the desktop). The panel
and a title strip above the panel and cards (the "toolbar") can be hidden. With it hidden, NoteSpin is just the
cards' box, with a small button at its top-left corner: a click brings the toolbar back, a drag moves the window.
With the toolbar out, the strip moves the window, the edges resize it, and its buttons hide the toolbar, minimize
and close. Only the visible part takes clicks; around it, clicks reach what's behind. It can't be maximized.

## How the page and the window share the work

The page always keeps room for the toolbar: the cards' box starts `--pwmax` (264) CSS px from the left and
`--strip` (34) from the top (`body.frameless` in `web/css/app.css`). The window covers that room too, but only the
part in use is kept:

- toolbar hidden: from (264, 34) to the bottom-right corner (the cards' box);
- toolbar out: from (264 − panel width, 0), so (0, 0) with the panel unfolded and (202, 0) folded.

So the toolbar comes and goes **without the window changing size** (resizing a web view from its left or top
edge makes it jump). On Windows, `MainForm` sets a window region; on Linux the same is an input shape
(`gdk_window_input_shape_combine_region`), and drawing needs no clipping since the page is transparent there.

## What the page calls (all in `src/HostApi.cs`, under `#if !LINUX` for now)

| Call | When | The window side |
|---|---|---|
| `FrameInfo()` | **synchronously** as the page starts | `{hidden}`: whether the window started frameless. Answering null (as the Linux `host.js` `callSync` does now for unknown names) hides the setting. |
| `FrameSet(hidden, cardsLeft, cardsTop, shownLeft, shownTop, fitWidth)` | turning it on or off; showing or hiding the toolbar; folding the panel; after startup | CSS px. Hidden: no frame, window = cards' box grown by (cardsLeft, cardsTop) to the left and up, input shape = from (shownLeft, shownTop). Keep the **cards' box where it is on screen** each time. `fitWidth` > 0: narrow the cards' box to that width around its middle (when it's wider). Not hidden: frame back so the panel and cards keep their place (the new client area = the old window less the strip and from `shownLeft`, which the page sends as the panel's left). Returns whether it's hidden. |
| `FrameDrag(mouse)` | pointer down on the strip, or the small button | Move the window with the pointer (`gtk_window_begin_move_drag`). Returns **whether it moved**: the small button opens the toolbar when it didn't. |
| `FrameSize(edge, mouse)` | pointer down on an edge zone (toolbar out) | `edge` is `left`, `right`, `top`, `bottom`, `topleft`, `topright`, `bottomleft`, `bottomright`: `gtk_window_begin_resize_drag` with the matching `GdkWindowEdge`. |
| `FrameCommand(what)` | the strip's buttons | `"minimize"` (`gtk_window_iconify`) or `"close"` (close as the window's X does, after the page has saved). |

Windows reference: the "window frame hidden" section of `src/MainForm.cs` (`SetFrame`, `PlaceAround`,
`ShowFrame`, `DragWindow`, `SizeWindow`, `WindowCommand`) and `RestoreFloat`/`SavePlacement`.

## What the Linux side needs

1. `src/linux/MainForm.cs`: `Frameless`, `SetFrame`, `DragWindow`, `SizeWindow`, `WindowCommand`, as above;
   `gtk_window_set_decorated(win, 0/1)` at runtime; the input shape on every resize.
2. At startup, read `pref:hideFrame` (`"1"`) before showing the window, start undecorated and see-through, and
   navigate to `index.html?frameless` (an inline script in `index.html` makes the first paint clear).
3. Placement with the frame hidden: `app:float` = `x,y,width,height,cardsLeft,cardsTop,shownLeft,shownTop`
   (window in px, the rest in CSS px, invariant culture). `app:window` stays the framed window's place.
4. `src/HostApi.cs`: drop the `#if !LINUX` around the Frame methods and add them to `TryDispatch`
   (`FrameInfo`, `FrameSet` with 5 doubles after the bool, `FrameDrag`, `FrameSize`, `FrameCommand`).
5. `web/js/host.js` (Linux part): `callSync('FrameInfo')` must answer without IPC, e.g. from a value injected at
   document start like `__nsInitPrefs` (`window.__nsFrameInfo = {hidden: …}`).
6. `src/EdgeSizer.cs` is Windows-only (excluded in `NoteSpin.csproj`); GTK's resize drag does the same job.
7. README: remove the "isn't in the Linux build yet" line in the Linux section.

## Wayland

An app can't move its own window or know where it is there. So: `FrameDrag`/`FrameSize` via begin_move/
resize_drag are fine; the Windows "move onto the screen to make room for the toolbar" step (`PlaceAround`'s nudge)
can't happen, so skip it; keeping the cards' box in place while the window *size* changes isn't needed, since the
toolbar doesn't change the size. Saved positions in `app:float` can't be applied on Wayland; sizes can. X11
(`GDK_BACKEND=x11`) can do everything Windows does.

## Checking it

The page is the same as on Windows, so `tools/cdp.mjs`-style checks of the page apply. On the window side: the
frame goes and comes back with the panel and cards staying put; with the toolbar hidden, clicks just left of and
above the cards' box reach the window behind; the small button opens the toolbar on a click and moves the window
on a drag; reopening NoteSpin brings it back the same way.
