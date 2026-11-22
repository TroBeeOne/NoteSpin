# NoteSpin

Sticky notes on a spinning 3D card wheel, for Windows and Linux. Each note is a pastel card in the colour of its
category; turn the wheel to flip through them, click the front card to open it.

On Windows, nothing needs installing: copy the `NoteSpin` folder anywhere and run `NoteSpin.exe`. It runs on
.NET Framework 4.8 and the Microsoft Edge WebView2 Runtime, both part of Windows 10 and 11. On
[Linux](#linux), extract the tarball and run `./NoteSpin` (uses the .NET 10 runtime and system GTK3 /
WebKit2GTK-4.1 / SQLite). It can also be made [portable](#portable-notespin-on-a-usb-drive), keeping your notes
in its own folder so it runs from a USB drive on any PC. Notebook files (`.notespin`) are standard SQLite
databases and work identically on both Windows and Linux.

- [Getting started](#getting-started)
- [The wheel](#the-wheel)
- [Writing notes](#writing-notes)
- [Notes in their own windows](#notes-in-their-own-windows)
- [Categories](#categories)
- [Show, order and search](#show-order-and-search)
- [Locking a note](#locking-a-note)
- [Notebooks, folders and backups](#notebooks-folders-and-backups)
- [Portable: NoteSpin on a USB drive](#portable-notespin-on-a-usb-drive)
- [Copying notes from Sticky Notes](#copying-notes-from-sticky-notes)
- [Linux](#linux)
- [Keyboard, mouse and touch](#keyboard-mouse-and-touch)
- [Questions](#questions)
- [Building and changing NoteSpin](#building-and-changing-notespin)

---

## Getting started

1. Run `NoteSpin.exe`. The first time, it makes a notebook called **My Notes** with a few example notes.
2. Turn the wheel with the **mouse wheel** (one notch moves one card), or click **Previous** / **Next** in
   the panel on the left.
3. **Click the front card** to open the note. Type; it saves as you go. Press **Esc** (or click outside the
   note) to close it.
4. **New note** (Ctrl+N) starts a blank one. The **first line is its title**.

Windows may say "Windows protected your PC" the first time if the folder was downloaded or emailed. Release
downloads are signed, but SmartScreen also waits until a new app has been run by enough people. Choose
*More info → Run anyway*; *More info* shows the publisher. A copy you build yourself isn't signed.

## The wheel

The notes stand on a drum between two side frames. Turning the drum brings the next card up from behind;
as a card passes the top it tips over and falls forward.

What you see:

| | |
|---|---|
| **The front card** | The note you're on: its title, and the start of the note with its lists, checkboxes and bold, italic, underlined or struck-through words. Hebrew, Arabic and other right-to-left lines start from the right, as in the note. If there's more than fits, it fades out at the bottom. A locked note shows only its title and a lock. |
| **The band across the top** | The note's category, in that category's colour, and (quieter) when the note was last edited: *Today*, *Yesterday*, *Nov 12*, *Nov 12, 2025*. |
| **The tab on the right** | In A–Z order, the first letter of the title (so you can see where you are in the alphabet). In Recent order there's no tab; the date on the band says it. On category cards, the number of notes. |
| **The cards behind** | The next three notes: the two right behind show their band and the first line of their title, the third just its band. Click one to bring it to the front. (In the Categories view only the bands show: they carry the category's name already.) |
| **The card in front, lying down** | The note before. Its writing shows faintly through the paper, reversed. |
| **The knobs** | They turn with the drum and take on the colour of the card at the front. |

Moving around:

- **Mouse wheel**: one notch, one card. Spin it quickly to go further.
- **Drag up or down** on the wheel (or swipe on a touch screen) to turn it; flick to let it spin, then press
  on it to stop it.
- **Previous / Next** and the **arrow keys** always move exactly one card.
- **Click a card behind** the front one to bring it forward. **Click the front card** to open it.
- **Drag sideways** to look at the wheel from another side; the **straighten** button (bottom of the panel)
  turns the view back. (Not with the spinner base hidden: see below.)
- The wheel goes round: after the last note comes the first again. With fewer than ten notes there's a gap
  between the last and the first, so you can tell where the end is: turning past it, the front is empty for a
  moment. (The cards behind the last note are the first ones, standing just as the others do.)

**Right-click a card** for its menu (on a touch screen, press and hold it; from the keyboard, the Menu key or
Shift+F10 for the front card). A card behind the front one comes forward first.

| | |
|---|---|
| **Open** | Opens the note (for a note that's out in its own window: brings that window forward). |
| **Open in its own window** | Opens it in a window of its own, whatever the setting. |
| **Copy text** | The note's text, to paste anywhere: a line per line, list items marked •, 1., ☐ or ☑ and indented by level. Not for a locked note. |
| **Copy as picture** | A picture of the card, as on the wheel, to paste into a message, document or image editor. |
| **Delete…** | Deletes the note, after asking, with **Undo** in the message that follows. A note that's out in its own window is closed there first (saved). |

On a category card the menu has *Open* and *Copy as picture*.

The **sound** button turns the click of falling cards on and off. The arrow at the top of the panel folds it
down to a strip of icons (it folds by itself in a narrow window). Folded, one button under the search switches
between **All notes** and **Categories**: its icon shows which is on, a stack of cards or a grid. **Ctrl+G** does
the same from the keyboard, with the panel folded or the toolbar hidden too. In a window too short for the whole
strip, it scrolls. Everything sizes to the window.

**See-through background.** *Settings → Background → See-through background around the cards* lets the
desktop, and any window behind NoteSpin, show through the space around the cards. The **Opacity** slider sets
how much of the dark background stays over it (80% to start: mostly dark, the desktop faintly behind); while
the slider is held, the Settings dialog's shading lifts so you can see the result. The panel, the cards and
open notes stay solid, and the cards still cast their shadows. Off, the background is solid as before.

**Hide the spinner base.** *Settings → Spinner → Hide the spinner base* shows just the cards, without the
frame, knobs, axle and stand (or their shadows). The cards turn, lean and fall exactly as before, and the view
is framed around them alone, so the window can be made much narrower (down to about 340 pixels), or as wide as
you like. Dragging sideways no longer turns the view, and the straighten button goes. It works with the
see-through background too.

**Hide the window frame** (Windows). *Settings → Window → Hide the window frame* takes away the title bar and
border, and makes the space around the cards completely clear, so the wheel seems to stand on the desktop. When
it's turned on, the window's sides draw in to the cards (they stay the same size). The panel stays, with a
dark strip above it and the cards in place of the title bar: drag the strip to move NoteSpin and the edges to
resize it. Its buttons minimize, close, and (the first one, a little window with a side panel) hide the
"toolbar", meaning the panel and the strip. With the toolbar hidden, NoteSpin is just the cards in their box.
Click the small button at the box's top-left corner to bring the toolbar back, or drag that button to move
NoteSpin. The toolbar comes and goes without the cards moving, unless they're close to the left or top of
the screen: then they move over to make room, and go back when it hides. Only the box takes clicks; around it,
clicks reach the desktop. The stand's shadow on the desktop is left out in this mode, as the edge of the box
would cut it off. Notes over the cards, note windows, and everything else work as usual. It's still a normal
window: on the taskbar, in Alt+Tab, and minimized by Win+D. It can't be maximized. Turn the setting off and the
frame comes back around the panel and cards.

## Writing notes

- **The first line is the title.** It's what the card shows in large type, and what A–Z order sorts by.
- **Bold, italic, underline and strikethrough**, from the first four toolbar buttons or **Ctrl+B**, **Ctrl+I**,
  **Ctrl+U** and **Ctrl+T** (as in Microsoft Sticky Notes). Select words first, or press one and type. They're
  kept with the note and show on its card. Otherwise there's one font and size, so every note looks the same.
- **Text size.** To make the text in notes bigger or smaller, press **Ctrl+Plus** or **Ctrl+Minus** in a note
  (or hold Ctrl and turn the mouse wheel over it), or use *Settings → Notes → Text size*: from 70% to 200%.
  **Ctrl+0** sets it back to 100%. It's one size for every note, in every window, and NoteSpin remembers it.
  It's how you see the notes, not part of them, so the cards, copied text and backups stay as they were.
- **Right-to-left text.** A line in Hebrew, Arabic or another right-to-left script reads from the right by itself,
  with its bullet, number or checkbox on the right; the next line goes back to the left if it's in English, so
  notes can mix both. To set a line's direction yourself on Windows, press **Ctrl+Right Shift** (right to left)
  or **Ctrl+Left Shift** (left to right) in it; NoteSpin keeps that too, and a new line started from it carries on
  the same way.
- **Lists.** There are three kinds, from the toolbar or by typing at the start of a line:

  | List | Toolbar | Or type |
  |---|---|---|
  | Bullets | • list | `- ` or `* ` |
  | Numbers | 1. list | `1. ` |
  | Checkboxes | ☑ list | `[] ` (or `[x] ` for one already ticked) |

  - **Tab** moves a list item in, under the one above it; **Shift+Tab** moves it out. The two arrow buttons
    in the toolbar do the same (they're for list items, so they're greyed out on ordinary lines).
  - **Enter** on an empty item moves it out a level; at the top level it ends the list.
  - **Backspace** at the start of an item moves it out (a top-level item becomes an ordinary line).
  - **Click a checkbox** to tick it, or press **Ctrl+Enter** on the item.
  - Pressing the same list button again turns the list back into ordinary lines; another list button
    changes its kind.
- **Pasting** keeps the text, its lines, its lists, and bold, italic, underline and strikethrough, and drops
  fonts, sizes, colours, links and pictures.
- **Text files**: drop a `.txt` file (or several) from File Explorer onto the cards, and NoteSpin asks whether
  to make a note of each. The first line becomes the title and every line a paragraph, spaces and tabs kept;
  the notes go in General (change the category in the note). One note opens straight away. Files saved as
  UTF-8, UTF-16 or the older "ANSI" all read correctly. A file over 1 MB, an empty one, or one that isn't
  plain text is left out, saying why. Dropping does nothing while a note is open over the cards.
- **Saving** happens as you type (the band says *Saved*). Closing the window saves anything still waiting.
- **Deleting**: the bin button in a note, or *Delete…* in its card's menu (right-click the card; with **Undo**).
  A note closed with nothing in it is deleted automatically, with **Undo** in the message that appears.

## Notes in their own windows

A note can leave the wheel and sit on the desktop in a small window of its own, like a sticky note: handy for
a list you're working through, even with NoteSpin minimized. Any number of notes can be out at once, each in
its own window with its own taskbar button.

- **Drag it out.** Open a note, grab its top strip (the coloured band, away from the category box and the
  buttons) and drag it past the edge of the NoteSpin window. It becomes a window that follows the mouse; let
  go where you want it. Or click the **pop-out button** in the band.
- **Or always.** *Settings → Notes → Always open notes in their own window* opens every note that way, each
  in a window of its own.
- **A note is open in one place at a time.** Clicking the card of a note that's out brings its window to the
  front instead of opening it twice. Other cards open over the wheel as usual while notes are out.
- **The window.** Move it by its top strip and resize it from its edges (the thin coloured border around the
  note); the **pin** keeps it on top of other windows. Each note remembers its own place, size and pin, and
  comes back there next time it's out. A note that hasn't been out before appears a step down and to the right
  of the last one, so they don't stack up. Esc or × closes the note and the window goes away (an emptied note is
  deleted as usual, with Undo in the NoteSpin window). **Ctrl+N** in it starts a new note in another window.
- **Minimize NoteSpin** and the notes stay on the desktop. **Closing NoteSpin** closes them too, and they open
  in their windows again next time NoteSpin starts (except locked ones).
- **Putting one back.** The **put-back button** in its band, or drag the window over the NoteSpin window and
  drop it on the outline that appears around the front card. Only that outline takes it back, so a note can
  also sit on top of the NoteSpin window. If a note is open over the wheel, it's saved and closed to make way.
- Cards on the wheel catch up as you type. Switching notebooks closes every note window first (saving them),
  as they belong to the notebook that was open. A locked note moves between windows without asking for its
  password again, and locks again when it's closed.

## Categories

Every note has a category, shown by its colour. Type a category in the box at the top of a note, or pick one
from the list that drops down. Leave it blank for **General**. A new name makes a new category with its own
pastel colour.

The **tag button** at the bottom of the panel lists the categories: change a colour (click the swatch),
rename one, delete one (its notes move to General), or add one. General can be recoloured but not renamed or
deleted.

## Show, order and search

- **Show: All notes** puts every note on the wheel. **Show: Categories** puts one card per category on it,
  listing the notes in it; open one (click it, or Enter) to see only that category's notes. **Esc**,
  **Backspace** or the *Categories* link in the panel goes back. **Ctrl+G** switches between all notes and the
  categories (from one category's notes, to all notes).
- **Order: A–Z** sorts by title; **Order: Recent** puts the most recently edited first. In the categories
  view it sorts the categories the same way.
- **Search** (Ctrl+F, or the box in the panel) narrows the wheel to the notes containing every word you type,
  in their title, text or category. The words are highlighted in a note you open. **Esc** clears the search.
  A locked note is found by its title and category only.

## Locking a note

The **lock button** in a note protects it with a password. Any password works, even a short one.

- The **title (first line) and the category stay readable**; everything else is encrypted.
- While it's open you can edit it as usual. It **locks again when you close it**; opening it asks for the
  password each time.
- **There's no way back in without the password.** But a locked note can still be **deleted**: *Delete note…*
  in its password prompt (with Undo).
- The lock button on an open locked note removes the lock.
- Locked notes stay locked in backups, and open on any PC with their password.

How it works: the key comes from the password with PBKDF2 (SHA-256, 150,000 rounds, random 16-byte salt) and
the note is encrypted with AES-256-GCM (random 12-byte IV); what's stored is `base64(salt | iv | ciphertext)`,
the same format as the stand-alone encrypt page, which can also decrypt it. Only the encrypted text is ever
written, and the earlier unencrypted text is wiped from the file. A short password is easy to guess for
anyone who has the file: this keeps a note from casual reading, it isn't a safe.

## Notebooks, folders and backups

**Notebooks.** Each notebook is one file, `<name>.notespin`. The notebook button at the top of the panel
switches notebooks and has *New notebook*, *Rename*, *Delete* (to the Recycle Bin), *Export a backup*,
*Import a backup* and *Open the notebooks folder*. The window's title bar shows the open notebook's name and
the path of its file.

**The notebooks folder** is `Documents\NoteSpin` unless you choose another. When OneDrive backs up your
Documents folder, as it does on most Windows PCs, your notebooks are backed up to the cloud with it.
*Settings → Notebooks folder* shows where they are and whether it's in OneDrive; **Change…** picks another
folder and either **moves your notebooks** there or **just switches** to the ones already there. If the
chosen folder isn't available (an unplugged drive), NoteSpin says so and uses `Documents\NoteSpin` until it's
back.

> Use NoteSpin on **one PC at a time** with a synced folder. If a notebook is changed in two places before
> OneDrive catches up, OneDrive keeps both copies (one renamed with the PC's name) instead of merging them.

**Backups.**

- **Export a backup** saves a complete copy of the open notebook as a `.notespin` file wherever you choose.
- **Import a backup** (or drop a `.notespin` file on the window, or double-click one; see below) offers:
  - **Add as a new notebook**: the backup becomes a notebook of its own.
  - **Replace** the open notebook's notes with the backup's. First, a copy of the notebook as it was goes to
    `Safety Backups` in the notebooks folder (the newest 30 are kept); *Settings → Safety Backups* opens it.
- *Settings → Opening backups → Turn on* makes `.notespin` files open in NoteSpin when double-clicked
  (for this copy of NoteSpin; turning it off removes the setting completely).

A `.notespin` file is an ordinary SQLite database, so any SQLite tool can open it. NoteSpin recognises its own
files by a stamp inside them, whatever they're called, and checks a file before importing it. A notebook is
always a single file (nothing beside it), so it can be copied while NoteSpin is closed.

## Portable: NoteSpin on a USB drive

NoteSpin can keep everything it saves in its own folder instead of on the PC, so the whole NoteSpin folder can go
on a USB drive and run on any PC, with your notes.

- **Turning it on.** *Settings → Portable → Make portable…* asks whether to **copy your notebooks** there or
  **start empty**, then restarts NoteSpin. From then on its settings, its notebooks (in
  `NoteSpin Data\Notebooks`) and its web view's cache are in a `NoteSpin Data` folder beside `NoteSpin.exe`.
  The notebooks and settings on the PC stay as they were. Or download `NoteSpin-<version>-portable.zip`, which
  comes with an empty `NoteSpin Data` folder: unzip it onto the drive and it starts portable. (An empty folder
  named `NoteSpin Data` made beside `NoteSpin.exe` does the same.)
- **Using it.** Copy the whole NoteSpin folder, `NoteSpin Data` and all, to a USB drive and run `NoteSpin.exe`
  from there. The drive's letter can change from PC to PC: NoteSpin keeps the paths on its own drive relative to
  its folder. The PC needs the WebView2 Runtime, which comes with Windows 11 (and most Windows 10 PCs).
  **Close NoteSpin before unplugging the drive**, and keep backups somewhere else too: a USB drive is easy to
  lose. *Opening backups* (double-clicking `.notespin` files) isn't offered while NoteSpin is portable, since it
  would point at one drive letter.
- **Turning it off.** *Settings → Portable → Keep everything on this PC…* restarts NoteSpin with its settings
  and notebooks on the PC again:
  - **Copy them to this PC**: the notebooks go to this PC's notebooks folder (normally `Documents\NoteSpin`),
    and NoteSpin's settings come along. A notebook already there is never replaced: one with the same name comes
    in as *Name (2)*. The PC's own settings as they were are kept as `settings.db.bak`.
  - **Leave them**: NoteSpin goes back to this PC's own notebooks and settings, as they were before.

  Either way `NoteSpin Data` is renamed `NoteSpin Data (off)`, with everything still in it: rename it back to
  make NoteSpin portable again, or delete it once you don't need it. Renaming or deleting `NoteSpin Data`
  yourself, while NoteSpin is closed, is the same as *Leave them*.

## Copying notes from Sticky Notes

Notes from **Microsoft Sticky Notes** (the app that comes with Windows) can be copied into the open notebook:
*Settings → Microsoft Sticky Notes → Import from Sticky Notes…*, or the same item in the notebook menu.

- A list shows every Sticky Note with text in it, last edited first: its colour, first line, a little of the
  rest, and when it was last edited. Tick the ones you want (**Select all** / **Select none**) and choose
  **Copy**.
- Each becomes a **new note** in NoteSpin, keeping the dates it had in Sticky Notes. **Sticky Notes isn't
  changed**: NoteSpin copies its files to a temporary folder, reads the copy and deletes it, so it never opens
  Sticky Notes' own files (and Sticky Notes can be running meanwhile).
- **Only the text comes across**, one line per paragraph, the first line being the title. Pictures,
  handwriting, window positions and formatting stay behind.
- **Colours become categories.** Yellow, the colour Sticky Notes starts with, goes to General (yellow here
  too); green, pink, blue, purple and the rest each get a category named after the colour (made if there isn't
  one, used if there is). Rename or recolour them afterwards as you like.
- **Running it again**: notes copied into this notebook before are marked *Copied before* and start unticked,
  so nothing is doubled by accident (tick them to copy them again).

## Linux

NoteSpin runs natively on Linux using GTK3, WebKit2GTK-4.1, and the system SQLite library (`libsqlite3`), sharing the
same web interface and `.notespin` SQLite notebook format as Windows.

- **Requirements:** The **.NET 10 Runtime** (plus GTK3, WebKit2GTK-4.1, and SQLite, which come preinstalled on Fedora
  Workstation and most desktop distros). If `.NET 10` isn't installed yet, launching `./NoteSpin` tells you the exact
  install command for your distro — for example on Fedora:
  ```bash
  sudo dnf install dotnet-runtime-10.0
  ```
- **Running:** Extract `NoteSpin-<version>-linux-x64.tar.gz` (or `-portable.tar.gz`) anywhere and run `./NoteSpin`.
- **Where files live:** Notebooks default to `~/Documents/NoteSpin` and app settings to `~/.local/share/NoteSpin` (or
  `NoteSpin Data/` beside `NoteSpin` when portable).
- **App icon & opening `.notespin` files:** Turn on *Settings → Opening backups → Turn on* to register a per-user
  `.desktop` entry, icon, and `.notespin` file association under `~/.local/share` (no `sudo` needed; turning it off
  removes them cleanly). On Wayland desktops (such as GNOME), restarting NoteSpin after turning this on gives it its
  real dock/launcher icon.
- **Wayland notes:** Pop-out note windows, moving/resizing them, the see-through background, and *Hide the window
  frame* (with click-through outside the cards) all work on Wayland. Due to Wayland security restrictions, dragging a
  note window back onto the main window to re-dock (use the put-back button instead), remembering exact window screen
  positions, and client-side always-on-top pinning aren't available natively on Wayland (on GNOME you can use
  `Alt+Space → Always on Top`, or launch under XWayland with `GDK_BACKEND=x11 ./NoteSpin`). Microsoft Sticky Notes
  import is Windows-only.

## Keyboard, mouse and touch

| Keys | |
|---|---|
| ↑ ↓ (or ← →, Page Up/Down) | Previous / next card |
| Home / End | First / last card |
| Enter or Space | Open the front card (or the category) |
| Ctrl+N | New note (in a note window: a new note in another window) |
| Ctrl+F | Search |
| Ctrl+G | All notes ↔ Categories |
| Menu key or Shift+F10 | The front card's menu (open, copy, delete) |
| Esc | Close the note · clear the search · back to the categories |
| Backspace | Back to the categories |
| *In a note:* Ctrl+Plus / Ctrl+Minus / Ctrl+0 | Bigger / smaller text / back to 100% (every note) |
| *In a note:* Ctrl+B / Ctrl+I / Ctrl+U / Ctrl+T | Bold / italic / underline / strikethrough |
| *In a note (Windows):* Ctrl+Right Shift / Ctrl+Left Shift | This line reads right to left / left to right |
| *In a note:* Tab / Shift+Tab | Move a list item in / out |
| *In a note:* Ctrl+Enter | Tick or untick a checkbox |
| *In a note:* Ctrl+S | Save now (it saves by itself anyway) |

| Mouse / touch | |
|---|---|
| Wheel | One card per notch |
| Ctrl+wheel over a note | Bigger or smaller text |
| Drag (swipe) up or down | Turn the wheel; flick to spin; press to stop |
| Drag sideways | Look at the wheel from another side (not with the spinner base hidden) |
| Click (tap) the front card | Open it |
| Click (tap) a card behind | Bring it to the front |
| Right-click a card (press and hold) | Its menu: open, copy its text or a picture of it, delete it |
| Drag a note's top strip out of the window | Give it a window of its own |
| Drop a note window on the front card | Put the note back |
| Drop a `.notespin` file on the window | Import it |
| Drop `.txt` files on the window | Make a note of each (after asking) |
| *Window frame hidden:* drag the strip at the top, or its edges | Move NoteSpin, or resize it |
| *Window frame hidden:* click (or drag) the small button by the cards | Bring the toolbar back (or move NoteSpin) |

## Questions

**Where are my notes?** In the notebooks folder: *Settings → Notebooks folder* shows it, *Open folder* opens it.
Settings for this PC (window position, which notebook was open, the panel, sound) are in
`%LOCALAPPDATA%\NoteSpin` and aren't synced. When NoteSpin is portable, everything is in `NoteSpin Data` beside
`NoteSpin.exe`.

**I forgot a note's password.** It can't be opened, but it can be deleted from its password prompt.

**A note I emptied disappeared.** Closing an empty note deletes it; use *Undo* in the message straight away.
Otherwise, a backup (or OneDrive's version history for the notebook file) has it.

**I see a notebook called "My Notes-LAPTOP".** OneDrive made it because the notebook changed on two PCs at
once. Open both and copy what you need into one.

**Moving to a new PC.** Sign in to OneDrive there (or copy the notebooks folder over), copy the NoteSpin
folder, and run it. If the notebooks aren't in `Documents\NoteSpin`, use *Settings → Change…* (*Just switch*).
Or make NoteSpin portable, copy its folder over, and on the new PC choose *Keep everything on this PC… → Copy
them to this PC*.

---

## Building and changing NoteSpin

**Windows** (`.NET Framework 4.8` + WinForms + WebView2):
```powershell
.\build.ps1          # -> dist\NoteSpin
.\release.ps1        # build, sign NoteSpin.exe, verify -> NoteSpin-<version>.zip and -portable.zip for a GitHub release
```

**Linux** (`.NET 10` + GTK3 + WebKit2GTK-4.1):
```bash
./build.sh           # -> dist/NoteSpin-linux
./release.sh         # -> NoteSpin-<version>-linux-x64.tar.gz and -portable.tar.gz for a GitHub release
```

`NoteSpin.csproj` automatically selects the Windows (`net48`) or Linux (`net10.0`) target based on the OS you build on.

`release.ps1` signs with Azure Artifact Signing: it needs the `sign` tool
(`dotnet tool install -g --prerelease sign`) and an `az login` with signing rights. The version comes from
`<Version>` in `NoteSpin.csproj`. The portable archives are the same files with an empty `NoteSpin Data` folder.

Needs the .NET SDK on the build PC only. The output folder is the whole app (about 3 MB):

| File | |
|---|---|
| `NoteSpin.exe` | the app, with the page from `web\` compiled in |
| `NoteSpin.exe.config` | .NET settings (runtime version, high-DPI) |
| `Microsoft.Web.WebView2.Core.dll`, `WebView2Loader.dll` | Microsoft's WebView2 SDK (signed by Microsoft) |
| `LICENSES.txt` | third-party licenses |

Third-party code: three.js (MIT), bundled in `web\js\vendor`; Microsoft's WebView2 SDK (BSD-3-Clause).
SQLite is the copy built into Windows (`winsqlite3.dll`); the font is Segoe UI. Nothing loads from the internet.

### How it fits together

NoteSpin is a small Windows Forms window around a WebView2 browser view. The page (HTML, CSS and JavaScript in
`web\`) is compiled into the exe and served from memory at `https://notespin.local/`; nothing else can load in
the window. The page reaches the C# side through one object (`chrome.webview.hostObjects.ns`, wrapped as
`window.NS` by `web\js\host.js`), which reads and writes the notebook files with SQLite.

Each note window is a borderless window with its own WebView2 (sharing the main window's browser) showing
`web\note.html`: the same editor, on its own, with one note. One spare window is kept made and hidden, so a
note can appear in the middle of a drag; when it's taken, the next spare is made, and a window whose note
closes becomes the spare (or goes, if there's one already). The pages don't talk directly: each has its own
host object, and the C# side passes notes and messages between them (`NoteWin…`, `ToMain` and `ToNote` in
`HostApi`; `MainForm` keeps the list of windows). A note moves as the editor's state
(`exportState`/`openState` in `editor.js`), including the key of an unlocked note, so only one window ever has
it open.

With the window frame hidden, the main window loses its title bar and border, and the page keeps room to the
left of the cards and above them for the panel and title strip (`body.frameless` in `app.css`). The window
takes in that room too, but `MainForm` keeps only the part the page shows (a window region): the cards' box
with the toolbar hidden, all of it with the toolbar out. So the toolbar comes and goes without the window
changing size (a web view resized from its left or top edge shows its old picture in the wrong place for a
moment), and outside the region, clicks reach whatever is behind. The page reports its layout with
`FrameSet`, and moves or resizes the window with `FrameDrag` and `FrameSize`.

- **C#:** `src\MainForm.cs` (main window, its see-through background, narrower minimum with the spinner
  base hidden and hidden frame, Windows dialogs, the note windows
  and the drop target for them),
  `src\NoteWindow.cs` (a note window: resizing, moving, following a drag, its place and pin),
  `src\EdgeSizer.cs` (resizing a window without a frame from its edges, for both),
  `src\StickyNotes.cs` (reading a copy of Microsoft Sticky Notes' notes), `src\PageHost.cs` (the web view
  in each window: serving the page, what it may open), `src\Native.cs` (the few Windows calls needed),
  `src\HostApi.cs` (what the pages can call),
  `src\Library.cs` (the notebooks folder, switching, backups), `src\Notebook.cs` (one notebook file:
  schema, notes, categories, search), `src\Settings.cs` (the app's settings), `src\Places.cs` (where things
  are kept: on the PC, or portable beside the exe), `src\PortableSwitch.cs` (turning portable on and off, in a
  restarted NoteSpin once the old one has closed), `src\FileAssoc.cs` (the optional
  double-click setting), `src\FolderPicker.cs` (the Windows folder picker), `src\Sqlite.cs`, `src\WebAssets.cs`,
  `src\Json.cs` (JSON on both platforms).
- **Linux:** `src/linux/` holds the GTK versions of `Program`, `MainForm`, `NoteWindow`, `PageHost` and
  `FileAssoc`, plus `GtkNative.cs` (the GTK3 / WebKit2GTK calls). The page is served at `notespin://app/` and
  reaches the C# side through a WebKit script message handler, which `host.js` wraps as the same `window.NS`.
  Everything else in `src\` is shared. GTK, WebKit2GTK, libsoup and SQLite are the system's own libraries,
  loaded at run time; none are bundled.
- **Pages:** `web\index.html` and `web\note.html`, `web\css\app.css`, and in `web\js`: `app.js` (panel,
  views, search, notebooks, settings, sending notes to note windows, copying from Sticky Notes, notes from
  dropped text files), `notewin.js`
  (a note window's page),
  `icons.js` (the icons, for both pages), `spinner.js` (the 3D wheel: drum, cards, movement, input), `cardface.js` (drawing a
  card), `editor.js` (the note editor, list handling, locking prompts), `notes.js` (what a note is made of,
  category colours), `crypto.js` (locking), `ui.js` (dialogs, menus, messages), `host.js` (the bridge).
- **A notebook file:** tables `notes` (title = first line, body = the note's HTML, or the encrypted text when
  locked; preview and search columns the page fills in), `categories` and `meta`; stamped with
  `PRAGMA application_id` `0x4E53504E`. It uses a rollback journal (not WAL) and `secure_delete`. Notes copied
  from Sticky Notes are noted in `meta` (`copied:sticky:<its id>`), for *Copied before*.
- **Icon:** `favicon.ico`, drawn by `tools\make-icon.ps1`.

### Testing

Environment variables for testing:

- `NOTESPIN_DATA=<folder>`: stands in for `%LOCALAPPDATA%\NoteSpin` (its notebooks stay inside it, in
  `Notebooks\`). A `NoteSpin Data` folder beside the exe still makes it portable, so to test portable, copy the
  build somewhere and run it from there with these set: turning portable off then writes to these folders, not
  the real ones.
- `NOTESPIN_NOTEBOOKS=<folder>`: stands in for `Documents\NoteSpin` as the default notebooks folder.
- `NOTESPIN_DEVTOOLS=1`: allow the browser developer tools (F12) and reload (F5).
- `NOTESPIN_WEB=<the web folder>`: serve the page from disk instead of the exe, so a reload shows changes.
- `NOTESPIN_WINDOW=x,y,width,height`: place the window exactly.
- `NOTESPIN_STICKYNOTES=<folder>`: read Sticky Notes' notes from a `plum.sqlite` in this folder instead.

`tools\run-test.ps1` starts a Debug build with these, a throwaway data folder (`tools\out`, not in Git) and
the DevTools protocol on port 9333; `tools\cdp.mjs` (Node 22+) drives it: run JavaScript, click, type, take
screenshots (`connect(port, timeout, 'note.html', targetId)` drives a note window; `pages(port, 'note.html')` lists them). `tools\filedlg.ps1` fills in the Windows Open/Save/folder dialogs, `tools\crop.ps1` enlarges part
of a screenshot, and `tools\screen.ps1` captures the window as it appears on screen.
