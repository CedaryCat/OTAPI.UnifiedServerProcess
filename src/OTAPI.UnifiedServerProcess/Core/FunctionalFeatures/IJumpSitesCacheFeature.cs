using Mono.Cecil;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Loggers;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Core.FunctionalFeatures
{
    public interface IJumpSitesCacheFeature : ILoggedComponent { }
    public static class JumpSitesCacheFeatureExtensions
    {

        static readonly Dictionary<MethodDefinition, Dictionary<Instruction, List<Instruction>>> cachedJumpSites =
            new(ReferenceEqualityComparer.Instance);

        #region Tools
        public static Dictionary<Instruction, List<Instruction>> GetMethodJumpSites<TFeature>(this TFeature _, MethodDefinition method) where TFeature : IJumpSitesCacheFeature {
            if (!cachedJumpSites.TryGetValue(method, out var result)) {
                cachedJumpSites.Add(method, result = MonoModCommon.Stack.BuildJumpSitesMap(method));
            }
            return result;
        }
        public static void ClearJumpSitesCache(this IJumpSitesCacheFeature _) => cachedJumpSites.Clear();
        public static void ClearJumpSitesCache(this IJumpSitesCacheFeature _, MethodDefinition method) => cachedJumpSites.Remove(method);
        #endregion
    }
}
