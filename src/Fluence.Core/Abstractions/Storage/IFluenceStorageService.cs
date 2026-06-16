using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Core.Abstractions.Storage;

public interface IFluenceStorageService
{
    // User scope — ~/.fluence/
    Task<T?> ReadUserAsync<T>(string fileName, JsonTypeInfo<T> typeInfo, CancellationToken ct = default);
    Task WriteUserAsync<T>(string fileName, T value, JsonTypeInfo<T> typeInfo, CancellationToken ct = default);
    string GetUserPath(string subPath = "");

    // Project scope — {workspaceRoot}/.fluence/
    Task<T?> ReadProjectAsync<T>(string workspaceRoot, string fileName, JsonTypeInfo<T> typeInfo, CancellationToken ct = default);
    Task WriteProjectAsync<T>(string workspaceRoot, string fileName, T value, JsonTypeInfo<T> typeInfo, CancellationToken ct = default);
    string GetProjectPath(string workspaceRoot, string subPath = "");
}
