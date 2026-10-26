# WinStickies

[简体中文](./README.md) | **English**

Lightweight sticky notes for your Windows desktop. Each note is an independent window that you can move, resize, and collapse into a title bar. Your notes are saved automatically on your computer.

No installation, account, or internet connection required. Supports Windows 10 / 11 with .NET Framework 4.8.

## Download and get started

1. Download `WinStickies.exe` from this repository's **Releases** page.
2. Save the exe in a permanent folder you can write to, then double-click it to run. No extraction or installation is needed.
3. A tray icon and a “启动成功” (Startup successful) notification appear when the app starts. The icon may be in the taskbar's hidden icons area.
4. **Double-click the tray icon** to create a note, or **right-click it** to open the management menu.

## Features

- **Independent notes:** Move, resize, and collapse each note separately. Expanded and collapsed sizes are remembered independently.
- **Editable titles:** Double-click the title text or press `F2` to edit it. Press `Enter` to confirm or `Esc` to cancel. Clearing a custom title restores the automatic title based on the first line of the note.
- **Quick collapse:** Double-click the blank space beside the title, or click the arrow on the left, to collapse or expand the note.
- **Text formatting:** Set the body font size in the “…” menu, use font size shortcuts, and apply bold to selected text. Formatting is saved automatically.
- **Six colors:** Choose a paper color for each note.
- **Archive and search:** Archive notes you do not currently need and reopen them from the tray menu or the overview. The overview supports searching titles and text, and filtering by display mode.
- **Local storage:** Automatically save content, titles, formatting, colors, positions, and display states. A backup of the previous successful save is retained.

### Three display modes

| Mode | Behavior |
| --- | --- |
| Floating | A normal independent window that other apps can cover. |
| Always on top | Stays above normal application windows. |
| Attached to desktop | Stays on the Windows desktop layer; use `Win+D` to show the desktop. |

Switch modes using the title bar's mode button, the tray menu, or the overview. Each note has its own setting.

### Closing, archiving, and quitting

Click **×** in a note's upper-right corner to choose **删除** (Delete), **归档** (Archive), or **取消** (Cancel).

- Archiving preserves the content and window state for when you reopen the note.
- Deleting removes the current note.
- Closing the overview leaves the notes running.
- To quit the app completely, right-click the tray icon and select **退出 WinStickies** (Exit WinStickies).

## Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl+N` | Create a note |
| `F2` | Edit the current note's title |
| `Ctrl++` / `Ctrl+-` | Increase / decrease the body font size; numeric keypad supported. `Ctrl+=` also increases the size |
| `Ctrl+B` | Toggle bold for selected text, or for subsequent typing if nothing is selected |
| `Ctrl+Z` / `Ctrl+Y` | Undo / redo body edits and formatting changes |
| `Ctrl+Shift+C` | Collapse / expand the current note |
| `Ctrl+Shift+L` | Open the overview |
| `Ctrl+S` | Request a save |
| `Ctrl+F` | Search in the overview |

## Data and updates

By default, data is stored in the `data` folder beside `WinStickies.exe`:

- `notes.json`: Note content and state.
- `notes.json.bak`: Backup of the previous successful save.

Changes are saved automatically after approximately 500 ms. If saving fails, the app notifies you and retries. If data is corrupted, the original file is preserved and the app attempts to recover from the backup.

**Before updating, quit the app from the tray menu, back up the `data` folder, and replace the exe. Keep the existing `data` folder to continue using your notes.**

You can also specify a data directory:

```powershell
.\WinStickies.exe --data-dir "D:\MyNotes"
```
