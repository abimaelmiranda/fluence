using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Storage;

namespace Fluence.Infrastructure;

public sealed class FluenceStorageService : IFluenceStorageService
{
    private static readonly string UserRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fluence");

    public string GetUserPath(string subPath = "") =>
        string.IsNullOrEmpty(subPath) ? UserRoot : Path.Combine(UserRoot, subPath);

    public string GetProjectPath(string workspaceRoot, string subPath = "")
    {
        var root = Path.Combine(workspaceRoot, ".fluence");
        return string.IsNullOrEmpty(subPath) ? root : Path.Combine(root, subPath);
    }

    public async Task<T?> ReadUserAsync<T>(string fileName, JsonTypeInfo<T> typeInfo, CancellationToken ct = default) =>
        await ReadJsonAsync(Path.Combine(UserRoot, fileName), typeInfo, ct).ConfigureAwait(false);

    public async Task WriteUserAsync<T>(string fileName, T value, JsonTypeInfo<T> typeInfo, CancellationToken ct = default) =>
        await WriteJsonAsync(Path.Combine(UserRoot, fileName), value, typeInfo, ct).ConfigureAwait(false);

    public async Task<T?> ReadProjectAsync<T>(string workspaceRoot, string fileName, JsonTypeInfo<T> typeInfo, CancellationToken ct = default) =>
        await ReadJsonAsync(Path.Combine(workspaceRoot, ".fluence", fileName), typeInfo, ct).ConfigureAwait(false);

    public async Task WriteProjectAsync<T>(string workspaceRoot, string fileName, T value, JsonTypeInfo<T> typeInfo, CancellationToken ct = default) =>
        await WriteJsonAsync(Path.Combine(workspaceRoot, ".fluence", fileName), value, typeInfo, ct).ConfigureAwait(false);

    private static async Task<T?> ReadJsonAsync<T>(string path, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        if (!File.Exists(path))
            return default;

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(stream, typeInfo, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"[FluenceStorage] Read failed for {path}: {ex.Message}");
            return default;
        }
    }

    private static async Task WriteJsonAsync<T>(string path, T value, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, typeInfo, ct).ConfigureAwait(false);
    }
}
