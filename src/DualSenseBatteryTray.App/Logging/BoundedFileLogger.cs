using System.IO;
using System.Security;
using System.Text;

namespace DualSenseBatteryTray.App.Logging;

public sealed class BoundedFileLogger
{
    private const int MaximumExceptionMessageCharacters = 16 * 1024;
    private const long MaximumLogBytes = 1_048_576;
    private readonly object _gate = new();
    private readonly string _activePath;
    private readonly string _backupPath;

    public BoundedFileLogger(string? directory = null)
    {
        var logDirectory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DualSenseBatteryTray");
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);

        _activePath = Path.Combine(logDirectory, "app.log");
        _backupPath = $"{_activePath}.1";
    }

    public void Log(string eventName, Exception? error)
    {
        ValidateEventName(eventName);
        var entry = Encoding.UTF8.GetBytes(BuildEntry(eventName, error));

        lock (_gate)
        {
            try
            {
                var directory = Path.GetDirectoryName(_activePath)!;
                Directory.CreateDirectory(directory);

                var currentLength = File.Exists(_activePath)
                    ? new FileInfo(_activePath).Length
                    : 0;
                if (currentLength > 0 && currentLength + entry.Length > MaximumLogBytes)
                    File.Move(_activePath, _backupPath, overwrite: true);

                using var stream = new FileStream(
                    _activePath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read);
                stream.Write(entry);
            }
            catch (Exception loggingError) when (
                loggingError is IOException or UnauthorizedAccessException or SecurityException)
            {
                // Logging is best-effort and must never keep the tray application alive or crash it.
            }
        }
    }

    private static string BuildEntry(string eventName, Exception? error)
    {
        var builder = new StringBuilder(128);
        builder.Append(DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        builder.Append(" event=");
        builder.Append(eventName);

        if (error is not null)
        {
            var message = error.Message;
            if (message.Length > MaximumExceptionMessageCharacters)
                message = message[..MaximumExceptionMessageCharacters];

            builder.Append(" error=");
            foreach (var character in message)
                builder.Append(char.IsControl(character) || character == '"' ? ' ' : character);
        }

        builder.AppendLine();
        return builder.ToString();
    }

    private static void ValidateEventName(string eventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        if (eventName.Length > 64 || eventName.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_'))
        {
            throw new ArgumentException(
                "Event names may contain only ASCII letters, digits, periods, hyphens, and underscores.",
                nameof(eventName));
        }
    }
}
