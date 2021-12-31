using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.Util;

namespace ICSharpCode.Decompiler.IL.Transforms
{
	public class FixCompilerGeneratedThis : IILTransform
	{
		public void Run(ILFunction function, ILTransformContext context)
		{
			foreach (var blockContainer in function.Descendants.OfType<BlockContainer>())
			{
				RunOnBlock(blockContainer.Blocks[0], context);
			}
		}

		static void RunOnBlock(Block block, ILTransformContext context)
		{
			for (int i = 0; i < block.Instructions.Count; i++)
			{
				if(block.Instructions[i].MatchStLoc(out ILVariable var, out ILInstruction val))
				{
					if (!var.Type.Name.Contains("<>c__CompilerGenerated"))
						continue;

					if (val.OpCode != OpCode.NewObj)
						continue;

					CallInstruction inst = val as CallInstruction;
					if (inst == null || inst.Arguments.Count != 1)
						continue;

					ILInstruction arg = inst.Arguments[0];
					if (!arg.MatchLdThis())
						continue;

					IMethod method = inst.Method;
					if (!method.IsConstructor)
						continue;

					var.Type = (arg as IInstructionWithVariableOperand).Variable.Type;
					var loads = var.LoadInstructions.ToArray();
					foreach (var exp in loads)
					{
						ILInstruction wrapper = exp.Parent.Parent;
						if (wrapper != null && MatchTrivialWrapper(wrapper, var.Type))
							wrapper.ReplaceWith(new LdLoc(var));

					}

					block.Instructions[i].ReplaceWith(new StLoc(var, arg));
				}
			}
		}

		static bool MatchTrivialWrapper(ILInstruction inst, IType thisType)
		{
			if(inst.MatchLdObj(out ILInstruction targ, out IType type))
			{
				return type.Equals(thisType);
			}

			return false;
		}
	}
}
