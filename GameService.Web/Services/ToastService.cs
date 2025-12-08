namespace GameService.Web.Services;

public class ToastService
{
    public event Action<string, string, ToastLevel>? OnShow;

    public void ShowSuccess(string message, string title = "Success")
    {
        OnShow?.Invoke(title, message, ToastLevel.Success);
    }

    public void ShowError(string message, string title = "Error")
    {
        OnShow?.Invoke(title, message, ToastLevel.Error);
    }

    public void ShowInfo(string message, string title = "Info")
    {
        OnShow?.Invoke(title, message, ToastLevel.Info);
    }

    public void ShowWarning(string message, string title = "Warning")
    {
        OnShow?.Invoke(title, message, ToastLevel.Warning);
    }
}

public enum ToastLevel
{
    Info,
    Success,
    Warning,
    Error
}