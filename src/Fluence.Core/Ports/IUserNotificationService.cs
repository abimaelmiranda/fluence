namespace Fluence.Core.Ports;

public interface IUserNotificationService
{
    void ShowWarning(string title, string message);

    void ShowError(string title, string message);
}
