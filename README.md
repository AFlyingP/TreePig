# TreePig

TreePig is a small Windows tool that shows you where your disk space went.
Point it at a drive or a folder and it scans everything below it, then lists
every subfolder with its size, file counts and a bar so you can see at a
glance which folders are hogging the space. Think of the classic TreeSize,
but free, no ads and no installer.

## Status

Early days. It scans, it draws the tree, it sorts. More to come.

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

You can also drag a folder from Explorer onto the window.

## License

MIT, see [LICENSE](LICENSE).
