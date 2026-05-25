using System.Diagnostics;

namespace TizenLoaderBRDesktop.Services;

public sealed class BrowserService
{
    public void Open(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
