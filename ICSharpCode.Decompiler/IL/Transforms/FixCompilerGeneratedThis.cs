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
				if(block.Instructions[i].MatchStLoc(out ILVariable vari, out ILInstruction val))
				{
					// not sure how to actually check the attribute for vars
					if (!vari.Type.Name.Contains("<>c__CompilerGenerated"))
						continue;

					// ensure instruction is newobj
					if (val.OpCode != OpCode.NewObj)
						continue;

					// ensure instruction is a call instruction with exactly one arg
					CallInstruction inst = val as CallInstruction;
					if (inst == null || inst.Arguments.Count != 1)
						continue;

					// ensure that one arg is `this`
					ILInstruction arg = inst.Arguments[0];
					if (!arg.MatchLdThis())
						continue;

					// ensure the call is to a constructor
					IMethod method = inst.Method;
					if (!method.IsConstructor)
						continue;

					// set variable type to the same type as `this`
					vari.Type = (arg as IInstructionWithVariableOperand).Variable.Type;

					// iterate through uses and simplify them
					var loads = vari.LoadInstructions.ToArray();
					foreach (var exp in loads)
					{
						ILInstruction wrapper = exp.Parent.Parent;
						if (wrapper != null && MatchWrapper(wrapper, vari.Type))
							wrapper.ReplaceWith(new LdLoc(vari));
					}

					// replace cgo with a simple reference that will get cleaned up by CopyPropogation
					block.Instructions[i].ReplaceWith(new StLoc(vari, arg));
				}
			}
		}

		static bool MatchWrapper(ILInstruction inst, IType thisType)
		{
			if(inst.MatchLdObj(out ILInstruction targ, out IType type))
			{
				return type.Equals(thisType);
			}

			return false;
		}
	}
}
