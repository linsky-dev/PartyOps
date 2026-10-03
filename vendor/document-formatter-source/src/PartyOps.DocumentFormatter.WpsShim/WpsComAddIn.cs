using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Extensibility;
using PartyOps.DocumentFormatter.Wps.Interop;

namespace PartyOps.DocumentFormatter.Wps;

/// <summary>
/// WPS Writer 原生 COM 加载核心。真正暴露给 COM 的薄入口位于
/// <see cref="WpsComAddInEntryPoint"/>；核心类只负责 Ribbon 与业务转发，
/// 避免 CLR 在激活同时实现多个 COM 接口的主类时出现类加载失败。
/// </summary>
[ComVisible(false)]
public class WpsComAddInCore : IDTExtensibility2, IRibbonExtensibility
{
	private object application;
	private object ribbonUi;

	public string GetCustomUI(string ribbonId)
	{
		WriteDiagnostic("GetCustomUI ribbonId=" + (ribbonId ?? string.Empty));
		return RibbonXml;
	}

	public void OnConnection(object applicationObject, ext_ConnectMode connectMode, object addInInstance, ref Array custom)
	{
		WriteDiagnostic("OnConnection mode=" + connectMode);
		application = applicationObject;
		WpsComHostBridgeAdapter.Initialize(application);
	}

	public void OnStartupComplete(ref Array custom)
	{
		WriteDiagnostic("OnStartupComplete");
	}

	public void OnAddInsUpdate(ref Array custom)
	{
	}

	public void OnBeginShutdown(ref Array custom)
	{
		WriteDiagnostic("OnBeginShutdown");
	}

	public void OnDisconnection(ext_DisconnectMode removeMode, ref Array custom)
	{
		WriteDiagnostic("OnDisconnection mode=" + removeMode);
		try
		{
			WpsComHostBridgeAdapter.Shutdown();
		}
		finally
		{
			ribbonUi = null;
			application = null;
		}
	}

	public void OnRibbonLoad(object ribbon)
	{
		ribbonUi = ribbon;
		WriteDiagnostic("OnRibbonLoad");
	}

	public void OnFormat(object control) => Run("format", () => WpsComHostBridgeAdapter.ExecuteFeature(application, "format", false));

	public void OnReplace(object control) => Run("replace", () => WpsComHostBridgeAdapter.ExecuteFeature(application, "replace", true));

	public void OnRedHeader(object control) => Run("redheader", () => WpsComHostBridgeAdapter.ExecuteFeature(application, "redheader", true));

	public void OnRename(object control) => Run("rename", () => WpsComHostBridgeAdapter.ExecuteFeature(application, "rename", true));

	public void OnConvert(object control) => Run("convert", () => WpsComHostBridgeAdapter.ExecuteFeature(application, "convert", true));

	public void OnPdfToWord(object control) => Run("pdf-to-word", () => WpsComHostBridgeAdapter.ExecuteFeature(application, "pdf-to-word", true));

	public void OnConvertDocx(object control) => Run("convert-docx", () => WpsComHostBridgeAdapter.ExecuteConvert(application, "Docx"));

	public void OnConvertPdf(object control) => Run("convert-pdf", () => WpsComHostBridgeAdapter.ExecuteConvert(application, "Pdf"));

	public void OnConvertImage(object control) => Run("convert-image", () => WpsComHostBridgeAdapter.ExecuteConvert(application, "Image"));

	public void OnConvertTxt(object control) => Run("convert-txt", () => WpsComHostBridgeAdapter.ExecuteConvert(application, "Txt"));

	public void OnFormatSettings(object control) => Run("format-settings", WpsComHostBridgeAdapter.OpenFormatSettings);

	public void OnReplaceSettings(object control) => Run("replace-settings", WpsComHostBridgeAdapter.OpenReplaceSettings);

	public void OnRedHeaderSettings(object control) => Run("redheader-settings", WpsComHostBridgeAdapter.OpenRedHeaderSettings);

