# RenoDXdb Editor

A simple tool for editing the RenoDX mod database (`RenoDXdb.json`).

---

## Getting Started

1. Run `RenoDXdbEditor.exe`
2. Click **Open JSON** and select your local copy of `RenoDXdb.json`
3. The game list on the left will populate with all entries

---

## Editing a Game

1. Click a game in the left panel — its details appear on the right
2. Edit any of the fields:
   - **Game Name** — must match exactly how the game appears in RHI (case-sensitive)
   - **Status** — `Done` (fully working mod) or `WIP` (in progress / has known issues)
   - **Author** — mod author name(s)
   - **Snapshot URL (64-bit)** — direct `.addon64` download link
   - **Snapshot URL (32-bit)** — direct `.addon32` download link, only if the game is 32-bit
   - **Nexus / GameBanana URL** — link to the mod page on Nexus or GameBanana
   - **Discord URL** — Discord invite or channel link, if the mod is Discord-only
   - **Discussion URL** — GitHub Discussions link for the mod
   - **Notes** — brief notes shown in RHI (e.g. "Disable in-game HDR", "Requires Lyall's fix")
3. Click **Apply Changes** to save your edits
4. If you change the game name it will automatically re-sort alphabetically

---

## Adding a New Game

1. Click **+ New Game**
2. Fill in the fields on the right
3. Click **Apply Changes** — the game is inserted into the list in alphabetical order

---

## Deleting a Game

1. Select the game in the list
2. Click **Delete**
3. Confirm the prompt

---

## Saving

- **Save** — overwrites the currently open file
- **Save As** — saves to a new location
- The title bar shows a dot indicator when there are unsaved changes
- You will be prompted to save if you close the window with unsaved changes

---

## Search / Filter

Type in the search box (top toolbar) to filter the list by game name or author. The counter next to it shows how many games match.

---

## URL field guide

| Field | What goes here |
|---|---|
| Snapshot URL (64-bit) | Direct `.addon64` file URL — GitHub releases or `.github.io` hosted |
| Snapshot URL (32-bit) | Direct `.addon32` file URL — only for 32-bit games |
| Nexus / GameBanana URL | `https://www.nexusmods.com/...` or `https://gamebanana.com/...` |
| Discord URL | `https://discord.com/invite/...` or channel link |
| Discussion URL | `https://github.com/clshortfuse/renodx/discussions/...` |

Leave fields empty if not applicable — they will be saved as `null`.

---

## Notes on game names

Game names must match exactly what RHI detects from the game's install folder (usually the Steam folder name or registry display name). Check RHI's game card title if unsure.

Special characters like `™`, `®`, `'`, and `:` should be written as-is — the editor saves them in plain text, not as escape codes.
