namespace TizenLoaderBRDesktop.Models;

public sealed class InstalledTizenApp
{
    public string PackageId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string PackageType { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string RawLine { get; set; } = string.Empty;

    public string IconSource { get; set; } = "/Assets/Images/app-logo-transparent.png";

    public string MiniIconText
    {
        get
        {
            var source = !string.IsNullOrWhiteSpace(Name) ? Name : PackageId;
            if (string.IsNullOrWhiteSpace(source))
            {
                return "?";
            }

            var tokens = source
                .Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.Length > 0)
                .ToArray();

            if (tokens.Length >= 2)
            {
                return $"{char.ToUpperInvariant(tokens[0][0])}{char.ToUpperInvariant(tokens[1][0])}";
            }

            var first = tokens.Length == 1 ? tokens[0] : source;
            return first.Length >= 2
                ? first[..2].ToUpperInvariant()
                : first.ToUpperInvariant();
        }
    }

    public string MiniIconBrush
    {
        get
        {
            var seed = string.IsNullOrWhiteSpace(PackageId) ? Name : PackageId;
            if (string.IsNullOrWhiteSpace(seed))
            {
                return "#FF2D6FB3";
            }

            var hash = seed.Aggregate(0, (current, ch) => current * 31 + ch);
            var palette = new[]
            {
                "#FF2D6FB3",
                "#FF2A8F6A",
                "#FF6F63C5",
                "#FFB36A2D",
                "#FF9A3E87",
                "#FF4C7EA7"
            };

            var index = Math.Abs(hash) % palette.Length;
            return palette[index];
        }
    }
}
