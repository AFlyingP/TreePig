using System;
using System.Collections.Generic;
using System.IO;

namespace TreePig.Core
{
    // One row in the tree. Directories carry the summed size and file counts
    // of everything below them, files just carry their own numbers.
    class FsNode
    {
        public string Name = "";
        public string FullName = "";
        public bool IsDirectory;
        public bool IsVirtualRoot;     // synthetic parent when several folders are scanned at once
        public bool IsReparsePoint;
        public bool HasError;          // something below could not be read
        public long Size;              // bytes of all files below
        public long Allocated;         // same, rounded up to cluster size
        public long Files;             // files below (self not counted)
        public long Folders;           // folders below (self not counted)
        public DateTime LastWriteUtc;
        public FileAttributes Attributes;
        public string Owner;
        public FsNode Parent;
        public List<FsNode> Children;

        public bool HasChildren => Children != null && Children.Count > 0;

        public void EnsureChildren()
        {
            if (Children == null) Children = new List<FsNode>();
        }

        // used while scanning: a plain file contributes its own numbers
        public void AddFileChild(FsNode file)
        {
            EnsureChildren();
            file.Parent = this;
            Children.Add(file);
            Files += 1;
            Size += file.Size;
            Allocated += file.Allocated;
        }

        // a directory shows up in the list right away, its numbers are rolled
        // in later by RollUp once the child scan finished
        public void AddDirChild(FsNode dir)
        {
            EnsureChildren();
            dir.Parent = this;
            Children.Add(dir);
        }

        public void RollUp(FsNode dir)
        {
            Size += dir.Size;
            Allocated += dir.Allocated;
            Files += dir.Files;
            Folders += dir.Folders + 1;
        }

        // takes a node (and everything below it) out of the tree and fixes up
        // the numbers of all ancestors
        public void RemoveFromTree()
        {
            var p = Parent;
            if (p == null) return;
            p.Children?.Remove(this);
            long folders = IsDirectory ? Folders + 1 : 0;
            p.Adjust(-Size, -Allocated, -Files, -folders);
        }

        // swaps a rescan result in for an existing child and corrects the
        // totals on every ancestor
        public void ReplaceChild(FsNode oldChild, FsNode newChild)
        {
            if (Children == null) return;
            int idx = Children.IndexOf(oldChild);
            if (idx < 0) return;
            Children[idx] = newChild;
            oldChild.Parent = null;
            newChild.Parent = this;
            Adjust(newChild.Size - oldChild.Size,
                   newChild.Allocated - oldChild.Allocated,
                   newChild.Files - oldChild.Files,
                   newChild.Folders - oldChild.Folders);
        }

        private void Adjust(long size, long allocated, long files, long folders)
        {
            var n = this;
            while (n != null)
            {
                n.Size += size;
                n.Allocated += allocated;
                n.Files += files;
                n.Folders += folders;
                n = n.Parent;
            }
        }

        public double PercentOfParent()
        {
            var p = Parent;
            if (p == null) return 100.0;
            return p.Size > 0 ? (double)Size / p.Size * 100.0 : 0.0;
        }

        public IEnumerable<FsNode> EnumerateAll()
        {
            // iterative so deep trees can't blow the stack
            var stack = new Stack<FsNode>();
            stack.Push(this);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                yield return n;
                if (n.Children != null)
                    for (int i = n.Children.Count - 1; i >= 0; i--)
                        stack.Push(n.Children[i]);
            }
        }

        public IEnumerable<FsNode> Ancestors()
        {
            var n = Parent;
            while (n != null)
            {
                yield return n;
                n = n.Parent;
            }
        }
    }
}
