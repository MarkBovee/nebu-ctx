namespace NebuCtx.Server.Host.Dashboard;

/// <summary>
/// Loads the shipped dashboard HTML that is copied beside the published host.
/// </summary>
public static class DashboardHtmlProvider
{
    private const string DashboardHtmlFileName = "dashboard.html";
    private const string DashboardLogoFileName = "logo.png";
    private const string DashboardFaviconFileName = "favicon.ico";
    private const string LogoPlaceholder = "{{LOGO_DATA_URL}}";
    private const string FaviconPlaceholder = "{{FAVICON_DATA_URL}}";

    /// <summary>
    /// Loads the dashboard HTML payload from the application base directory.
    /// </summary>
    /// <returns>The dashboard HTML, or a small fallback page if the asset is unavailable.</returns>
    public static string LoadHtml()
    {
        foreach (var htmlPath in BuildCandidatePaths(DashboardHtmlFileName))
        {
            if (File.Exists(htmlPath))
            {
                return InjectEmbeddedAssets(File.ReadAllText(htmlPath));
            }
        }

        return """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>nebu-ctx Observatory</title>
</head>
<body>
  <h1>nebu-ctx Observatory</h1>
  <p>The server is running, but dashboard.html was not copied to the output directory.</p>
</body>
</html>
""";
    }

    /// <summary>
    /// Injects embedded dashboard asset data URLs into the shipped HTML shell.
    /// </summary>
    /// <param name="html">Dashboard HTML template content.</param>
    /// <returns>HTML with inline data URLs for logo and favicon when available.</returns>
    private static string InjectEmbeddedAssets(string html)
    {
        var logoDataUrl = BuildDataUrl(DashboardLogoFileName, "image/png");
        var faviconDataUrl = BuildDataUrl(DashboardFaviconFileName, "image/x-icon");

        return html
            .Replace(LogoPlaceholder, logoDataUrl ?? "/logo.png", StringComparison.Ordinal)
            .Replace(FaviconPlaceholder, faviconDataUrl ?? "/favicon.ico", StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the shipped dashboard logo path from the application base directory.
    /// </summary>
    /// <returns>The first existing logo path, or <see langword="null"/> when the asset is unavailable.</returns>
    public static string? ResolveLogoPath() => ResolveAssetPath(DashboardLogoFileName);

    /// <summary>
    /// Resolves the shipped dashboard favicon path from the application base directory.
    /// </summary>
    /// <returns>The first existing favicon path, or <see langword="null"/> when the asset is unavailable.</returns>
    public static string? ResolveFaviconPath() => ResolveAssetPath(DashboardFaviconFileName);

    /// <summary>
    /// Resolves a dashboard asset path from the application base directory.
    /// </summary>
    /// <param name="assetFileName">Dashboard asset file name to locate.</param>
    /// <returns>The first existing asset path, or <see langword="null"/> when the asset is unavailable.</returns>
    private static string? ResolveAssetPath(string assetFileName)
    {
        foreach (var assetPath in BuildCandidatePaths(assetFileName))
        {
            if (File.Exists(assetPath))
            {
                return assetPath;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds a data URL for a shipped dashboard asset.
    /// </summary>
    /// <param name="assetFileName">Asset file name to read.</param>
    /// <param name="contentType">MIME type for the asset.</param>
    /// <returns>Base64 data URL when the asset exists; otherwise <see langword="null" />.</returns>
    private static string? BuildDataUrl(string assetFileName, string contentType)
    {
        var assetPath = ResolveAssetPath(assetFileName);
        if (assetPath is null)
        {
            return null;
        }

        return $"data:{contentType};base64,{Convert.ToBase64String(File.ReadAllBytes(assetPath))}";
    }

    /// <summary>
    /// Builds candidate output paths for a dashboard asset file.
    /// </summary>
    /// <param name="assetFileName">Dashboard asset file name to locate.</param>
    /// <returns>Ordered candidate paths under the application base directory.</returns>
    private static string[] BuildCandidatePaths(string assetFileName)
    {
        return
        [
            Path.Combine(AppContext.BaseDirectory, assetFileName),
            Path.Combine(AppContext.BaseDirectory, "Dashboard", assetFileName),
        ];
    }
}
