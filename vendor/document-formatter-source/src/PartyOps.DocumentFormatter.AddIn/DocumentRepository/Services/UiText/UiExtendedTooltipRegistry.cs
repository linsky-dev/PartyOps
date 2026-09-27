using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.UiText;

internal static class UiExtendedTooltipRegistry
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static void Register(IDictionary<string, UiTextMeta> items)
	{
		Add(items, "Ribbon.PdfToWord", "PDF 转 Word", "选择 PDF 文件并转换为可编辑的 Word 文档。");
		Add(items, "Ribbon.Convert.Execute", "开始转换", "按当前选中的输出格式和转换规则执行转换。");
		Add(items, "Ribbon.Convert.Settings", "转换设置...", "设置输出位置、同名处理、图片清晰度等转换规则。");
		Add(items, "Compilation.Marker", "固定标记", "标记必须单独占一段并放在每篇文章完整主标题之前，不能与标题写在同一行。");
		Add(items, "Compilation.StartNewPage", "每篇文章另起一页", "全文汇编时让每篇文章从新页开始；每篇重新编号页码时会据此建立分节。");
		Add(items, "Compilation.PreserveFrontMatter", "保留前置内容", "保留第一个汇编标记前的封面、说明等内容；它不算文章，也不进入目录，但全文页面设置仍可能影响它。");
		Add(items, "Compilation.RejectFrontMatter", "发现前置内容时停止执行", "第一个汇编标记前存在非空内容时，在修改文档前停止并提示检查。");
		Add(items, "Compilation.GenerateToc", "生成目录", "全文汇编时创建或更新插件管理的目录，重复执行不会重复插入；部分汇编不会生成或更新目录。");
		Add(items, "Compilation.Toc.DocumentStart", "文档最前", "把插件目录放在整个文档的最前面，位于保留的前置内容之前。");
		Add(items, "Compilation.Toc.BeforeFirstArticle", "第一篇之前", "把插件目录放在第一篇文章之前；保留的封面或说明仍位于目录之前。");
		Add(items, "Compilation.Toc.Title", "目录标题", "设置目录顶部显示的标题文字。");
		Add(items, "Compilation.Toc.TitleFont", "标题字体", "设置目录标题的中文字体。");
		Add(items, "Compilation.Toc.TitleSize", "标题字号", "设置目录标题字号，单位为磅。");
		Add(items, "Compilation.Toc.TitleBold", "标题加粗", "设置目录标题是否加粗。");
		Add(items, "Compilation.Toc.TitleAlign", "标题对齐", "设置目录标题在版心内的对齐方式。");
		Add(items, "Compilation.Toc.TitleBefore", "标题段前", "设置目录标题段前距离，单位为磅。");
		Add(items, "Compilation.Toc.TitleAfter", "标题段后", "设置目录标题段后距离，单位为磅。");
		Add(items, "Compilation.Toc.EntryFontFarEast", "条目中文字体", "设置目录条目中中文文字使用的字体。");
		Add(items, "Compilation.Toc.EntryFontAscii", "英文数字字体", "设置目录条目中英文和数字使用的字体。");
		Add(items, "Compilation.Toc.EntrySize", "条目字号", "设置目录条目字号，单位为磅。");
		Add(items, "Compilation.Toc.EntryLineSpacing", "条目行距", "设置目录条目的固定行距，单位为磅。");
		Add(items, "Compilation.Toc.Leader", "前导符", "设置目录标题与页码之间使用点线、短线或不使用前导符。");
		Add(items, "Compilation.Toc.Hyperlink", "页码带跳转链接", "让目录页码可点击并跳转到对应文章。");
		Add(items, "Compilation.Toc.PageBreak", "目录后另起一页", "在目录结束后插入分页，使第一篇文章从新页开始。");
		Add(items, "Compilation.Toc.CountInNumbering", "目录页计入连续页码", "只在全文连续编号时生效。勾选后目录参与连续编号；不勾选时目录不显示页码，正文从 1 重新开始。");
		Add(items, "Compilation.Toc.ExistingToc", "已有目录处理", "保留会保留用户目录并另建插件目录，文档中可能出现两个目录；替换会先删除已有目录域；有用户目录则不生成会跳过新建，但已存在的插件目录仍可更新。");
		Add(items, "Compilation.Page.NoChange", "不调整页码", "保留文档当前页码设置，不新增、删除或重新编号。");
		Add(items, "Compilation.Page.Remove", "删除页码", "删除汇编范围相关的页码设置。");
		Add(items, "Compilation.Page.Continuous", "全文连续", "让正文页码从 1 开始连续编号；目录是否参与编号由目录页选项决定。");
		Add(items, "Compilation.Page.Restart", "每篇重新开始", "每篇文章建立独立编号边界，并从 1 重新开始显示页码。");
		Add(items, "Compilation.Settings.Confirm", "确定", "校验并保存当前汇编参数到工作副本；还需在上一级参数窗口保存模板才会最终生效。");
		Add(items, "Compilation.Settings.Cancel", "取消", "放弃本窗口尚未保存的修改并返回。");
		Add(items, "Compilation.Enable.CopyMarker", "复制标记", "只把固定标记复制到剪贴板，不修改当前文档。");
		Add(items, "Compilation.Enable.Confirm", "确认开启", "保留汇编排版开关为开启状态；此操作不会立即排版文档。");
		Add(items, "Compilation.Enable.Cancel", "取消", "取消开启汇编排版，并把开关恢复为关闭状态。");
		Add(items, "Compilation.Confirmation.ArticleList", "识别到的文章", "显示本次识别到的文章；悬停具体条目可查看未截断的完整标题。");
		Add(items, "Compilation.Confirmation.Full", "确认无误，开始排版", "按当前参数对全文识别到的文章执行汇编排版。");
		Add(items, "Compilation.Confirmation.Partial", "确认并排版", "只排版当前选区覆盖的文章正文，不生成目录，也不重新设置全文页码。");
		Add(items, "Compilation.Confirmation.Expand", "扩展到完整范围并排版", "把首尾截断的选区扩展到完整文章边界后再排版。");
		Add(items, "Compilation.Confirmation.Cancel", "返回文档", "取消本次汇编排版并返回文档，不写入任何修改。");
		Add(items, "Dialog.Close", "关闭", "关闭当前窗口，不执行新的操作。");
		Add(items, "Progress.CancelFormatting", "取消任务", "请求在安全检查点停止排版并恢复文档，不会强制关闭 Word/WPS。");
		Add(items, "Progress.CancelConversion", "取消", "请求在安全检查点停止转换；已经安全完成的步骤不会被强制回滚。");
		Add(items, "Recovery.CopyPath", "复制副本路径", "把恢复副本的完整路径复制到剪贴板。");
		Add(items, "Recovery.OpenFolder", "打开副本文件夹", "打开恢复副本所在文件夹并尽量选中该文件。");
		Add(items, "Convert.Settings.Confirm", "确定", "保存当前转换规则并关闭窗口。");
		Add(items, "Convert.Settings.Cancel", "取消", "放弃本窗口尚未保存的转换规则修改。");
	}

	private static void Add(IDictionary<string, UiTextMeta> items, string key, string text, string tooltip)
	{
		items.Add(key, new UiTextMeta(key, text, tooltip));
	}
}
