using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DocumentRepository.Services.UiText;

public static class UiTextRegistry
{
	private static readonly Dictionary<string, UiTextMeta> Items;

	[MethodImpl(MethodImplOptions.NoInlining)]
	static UiTextRegistry()
	{
		Items = new Dictionary<string, UiTextMeta>
		{
			{
				"Ribbon.Settings",
				new UiTextMeta("Ribbon.Settings", "自定义参数", "点击下拉箭头切换排版模板，点击上方按钮进入参数设置")
			},
			{
				"Ribbon.TemplateSettings",
				new UiTextMeta("Ribbon.TemplateSettings", "参数设置...", "进入一键排版自定义参数设置界面")
			},
			{
				"Ribbon.Format",
				new UiTextMeta("Ribbon.Format", "一键排版", "选中部分文字点击后仅排版选中部分，否则对全文进行排版")
			},
			{
				"Ribbon.Replace",
				new UiTextMeta("Ribbon.Replace", "一键替换", "批量替换文字和格式")
			},
			{
				"Ribbon.ReplaceSettings",
				new UiTextMeta("Ribbon.ReplaceSettings", "方案设置...", "新增、修改或删除一键替换方案及其条目")
			},
			{
				"Ribbon.RedHeader",
				new UiTextMeta("Ribbon.RedHeader", "一键套红", "点击下拉箭头切换红头模板，点击上方按钮直接套用当前模板")
			},
			{
				"Ribbon.RedHeader.Execute",
				new UiTextMeta("Ribbon.RedHeader.Execute", "一键套红", "按当前选中的红头模板直接套红")
			},
			{
				"Ribbon.RedHeader.Settings",
				new UiTextMeta("Ribbon.RedHeader.Settings", "模板设置...", "新增、修改或删除一键套红模板")
			},
			{
				"Ribbon.Rename",
				new UiTextMeta("Ribbon.Rename", "一键命名", "点击下拉箭头切换命名规则，点击上方按钮直接重命名")
			},
			{
				"Ribbon.Rename.Execute",
				new UiTextMeta("Ribbon.Rename.Execute", "一键命名", "按当前选中的命名规则直接重命名")
			},
			{
				"Ribbon.Rename.Settings",
				new UiTextMeta("Ribbon.Rename.Settings", "规则设置...", "新增、修改或删除一键命名规则")
			},
			{
				"Ribbon.Convert",
				new UiTextMeta("Ribbon.Convert", "一键转换", "将当前文档转换为 DOCX、PDF、TXT 或图片")
			},
			{
				"Convert.SaveLocation",
				new UiTextMeta("Convert.SaveLocation", "保存位置", "设置转换结果保存到原文件夹或指定的自定义文件夹。")
			},
			{
				"Convert.CustomFolder",
				new UiTextMeta("Convert.CustomFolder", "自定义路径", "选择自定义文件夹时设置转换结果的保存路径。")
			},
			{
				"Convert.SameName",
				new UiTextMeta("Convert.SameName", "同名处理", "目标文件已存在时，可每次询问、自动改名、覆盖或取消转换。")
			},
			{
				"Convert.OpenFolder",
				new UiTextMeta("Convert.OpenFolder", "转换完成后打开输出文件夹", "开启后转换成功时自动打开结果所在文件夹。")
			},
			{
				"Convert.DocxMode",
				new UiTextMeta("Convert.DocxMode", "转换方式", "另存新文件会保留原文档；替换当前文档仅在新文件验证成功后处理原文件。")
			},
			{
				"Convert.ImageMode",
				new UiTextMeta("Convert.ImageMode", "输出形式", "单图按页输出独立图片；长图把所选页面按顺序拼接为一张图片。")
			},
			{
				"Convert.PageMode",
				new UiTextMeta("Convert.PageMode", "页码选择", "设置转换全部页面、连续范围页或指定的多个页面。")
			},
			{
				"Convert.PageRange",
				new UiTextMeta("Convert.PageRange", "范围页", "范围页模式下填写连续页码，例如 2-6。")
			},
			{
				"Convert.SelectedPages",
				new UiTextMeta("Convert.SelectedPages", "指定页", "指定页模式下填写页码并用逗号分隔，例如 1,3,9。")
			},
			{
				"Convert.ImageFormat",
				new UiTextMeta("Convert.ImageFormat", "图片格式", "设置图片输出为 PNG 或 JPG；PNG 更清晰，JPG 文件通常更小。")
			},
			{
				"Convert.ImageDpi",
				new UiTextMeta("Convert.ImageDpi", "清晰度", "设置图片输出分辨率，单位为 DPI；数值越高越清晰，生成文件也越大。")
			},
			{
				"Convert.TxtRemoveBlank",
				new UiTextMeta("Convert.TxtRemoveBlank", "导出 TXT 时清理多余空行", "开启后导出纯文本时合并连续空行，不保留页码和版式。")
			},
			{
				"Convert.PdfToWordEngine",
				new UiTextMeta("Convert.PdfToWordEngine", "PDF 转 Word 引擎", "本地引擎不依赖 Word/WPS 内建导入；仅当 PDF 解析或版面识别不兼容时尝试宿主导入，输出写入失败不会重复回退。")
			},
			{
				"Settings.MainTitleRecognition.Normative",
				new UiTextMeta("Settings.MainTitleRecognition.Normative", "规范标题", "规范严谨公文标题识别规则，支持主副标题、多层标题，仅允许标题出现书名号、引号、实心点号、括号等标点。")
			},
			{
				"Settings.MainTitleRecognition.FirstParagraph",
				new UiTextMeta("Settings.MainTitleRecognition.FirstParagraph", "首段为标题", "将文章非空首段的任何内容强制设为主标题。")
			},
			{
				"Settings.PageMargins",
				new UiTextMeta("Settings.PageMargins", "页边距参数", "设置页边距，支持填入小数点2位数")
			},
			{
				"Settings.DocumentGrid",
				new UiTextMeta("Settings.DocumentGrid", "文档网格", "开启后按网格参数设置每页行数和每行字数，行距根据版心动态计算。")
			},
			{
				"Settings.FixSemicolons",
				new UiTextMeta("Settings.FixSemicolons", "修正分号", "设置关键词加粗的句子使用分号并列")
			},
			{
				"Settings.SignatureWithSeal",
				new UiTextMeta("Settings.SignatureWithSeal", "落款排版", "开启后按落款参数排版署名和日期；关闭后按普通正文处理。")
			},
			{
				"Settings.OrphanCharFix",
				new UiTextMeta("Settings.OrphanCharFix", "孤字不成行", "调整字间距避免单字居于一行")
			},
			{
				"Settings.AttachmentFormatting",
				new UiTextMeta("Settings.AttachmentFormatting", "附件排版", "对含有附件的正文排版，具体可设置参数")
			},
			{
				"Settings.TableFormatting",
				new UiTextMeta("Settings.TableFormatting", "表格排版", "对含有表格的正文排版，具体可设置参数")
			},
			{
				"Settings.ImageFormatting",
				new UiTextMeta("Settings.ImageFormatting", "图片排版", "开启后仅处理识别为真实图片的对象；文本框、线条、图表、公式和 OLE 对象会被排除。")
			},
			{
				"Settings.CompilationFormatting",
				new UiTextMeta("Settings.CompilationFormatting", "汇编排版", "按固定标记“@@汇编@@”把文档划分成多篇文章并逐篇排版。")
			},
			{
				"Settings.CompilationOptions",
				new UiTextMeta("Settings.CompilationOptions", "汇编参数", "进入汇编排版的参数设置。")
			},
			{
				"Settings.Font",
				new UiTextMeta("Settings.Font", "字体", "加载系统字体，请确保本机有常用字体")
			},
			{
				"Settings.EnglishNumberFont",
				new UiTextMeta("Settings.EnglishNumberFont", "英文和数字", "以三级标题的中文字体为匹配条件；凡一键排版后使用同一中文字体的内容，其中的英文和数字都使用这里设置的字体。")
			},
			{
				"Settings.FontSize",
				new UiTextMeta("Settings.FontSize", "字号", "字号选项，支持手写填入阿拉伯数值")
			},
			{
				"Settings.RecognitionStyle",
				new UiTextMeta("Settings.RecognitionStyle", "识别样式", "各级标题的识别规则")
			},
			{
				"Settings.SpaceBefore",
				new UiTextMeta("Settings.SpaceBefore", "段前（磅）", "段前磅值距离，28磅约为一行距离")
			},
			{
				"Settings.SpaceAfter",
				new UiTextMeta("Settings.SpaceAfter", "段后（磅）", "段后磅值距离，28磅约为一行距离")
			},
			{
				"Settings.KeywordBold",
				new UiTextMeta("Settings.KeywordBold", "关键词加粗", "对“第一”“一是”“一要”等三种常见关键词进行加粗处理。")
			},
			{
				"Settings.KeywordBold.Sentence",
				new UiTextMeta("Settings.KeywordBold.Sentence", "整句加粗", "包含关键词的整句加粗")
			},
			{
				"Settings.KeywordBold.Phrase",
				new UiTextMeta("Settings.KeywordBold.Phrase", "短语加粗", "只加粗关键词本身")
			},
			{
				"Settings.KeywordBold.None",
				new UiTextMeta("Settings.KeywordBold.None", "不加粗", "不加粗，字体格式继承所在标题层级格式")
			},
			{
				"Settings.Page.LeftSymbol",
				new UiTextMeta("Settings.Page.LeftSymbol", "左侧符号", "设置页码左侧翅膀样式，支持填入任何自定义内容")
			},
			{
				"Settings.Page.RightSymbol",
				new UiTextMeta("Settings.Page.RightSymbol", "右侧符号", "设置页码右侧翅膀样式，支持填入任何自定义内容")
			},
			{
				"Settings.Page.NumberMode",
				new UiTextMeta("Settings.Page.NumberMode", "页码开启", "设置是否生成页码。")
			},
			{
				"Attachment.List",
				new UiTextMeta("Attachment.List", "排版附件说明（清单）", "开启后规范正文末尾“附件：”及多项附件清单的缩进、换行和编号格式。")
			},
			{
				"Attachment.Body",
				new UiTextMeta("Attachment.Body", "排版附件正文", "开启后识别分页后的附件标识、附件主标题和附件正文，并分别应用对应格式。")
			},
			{
				"Attachment.BodyTitleStyle",
				new UiTextMeta("Attachment.BodyTitleStyle", "附件正文主标题字体", "设置附件正文主标题使用的字体和字号，仅影响附件正文主标题。")
			},
			{
				"Table.Borders",
				new UiTextMeta("Table.Borders", "统一设置黑色单线边框", "开启后将表格边框统一为黑色单线，覆盖原有边框颜色和线型。")
			},
			{
				"Table.ShadeHeader",
				new UiTextMeta("Table.ShadeHeader", "表头使用浅灰底纹", "开启后为指定的表头行添加浅灰色底纹。")
			},
			{
				"Table.RepeatHeaderRows",
				new UiTextMeta("Table.RepeatHeaderRows", "跨页重复表头", "开启后表格跨页时在每页顶部重复显示指定表头行。")
			},
			{
				"Table.ClearCellIndents",
				new UiTextMeta("Table.ClearCellIndents", "清除单元格段落缩进", "开启后清除表格单元格内段落的左右缩进和首行缩进。")
			},
			{
				"Table.HeaderRows",
				new UiTextMeta("Table.HeaderRows", "表头行数", "设置从表格第一行开始作为表头的行数；填 0 表示不单独处理表头。")
			},
			{
				"Table.RowHeightMode",
				new UiTextMeta("Table.RowHeightMode", "行高方式", "设置表格行高采用最小值、固定值或由内容自动调整。")
			},
			{
				"Table.RowHeight",
				new UiTextMeta("Table.RowHeight", "行高值", "设置表格行高，单位为磅；选择自动行高时此数值不参与处理。")
			},
			{
				"Table.ColumnWidthMode",
				new UiTextMeta("Table.ColumnWidthMode", "列宽方式", "设置列宽适应页面、根据内容、固定列宽或自动调整。")
			},
			{
				"Table.ColumnWidth",
				new UiTextMeta("Table.ColumnWidth", "固定列宽", "选择固定列宽时设置每列宽度，单位为厘米。")
			},
			{
				"Table.Alignment",
				new UiTextMeta("Table.Alignment", "表格对齐", "设置整张表格相对于页面版心的左对齐、居中或右对齐。")
			},
			{
				"Table.TextWrapping",
				new UiTextMeta("Table.TextWrapping", "文字环绕", "设置正文文字是否环绕表格；普通公文表格通常使用无环绕。")
			},
			{
				"Table.HeaderStyle",
				new UiTextMeta("Table.HeaderStyle", "表头字体", "设置表头文字使用的字体和字号。")
			},
			{
				"Table.BodyStyle",
				new UiTextMeta("Table.BodyStyle", "内容字体", "设置表格数据区域文字使用的字体和字号。")
			},
			{
				"Table.HeaderAlignment",
				new UiTextMeta("Table.HeaderAlignment", "表头对齐", "设置表头单元格内文字的水平对齐方式。")
			},
			{
				"Table.BodyAlignment",
				new UiTextMeta("Table.BodyAlignment", "内容对齐", "设置表格数据区域单元格内文字的水平对齐方式。")
			},
			{
				"Table.TopPadding",
				new UiTextMeta("Table.TopPadding", "上边距", "设置单元格文字与上边框之间的距离，单位为厘米。")
			},
			{
				"Table.BottomPadding",
				new UiTextMeta("Table.BottomPadding", "下边距", "设置单元格文字与下边框之间的距离，单位为厘米。")
			},
			{
				"Table.LeftPadding",
				new UiTextMeta("Table.LeftPadding", "左边距", "设置单元格文字与左边框之间的距离，单位为厘米。")
			},
			{
				"Table.RightPadding",
				new UiTextMeta("Table.RightPadding", "右边距", "设置单元格文字与右边框之间的距离，单位为厘米。")
			},
			{
				"RedHeader.TemplateName",
				new UiTextMeta("RedHeader.TemplateName", "模板名称", "用于区分不同红头模板，只影响模板列表显示，不写入正文。")
			},
			{
				"RedHeader.HeaderText",
				new UiTextMeta("RedHeader.HeaderText", "发文机关", "设置红头顶部显示的发文机关名称，可输入一行或多行文字。")
			},
			{
				"RedHeader.DocumentNumberText",
				new UiTextMeta("RedHeader.DocumentNumberText", "发文字号", "设置红线以上显示的发文字号内容，可留空。")
			},
			{
				"RedHeader.TopMark.CopyNumberEnabled",
				new UiTextMeta("RedHeader.TopMark.CopyNumberEnabled", "启用份号", "开启后可填写公文份号；留空时不生成份号。")
			},
			{
				"RedHeader.TopMark.CopyNumber",
				new UiTextMeta("RedHeader.TopMark.CopyNumber", "公文份号", "可留空；填写时输入1至6位数字，生成时自动在左侧补零为六位。")
			},
			{
				"RedHeader.TopMark.SecurityLevel",
				new UiTextMeta("RedHeader.TopMark.SecurityLevel", "公文密级", "选择秘密、机密或绝密；选择无时不生成密级标识。")
			},
			{
				"RedHeader.TopMark.ConfidentialityPeriod",
				new UiTextMeta("RedHeader.TopMark.ConfidentialityPeriod", "保密期限", "设置密级后的保密期限，生成格式为“密级★期限”；未选择密级时不可填写。")
			},
			{
				"RedHeader.TopMark.UrgencyLevel",
				new UiTextMeta("RedHeader.TopMark.UrgencyLevel", "紧急程度", "选择加急或特急；选择无时不生成紧急程度。")
			},
			{
				"RedHeader.TopMark.Font",
				new UiTextMeta("RedHeader.TopMark.Font", "字体", "设置份号、密级、保密期限和紧急程度使用的字体。")
			},
			{
				"RedHeader.TopMark.Size",
				new UiTextMeta("RedHeader.TopMark.Size", "字号", "设置版头附加标识的字号，单位为磅。")
			},
			{
				"RedHeader.TopMark.LineSpacing",
				new UiTextMeta("RedHeader.TopMark.LineSpacing", "行距", "设置版头附加标识各行的固定行距，单位为磅。")
			},
			{
				"RedHeader.TopMark.Indent",
				new UiTextMeta("RedHeader.TopMark.Indent", "左缩进字符", "设置版头附加标识相对版心左侧的字符缩进量。")
			},
			{
				"RedHeader.TopMark.Color",
				new UiTextMeta("RedHeader.TopMark.Color", "颜色", "设置版头附加标识的文字颜色。")
			},
			{
				"RedHeader.Header.Font",
				new UiTextMeta("RedHeader.Header.Font", "字体", "设置发文机关名称使用的字体，必须确保用户电脑已安装该字体。")
			},
			{
				"RedHeader.Header.Size",
				new UiTextMeta("RedHeader.Header.Size", "字号", "设置发文机关名称的字号，单位为磅。")
			},
			{
				"RedHeader.Header.Bold",
				new UiTextMeta("RedHeader.Header.Bold", "加粗", "设置发文机关名称是否加粗。")
			},
			{
				"RedHeader.Header.Color",
				new UiTextMeta("RedHeader.Header.Color", "颜色", "设置发文机关名称的文字颜色，可选择红色、黑色或白色。")
			},
			{
				"RedHeader.Header.Alignment",
				new UiTextMeta("RedHeader.Header.Alignment", "对齐", "设置发文机关名称在版心内的对齐方式。")
			},
			{
				"RedHeader.Header.LineSpacing",
				new UiTextMeta("RedHeader.Header.LineSpacing", "行距", "设置多行发文机关名称的固定行距，单位为磅。")
			},
			{
				"RedHeader.Header.Before",
				new UiTextMeta("RedHeader.Header.Before", "段前", "设置发文机关名称段前距离，单位为磅。")
			},
			{
				"RedHeader.Header.After",
				new UiTextMeta("RedHeader.Header.After", "段后", "设置发文机关名称段后距离，单位为磅。")
			},
			{
				"RedHeader.Header.Indent",
				new UiTextMeta("RedHeader.Header.Indent", "左缩进字符", "设置发文机关名称相对版心左侧的字符缩进量。")
			},
			{
				"RedHeader.Header.Width",
				new UiTextMeta("RedHeader.Header.Width", "排布宽度比例", "设置分散对齐时发文机关占用版心宽度的比例，100表示占满版心。")
			},
			{
				"RedHeader.Header.Scale",
				new UiTextMeta("RedHeader.Header.Scale", "字符缩放比例", "直接压缩或拉伸发文机关字符本身的宽度，100表示保持原宽度。")
			},
			{
				"RedHeader.Header.FitMode",
				new UiTextMeta("RedHeader.Header.FitMode", "适配方式", "手动按指定比例缩放；自动适应一行会寻找可保持单行的最大比例；允许换行不限制行数。")
			},
			{
				"RedHeader.Header.MinimumScale",
				new UiTextMeta("RedHeader.Header.MinimumScale", "最低缩放比例", "自动适应一行时允许使用的最低字符缩放比例。")
			},
			{
				"RedHeader.Header.CharSpacing",
				new UiTextMeta("RedHeader.Header.CharSpacing", "字间距", "设置发文机关名称字符之间的附加间距，单位为磅。")
			},
			{
				"RedHeader.Number.Font",
				new UiTextMeta("RedHeader.Number.Font", "字体", "设置发文字号使用的字体。")
			},
			{
				"RedHeader.Number.Size",
				new UiTextMeta("RedHeader.Number.Size", "字号", "设置发文字号的字号，单位为磅。")
			},
			{
				"RedHeader.Number.LineSpacing",
				new UiTextMeta("RedHeader.Number.LineSpacing", "行距", "设置发文字号段落的固定行距，单位为磅。")
			},
			{
				"RedHeader.Number.Before",
				new UiTextMeta("RedHeader.Number.Before", "段前", "设置发文字号段前距离，单位为磅。")
			},
			{
				"RedHeader.Number.After",
				new UiTextMeta("RedHeader.Number.After", "段后", "设置发文字号段后距离，单位为磅。")
			},
			{
				"RedHeader.Line.Style",
				new UiTextMeta("RedHeader.Line.Style", "红线样式", "选择红线使用普通公文分隔线或其他已支持的线型。")
			},
			{
				"RedHeader.Line.Width",
				new UiTextMeta("RedHeader.Line.Width", "线宽比例", "设置红线相对版心宽度的百分比，100 表示与版心等宽。")
			},
			{
				"RedHeader.Line.Thickness",
				new UiTextMeta("RedHeader.Line.Thickness", "粗细", "设置红线粗细，单位为磅。")
			},
			{
				"RedHeader.Line.Color",
				new UiTextMeta("RedHeader.Line.Color", "颜色", "设置红线颜色，可选择红色、黑色或白色。")
			},
			{
				"RedHeader.Line.Before",
				new UiTextMeta("RedHeader.Line.Before", "段前", "设置红线所在段落的段前距离，单位为磅。")
			},
			{
				"RedHeader.Line.After",
				new UiTextMeta("RedHeader.Line.After", "段后", "设置红线所在段落的段后距离，单位为磅。")
			},
			{
				"RedHeader.TitleGap.Lines",
				new UiTextMeta("RedHeader.TitleGap.Lines", "空行数", "设置红线与正文主标题之间插入的空行数量。")
			},
			{
				"RedHeader.TitleGap.LineSpacing",
				new UiTextMeta("RedHeader.TitleGap.LineSpacing", "空行行距", "设置红线与正文主标题之间空行的固定行距，单位为磅。")
			},
			{
				"RedHeader.Imprint.Enabled",
				new UiTextMeta("RedHeader.Imprint.Enabled", "启用版记", "开启后在文末生成版记；关闭后不生成版记。")
			},
			{
				"RedHeader.Imprint.EvenPage",
				new UiTextMeta("RedHeader.Imprint.EvenPage", "偶数页版记", "开启后确保版记位于最后一个偶数页；必要时会增加页面。")
			},
			{
				"RedHeader.Imprint.Font",
				new UiTextMeta("RedHeader.Imprint.Font", "版记字体", "设置版记中抄送、制发机关和印发日期使用的字体。")
			},
			{
				"RedHeader.Imprint.Size",
				new UiTextMeta("RedHeader.Imprint.Size", "版记字号", "设置版记文字字号。")
			},
			{
				"RedHeader.Imprint.BottomOffset",
				new UiTextMeta("RedHeader.Imprint.BottomOffset", "贴底微调", "在自动贴近版心底部的基础上微调版记位置；正负值分别向不同方向移动。")
			},
			{
				"RedHeader.Imprint.CellPadding",
				new UiTextMeta("RedHeader.Imprint.CellPadding", "文字上下边距", "设置版记文字与表格上下边框之间的距离，单位为厘米。")
			},
			{
				"RedHeader.Imprint.Send",
				new UiTextMeta("RedHeader.Imprint.Send", "抄送", "设置版记第一行抄送机关。留空时不生成抄送行。")
			},
			{
				"RedHeader.Imprint.Office",
				new UiTextMeta("RedHeader.Imprint.Office", "制发机关", "设置版记末行左侧显示的制发机关名称。")
			},
			{
				"RedHeader.Imprint.Date",
				new UiTextMeta("RedHeader.Imprint.Date", "印发日期", "设置版记末行右侧显示的印发日期，程序按输入内容原样写入。")
			},
			{
				"RedHeader.Numbers.UseTimesNewRoman",
				new UiTextMeta("RedHeader.Numbers.UseTimesNewRoman", "数字使用新罗马", "开启后，将发文字号和版记印发日期中的半角阿拉伯数字设置为 Times New Roman；关闭时分别沿用发文字号字体和版记字体。")
			},
			{
				"Rename.RuleName",
				new UiTextMeta("Rename.RuleName", "规则名称", "设置命名规则在列表中的显示名称，不会写入文件名。")
			},
			{
				"Rename.Mode",
				new UiTextMeta("Rename.Mode", "命名方式", "设置另存新文件或按当前文档重命名方式执行；涉及删除原文件时请确认文档已保存。")
			},
			{
				"Rename.SavePath",
				new UiTextMeta("Rename.SavePath", "另存路径", "设置新文件保存在原文件夹、指定文件夹或其他已支持位置。")
			},
			{
				"Rename.Structure",
				new UiTextMeta("Rename.Structure", "当前命名结构", "这里的部件顺序就是最终文件名顺序，可使用上移、下移和删除调整。")
			},
			{
				"Rename.RotateWords",
				new UiTextMeta("Rename.RotateWords", "轮替词", "填写需要依次轮换使用的词语，多个词语之间使用顿号（、）分隔。")
			},
			{
				"Rename.RotateResetOnStartup",
				new UiTextMeta("Rename.RotateResetOnStartup", "轮替重启软件初始化", "勾选后，每次重新打开 Word/WPS 都从第一个轮替词开始；不勾选则接着上次关闭时的位置继续轮替。")
			},
			{
				"Rename.CustomPart",
				new UiTextMeta("Rename.CustomPart", "添加自定义", "把输入的固定文字作为一个文件名部件追加到当前命名结构。")
			},
			{
				"Rename.Parts",
				new UiTextMeta("Rename.Parts", "添加部件", "将发文字号、主标题、副标题、时间或轮替词按点击顺序加入文件名结构。")
			},
			{
				"Rename.DateFormat",
				new UiTextMeta("Rename.DateFormat", "时间格式", "设置时间部件的显示格式，例如 yyyy.MM.dd 或 yyyyMMdd。")
			},
			{
				"Rename.Preview",
				new UiTextMeta("Rename.Preview", "效果示例", "根据当前文档识别结果和规则实时预览最终文件名。")
			},
			{
				"Replace.PlanName",
				new UiTextMeta("Replace.PlanName", "方案名称", "设置替换方案在列表中的显示名称。")
			},
			{
				"Replace.PlanList",
				new UiTextMeta("Replace.PlanList", "替换方案", "选择需要查看或执行的替换方案；圆点标记表示当前执行方案。")
			},
			{
				"Replace.Rules",
				new UiTextMeta("Replace.Rules", "方案内规则", "规则按列表顺序依次执行；前一条规则的结果可能成为后一条规则的输入。")
			},
			{
				"Replace.FindText",
				new UiTextMeta("Replace.FindText", "查找内容", "填写需要查找的文字；使用正则表达式时填写正则模式。留空表示仅按格式查找。")
			},
			{
				"Replace.TargetText",
				new UiTextMeta("Replace.TargetText", "替换为", "填写替换后的文字；留空可删除匹配文字，或在仅改格式时保持文字不变。")
			},
			{
				"Replace.Regex",
				new UiTextMeta("Replace.Regex", "按正则表达式查找", "开启后将查找内容按正则表达式解析；错误表达式会导致本规则无法执行。")
			},
			{
				"Replace.Enabled",
				new UiTextMeta("Replace.Enabled", "启用本条目", "关闭后保留该条目配置，但执行替换方案时跳过本条目。")
			},
			{
				"Replace.FindFormat",
				new UiTextMeta("Replace.FindFormat", "查找格式（留空或不限表示不限）", "设置匹配文字必须同时满足的字体、字号、加粗、对齐和大纲条件。")
			},
			{
				"Replace.TargetFormat",
				new UiTextMeta("Replace.TargetFormat", "替换后的格式（留空或不修改表示不修改）", "设置匹配内容替换后应用的字体、字号、加粗、对齐、大纲、缩进和行距。")
			},
			{
				"Replace.RuleName",
				new UiTextMeta("Replace.RuleName", "条目名称", "填写便于识别的条目名称，它会显示在替换步骤列表中。")
			},
			{
				"Replace.RuleType",
				new UiTextMeta("Replace.RuleType", "条目类型", "选择普通文字替换、正则表达式替换或仅修改格式。")
			},
			{
				"Replace.RegexBuilder",
				new UiTextMeta("Replace.RegexBuilder", "帮我创建正则", "从常用场景中选择并自动生成正则查找和替换表达式。")
			},
			{
				"Replace.Preview",
				new UiTextMeta("Replace.Preview", "效果预览（只处理示例，不修改文档）", "使用示例文字即时检查规则的匹配数量和替换结果，不会修改当前文档。")
			}
		};
		UiExtendedTooltipRegistry.Register(Items);
		ValidateRegistry();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ValidateRegistry()
	{
		FieldInfo[] fields = typeof(UiTextKeys).GetFields(BindingFlags.Static | BindingFlags.Public);
		foreach (FieldInfo fieldInfo in fields)
		{
			if (!(fieldInfo.FieldType != typeof(string)))
			{
				string text = fieldInfo.GetValue(null) as string;
				if (string.IsNullOrWhiteSpace(text) || !Items.TryGetValue(text, out var value))
				{
					throw new InvalidOperationException("界面文案键未注册：" + fieldInfo.Name);
				}
				if (value == null || string.IsNullOrWhiteSpace(value.Tooltip))
				{
					throw new InvalidOperationException("界面悬停提示为空：" + fieldInfo.Name);
				}
			}
		}
	}

	public static UiTextMeta Get(string key)
	{
		if (!string.IsNullOrEmpty(key) && Items.TryGetValue(key, out var value))
		{
			return value;
		}
		return new UiTextMeta(key ?? "", key ?? "", "", tooltipEnabled: false);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string GetMainTitleRecognitionKey(string mode)
	{
		if (!string.Equals((mode ?? "").Trim(), "首段为标题", StringComparison.OrdinalIgnoreCase))
		{
			return "Settings.MainTitleRecognition.Normative";
		}
		return "Settings.MainTitleRecognition.FirstParagraph";
	}

	public static string GetMainTitleRecognitionTip(string mode)
	{
		return Get(GetMainTitleRecognitionKey(mode)).Tooltip;
	}
}
