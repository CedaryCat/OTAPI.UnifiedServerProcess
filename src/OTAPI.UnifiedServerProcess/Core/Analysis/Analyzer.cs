using OTAPI.UnifiedServerProcess.Core.FunctionalFeatures;
using OTAPI.UnifiedServerProcess.Loggers;

namespace OTAPI.UnifiedServerProcess.Core.Analysis {
    public abstract class Analyzer(ILogger logger) : LoggedComponent(logger), IJumpSitesCacheFeature {
    }
}
