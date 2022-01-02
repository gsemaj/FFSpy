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

					// first pass to simplify basic wrapper
					var loads = vari.LoadInstructions.ToArray();
					var nestedFields = new List<(IField f, ILInstruction p)>();
					foreach (var exp in loads)
					{
						ILInstruction loadInstr = exp.Parent.Parent;

						// if just a wrapper for `this`, simplify immediately
						if (MatchWrapper(loadInstr, vari.Type))
						{
							loadInstr.ReplaceWith(new LdLoc(vari));
							continue;
						}

						// if it's copying the param to an extra field, make a note to replace references
						if (MatchNestedField(loadInstr, vari.Type, out ILInstruction param, out IField field))
						{
							// i actually can't figure out how to straight up remove this instruction safely within the loop, so
							// instead replace it with a nop and clean it up later
							// i don't think there are any other nops in the code at this point, so this should be safe
							loadInstr.ReplaceWith(new Nop());
							// make note of this field so we can replace all references to it later
							nestedFields.Add((field, param));
						}
					}

					// second pass to find and replace references to any nested fields
					loads = vari.LoadInstructions.ToArray();
					foreach(var exp in loads)
					{
						ILInstruction loadInstr = exp.Parent;
						foreach(var sub in nestedFields)
						{
							if (MatchNestedFieldReference(loadInstr, sub.f))
							{
								if(sub.p.MatchLdLoc(out _))
								{
									loadInstr.ReplaceWith(new LdLoca((sub.p as IInstructionWithVariableOperand).Variable));
								}
								else if(sub.p.MatchUnbox(out _, out _))
								{
									loadInstr.ReplaceWith(sub.p.Clone());
								}
								
								break;
							}
						}
					}

					// replace cgo with a simple reference that will get cleaned up by CopyPropogation
					block.Instructions[i].ReplaceWith(new StLoc(vari, arg));
				}
			}

			CleanupNops(block, context); // get rid of those nops we put in
		}

		static void CleanupNops(Block block, ILTransformContext context)
		{
			for(int i = 0; i < block.Instructions.Count; i++)
			{
				if(block.Instructions[i].MatchNop())
				{
					// remove instruction
					block.Instructions.RemoveAt(i);
					int c = ILInlining.InlineInto(block, i, InliningOptions.None, context: context);
					i -= c + 1;
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

		static bool MatchNestedField(ILInstruction inst, IType thisType, out ILInstruction param, out IField nField)
		{
			param = null;
			nField = null;

			if(inst.MatchStObj(out ILInstruction targ, out ILInstruction val, out IType type))
			{
				if(targ.MatchLdFlda(out _, out IField field) && field.DeclaringType.DeclaringType.Equals(thisType))
				{
					if(val.MatchLdLoc(out _))
					{
						param = val;
						nField = field;
						return true;
					}

					if (val.MatchLdObj(out ILInstruction unbox, out _))
					{
						param = unbox;
						nField = field;
						return true;
					}
				}
			}

			return false;
		}

		static bool MatchNestedFieldReference(ILInstruction inst, IField nestedField)
		{
			if (inst.MatchLdFlda(out ILInstruction targ, out IField fieldRef))
			{
				return nestedField.Equals(fieldRef);
			}

			return false;
		}
	}
}
