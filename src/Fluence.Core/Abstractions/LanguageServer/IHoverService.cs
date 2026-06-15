using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Core.Abstractions.LanguageServer;

public interface IHoverService
{
    Task<LspHover?> GetHoverAsync(string filePath, int line, int character, CancellationToken cancellationToken = default);
}
