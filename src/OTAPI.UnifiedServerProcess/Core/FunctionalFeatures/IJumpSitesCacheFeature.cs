using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core.FunctionalFeatures
{
    public interface IJumpSitesCacheFeature : ILoggedComponent { }
    public static class JumpSitesCacheFeatureExtensions
    {

        static readonly Dictionary<string, Dictionary<Instruction, List<Instruction>>> cachedJumpSites = [];

        #region Tools
        public static Dictionary<Instruction, List<Instruction>> GetMethodJumpSites<TFeature>(this TFeature _, MethodDefinition method) where TFeature : IJumpSitesCacheFeature {
            var id = method.GetIdentifier();
            if (!cachedJumpSites.TryGetValue(id, out var result)) {
                cachedJumpSites.Add(id, result = MonoModCommon.Stack.BuildJumpSitesMap(method));
            }
            return result;
        }
        public static void ClearJumpSitesCache(this IJumpSitesCacheFeature _) => cachedJumpSites.Clear();
        public static void ClearJumpSitesCache(this IJumpSitesCacheFeature _, MethodDefinition method) => cachedJumpSites.Remove(method.GetIdentifier());
        #endregion
    }
}
