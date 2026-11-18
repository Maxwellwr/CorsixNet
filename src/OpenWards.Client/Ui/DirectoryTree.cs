// The folder tree of CorsixTH's directory browser, ported from CorsixTH CorsixTH/Lua/dialogs/tree_ctrl.lua (TreeNode,
// FileTreeNode: children found when first asked for, sorted by name in lower case; root level nodes are taken to have
// children until opened; DummyRootNode: one root for each volume) and dialogs/resizables/directory_browser.lua
// (DirTreeNode: only folders that can be read).
// Copyright (c) the CorsixTH contributors. Licensed under the MIT license; see THIRD-PARTY-NOTICES.md.

namespace OpenWards.Client.Ui;

/// <summary>
/// A tree of folders, opened a level at a time: the volumes at the top (<c>lfsext.volumes</c>), each folder's subfolders
/// listed the first time it is opened.
/// </summary>
internal sealed class DirectoryTree
{
    private readonly Func<string, IEnumerable<string>> _subfolders;

    /// <param name="roots">The top level folders, such as the drives.</param>
    /// <param name="subfolders">The full paths of the folders in a folder that can be read.</param>
    public DirectoryTree(IEnumerable<string> roots, Func<string, IEnumerable<string>> subfolders)
    {
        _subfolders = subfolders;
        Roots = [.. roots.Select(root => new Node(this, root, root, null))];
    }

    /// <summary>The volumes of this computer: the drives that are ready, or <c>/</c>.</summary>
    public static DirectoryTree ForVolumes()
    {
        var drives = OperatingSystem.IsWindows()
            ? DriveInfo.GetDrives().Where(drive => drive.IsReady).Select(drive => drive.RootDirectory.FullName).ToList()
            : ["/"];
        return new DirectoryTree(drives.Count > 0 ? drives : [Path.GetPathRoot(Environment.CurrentDirectory) ?? "/"], ReadableSubfolders);
    }

    /// <summary>The top level nodes.</summary>
    public IReadOnlyList<Node> Roots { get; }

    /// <summary>The nodes shown, in order: each root, then the children of an open node under it.</summary>
    public List<Node> Visible()
    {
        var visible = new List<Node>();
        foreach (var root in Roots)
        {
            Add(root);
        }

        return visible;

        void Add(Node node)
        {
            visible.Add(node);
            if (node.IsExpanded)
            {
                foreach (var child in node.Children)
                {
                    Add(child);
                }
            }
        }
    }

    /// <summary>
    /// <c>DirTreeNode:isValidFile</c>: the folders in <paramref name="path"/> whose contents can be listed; none if the
    /// folder itself cannot be.
    /// </summary>
    public static IEnumerable<string> ReadableSubfolders(string path)
    {
        string[] folders;
        try
        {
            folders = Directory.GetDirectories(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }

        return folders.Where(CanList);

        static bool CanList(string folder)
        {
            try
            {
                using var entries = Directory.EnumerateFileSystemEntries(folder).GetEnumerator();
                entries.MoveNext();
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return false;
            }
        }
    }

    /// <summary>A folder in the tree (<c>FileTreeNode</c>).</summary>
    internal sealed class Node
    {
        private readonly DirectoryTree _tree;
        private List<Node>? _children;

        internal Node(DirectoryTree tree, string path, string label, Node? parent)
        {
            _tree = tree;
            Path = path;
            Label = label;
            Parent = parent;
            Level = parent is null ? 0 : parent.Level + 1;
        }

        public string Path { get; }

        /// <summary><c>getLabel</c>: the folder's name, or the whole path of a root.</summary>
        public string Label { get; }

        public Node? Parent { get; }

        /// <summary>How deep the node is (<c>getLevel</c>): 0 for a root.</summary>
        public int Level { get; }

        public bool IsExpanded { get; private set; }

        /// <summary>Whether the children have been looked for (<c>has_looked_for_children</c>).</summary>
        public bool HasLookedForChildren => _children is not null;

        /// <summary><c>hasChildren</c>: a root is taken to have some until it is opened; others are looked for.</summary>
        public bool HasChildren => _children is null && Level == 0 || Children.Count > 0;

        /// <summary>The subfolders, by name in lower case (<c>reSortChildren("name", "ascending")</c>).</summary>
        public IReadOnlyList<Node> Children => _children ??= [.. _tree._subfolders(Path)
            .Select(path => (Path: path, Name: System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path))))
            .OrderBy(child => child.Name.ToLowerInvariant(), StringComparer.Ordinal)
            .Select(child => new Node(_tree, child.Path, child.Name, this))];

        /// <summary>
        /// <c>expand</c> or <c>contract</c>, for a node with children; a root found empty on opening stays shut, and shows
        /// as a leaf from then on.
        /// </summary>
        public void Toggle()
        {
            if (HasChildren)
            {
                IsExpanded = !IsExpanded && Children.Count > 0;
            }
        }
    }
}
