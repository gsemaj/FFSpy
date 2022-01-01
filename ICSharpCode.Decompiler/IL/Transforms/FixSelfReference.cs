using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.Util;

namespace ICSharpCode.Decompiler.IL.Transforms
{
	public class FixSelfReference : IILTransform
	{
		public void Run(ILFunction function, ILTransformContext context)
		{
			foreach (var block in function.Descendants.OfType<Block>())
			{
				if (block.Kind != BlockKind.ControlFlow)
					continue;
				RunOnBlock(block, context);
			}
		}

		static void RunOnBlock(Block block, ILTransformContext context)
		{
			for (int i = 0; i < block.Instructions.Count; i++)
			{
				if (block.Instructions[i].MatchStObj(out ILInstruction targSt, out ILInstruction value, out IType typeSt))
				{
					if (value.MatchLdObj(out ILInstruction targLd, out IType typeLd))
					{
						// types do not match
						if (!typeSt.Equals(typeLd))
							continue;

						// op codes do not match
						if (targSt.OpCode != targLd.OpCode)
							continue;

						// ensure the targets are for actual fields
						IInstructionWithFieldOperand instSt = targSt as IInstructionWithFieldOperand;
						IInstructionWithFieldOperand instLd = targLd as IInstructionWithFieldOperand;
						if (instSt == null)
							continue;

						// ensure the targets are for the same field
						if (instSt.Field != instLd.Field)
							continue;

						// strip the instruction
						block.Instructions.RemoveAt(i);
						int c = ILInlining.InlineInto(block, i, InliningOptions.None, context: context);
						i -= c + 1;
					}
				}
				else if (block.Instructions[i].MatchStLoc(out ILVariable varSt, out ILInstruction inst))
				{
					if(inst.MatchLdLoc(out ILVariable varLd))
					{
						// types do not match
						if (!varSt.Type.Equals(varLd.Type))
							continue;

						// names do not match
						if (!varSt.Name.Equals(varLd.Name))
							continue;

						// strip the instruction
						block.Instructions.RemoveAt(i);
						int c = ILInlining.InlineInto(block, i, InliningOptions.None, context: context);
						i -= c + 1;
					}
				}
			}
		}
	}
}
