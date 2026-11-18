# NoteSpin

Sticky notes on a spinning 3D card wheel, for Windows. Each note is a pastel card in the colour of its
category; turn the wheel to flip through them, click the front card to open it.

NoteSpin is portable: copy the `NoteSpin` folder anywhere and run `NoteSpin.exe`. Nothing needs installing.
It runs on .NET Framework 4.8 and the Microsoft Edge WebView2 Runtime, both part of Windows 10 and 11.

- [Getting started](#getting-started)
- [The wheel](#the-wheel)
- [Writing notes](#writing-notes)
- [Notes in their own windows](#notes-in-their-own-windows)
- [Categories](#categories)
- [Show, order and search](#show-order-and-search)
- [Locking a note](#locking-a-note)
- [Notebooks, folders and backups](#notebooks-folders-and-backups)
- [Copying notes from Sticky Notes](#copying-notes-from-sticky-notes)
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
| **The front card** | The note you're on: its title, and the start of the note with its lists and checkboxes. If there's more than fits, it fades out at the bottom. A locked note shows only its title and a lock. |
| **The band across the top** | The note's category, in that category's colour, and (quieter) when the note was last edited: *Today*, *Yesterday*, *Nov 12*, *Nov 12, 2025*. |
| **The tab on the right** | In A–Z order, the first letter of the title (so you can see where you are in the alphabet). In Recent order there's no tab; the date on the band says it. On category cards, the number of notes. |
| **The cards behind** | The next three notes: just their bands show. Click one to bring it to the front. |
| **The card in front, lying down** | The note before. Its writing shows faintly through the paper, reversed. |
| **The knobs** | They turn with the drum and take on the colour of the card at the front. |

Moving around:

- **Mouse wheel**: one notch, one card. Spin it quickly to go further.
- **Drag up or down** on the wheel (or swipe on a touch screen) to turn it; flick to let it spin, then press
  on it to stop it.
- **Previous / Next** and the **arrow keys** always move exactly one card.
- **Click a card behind** the front one to bring it forward. **Click the front card** to open it.
- **Drag sideways** to look at the wheel from another side; the **straighten** button (bottom of the panel)
  turns the view back.
- The wheel goes round: after the last note comes the first again. With only a few notes there's a gap
  between the last and the first, so you can tell where the end is.

The **sound** button turns the click of falling cards on and off. The arrow at the top of the panel folds it
down to a strip of icons (it folds by itself in a narrow window). Everything sizes to the window.

## Writing notes

- **The first line is the title.** It's what the card shows in large type, and what A–Z order sorts by.
- **One font, no formatting**, except lists. There are three kinds, from the toolbar or by typing at the start
  of a line:

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
- **Pasting** keeps the text, its lines and its lists, and drops fonts, sizes, colours, links and pictures.
- **Saving** happens as you type (the band says *Saved*). Closing the window saves anything still waiting.
- **Deleting**: the bin button in a note. A note closed with nothing in it is deleted automatically, with
  **Undo** in the message that appears.

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
  **Backspace** or the *Categories* link in the panel goes back.
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

## Keyboard, mouse and touch

| Keys | |
|---|---|
| ↑ ↓ (or ← →, Page Up/Down) | Previous / next card |
| Home / End | First / last card |
| Enter or Space | Open the front card (or the category) |
| Ctrl+N | New note (in a note window: a new note in another window) |
| Ctrl+F | Search |
| Esc | Close the note · clear the search · back to the categories |
| Backspace | Back to the categories |
| *In a note:* Tab / Shift+Tab | Move a list item in / out |
| *In a note:* Ctrl+Enter | Tick or untick a checkbox |
| *In a note:* Ctrl+S | Save now (it saves by itself anyway) |

| Mouse / touch | |
|---|---|
| Wheel | One card per notch |
| Drag (swipe) up or down | Turn the wheel; flick to spin; press to stop |
| Drag sideways | Look at the wheel from another side |
| Click (tap) the front card | Open it |
| Click (tap) a card behind | Bring it to the front |
| Drag a note's top strip out of the window | Give it a window of its own |
| Drop a note window on the front card | Put the note back |
| Drop a `.notespin` file on the window | Import it |

## Questions

**Where are my notes?** In the notebooks folder: *Settings → Notebooks folder* shows it, *Open folder* opens it.
Settings for this PC (window position, which notebook was open, the panel, sound) are in
`%LOCALAPPDATA%\NoteSpin` and aren't synced.

**I forgot a note's password.** It can't be opened, but it can be deleted from its password prompt.

**A note I emptied disappeared.** Closing an empty note deletes it; use *Undo* in the message straight away.
Otherwise, a backup (or OneDrive's version history for the notebook file) has it.

**I see a notebook called "My Notes-LAPTOP".** OneDrive made it because the notebook changed on two PCs at
once. Open both and copy what you need into one.

**Moving to a new PC.** Sign in to OneDrive there (or copy the notebooks folder over), copy the NoteSpin
folder, and run it. If the notebooks aren't in `Documents\NoteSpin`, use *Settings → Change…* (*Just switch*).

---

## Building and changing NoteSpin

```powershell
.\build.ps1          # -> dist\NoteSpin
.\release.ps1        # build, sign NoteSpin.exe, verify -> NoteSpin-<version>.zip for a GitHub release
```

`release.ps1` signs with Azure Artifact Signing: it needs the `sign` tool
(`dotnet tool install -g --prerelease sign`) and an `az login` with signing rights. The version comes from
`<Version>` in `NoteSpin.csproj`.

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

- **C#:** `src\MainForm.cs` (main window, Windows dialogs, the note windows and the drop target for them),
  `src\NoteWindow.cs` (a note window: resizing, moving, following a drag, its place and pin),
  `src\StickyNotes.cs` (reading a copy of Microsoft Sticky Notes' notes), `src\PageHost.cs` (the web view
  in each window: serving the page, what it may open), `src\Native.cs` (the few Windows calls needed),
  `src\HostApi.cs` (what the pages can call),
  `src\Library.cs` (the notebooks folder, switching, backups), `src\Notebook.cs` (one notebook file:
  schema, notes, categories, search), `src\Settings.cs` (this PC's settings), `src\FileAssoc.cs` (the optional
  double-click setting), `src\FolderPicker.cs` (the Windows folder picker), `src\Sqlite.cs`, `src\WebAssets.cs`.
- **Pages:** `web\index.html` and `web\note.html`, `web\css\app.css`, and in `web\js`: `app.js` (panel,
  views, search, notebooks, settings, sending notes to note windows, copying from Sticky Notes), `notewin.js`
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

- `NOTESPIN_DATA=<folder>`: use a different data folder (its notebooks stay inside it, in `Notebooks\`).
- `NOTESPIN_NOTEBOOKS=<folder>`: stands in for `Documents\NoteSpin` as the default notebooks folder.
- `NOTESPIN_DEVTOOLS=1`: allow the browser developer tools (F12) and reload (F5).
- `NOTESPIN_WEB=<the web folder>`: serve the page from disk instead of the exe, so a reload shows changes.
- `NOTESPIN_WINDOW=x,y,width,height`: place the window exactly.
- `NOTESPIN_STICKYNOTES=<folder>`: read Sticky Notes' notes from a `plum.sqlite` in this folder instead.

`tools\run-test.ps1` starts a Debug build with these, a throwaway data folder (`tools\out`, not in Git) and
the DevTools protocol on port 9333; `tools\cdp.mjs` (Node 22+) drives it: run JavaScript, click, type, take
screenshots (`connect(port, timeout, 'note.html', targetId)` drives a note window; `pages(port, 'note.html')` lists them). `tools\filedlg.ps1` fills in the Windows Open/Save/folder dialogs, `tools\crop.ps1` enlarges part
of a screenshot, and `tools\screen.ps1` captures the window as it appears on screen.
