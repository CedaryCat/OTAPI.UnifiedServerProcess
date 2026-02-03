using ModFramework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;

namespace OTAPI.UnifiedServerProcess.Core;

public class PatchProjHookSets(ModuleDefinition module)
{
    public void Patch() {

        var setDefaultsMDef = module.GetType("Terraria.Projectile").Method(nameof(Projectile.mfwh_SetDefaults));

        static List<StyleExtractor.Rule> CoalesceByAssignmentSite(IEnumerable<StyleExtractor.Rule> rules) {
            return [.. rules
                .GroupBy(r => (r.ILOffset, r.AiStyle))
                .Select(g => {
                    var td = StyleExtractor.TypeDomain.Universe();
                    foreach (var r in g) td.UnionWith(r.TypeConstraint);
                    return new StyleExtractor.Rule(td, g.Key.AiStyle, g.Key.ILOffset);
                })
                .OrderBy(r => r.ILOffset)];
        }
        static bool Matches(StyleExtractor.TypeDomain d, int id) {
            if (d.Possible != null) return d.Possible.Contains(id);
            return !d.Excluded.Contains(id);
        }
        var rules = CoalesceByAssignmentSite(StyleExtractor.Extract(setDefaultsMDef));


        var mainTDef = module.GetType("Terraria.Main");
        var initProjHookMDef = new MethodDefinition("Initialize_ProjHook", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
        var body = initProjHookMDef.Body = new MethodBody(initProjHookMDef);
        var il = body.GetILProcessor();

        mainTDef.Methods.Add(initProjHookMDef);

        var mfwh_Initialize_AlmostEverything = mainTDef.Method(nameof(Main.mfwh_Initialize_AlmostEverything));

        var field = mainTDef.Field("projHook");

        //for (int i = 0; i < ProjectileID.Count; i++) {
        //    var proj = new Projectile();
        //    proj.SetDefaults(i);
        //    if (proj.aiStyle == 7) {
        //        il.Append(Instruction.Create(OpCodes.Ldsfld, field));
        //        il.Append(Instruction.Create(OpCodes.Ldc_I4, i));
        //        il.Append(Instruction.Create(OpCodes.Ldc_I4_1));
        //        il.Append(Instruction.Create(OpCodes.Stelem_I1));
        //    }
        //}

        for (int id = 0; id <= ProjectileID.Count; id++) {
            int? bestAi = null;
            int bestOff = int.MinValue;

            foreach (var r in rules) {
                if (!Matches(r.TypeConstraint, id)) continue;
                if (r.ILOffset >= bestOff) {
                    bestOff = r.ILOffset;
                    bestAi = r.AiStyle;
                }
            }

            if (bestAi.HasValue && bestAi.Value is 7) {
                il.Append(Instruction.Create(OpCodes.Ldsfld, field));
                il.Append(Instruction.Create(OpCodes.Ldc_I4, id));
                il.Append(Instruction.Create(OpCodes.Ldc_I4_1));
                il.Append(Instruction.Create(OpCodes.Stelem_I1));
            }
        }


        il.Append(Instruction.Create(OpCodes.Ret));

        il = mfwh_Initialize_AlmostEverything.Body.GetILProcessor();
        var inst = mfwh_Initialize_AlmostEverything.Body.Instructions[0];
        if (Match(ref inst,
            i => i is Instruction { OpCode.Code: Code.Ldfld, Operand: FieldReference { Name: "aiStyle" } },
            i => i.OpCode == OpCodes.Ldc_I4_7,
            i => i.OpCode.Code is Code.Bne_Un_S or Code.Bne_Un,
            i => i is Instruction { OpCode.Code: Code.Ldsfld, Operand: FieldReference { Name: "projHook" } },
            i => true,
            i => i.OpCode == OpCodes.Ldc_I4_1,
            i => i.OpCode == OpCodes.Stelem_I1
            )) {
        }
        else {
            throw new InvalidOperationException("Failed to match projHook set pattern in Initialize_AlmostEverything");
        }

        inst.OpCode = OpCodes.Pop;
        inst.Operand = null;

        for (int i = 0; i < 6; i++) {
            mfwh_Initialize_AlmostEverything.Body.Instructions.Remove(inst.Next);
        }

        if (Match(ref inst,
            i => i is Instruction { 
                OpCode.Code: Code.Ldfld, 
                Operand: FieldReference { DeclaringType.FullName: "Terraria.ID.ProjectileID", Name: "aiStyle" } },
            i => i.OpCode.Code is Code.Blt_S or Code.Blt
        )) {
            il.InsertAfter(inst.Next, Instruction.Create(OpCodes.Call, initProjHookMDef));
        }
        else {
            throw new InvalidOperationException("Failed to match projHook set pattern loop end");
        }
    }
    static bool Match(ref Instruction cur, params Span<Predicate<Instruction>> predicates) {
        if (predicates.Length == 0)
            return true;
        for (var start = cur; start != null; start = start.Next) {
            var ins = start;
            int i = 0;
            while (i < predicates.Length) {
                if (ins == null) break;

                var pred = predicates[i];
                if (pred == null || !pred(ins)) break;

                ins = ins.Next;
                i++;
            }
            if (i == predicates.Length) {
                cur = start;
                return true;
            }
        }
        return false;
    }

    static class StyleExtractor
    {
        public sealed class TypeDomain
        {
            public HashSet<int>? Possible;     // null => Universe
            public HashSet<int> Excluded = [];

            public static TypeDomain Universe() => new() { Possible = null };
            public static TypeDomain FromSingle(int k) => new() { Possible = [k] };

            public TypeDomain Clone() {
                return new TypeDomain {
                    Possible = Possible == null ? null : [.. Possible],
                    Excluded = [.. Excluded]
                };
            }

            public bool IsEmpty() {
                return Possible != null && Possible.Count == 0;
            }

            public void ApplyEq(int k) {
                if (Possible == null) {
                    Possible = [k];
                    Excluded.Clear();
                }
                else {
                    Possible.RemoveWhere(v => v != k);
                }
            }

            public void ApplyNeq(int k) {
                if (Possible == null) Excluded.Add(k);
                else Possible.Remove(k);
            }

            // Join as union of possible values (over-approx is fine for rules).
            public void UnionWith(TypeDomain other) {
                if (this.Possible == null || other.Possible == null) {
                    // Universe U X = Universe, but exclusions must be intersected (only excluded on ALL paths stay excluded).
                    if (this.Possible != null && other.Possible == null) {
                        // this is finite, other is universe => universe wins
                        this.Possible = null;
                        this.Excluded = [.. other.Excluded];
                    }
                    else if (this.Possible == null && other.Possible != null) {
                        // keep universe; exclusions become intersection
                        this.Excluded.IntersectWith(other.Excluded);
                    }
                    else {
                        // both universe
                        this.Excluded.IntersectWith(other.Excluded);
                    }
                    return;
                }

                this.Possible.UnionWith(other.Possible);
            }

            public override string ToString() {
                if (Possible != null)
                    return "{" + string.Join(",", Possible.OrderBy(x => x)) + "}";
                if (Excluded.Count == 0) return "ANY";
                return "ANY \\ {" + string.Join(",", Excluded.OrderBy(x => x)) + "}";
            }
        }

        public sealed record Rule(TypeDomain TypeConstraint, int AiStyle, int ILOffset);

        private sealed record State(TypeDomain Type, int? AiStyle);

        // --- CFG ---
        private sealed class Block
        {
            public int Id;
            public Instruction First = null!;
            public Instruction Last = null!;
            public List<Instruction> Insns = [];
            public List<Edge> Succ = [];
        }

        private sealed record Edge(Block To, Cond? Condition);

        private sealed record Cond(bool IsEq, int K); // IsEq=true => (type==K), else (type!=K)

        public static List<Rule> Extract(MethodDefinition m) {
            if (!m.HasBody) throw new ArgumentException("Method has no body.");
            var cfg = BuildCfg(m);

            // Disjunctive states per block: we don't merge different AiStyle values (keeps precision for mapping).
            var inStates = cfg.ToDictionary(b => b, _ => new List<State>());
            var work = new Queue<(Block b, State s)>();

            void Enqueue(Block b, State s) {
                if (s.Type.IsEmpty()) return;

                // merge only if AiStyle matches; union type domains
                var list = inStates[b];
                for (int i = 0; i < list.Count; i++) {
                    if (list[i].AiStyle == s.AiStyle) {
                        var merged = list[i] with { Type = list[i].Type.Clone() };
                        merged.Type.UnionWith(s.Type);
                        list[i] = merged;
                        // re-enqueue because state changed
                        work.Enqueue((b, list[i]));
                        return;
                    }
                }

                list.Add(s);
                work.Enqueue((b, s));
            }

            // entry
            Enqueue(cfg[0], new State(TypeDomain.Universe(), null));

            var rules = new List<Rule>();

            while (work.Count > 0) {
                var (b, inS) = work.Dequeue();

                // interpret block sequentially
                var cur = new State(inS.Type.Clone(), inS.AiStyle);

                foreach (var ins in b.Insns) {
                    if (IsAiStyleStore(ins, out var aiConst)) {
                        cur = cur with { AiStyle = aiConst };
                        rules.Add(new Rule(cur.Type.Clone(), aiConst, ins.Offset));
                    }
                }

                // propagate to successors with condition refinement
                foreach (var e in b.Succ) {
                    var next = cur;
                    if (e.Condition is Cond c) {
                        var td = next.Type.Clone();
                        if (c.IsEq) td.ApplyEq(c.K);
                        else td.ApplyNeq(c.K);
                        next = next with { Type = td };
                    }
                    Enqueue(e.To, next);
                }
            }
            return CoalesceRules(rules);
        }

        private static List<Rule> CoalesceRules(List<Rule> rules) {
            var seen = new HashSet<string>();
            var outList = new List<Rule>();
            foreach (var r in rules) {
                var key = $"{r.ILOffset}:{r.AiStyle}:{r.TypeConstraint}";
                if (seen.Add(key)) outList.Add(r);
            }
            return outList;
        }

        private static bool IsAiStyleStore(Instruction stfldInsn, out int aiConst) {
            aiConst = default;
            if (stfldInsn.OpCode.Code != Code.Stfld) return false;
            if (stfldInsn.Operand is not FieldReference fr) return false;
            if (!string.Equals(fr.Name, "aiStyle", StringComparison.Ordinal)) return false;

            var prev = stfldInsn.Previous;
            return prev != null && TryGetI4(prev, out aiConst);
        }

        private static bool TryGetI4(Instruction ins, out int value) {
            value = 0;
            return ins.OpCode.Code switch {
                Code.Ldc_I4_M1 => (value = -1) == -1,
                Code.Ldc_I4_0 => (value = 0) == 0,
                Code.Ldc_I4_1 => (value = 1) == 1,
                Code.Ldc_I4_2 => (value = 2) == 2,
                Code.Ldc_I4_3 => (value = 3) == 3,
                Code.Ldc_I4_4 => (value = 4) == 4,
                Code.Ldc_I4_5 => (value = 5) == 5,
                Code.Ldc_I4_6 => (value = 6) == 6,
                Code.Ldc_I4_7 => (value = 7) == 7,
                Code.Ldc_I4_8 => (value = 8) == 8,
                Code.Ldc_I4_S => (value = (sbyte)ins.Operand) == value,
                Code.Ldc_I4 => (value = (int)ins.Operand) == value,
                _ => false
            };
        }

        private static List<Block> BuildCfg(MethodDefinition m) {
            var ins = m.Body.Instructions;
            if (ins.Count == 0) return [];

            // leaders: first, branch targets, fallthrough after branch/switch
            var leaders = new HashSet<Instruction> { ins[0] };

            foreach (var i in ins) {
                if (i.Operand is Instruction t) {
                    leaders.Add(t);
                    if (i.Next != null) leaders.Add(i.Next);
                }
                else if (i.Operand is Instruction[] ts) {
                    foreach (var t2 in ts) leaders.Add(t2);
                    if (i.Next != null) leaders.Add(i.Next);
                }
            }

            // build blocks by scanning instructions
            var blocks = new List<Block>();
            var map = new Dictionary<Instruction, Block>();
            Block? cur = null;

            int id = 0;
            foreach (var i in ins) {
                if (leaders.Contains(i)) {
                    cur = new Block { Id = id++, First = i };
                    blocks.Add(cur);
                }
                cur!.Insns.Add(i);
                map[i] = cur;
            }
            foreach (var b in blocks) b.Last = b.Insns[^1];

            // add edges
            foreach (var b in blocks) {
                var last = b.Last;
                if (last.OpCode.FlowControl == FlowControl.Return ||
                    last.OpCode.FlowControl == FlowControl.Throw) {
                    continue;
                }

                if (last.OpCode.Code == Code.Br || last.OpCode.Code == Code.Br_S) {
                    var tgt = (Instruction)last.Operand;
                    b.Succ.Add(new Edge(map[tgt], null));
                    continue;
                }

                if (last.OpCode.FlowControl == FlowControl.Cond_Branch) {
                    // conditional branch: taken + fallthrough
                    if (last.Operand is Instruction tgt) {
                        // Try extract (type == K) or (type != K) from the compare instruction pattern immediately before branch
                        // Supports: beq / bne.un where stack has [type, const]
                        Cond? takenCond = TryParseTypeCondFromBranch(last, takenBranch: true);
                        Cond? fallCond = TryParseTypeCondFromBranch(last, takenBranch: false);

                        b.Succ.Add(new Edge(map[tgt], takenCond));
                        if (last.Next != null)
                            b.Succ.Add(new Edge(map[last.Next], fallCond));
                    }
                    else if (last.OpCode.Code == Code.Switch) {
                        var tgts = (Instruction[])last.Operand;
                        foreach (var t in tgts) b.Succ.Add(new Edge(map[t], null));
                        if (last.Next != null) b.Succ.Add(new Edge(map[last.Next], null));
                    }
                    continue;
                }

                // default fallthrough
                if (last.Next != null)
                    b.Succ.Add(new Edge(map[last.Next], null));
            }

            return blocks;
        }

        private static Cond? TryParseTypeCondFromBranch(Instruction branch, bool takenBranch) {
            // handle beq/beq.s and bne.un/bne.un.s patterns:
            //   ... ldfld type
            //       ldc.i4 K
            //       bne.un.s target   (taken => type != K, fall => type == K)
            //       beq.s target      (taken => type == K, fall => type != K)

            if (branch.Previous == null || branch.Previous.Previous == null) return null;

            var cst = branch.Previous;
            var lhs = branch.Previous.Previous;

            if (!TryGetI4(cst, out int k)) return null;
            if (!IsLoadNpcType(lhs)) return null;

            switch (branch.OpCode.Code) {
                case Code.Bne_Un:
                case Code.Bne_Un_S:
                    return takenBranch ? new Cond(IsEq: false, K: k) : new Cond(IsEq: true, K: k);

                case Code.Beq:
                case Code.Beq_S:
                    return takenBranch ? new Cond(IsEq: true, K: k) : new Cond(IsEq: false, K: k);

                default:
                    return null;
            }
        }

        private static bool IsLoadNpcType(Instruction ins) {
            if (ins.OpCode.Code != Code.Ldfld) return false;
            if (ins.Operand is not FieldReference fr) return false;
            if (!string.Equals(fr.Name, "type", StringComparison.Ordinal)) return false;

            return fr.DeclaringType.FullName == "Terraria.Projectile";
        }
    }
}

