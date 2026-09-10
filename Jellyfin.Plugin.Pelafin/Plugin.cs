using System.Globalization;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Pelafin;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the Pelafin app configuration as a raw JSON blob.
    /// </summary>
    public string AppConfigJson { get; set; } = "{}";

    /// <summary>
    /// Gets or sets the content type of the uploaded light mode logo, if any.
    /// </summary>
    public string? LogoLightContentType { get; set; }

    /// <summary>
    /// Gets or sets the content type of the uploaded dark mode logo, if any.
    /// </summary>
    public string? LogoDarkContentType { get; set; }
}

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths appPaths, IXmlSerializer xmlSerializer)
        : base(appPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Pelafin";

    public override Guid Id => new("7c3f2a91-5b84-4d26-9e57-a1f08c6b42d3");

    public override string Description =>
        "The companion plugin for the Pelafin app. It allows you to save your Pelafin Configuration.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.configPage.html",
                    GetType().Namespace)
            }
        };
    }
}
