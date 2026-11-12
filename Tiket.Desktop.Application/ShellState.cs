using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Tiket.Desktop.Application;

public enum Destination { Cashier, Administration, Connection }

/// <summary>Presentation only: navigation never connects or submits work.</summary>
public sealed class ShellState : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public Destination Destination { get; private set; }
    public string ConnectionStatus => "Unconnected — demonstration only";
    public string Station => "Demo station 01";
    public string Event => "Sample event • no backend event selected";
    public bool CanSubmit => false;
    public void Navigate(Destination destination)
    {
        if (!Enum.IsDefined(destination)) throw new ArgumentOutOfRangeException(nameof(destination));
        if (Destination == destination) return;
        Destination = destination;
        OnPropertyChanged(nameof(Destination));
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Future managed endpoint validation; no network or persistence.</summary>
public static class ConnectionConfiguration
{
    public static bool IsValidEndpoint(string? value, bool allowLoopbackDevelopment = false)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment)) return false;
        return uri.Scheme == Uri.UriSchemeHttps ||
            (allowLoopbackDevelopment && uri.Scheme == Uri.UriSchemeHttp &&
             uri.Host == "127.0.0.1");
    }
}
