using ModFramework;
using Mono.Cecil;
using OTAPI.UnifiedServerProcess.Core.Analysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.DelegateInvocationAnalysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.MethodCallAnalysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.ParameterFlowAnalysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldModificationAnalysis;
using OTAPI.UnifiedServerProcess.Core.Analysis.StaticFieldReferenceAnalysis;
using OTAPI.UnifiedServerProcess.Loggers;

namespace OTAPI.UnifiedServerProcess.Core {
    public class AnalyzerGroups {
        readonly ILogger logger;
        readonly ModuleDefinition module;

        public readonly TypeInheritanceGraph TypeInheritanceGraph;
        public readonly MethodInheritanceGraph MethodInheritanceGraph;
        public readonly DelegateInvocationGraph DelegateInvocationGraph;
        public readonly MethodCallGraph MethodCallGraph;

        private ParameterFlowAnalyzer? parameterFlowAnalyzer;
        private ParamModificationAnalyzer? paramModificationAnalyzer;
        private StaticFieldReferenceAnalyzer? staticFieldReferenceAnalyzer;
        private StaticFieldModificationAnalyzer? staticFieldModificationAnalyzer;
        public ParameterFlowAnalyzer ParameterFlowAnalyzer =>
            parameterFlowAnalyzer ??= new ParameterFlowAnalyzer(logger, module,
                TypeInheritanceGraph,
                MethodCallGraph,
                DelegateInvocationGraph,
                MethodInheritanceGraph);
        public ParamModificationAnalyzer ParamModificationAnalyzer =>
            paramModificationAnalyzer ??= new ParamModificationAnalyzer(logger, module,
                ParameterFlowAnalyzer,
                MethodCallGraph,
                DelegateInvocationGraph,
                MethodInheritanceGraph,
                TypeInheritanceGraph);
        public StaticFieldReferenceAnalyzer StaticFieldReferenceAnalyzer =>
            staticFieldReferenceAnalyzer ??= new StaticFieldReferenceAnalyzer(logger, module,
                TypeInheritanceGraph,
                MethodCallGraph,
                DelegateInvocationGraph,
                MethodInheritanceGraph);
        public StaticFieldModificationAnalyzer StaticFieldModificationAnalyzer =>
            staticFieldModificationAnalyzer ??= new StaticFieldModificationAnalyzer(logger,
                StaticFieldReferenceAnalyzer,
                ParamModificationAnalyzer,
                ParameterFlowAnalyzer,
                MethodCallGraph,
                DelegateInvocationGraph,
                MethodInheritanceGraph,
                TypeInheritanceGraph);

        public AnalyzerGroups(ILogger logger, ModuleDefinition module) {
            this.logger = logger;
            this.module = module;
            var modframework = module.ImportReference(typeof(DefaultCollection<>)).Resolve().Module;

            TypeInheritanceGraph = new TypeInheritanceGraph(module);
            MethodInheritanceGraph = new MethodInheritanceGraph(modframework, module);
            DelegateInvocationGraph = new DelegateInvocationGraph(logger, module, MethodInheritanceGraph);
            MethodCallGraph = new MethodCallGraph(logger, module, DelegateInvocationGraph, MethodInheritanceGraph);
        }
    }
}
