using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Ports;

namespace Fluence.Desktop.Services;

public sealed class FileClipboardService : IFileClipboardService
{
    public string? SourcePath { get; private set; }

    public bool HasSourcePath => !string.IsNullOrWhiteSpace(SourcePath);

    public void Copy(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        SourcePath = Path.GetFullPath(path);
    }

    public async Task<string> PasteAsync(string destinationDirectory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!HasSourcePath)
            throw new InvalidOperationException("No copied file or folder is available.");

        if (!Directory.Exists(destinationDirectory))
            throw new DirectoryNotFoundException(destinationDirectory);

        var sourcePath = SourcePath!;
        return await Task.Run(() =>
        {
            var targetPath = CreateTargetPath(sourcePath, destinationDirectory);
            if (Directory.Exists(sourcePath))
                CopyDirectory(sourcePath, targetPath, cancellationToken);
            else
                File.Copy(sourcePath, targetPath, overwrite: false);
            return targetPath;
        }, cancellationToken);
    }

    private static string CreateTargetPath(string sourcePath, string destinationDirectory)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var isDirectory = Directory.Exists(fullSourcePath);
        var sourceName = Path.GetFileName(fullSourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            throw new InvalidOperationException("The copied item has no name.");
        }

        if (isDirectory)
        {
            if (IsSameOrNestedDirectory(fullSourcePath, destinationDirectory))
            {
                throw new InvalidOperationException("Cannot paste a folder into itself or one of its descendants.");
            }

            return GetAvailablePath(destinationDirectory, sourceName, string.Empty, isDirectory: true);
        }

        var extension = Path.GetExtension(fullSourcePath);
        var fileName = Path.GetFileNameWithoutExtension(fullSourcePath);
        return GetAvailablePath(destinationDirectory, fileName, extension, isDirectory: false);
    }

    private static string GetAvailablePath(string destinationDirectory, string baseName, string extension, bool isDirectory)
    {
        var candidate = Path.Combine(destinationDirectory, baseName + extension);
        if (!Exists(candidate, isDirectory))
        {
            return candidate;
        }

        for (var index = 2; index < 10_000; index++)
        {
            var suffix = index == 2 ? " copy" : $" copy {index}";
            candidate = Path.Combine(destinationDirectory, baseName + suffix + extension);
            if (!Exists(candidate, isDirectory))
            {
                return candidate;
            }
        }

        throw new IOException("Unable to find an available destination name.");
    }

    private static bool Exists(string path, bool isDirectory)
        => isDirectory ? Directory.Exists(path) : File.Exists(path);

    private static bool IsSameOrNestedDirectory(string sourceDirectory, string candidateDirectory)
    {
        var source = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(candidateDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(source, candidate, StringComparison.OrdinalIgnoreCase) ||
               candidate.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationFile = Path.Combine(targetDirectory, Path.GetFileName(file));
            File.Copy(file, destinationFile, overwrite: false);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var childTarget = Path.Combine(targetDirectory, Path.GetFileName(directory));
            CopyDirectory(directory, childTarget, cancellationToken);
        }
    }
}
