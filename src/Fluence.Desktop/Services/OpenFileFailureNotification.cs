using System;
using System.IO;
using System.Text;
using Fluence.Application.Workspace;

namespace Fluence.Desktop.Services;

internal static class OpenFileFailureNotification
{
    public static bool TryShow(IUserNotificationService notifications, string path, Exception exception)
    {
        var reason = exception switch
        {
            UnsupportedTextFileException unsupported => unsupported.Reason,
            UnauthorizedAccessException => "Access was denied.",
            IOException => "The file could not be read.",
            DecoderFallbackException => "This file could not be decoded as text.",
            _ => null,
        };

        if (reason is null)
        {
            return false;
        }

        var fileName = Path.GetFileName(path);
        notifications.ShowWarning("Unable to open file", $"{fileName}: {reason}");
        return true;
    }
}
