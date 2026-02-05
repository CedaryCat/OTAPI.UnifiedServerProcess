using ModFramework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using OTAPI.UnifiedServerProcess.Commons;
using OTAPI.UnifiedServerProcess.Core.Patching.GeneralPatching.Arguments;
using OTAPI.UnifiedServerProcess.Extensions;
using OTAPI.UnifiedServerProcess.Loggers;
using System;
using System.Linq;

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
            var dedServ = main.ContextTypeDef.GetField("dedServ");

            var baseCtorCall = MonoModCommon.IL.GetBaseConstructorCall(mainCtor.Body) ?? throw new Exception("Failed to find base constructor call");
            var il = mainCtor.Body.GetILProcessor();
            il.InsertBefore(baseCtorCall, Instruction.Create(OpCodes.Ldarg_0));
            il.InsertBefore(baseCtorCall, Instruction.Create(OpCodes.Ldc_I4_1));
            il.InsertBefore(baseCtorCall, Instruction.Create(OpCodes.Stfld, dedServ));

            // Memory for tiles should only be allocated when needed. Before that, Main.tile can be initialized with a meaningless 2x2 size 
            // (to avoid issues caused by null or 1x1 allocations). 
            // Therefore, set rightWorld and bottomWorld to 16, so that in subsequent logic calculations:
            // initial maxTilesX = rightWorld / 16 + 1 = 2
            // initial maxTilesY = bottomWorld / 16 + 1 = 2

            var loadRightWorldConst = mainCtor.Body.Instructions.Single(inst =>
                inst.OpCode == OpCodes.Ldc_R4 &&
                inst.Next.MatchStfld(out var rightWorldField) &&
                rightWorldField.Name == "rightWorld");
            loadRightWorldConst.Operand = 16f;

            var loadBottomWorldConst = mainCtor.Body.Instructions.Single(inst =>
                inst.OpCode == OpCodes.Ldc_R4 &&
                inst.Next.MatchStfld(out var bottomWorldField) &&
                bottomWorldField.Name == "bottomWorld");
            loadBottomWorldConst.Operand = 16f;

            var worldGen = arguments.ContextTypes["Terraria.WorldGen" + Constants.ContextSuffix];
            var clearWorld = worldGen.ContextTypeDef.GetMethod("mfwh_clearWorld");
            var resetWorldSize = new MethodDefinition("ResetWorldSize", MethodAttributes.Public | MethodAttributes.HideBySig, arguments.MainModule.TypeSystem.Void);
            worldGen.ContextTypeDef.Methods.Add(resetWorldSize);
            var body = resetWorldSize.Body = new MethodBody(resetWorldSize);
            var local_main = new VariableDefinition(main.ContextTypeDef);
            body.Variables.Add(local_main);
            var tileField = main.ContextTypeDef.GetField("tile");
            var maxTilesX = main.ContextTypeDef.GetField("maxTilesX");
            var maxTilesY = main.ContextTypeDef.GetField("maxTilesY");
            var tileProviderCreate = new MethodReference("Create", tileField.FieldType, tileField.FieldType);
            tileProviderCreate.Parameters.AddRange([
                new(arguments.MainModule.TypeSystem.Int32),
                new(arguments.MainModule.TypeSystem.Int32),
                new(arguments.MainModule.TypeSystem.String),
            ]);

            var placeHolder = Instruction.Create(OpCodes.Ldloc_0);

            body.Instructions.AddRange([
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Ldfld, worldGen.rootContextField),
                Instruction.Create(OpCodes.Ldfld, arguments.RootContextDef.GetField("Main")),
                Instruction.Create(OpCodes.Stloc_0),

                Instruction.Create(OpCodes.Ldloc_0),
                Instruction.Create(OpCodes.Ldfld, tileField),
                Instruction.Create(OpCodes.Callvirt, new MethodReference("get_Width", arguments.MainModule.TypeSystem.Int32, tileField.FieldType) { HasThis = true }),

                Instruction.Create(OpCodes.Ldloc_0),
                Instruction.Create(OpCodes.Ldfld, maxTilesX),
                Instruction.Create(OpCodes.Bne_Un_S, placeHolder),

                Instruction.Create(OpCodes.Ldloc_0),
                Instruction.Create(OpCodes.Ldfld, tileField),
                Instruction.Create(OpCodes.Callvirt, new MethodReference("get_Width", arguments.MainModule.TypeSystem.Int32, tileField.FieldType) { HasThis = true }),

                Instruction.Create(OpCodes.Ldloc_0),
                Instruction.Create(OpCodes.Ldfld, maxTilesY),
                Instruction.Create(OpCodes.Bne_Un_S, placeHolder),
                Instruction.Create(OpCodes.Ret),
                placeHolder,
                Instruction.Create(OpCodes.Ldfld, tileField),
                Instruction.Create(OpCodes.Callvirt, new MethodReference("Dispose", arguments.MainModule.TypeSystem.Void, tileField.FieldType) { HasThis = true }),

                Instruction.Create(OpCodes.Ldloc_0),
                Instruction.Create(OpCodes.Ldloc_0),
                Instruction.Create(OpCodes.Ldfld, maxTilesX),
                Instruction.Create(OpCodes.Ldloc_0),
                Instruction.Create(OpCodes.Ldfld, maxTilesY),
                Instruction.Create(OpCodes.Ldstr, resetWorldSize.FullName),
                Instruction.Create(OpCodes.Call, tileProviderCreate),
                Instruction.Create(OpCodes.Stfld, tileField),
                Instruction.Create(OpCodes.Ret),
            ]);

            var cursor = clearWorld.GetILCursor();
            cursor
                .Goto(cursor.Body.Instructions.First())
                .Emit(OpCodes.Ldarg_0)
                .Emit(OpCodes.Call, resetWorldSize);
        }
    }
}
