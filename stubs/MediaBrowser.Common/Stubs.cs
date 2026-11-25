// Reference stubs for MediaBrowser.Common 10.11 (signatures copied from v10.11.11).
using System;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace MediaBrowser.Common.Configuration
{
    public interface IApplicationPaths
    {
        string DataPath { get; }
        string PluginConfigurationsPath { get; }
    }
}

namespace MediaBrowser.Common.Plugins
{
    public abstract class BasePlugin
    {
        public abstract string Name { get; }
        public virtual string Description => string.Empty;
        public virtual Guid Id { get; private set; }
        public string DataFolderPath { get; private set; } = string.Empty;
    }

    public abstract class BasePlugin<TConfigurationType> : BasePlugin
        where TConfigurationType : BasePluginConfiguration
    {
        protected BasePlugin(MediaBrowser.Common.Configuration.IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) { }
        public TConfigurationType Configuration { get; protected set; } = null!;
        public virtual void SaveConfiguration() { }
    }
}
