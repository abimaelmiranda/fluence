using System.Threading.Tasks;
using Fluence.Core.Models.Modules;

namespace Fluence.Core.Abstractions.Modules;

public interface IModuleShutdownParticipant
{
    Task StopAsync(ModuleShutdownContext context);
}
