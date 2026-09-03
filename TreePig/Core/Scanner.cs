using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;namespace TreePig.Core
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

    // Walks a directory tree and builds the FsNode graph. A fixed pool of
    // workers pulls directories off a shared queue; every directory keeps an
    // atomic count of unfinished children and merges itself into its parent
    // once the count hits zero, so totals stay correct without any locking on
    // the nodes themselves.
    class Scanner
    {
        class DirWork
        {
            public FsNode Node;
            public DirWork Parent;
            public int Pending = 1;   // itself + children not yet merged
        }

        private readonly string _path;
        private readonly ScanOptions _opt;
        private IProgress<ScanProgress> _progress;
        private CancellationToken _ct;
        private Stopwatch _watch;
        private uint _cluster;
        private ConcurrentQueue<DirWork> _queue;

        private long _files, _dirs, _bytes, _errors, _active;
        private long _lastReportMs;

        public FsNode Root { get; private set; }

        public long ErrorCount => Interlocked.Read(ref _errors);

        public Scanner(string path, ScanOptions opt = null)
        {
            _path = Path.GetFullPath(path);
            _opt = opt ?? new ScanOptions();
        }

        public async Task<FsNode> ScanAsync(IProgress<ScanProgress> progress, CancellationToken ct)
        {
            _progress = progress;
            _ct = ct;
            _watch = Stopwatch.StartNew();
            _cluster = Util.GetClusterSize(_path);

            int workers = Math.Max(1, _opt.Threads > 0 ? _opt.Threads : Environment.ProcessorCount * 4);
            _queue = new ConcurrentQueue<DirWork>();

            Root = MakeRootNode(_path);
            Interlocked.Increment(ref _active);
            _queue.Enqueue(new DirWork { Node = Root });

            // the pool grows lazily, a scan would crawl for the first seconds
            // while it waits for threads to show up
            ThreadPool.GetMinThreads(out int oldWorkerMin, out int oldIoMin);
            ThreadPool.SetMinThreads(Math.Max(oldWorkerMin, workers), oldIoMin);
            try
            {
                var tasks = new Task[workers];
                for (int i = 0; i < workers; i++)
                    tasks[i] = Task.Run(() => WorkerLoop(), CancellationToken.None);

                Report(_path, true);
                try
                {
                    await Task.WhenAll(tasks);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
            }
            finally
            {
                ThreadPool.SetMinThreads(oldWorkerMin, oldIoMin);
            }
            Report(_path, true);
            return Root;
        }

        FsNode MakeRootNode(string path)
        {
            string name;
            if (path.EndsWith("\\") || path.Length <= 3)
            {
                // drive root, make it read like Explorer does
                try
                {
                    var drive = new DriveInfo(path);
                    name = string.IsNullOrEmpty(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel;
                    name += " (" + drive.Name.TrimEnd('\\') + ")";
                }
                catch { name = path; }
            }
            else
            {
                name = Path.GetFileName(path.TrimEnd('\\'));
                if (string.IsNullOrEmpty(name)) name = path;
            }

            DateTime lastWrite = DateTime.MinValue;
            try { lastWrite = Directory.GetLastWriteTimeUtc(path); } catch { }

            return new FsNode
            {
                Name = name,
                FullName = path,
                IsDirectory = true,
                LastWriteUtc = lastWrite
            };
        }

        void WorkerLoop()
        {
            while (true)
            {
                if (_queue.TryDequeue(out DirWork w))
                {
                    Process(w);
                    continue;
                }
                // nothing in the queue, but a directory somewhere might still
                // be mid-enumeration about to queue its children
                if (Volatile.Read(ref _active) == 0) return;
                _ct.ThrowIfCancellationRequested();
                Thread.Yield();
            }
        }

        void Process(DirWork w)
        {
            FsNode node = w.Node;
            long filesHere = 0, bytesHere = 0;

            if (_opt.CollectOwner) node.Owner = Util.GetOwner(node.FullName);

            var entries = FindFile.ListDirectory(node.FullName, out int err);
            if (err != 0)
            {
                // vanished or refused, count it and move on
                node.HasError = true;
                Interlocked.Increment(ref _errors);
            }

            string prefix = node.FullName.EndsWith("\\") ? node.FullName : node.FullName + "\\";
            foreach (var e in entries)
            {
                _ct.ThrowIfCancellationRequested();

                bool isDir = (e.Attributes & FileAttributes.Directory) != 0;
                if (isDir)
                {
                    bool reparse = (e.Attributes & FileAttributes.ReparsePoint) != 0;
                    var child = new FsNode
                    {
                        Name = e.Name,
                        FullName = prefix + e.Name,
                        IsDirectory = true,
                        IsReparsePoint = reparse,
                        Attributes = e.Attributes,
                        LastWriteUtc = e.LastWriteUtc
                    };
                    node.AddDirChild(child);
                    // junctions and symlinks are listed but not followed
                    if (!reparse)
                    {
                        Interlocked.Increment(ref w.Pending);
                        Interlocked.Increment(ref _active);
                        _queue.Enqueue(new DirWork { Node = child, Parent = w });
                    }
                }
                else
                {
                    long len = e.Length;
                    var child = new FsNode
                    {
                        Name = e.Name,
                        FullName = prefix + e.Name,
                        IsDirectory = false,
                        Attributes = e.Attributes,
                        Size = len,
                        Allocated = Util.RoundUpToCluster(len, _cluster),
                        LastWriteUtc = e.LastWriteUtc
                    };
                    if (_opt.CollectOwner) child.Owner = Util.GetOwner(child.FullName);
                    node.AddFileChild(child);
                    filesHere++;
                    bytesHere += len;
                    MaybeReport(child.FullName);
                }
            }

            Interlocked.Add(ref _files, filesHere);
            Interlocked.Add(ref _bytes, bytesHere);
            Interlocked.Increment(ref _dirs);
            MaybeReport(node.FullName);
            Finish(w);
        }

        // merges a finished directory (and, cascading upward, every ancestor
        // whose children are all done) into its parent
        void Finish(DirWork w)
        {
            while (true)
            {
                if (Interlocked.Decrement(ref w.Pending) != 0) return;
                DirWork parent = w.Parent;
                if (parent != null)
                    parent.Node.RollUp(w.Node);
                Interlocked.Decrement(ref _active);
                if (parent == null) return;
                w = parent;
            }
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
