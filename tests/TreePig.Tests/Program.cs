using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using TreePig.Core;
using TreePig.Ui;

// quick smoke test, run with: dotnet run --project tests/TreePig.Tests
// pass a folder to just time a scan of it, e.g.
//   dotnet run --project tests/TreePig.Tests -- C:\Windows
class Harness
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (args.Length > 0 && Directory.Exists(args[0]))
            return TimeScan(args[0]);

        return SmokeTest();
    }

    static int TimeScan(string path)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var scanner = new Scanner(path, new ScanOptions());
        var scanned = scanner.ScanAsync(new Progress<ScanProgress>(), System.Threading.CancellationToken.None)
                       .GetAwaiter().GetResult();
        sw.Stop();
        Console.WriteLine($"scanned {scanned.Files:N0} files, {scanned.Folders:N0} folders, {Util.FormatBytes(scanned.Size)}, " +
                          $"{scanner.ErrorCount} errors in {sw.ElapsedMilliseconds} ms ({sw.Elapsed.TotalSeconds:0.0}s)");
        return 0;
    }

    static int SmokeTest()
    {
        // builds a known folder tree under %TEMP%, scans it and drives the
        // tree list off-screen (the form sits at x=-30000, no taskbar button)
        string target = MakeTestTree();
        long expectedSize = 11403264;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var scanner = new Scanner(target, new ScanOptions());
        var root = scanner.ScanAsync(new Progress<ScanProgress>(), System.Threading.CancellationToken.None)
                       .GetAwaiter().GetResult();
        sw.Stop();
        Console.WriteLine($"scan took {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"size={root.Size} (expected {expectedSize}) files={root.Files} (6) folders={root.Folders} (5) errors={scanner.ErrorCount} (0)");
        if (root.Size != expectedSize || root.Files != 6 || root.Folders != 5) { Console.WriteLine("SCAN TOTALS WRONG"); return 1; }

        var form = new Form
        {
            Size = new Size(1000, 600),
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-30000, -30000),
            ShowInTaskbar = false
        };
        var tree = new TreeListView { Dock = DockStyle.Fill, Parent = form };
        form.Show();
        Application.DoEvents();

        var tv = (TreeView)typeof(TreeListView).GetField("_tree", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(tree);

        tree.SetRoot(root);
        Application.DoEvents();
        // SetRoot leaves the root expanded: root + its 3 subfolders
        int afterSetRoot = CountReal(tv.Nodes);
        Console.WriteLine($"real rows right after attach: {afterSetRoot} (expected 4)");
        if (afterSetRoot != 4) { Console.WriteLine("ATTACH WRONG"); return 1; }

        foreach (TreeNode n in tv.Nodes[0].Nodes)
            if (n.Text.StartsWith("Docs")) n.Expand();
        Application.DoEvents();
        int afterDocs = CountReal(tv.Nodes);
        Console.WriteLine($"real rows after expanding Docs: {afterDocs} (expected 7)");
        if (afterDocs != 7) { Console.WriteLine("DOCS EXPAND WRONG"); return 1; }

        tree.SetSort(0, true); // by name ascending
        Application.DoEvents();
        var order = string.Join(",", root.Children.Select(c => c.Name));
        Console.WriteLine($"root children after sort: {order} (expected Docs,Music,Videos)");
        if (order != "Docs,Music,Videos") { Console.WriteLine("SORT WRONG"); return 1; }

        // collapse and re-expand to make sure the dummy/populate dance holds up
        tv.Nodes[0].Collapse();
        tv.Nodes[0].Expand();
        Application.DoEvents();
        int afterReExpand = CountReal(tv.Nodes);
        Console.WriteLine($"real rows after collapse+expand: {afterReExpand} (expected 7)");
        if (afterReExpand != 7) { Console.WriteLine("RE-EXPAND WRONG"); return 1; }

        using (var bmp = new Bitmap(1000, 600))
        {
            tree.DrawToBitmap(bmp, new Rectangle(0, 0, 1000, 600));
            Console.WriteLine($"draw ok, middle pixel {bmp.GetPixel(500, 120)}");
        }

        form.Close();

        // cancel a big scan mid flight, it must return promptly instead of
        // leaving workers hanging around
        var cts = new CancellationTokenSource(1500);
        var cancelScanner = new Scanner(@"C:\", new ScanOptions());
        var swCancel = System.Diagnostics.Stopwatch.StartNew();
        bool canceled = false;
        try
        {
            cancelScanner.ScanAsync(new Progress<ScanProgress>(), cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { canceled = true; }
        swCancel.Stop();
        Console.WriteLine($"cancel after 1.5s: returned in {swCancel.ElapsedMilliseconds} ms, canceled={canceled}, " +
                          $"token fired={cts.Token.IsCancellationRequested}");
        if (!canceled || swCancel.ElapsedMilliseconds > 10000) { Console.WriteLine("CANCEL WRONG"); return 1; }

        Console.WriteLine("ALL CHECKS PASSED");
        return 0;
    }

    static string MakeTestTree()
    {
        string t = Path.Combine(Path.GetTempPath(), "TreePigTest");
        Directory.CreateDirectory(Path.Combine(t, "Videos"));
        Directory.CreateDirectory(Path.Combine(t, "Docs", "notes"));
        Directory.CreateDirectory(Path.Combine(t, "Docs", "Photos"));
        Directory.CreateDirectory(Path.Combine(t, "Music"));
        Write(t, @"Videos\movie1.bin", 5 * 1024 * 1024);
        Write(t, @"Videos\clip2.bin", 2 * 1024 * 1024);
        Write(t, @"Docs\report.bin", 3 * 1024 * 1024);
        Write(t, @"Docs\notes\todo.bin", 512 * 1024);
        Write(t, @"Docs\Photos\pic1.bin", 256 * 1024);
        Write(t, @"Music\song1.bin", 128 * 1024);
        return t;
    }

    static void Write(string root, string rel, int bytes)
    {
        using var f = new FileStream(Path.Combine(root, rel), FileMode.Create);
        f.Write(new byte[bytes]);
    }

    static int CountReal(TreeNodeCollection nodes)
    {
        // placeholders (Tag == null) only exist to offer the [+], don't count
        int n = 0;
        foreach (TreeNode x in nodes)
        {
            if (x.Tag != null) n++;
            n += CountReal(x.Nodes);
        }
        return n;
    }
}
