using DownloadRouter.Core.Paths;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace DownloadRouter.App;

public sealed class FolderTreePicker : UserControl
{
    private readonly SafeFolderTreeProvider provider = new(new PathBoundaryValidator());
    private readonly TreeView tree = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        SelectionMode = TreeViewSelectionMode.Single,
    };
    private readonly TextBlock selectedPath = new()
    {
        TextWrapping = TextWrapping.Wrap,
        MaxLines = 2,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Opacity = 0.75,
    };
    private readonly TextBlock nodeError = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed,
    };
    private readonly ProgressRing loading = new()
    {
        Width = 20,
        Height = 20,
        Visibility = Visibility.Collapsed,
    };
    private readonly Dictionary<TreeViewNode, FolderTreeEntry> entries = [];
    private readonly HashSet<TreeViewNode> loadedNodes = [];
    private string storageRoot = string.Empty;
    private readonly Button parentButton = new() { Content = "상위 폴더로", Visibility = Visibility.Collapsed };
    public bool AllowParentNavigation { get; set; }

    public event EventHandler? SelectionChanged;

    public FolderTreePicker()
    {
        tree.ItemTemplate = (DataTemplate)XamlReader.Load(
            """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <TextBlock Text="{Binding}"
                         TextWrapping="Wrap"
                         HorizontalAlignment="Stretch"
                         ToolTipService.ToolTip="{Binding}" />
            </DataTemplate>
            """);
        tree.ItemContainerStyle = new Style(typeof(TreeViewItem));
        tree.ItemContainerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        ScrollViewer.SetHorizontalScrollBarVisibility(tree, ScrollBarVisibility.Disabled);
        ScrollViewer.SetHorizontalScrollMode(tree, ScrollMode.Disabled);
        var refresh = new Button
        {
            Content = "새로 고침",
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        ToolTipService.SetToolTip(refresh, "선택한 폴더 새로 고침");
        refresh.Click += async (_, _) => await RefreshSelectedAsync();
        parentButton.Click += async (_, _) =>
        {
            var parent = Directory.GetParent(SelectedFullPath);
            if (parent is null) return;
            try { await InitializeAsync(SelectionDestination.ValidateFolder(parent.FullName)); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                nodeError.Text = "상위 폴더에 접근할 수 없습니다.";
                nodeError.Visibility = Visibility.Visible;
            }
        };
        tree.Expanding += Tree_Expanding;
        tree.SelectionChanged += Tree_SelectionChanged;

        var panel = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            RowSpacing = 8,
        };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        navigation.Children.Add(parentButton);
        navigation.Children.Add(refresh);
        navigation.Children.Add(loading);
        var toolbar = new StackPanel { Spacing = 8 };
        toolbar.Children.Add(navigation);
        toolbar.Children.Add(selectedPath);
        toolbar.Children.Add(nodeError);
        panel.Children.Add(toolbar);
        Grid.SetRow(tree, 1);
        panel.Children.Add(tree);
        Content = panel;
    }

    public string SelectedRelativeFolder
        => tree.SelectedNode is not null && entries.TryGetValue(tree.SelectedNode, out var entry)
            ? entry.RelativePath == "." ? string.Empty : entry.RelativePath
            : string.Empty;

    public string SelectedFullPath
        => tree.SelectedNode is not null && entries.TryGetValue(tree.SelectedNode, out var entry)
            ? entry.FullPath
            : storageRoot;

    public bool IsSelectionAccessible
        => tree.SelectedNode is not null
            && entries.TryGetValue(tree.SelectedNode, out var entry)
            && entry.IsAccessible;

    public async Task InitializeAsync(
        string root,
        string? selectedRelativeFolder = null,
        CancellationToken cancellationToken = default)
    {
        storageRoot = Path.GetFullPath(root);
        parentButton.Visibility = AllowParentNavigation ? Visibility.Visible : Visibility.Collapsed;
        parentButton.IsEnabled = Directory.GetParent(storageRoot) is not null;
        entries.Clear();
        loadedNodes.Clear();
        tree.RootNodes.Clear();
        var rootEntry = new FolderTreeEntry(
            storageRoot,
            storageRoot,
            ".",
            true);
        var rootNode = CreateNode(rootEntry);
        rootNode.HasUnrealizedChildren = true;
        tree.RootNodes.Add(rootNode);
        tree.SelectedNode = rootNode;
        selectedPath.Text = $"선택 위치: {storageRoot}";
        ToolTipService.SetToolTip(selectedPath, selectedPath.Text);

        // Show the first level immediately without recursively scanning the tree.
        await EnsureChildrenLoadedAsync(rootNode, cancellationToken);
        rootNode.IsExpanded = true;

        if (!string.IsNullOrWhiteSpace(selectedRelativeFolder))
        {
            await SelectRelativePathAsync(selectedRelativeFolder, cancellationToken);
            if (tree.SelectedNode is { } selectedNode && selectedNode != rootNode)
            {
                await EnsureChildrenLoadedAsync(selectedNode, cancellationToken);
                selectedNode.IsExpanded = true;
            }
        }
    }

    public async Task SelectRelativePathAsync(
        string relativeFolder,
        CancellationToken cancellationToken = default)
    {
        var rootNode = tree.RootNodes.FirstOrDefault();
        if (rootNode is null)
        {
            return;
        }

        var current = rootNode;
        foreach (var part in relativeFolder.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            await EnsureChildrenLoadedAsync(current, cancellationToken);
            var next = current.Children.FirstOrDefault(candidate =>
                entries.TryGetValue(candidate, out var entry)
                && string.Equals(entry.Name, part, StringComparison.OrdinalIgnoreCase));
            if (next is null)
            {
                return;
            }

            current.IsExpanded = true;
            current = next;
        }

        tree.SelectedNode = current;
    }

    private TreeViewNode CreateNode(FolderTreeEntry entry)
    {
        var node = new TreeViewNode
        {
            Content = entry.Name,
            HasUnrealizedChildren = entry.IsAccessible,
        };
        entries[node] = entry;
        return node;
    }

    private async void Tree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        await EnsureChildrenLoadedAsync(args.Node, CancellationToken.None);
        args.Node.IsExpanded = true;
    }

    private void Tree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (tree.SelectedNode is null || !entries.TryGetValue(tree.SelectedNode, out var entry))
        {
            return;
        }

        selectedPath.Text = entry.IsAccessible
            ? $"선택 위치: {entry.FullPath}"
            : entry.ErrorMessage ?? "이 폴더에 접근할 수 없습니다.";
        ToolTipService.SetToolTip(selectedPath, selectedPath.Text);
        parentButton.IsEnabled = Directory.GetParent(entry.FullPath) is not null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task EnsureChildrenLoadedAsync(TreeViewNode node, CancellationToken cancellationToken)
    {
        if (loadedNodes.Contains(node) || !entries.TryGetValue(node, out var entry) || !entry.IsAccessible)
        {
            return;
        }

        loadedNodes.Add(node);
        loading.Visibility = Visibility.Visible;
        loading.IsActive = true;
        nodeError.Visibility = Visibility.Collapsed;
        try
        {
            var children = await provider.GetChildrenAsync(storageRoot, entry.FullPath, cancellationToken);
            node.Children.Clear();
            foreach (var child in children.Entries)
            {
                node.Children.Add(CreateNode(child));
            }

            node.HasUnrealizedChildren = false;
            if (!string.IsNullOrWhiteSpace(children.ErrorMessage))
            {
                nodeError.Text = children.ErrorMessage;
                nodeError.Visibility = Visibility.Visible;
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            node.HasUnrealizedChildren = false;
            nodeError.Text = "이 폴더의 하위 항목을 읽을 수 없습니다.";
            nodeError.Visibility = Visibility.Visible;
        }
        finally
        {
            loading.IsActive = false;
            loading.Visibility = Visibility.Collapsed;
        }
    }

    private async Task RefreshSelectedAsync()
    {
        var node = tree.SelectedNode ?? tree.RootNodes.FirstOrDefault();
        if (node is null || !entries.TryGetValue(node, out var entry) || !entry.IsAccessible)
        {
            return;
        }

        loadedNodes.Remove(node);
        node.Children.Clear();
        node.HasUnrealizedChildren = true;
        await EnsureChildrenLoadedAsync(node, CancellationToken.None);
        node.IsExpanded = true;
    }
}
