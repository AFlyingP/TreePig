# TreePig

TreePig is a small Windows tool that shows you where your disk space went.
Point it at a drive or a folder and it scans everything below it, then lists
every subfolder with its size, file counts and a bar so you can see at a
glance which folders are hogging the space. Think of the classic TreeSize,
but free, no ads and no installer.

## Status

Usable for everyday disk cleanup work. Things that are in:

- fast parallel scan with live progress (path, size, files/s) and cancel
- tree list with size, allocated size, file/folder counts, percent of parent
  and last change columns, plus the red bars behind the size numbers
- sort by any column (natural order for names, so file10 comes after file9)
- multi-folder scans under one virtual root
- delete folders/files to the recycle bin, rescan a single branch after
  changes, everything stays consistent
- largest files window, file types (extension) breakdown
- export the tree as CSV or copy it to the clipboard
- junctions and symlinks are listed but not followed (no infinite loops)
- unreadable folders are marked, errors counted in the status bar
- options dialog: units, owner collection, bar color, auto-rescan at startup
- window, columns and sort order are remembered between runs
- open a path from the command line or drag a folder onto the window

## Building

Needs the .NET SDK (9.x) and Windows. Either open `TreePig.sln` in Visual
Studio or just:

```
dotnet build
dotnet run --project TreePig
```

## Usage

Start it and pick a folder to scan, or pass one on the command line:

```
TreePig.exe C:\Users\me\Documents
```

Handy keys: F5 rescan, Del sends the selection to the recycle bin, Enter
opens it, Ctrl+C copies the path, `*` and `/` on the numpad expand/collapse
everything.

## License

MIT, see [LICENSE](LICENSE).
