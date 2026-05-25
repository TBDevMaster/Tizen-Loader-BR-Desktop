using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using TizenLoaderBRDesktop.Helpers;
using TizenLoaderBRDesktop.Models;

namespace TizenLoaderBRDesktop.Services;

public sealed class LogService
{
    public ObservableCollection<LogEntry> Entries { get; } = new();

    public void Info(string category, string message) => Add("INFO", category, message);

    public void Warn(string category, string message) => Add("WARN", category, message);

    public void Error(string category, string message) => Add("ERRO", category, message);

    public void Add(string level, string category, string message)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTimeOffset.Now,
            Level = level,
            Category = category,
            Message = message
        };

        void Insert()
        {
            Entries.Add(entry);
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(Insert);
            return;
        }

        Insert();
    }

    public string GetPlainText()
    {
        var sb = new StringBuilder();
        foreach (var entry in Entries)
        {
            sb.Append('[')
              .Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"))
              .Append("] [")
              .Append(entry.Level)
              .Append("] [")
              .Append(entry.Category)
              .Append("] ")
              .AppendLine(entry.Message);
        }

        return sb.ToString();
    }

    public void Clear()
    {
        var dispatcher = Application.Current?.Dispatcher;
        void ClearEntries() => Entries.Clear();
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(ClearEntries);
            return;
        }

        ClearEntries();
    }

    public void SaveToFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppPaths.BaseFolder);
        File.WriteAllText(path, GetPlainText(), Encoding.UTF8);
    }
}
