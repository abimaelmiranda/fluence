using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Fluence.Modules.FileExplorer.ViewModels;

public sealed partial class FileTreeItem : ObservableObject
{
    private static readonly FileTreeItem LoadingPlaceholder = new(
        "Loading...",
        string.Empty,
        false,
        static _ => throw new InvalidOperationException(),
        static _ => throw new InvalidOperationException());

    private readonly Func<string, FileTreeItem> _createFileItem;
    private readonly Func<string, FileTreeItem> _createDirectoryItem;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isActive;

    public FileTreeItem(
        string name,
        string path,
        bool isDirectory,
        Func<string, FileTreeItem> createFileItem,
        Func<string, FileTreeItem> createDirectoryItem)
    {
        Name = name;
        Path = path;
        IsDirectory = isDirectory;
        _createFileItem = createFileItem;
        _createDirectoryItem = createDirectoryItem;
        Children = [];
    }

    public string Name { get; }
    public string Path { get; }
    public bool IsDirectory { get; }
    public ObservableCollection<FileTreeItem> Children { get; }
    public ICommand? OpenCommand { get; set; }
    public ICommand? NewFileCommand { get; set; }
    public ICommand? NewFolderCommand { get; set; }
    public ICommand? CopyCommand { get; set; }
    public ICommand? PasteCommand { get; set; }
    public ICommand? DeleteCommand { get; set; }
    public ICommand? LoadSolutionCommand { get; set; }

    public bool HasOpenCommand => OpenCommand is not null;
    public bool HasNewFileCommand => NewFileCommand is not null;
    public bool HasNewFolderCommand => NewFolderCommand is not null;
    public bool HasCopyCommand => CopyCommand is not null;
    public bool HasPasteCommand => PasteCommand is not null;
    public bool HasDeleteCommand => DeleteCommand is not null;
    public bool HasLoadSolutionCommand => LoadSolutionCommand is not null;
    public bool HasContextMenu => HasOpenCommand ||
                                  HasNewFileCommand ||
                                  HasNewFolderCommand ||
                                  HasCopyCommand ||
                                  HasPasteCommand ||
                                  HasDeleteCommand ||
                                  HasLoadSolutionCommand;
    public bool IsSolutionFile => string.Equals(System.IO.Path.GetExtension(Path), ".sln", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(System.IO.Path.GetExtension(Path), ".slnx", StringComparison.OrdinalIgnoreCase);

    public static FileTreeItem CreateFile(
        string path,
        Func<string, FileTreeItem> createFileItem,
        Func<string, FileTreeItem> createDirectoryItem)
    {
        return new FileTreeItem(System.IO.Path.GetFileName(path), path, false, createFileItem, createDirectoryItem);
    }

    public static FileTreeItem CreateDirectory(
        string path,
        Func<string, FileTreeItem> createFileItem,
        Func<string, FileTreeItem> createDirectoryItem)
    {
        var item = new FileTreeItem(System.IO.Path.GetFileName(path), path, true, createFileItem, createDirectoryItem);
        item.Children.Add(LoadingPlaceholder);
        return item;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value || !IsDirectory)
            return;

        if (Children.Count == 1 && ReferenceEquals(Children[0], LoadingPlaceholder))
            LoadChildren();
    }

    private void LoadChildren()
    {
        Children.Clear();

        try
        {
            foreach (var dir in Directory.GetDirectories(Path))
                Children.Add(_createDirectoryItem(dir));

            foreach (var file in Directory.GetFiles(Path))
                Children.Add(_createFileItem(file));
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    public void ReloadChildren()
    {
        if (IsDirectory)
        {
            LoadChildren();
        }
    }

    public void Activate()
    {
        OpenCommand?.Execute(null);
    }
}