	public void OnRenameSettings(object control) => Run("rename-settings", () => WpsComHostBridgeAdapter.OpenRenameSettings(application));

	public void OnConvertSettings(object control) => Run("convert-settings", WpsComHostBridgeAdapter.OpenConvertSettings);

	private static void Run(string callbackName, Action action)
	{
		try
		{
			WriteDiagnostic("Callback " + callbackName);
			action();
		}
		catch (Exception ex)
		{
			WriteDiagnostic("Callback failed " + callbackName + ": " + ex);
			System.Windows.Forms.MessageBox.Show("操作未能启动，详细原因已写入 WPS 加载桥日志。\r\n\r\n" + ex.Message, "partyops公文排版助手", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
		}
	}

	private static void WriteDiagnostic(string message)
	{
		try
		{
			string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PartyOps.DocumentFormatter", "Logs");
			Directory.CreateDirectory(directory);
			File.AppendAllText(Path.Combine(directory, "wps-com-startup.log"), DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine);
		}
		catch
		{
			// 日志失败不得阻止 WPS 加载 COM 插件。
		}
	}

	private const string RibbonXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<customUI xmlns=""http://schemas.microsoft.com/office/2006/01/customui"" onLoad=""OnRibbonLoad"">
  <ribbon>
    <tabs>
      <tab id=""tabPartyOps.DocumentFormatter"" label=""partyops公文排版助手"">
        <group id=""groupPrimary"" label=""公文处理"">
          <button id=""btnFormat"" label=""一键排版"" size=""large"" imageMso=""StylesDialogClassic"" onAction=""OnFormat"" screentip=""智能识别公文要素并按规范排版"" />
          <button id=""btnReplace"" label=""一键替换"" size=""large"" imageMso=""ReplaceDialog"" onAction=""OnReplace"" screentip=""执行当前批量替换方案"" />
          <button id=""btnRedHeader"" label=""一键套红"" size=""large"" imageMso=""FileNew"" onAction=""OnRedHeader"" screentip=""套用当前红头模板"" />
          <button id=""btnRename"" label=""一键命名"" size=""large"" imageMso=""FileSaveAs"" onAction=""OnRename"" screentip=""按当前命名规则保存文档"" />
          <button id=""btnConvert"" label=""一键转换"" size=""large"" imageMso=""FileSaveAsPdfOrXps"" onAction=""OnConvert"" screentip=""按当前转换参数输出文档"" />
          <button id=""btnPdfToWord"" label=""PDF 转 Word"" size=""large"" imageMso=""FileOpen"" onAction=""OnPdfToWord"" screentip=""本地 PDF 转为可编辑 Word"" />
        </group>
        <group id=""groupConversion"" label=""快速转换"">
          <button id=""btnConvertDocx"" label=""转 DOCX"" onAction=""OnConvertDocx"" />
          <button id=""btnConvertPdf"" label=""转 PDF"" onAction=""OnConvertPdf"" />
          <button id=""btnConvertImage"" label=""转图片/长图"" onAction=""OnConvertImage"" />
          <button id=""btnConvertTxt"" label=""转 TXT"" onAction=""OnConvertTxt"" />
        </group>
        <group id=""groupSettings"" label=""参数与方案"">
          <button id=""btnFormatSettings"" label=""排版参数"" imageMso=""OptionsWord"" onAction=""OnFormatSettings"" />
          <button id=""btnReplaceSettings"" label=""替换方案"" imageMso=""TableProperties"" onAction=""OnReplaceSettings"" />
          <button id=""btnRedHeaderSettings"" label=""红头模板"" imageMso=""PageSetupDialog"" onAction=""OnRedHeaderSettings"" />
          <button id=""btnRenameSettings"" label=""命名规则"" imageMso=""FileProperties"" onAction=""OnRenameSettings"" />
          <button id=""btnConvertSettings"" label=""转换设置"" imageMso=""FileSaveAs"" onAction=""OnConvertSettings"" />
        </group>
      </tab>
    </tabs>
  </ribbon>
</customUI>";
}
