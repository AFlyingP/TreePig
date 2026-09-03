using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TreePig.Core
{
    class ScanOptions
    {
        public bool CollectOwner;   // slower, but fills the owner column
        public int Threads;         // 0 = pick automatically
    }

    class ScanProgress
    {
        public string CurrentPath;
        public long Files;
        public long Dirs;
        public long Bytes;
        public long Errors;
        public TimeSpan Elapsed;
    }

    // Walks a directory tree and builds the FsNode graph. The scan fans out
    // across threads (one task per directory, capped by a semaphore) and every
    // parent rolls its finished children into its own totals.
    class Scanner
    {
        private readonly string _path;
        private readonly ScanOptions _opt;
        private IProgress<ScanProgress> _progress;
        private CancellationToken _ct;
        private SemaphoreSlim _gate;
        private Stopwatch _watch;
        private uint _cluster;

        private long _files, _dirs, _bytes, _errors;
        private long _lastReportMs;

        public FsNode Root { get; private set; }

        public Scanner(string path, ScanOptions opt = null)
        {
            _path = Path.GetFullPath(path);
            _opt = opt ?? new ScanOptions();
        }

        public async Task<FsNode> ScanAsync(IProgress<ScanProgress> progress, CancellationToken ct)
        {
            _progress = progress;
            _ct = ct;
            _gate = new SemaphoreSlim(_opt.Threads > 0 ? _opt.Threads : Environment.ProcessorCount * 8);
            _cluster = Util.GetClusterSize(_path);
            _watch = Stopwatch.StartNew();

            var di = new DirectoryInfo(_path);
            Root = MakeRootNode(di);
            Interlocked.Increment(ref _dirs);

            Report(_path, true);
            try
            {
                await ScanDirAsync(Root, di);
            }
            catch (OperationCanceledException)
            {
                Root.HasError = true;
                throw;
            }
            Report(_path, true);
            return Root;
        }

        private FsNode MakeRootNode(DirectoryInfo di)
        {
            string name;
            if (di.FullName.EndsWith(Path.DirectorySeparatorChar) || di.FullName.Length <= 3)
            {
                // drive root, make it read like Explorer does
                try
                {
                    var drive = new DriveInfo(di.FullName);
                    name = string.IsNullOrEmpty(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel;
                    name += " (" + drive.Name.TrimEnd('\\') + ")";
                }
                catch { name = di.FullName; }
            }
            else
            {
                name = di.Name;
                if (string.IsNullOrEmpty(name)) name = di.FullName;
            }

            return new FsNode
            {
                Name = name,
                FullName = di.FullName,
                IsDirectory = true,
                LastWriteUtc = SafeLastWrite(di)
            };
        }

        private async Task<FsNode> ScanDirAsync(FsNode node, DirectoryInfo di)
        {
            List<Task<FsNode>> subs = null;
            await _gate.WaitAsync(_ct);
            try
            {
                if (_opt.CollectOwner) node.Owner = Util.GetOwner(di.FullName);
                subs = EnumerateInto(node, di);
                Interlocked.Increment(ref _dirs);
                MaybeReport(di.FullName);
            }
            catch (OperationCanceledException) { throw; }
            catch (UnauthorizedAccessException)
            {
                node.HasError = true;
                Interlocked.Increment(ref _errors);
            }
            catch (IOException)
            {
                node.HasError = true;
                Interlocked.Increment(ref _errors);
            }
            finally { _gate.Release(); }

            if (subs != null)
            {
                // wait for the subdirs one by one and fold their numbers in
                foreach (var t in subs)
                {
                    var child = await t;
                    node.RollUp(child);
                }
            }
            return node;
        }

        private List<Task<FsNode>> EnumerateInto(FsNode node, DirectoryInfo di)
        {
            List<Task<FsNode>> subs = null;
            foreach (var entry in di.EnumerateFileSystemInfos())
            {
                _ct.ThrowIfCancellationRequested();

                FileAttributes attr;
                bool isDir;
                try { attr = entry.Attributes; isDir = entry is DirectoryInfo; }
                catch { continue; } // vanished between listing and stat

                if (isDir)
                {
                    bool reparse = (attr & FileAttributes.ReparsePoint) != 0;
                    var child = new FsNode
                    {
                        Name = entry.Name,
                        FullName = entry.FullName,
                        IsDirectory = true,
                        IsReparsePoint = reparse,
                        Attributes = attr,
                        LastWriteUtc = entry.LastWriteTimeUtc
                    };
                    node.AddDirChild(child);
                    // junctions and symlinks are listed but not followed
                    if (!reparse)
                        (subs ??= new List<Task<FsNode>>()).Add(ScanDirAsync(child, (DirectoryInfo)entry));
                }
                else
                {
                    long len = 0;
                    try { len = ((FileInfo)entry).Length; }
                    catch { }
                    var child = new FsNode
                    {
                        Name = entry.Name,
                        FullName = entry.FullName,
                        IsDirectory = false,
                        Attributes = attr,
                        Size = len,
                        Allocated = Util.RoundUpToCluster(len, _cluster),
                        LastWriteUtc = entry.LastWriteTimeUtc
                    };
                    if (_opt.CollectOwner) child.Owner = Util.GetOwner(entry.FullName);
                    node.AddFileChild(child);
                    Interlocked.Add(ref _bytes, len);
                    Interlocked.Increment(ref _files);
                    MaybeReport(entry.FullName);
                }
            }
            return subs;
        }

        private DateTime SafeLastWrite(DirectoryInfo di)
        {
            try { return di.LastWriteTimeUtc; }
            catch { return DateTime.MinValue; }
        }

        private void MaybeReport(string path)
        {
            long now = _watch.ElapsedMilliseconds;
            long last = Interlocked.Read(ref _lastReportMs);
            if (now - last < 100) return;
            if (Interlocked.CompareExchange(ref _lastReportMs, now, last) == last)
                Report(path, false);
        }

        private void Report(string path, bool force)
        {
            if (_progress == null) return;
            _progress.Report(new ScanProgress
            {
                CurrentPath = path,
                Files = Interlocked.Read(ref _files),
                Dirs = Interlocked.Read(ref _dirs),
                Bytes = Interlocked.Read(ref _bytes),
                Errors = Interlocked.Read(ref _errors),
                Elapsed = _watch.Elapsed
            });
        }
    }
}
