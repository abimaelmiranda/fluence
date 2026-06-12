using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Fluence.Desktop.ViewModels;

public sealed partial class FileTreeItem : ObservableObject
{
    private static readonly FileTreeItem LoadingPlaceholder = new("Loading...", string.Empty, false);

    private Action<FileTreeItem>? _onFileActivated;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isActive;

    private FileTreeItem(string name, string path, bool isDirectory)
    {
        Name = name;
        Path = path;
        IsDirectory = isDirectory;
        Children = [];
    }

    public string Name { get; }
    public string Path { get; }
    public bool IsDirectory { get; }
    public ObservableCollection<FileTreeItem> Children { get; }

    public static FileTreeItem CreateFile(string path, Action<FileTreeItem> onFileActivated)
    {
        return new FileTreeItem(System.IO.Path.GetFileName(path), path, false)
        {
            _onFileActivated = onFileActivated
        };
    }

    public static FileTreeItem CreateDirectory(string path, Action<FileTreeItem> onFileActivated)
    {
        var item = new FileTreeItem(System.IO.Path.GetFileName(path), path, true)
        {
            _onFileActivated = onFileActivated
        };
        item.Children.Add(LoadingPlaceholder);
        return item;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value || !IsDirectory)
        {
            return;
        }

        if (Children.Count == 1 && ReferenceEquals(Children[0], LoadingPlaceholder))
        {
            LoadChildren();
        }
    }

    private void LoadChildren()
    {
        Children.Clear();

        try
        {
            foreach (var dir in Directory.GetDirectories(Path))
            {
                Children.Add(CreateDirectory(dir, _onFileActivated!));
            }

            foreach (var file in Directory.GetFiles(Path))
            {
                Children.Add(CreateFile(file, _onFileActivated!));
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }

    public void Activate()
    {
        if (!IsDirectory)
        {
            _onFileActivated?.Invoke(this);
        }
    }
}
