// Reference stubs for MediaBrowser.Model 10.11 (signatures copied from v10.11.11).
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Model.Plugins
{
    public class BasePluginConfiguration { }
}

namespace MediaBrowser.Model.Serialization
{
    public interface IXmlSerializer { }
}

namespace MediaBrowser.Model.Tasks
{
    public interface IScheduledTask
    {
        string Name { get; }
        string Key { get; }
        string Description { get; }
        string Category { get; }
        Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken);
        IEnumerable<TaskTriggerInfo> GetDefaultTriggers();
    }

    public class TaskTriggerInfo
    {
        public TaskTriggerInfoType Type { get; set; }
        public long? TimeOfDayTicks { get; set; }
        public long? IntervalTicks { get; set; }
        public DayOfWeek? DayOfWeek { get; set; }
        public long? MaxRuntimeTicks { get; set; }
    }
}
