using ModFramework;
using Mono.Cecil.Cil;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments;
using OTAPI.UnifiedServerProcess.Loggers;
using System;

namespace OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching
{
    /// <summary>
    /// After contextualization, lazy initialization logic originally bound to specific static types is integrated into contextualized type constructors and executed sequentially during RootContext construction.
    /// <para>This premature execution may trigger unintended logic flows when accessing variables whose values are supposed to be modified after specific initialization stages.</para>
    /// <para>To prevent this, we preinitialize default values of critical variables within targeted contextual constructors.</para>
    /// </summary>
    /// <param name="logger"></param>
    public class TrivialDefaultValuePatcher(ILogger logger) : GeneralPatcher(logger)
    {
        public override string Name => nameof(TrivialDefaultValuePatcher);

        public override void Patch(PatcherArguments arguments) {
            var main = arguments.ContextTypes["Terraria.Main" + Constants.ContextSuffix];

            // Because the RootContext contains most of the static conversion entities,
            // this makes it possible for some lazy loading logic to be triggered earlier (before Program.RunGame).

            // Such as Main.dedServ is only set to true in Program.RunGame,
            // so setting MainSystenContext.dedServ to true in the initialization is very necessary, otherwise some servers that should not run will be executed incorrectly.
            var mainCtor = main.constructor;
            var dedServ = main.ContextTypeDef.Field("dedServ");
            var baseCtorCall = MonoModCommon.IL.GetBaseConstructorCall(mainCtor.Body) ?? throw new Exception("Failed to find base constructor call");
            var il = mainCtor.Body.GetILProcessor();
            il.InsertBefore(baseCtorCall, Instruction.Create(OpCodes.Ldarg_0));
            il.InsertBefore(baseCtorCall, Instruction.Create(OpCodes.Ldc_I4_1));
            il.InsertBefore(baseCtorCall, Instruction.Create(OpCodes.Stfld, dedServ));
        }
    }
}
