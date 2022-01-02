using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.Util;

namespace ICSharpCode.Decompiler.IL.Transforms
{
	public class RemoveCompilerGeneratedThis : IILTransform
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
				if(block.Instructions[i].MatchStLoc(out ILVariable vari, out ILInstruction val))
				{
					if (!vari.Name.Contains("<>c__CompilerGenerated"))
						continue;

					if (!val.MatchLdThis())
						continue;

					var refs = vari.LoadInstructions.ToList();
					foreach(var exp in refs)
					{
						exp.Parent.Parent.ReplaceWith(val.Clone());
					}

					block.Instructions.RemoveAt(i);
					int c = ILInlining.InlineInto(block, i, InliningOptions.None, context: context);
					i -= c + 1;
				}
			}
		}
	}
}
