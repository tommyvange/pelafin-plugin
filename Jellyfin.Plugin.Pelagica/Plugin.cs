using System.Globalization;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Pelagica;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the Pelagica app configuration as a raw JSON blob.
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

    public override string Name => "Pelagica";

    public override Guid Id => new("3b9ad352-24fd-4792-a41d-b7673744bb03");

    public override string Description =>
        "The companion plugin for the Pelagica app. It allows you to save your Pelagica Configuration.";

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
