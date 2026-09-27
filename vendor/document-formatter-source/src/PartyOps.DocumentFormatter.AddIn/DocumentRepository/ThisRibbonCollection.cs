using System.CodeDom.Compiler;
using System.Diagnostics;
using Microsoft.Office.Tools.Ribbon;

namespace DocumentRepository;

[DebuggerNonUserCode]
[GeneratedCode("Microsoft.VisualStudio.Tools.Office.ProgrammingModel.dll", "17.0.0.0")]
internal sealed class ThisRibbonCollection : RibbonCollectionBase
{
	internal Ribbon1 Ribbon1 => GetRibbon<Ribbon1>();

	internal ThisRibbonCollection(RibbonFactory factory)
		: base(factory)
	{
	}
}
