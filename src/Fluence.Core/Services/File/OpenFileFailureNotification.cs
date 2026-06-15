using System;
using System.IO;
using System.Text;
using Fluence.Core.Abstractions.Exceptions;
using Fluence.Core.Abstractions.Notifications;

namespace Fluence.Core.Services.File;

public static class OpenFileFailureNotification
{
    public static bool TryShow(IUserNotificationService notifications, string path, Exception exception)
    {
        var reason = exception switch
        {
            FluenceExceptionBase fluenceException => fluenceException.UserMessage,
            UnauthorizedAccessException => "Access was denied.",
            IOException => "The file could not be read.",
            DecoderFallbackException => "This file could not be decoded as text.",
            _ => null,
        };

        if (reason is null)
            return false;

        var fileName = Path.GetFileName(path);
        notifications.ShowWarning("Unable to open file", $"{fileName}: {reason}");
        return true;
    }
}
