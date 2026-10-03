using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Threading;
using System.Windows.Forms;
using DocumentRepository.Models;
using DocumentRepository.Models.Features;
using DocumentRepository.Models.Tasks;
using DocumentRepository.Services.Configuration;
using DocumentRepository.Services.Features;
using DocumentRepository.Services.Formatting;
using DocumentRepository.Services.Hosting;
using DocumentRepository.Services.Logging;
using DocumentRepository.Services.Startup;
using DocumentRepository.Services.Ui;
using DocumentRepository.UI.Features;
using Microsoft.Office.Interop.Word;
using Microsoft.Office.Tools;
using Microsoft.Office.Tools.Word;
using Microsoft.VisualStudio.Tools.Applications.Runtime;
using Action = System.Action;
using Application = Microsoft.Office.Interop.Word.Application;
using Document = Microsoft.Office.Interop.Word.Document;
using OfficeFactory = Microsoft.Office.Tools.Factory;
using Timer = System.Threading.Timer;

namespace DocumentRepository;

[StartupObject(0)]
[PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
public sealed class ThisAddIn : AddInBase
{
	public static readonly string CurrentVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

	private Timer _externalFormatRequestTimer;

	private int _externalFormatDispatchPending;

	private KeyboardHook _keyboardHook;

	private AddInStartupCoordinator _startupCoordinator;

	private bool _documentBeforeCloseRegistered;

	private readonly object _startupSignalLock = new object();

	private readonly List<string> _startupFailuresBeforeCoordinator = new List<string>();

	private bool _ribbonLoadStarted;

	private bool _ribbonLoaded;

	private bool _deferredRibbonStartupScheduled;

	private bool _shutdownStarted;

	private static Control _syncContext;

	internal CustomTaskPaneCollection CustomTaskPanes;

	internal SmartTagCollection VstoSmartTags;

	[GeneratedCode("Microsoft.VisualStudio.Tools.Office.ProgrammingModel.dll", "17.0.0.0")]
	private object missing = Type.Missing;

	[GeneratedCode("Microsoft.VisualStudio.Tools.Office.ProgrammingModel.dll", "17.0.0.0")]
	internal Application Application;

	public static bool IsCloudMode { get; private set; } = true;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ThisAddIn_Startup(object sender, EventArgs e)
	{
		HostThreadRuntime.Initialize("ThisAddIn.Startup");
		GetHostIdentity(out var hostName, out var hostVersion);
		try
		{
			_startupCoordinator = AddInStartupCoordinator.Begin(CurrentVersion, hostName, hostVersion);
			ReplayPendingRibbonState();
		}
		catch (Exception ex)
		{
			LogService.Error("ThisAddIn.StartupCoordinator.Begin", ex);
		}
		RunStartupStep("configuration-migration", StartupStepKind.Core, delegate
		{
			ConfigurationMigrationService.Ensure440Migration();
		});
		RunStartupStep("document-close-event", StartupStepKind.Core, [MethodImpl(MethodImplOptions.NoInlining)] () =>
		{
			new ComAwareEventInfo(typeof(ApplicationEvents4_Event), "DocumentBeforeClose").AddEventHandler(Application, new ApplicationEvents4_DocumentBeforeCloseEventHandler(Application_DocumentBeforeClose));
			_documentBeforeCloseRegistered = true;
		});
		RunStartupStep("runtime-mode", StartupStepKind.Core, DetectRunMode);
		if (_startupCoordinator != null)
		{
			_startupCoordinator.MarkCoreReady();
		}
		RunStartupStep("ui-sync-context", StartupStepKind.Optional, delegate
		{
			_syncContext = new Control();
			_syncContext.CreateControl();
		});
		RunStartupStep("keyboard-hook", StartupStepKind.Optional, InitializeKeyboardHook);
		RunStartupStep("external-format-request-timer", StartupStepKind.Optional, delegate
		{
			_externalFormatRequestTimer = new Timer(delegate
			{
				ScheduleExternalFormatRequest();
			}, null, 500, 500);
		});
		if (_startupCoordinator != null)
		{
			_startupCoordinator.MarkHandlersReady();
		}
	}

	private void InitializeKeyboardHook()
	{
		_keyboardHook = new KeyboardHook([MethodImpl(MethodImplOptions.NoInlining)] () =>
		{
			PostToHostThread("keyboard-format", ExecuteKeyboardShortcutFormat);
		});
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ExecuteKeyboardShortcutFormat()
	{
		ExecuteExternalFormat("shortcut");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ProcessExternalFormatRequest()
	{
		HostThreadRuntime.AssertAccess("ThisAddIn.ProcessExternalFormatRequest");
		ThisAddIn thisAddIn = Globals.ThisAddIn;
		if (thisAddIn != null && thisAddIn.Application != null && ExternalFormatRequestService.TryActivateNextRequest(thisAddIn.Application))
		{
			ExecuteExternalFormat("shell-context-menu");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ScheduleExternalFormatRequest()
	{
		if (!ExternalFormatRequestService.HasPendingRequests() || Interlocked.Exchange(ref _externalFormatDispatchPending, 1) != 0)
		{
			return;
		}
		PostToHostThread("external-format-request", delegate
		{
			try
			{
				ProcessExternalFormatRequest();
			}
			finally
			{
				Interlocked.Exchange(ref _externalFormatDispatchPending, 0);
			}
		});
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ExecuteExternalFormat(string source)
	{
		HostThreadRuntime.AssertAccess("ThisAddIn.ExecuteExternalFormat");
		ThisAddIn thisAddIn = Globals.ThisAddIn;
		if (thisAddIn == null || thisAddIn.Application == null)
		{
			return;
		}
		Application application = thisAddIn.Application;
		if (application.Documents.Count == 0)
		{
			return;
		}
		OperationContext operationContext = OperationContext.FromApplication(application);
		operationContext.UserInterface = WinFormsFeatureUiService.Instance;
		CommandResult commandResult = FeatureTaskExecutor.Execute("format", operationContext, FeatureExecutionOptions.External(source));
		if (commandResult.PresentationHandled || string.IsNullOrEmpty(commandResult.Message))
		{
			return;
		}
		UserOutcomeKind userOutcomeKind = commandResult.ResolveOutcomeKind();
		WinFormsFeatureUiService instance = WinFormsFeatureUiService.Instance;
		if (userOutcomeKind != UserOutcomeKind.Busy)
		{
			if (userOutcomeKind == UserOutcomeKind.Cancelled || userOutcomeKind == UserOutcomeKind.Completed)
			{
				return;
			}
			if (userOutcomeKind == UserOutcomeKind.RecoveryRequired)
			{
				instance.ShowRecoveryAssistant("一键排版", commandResult.Message, commandResult.RecoveryId);
			}
			else if (userOutcomeKind == UserOutcomeKind.CompletedWithWarnings)
			{
				if (commandResult.Warnings == null || commandResult.Warnings.Count == 0 || WarningOnceGate.ShouldShowAndMark(commandResult.Warnings))
				{
					instance.ShowNonModalWarning("一键排版", commandResult.Message);
				}
			}
			else if (userOutcomeKind != UserOutcomeKind.FailedButRecovered)
			{
				int num;
				switch (userOutcomeKind)
				{
				case UserOutcomeKind.NoChanges:
					instance.ShowTimedMessage("一键排版", commandResult.Message, 1800);
					return;
				default:
					num = 2;
					break;
				case UserOutcomeKind.ActionRequired:
					num = 0;
					break;
				}
				FeatureMessageKind kind = (FeatureMessageKind)num;
				instance.ShowMessage("一键排版", commandResult.Message, kind);
			}
			else
			{
				instance.ShowNonModalWarning("一键排版", commandResult.Message);
			}
		}
		else
		{
			SimpleTaskProgressFormReporter.BringActiveToFront();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void PostToHostThread(string operation, Action action)
	{
		if (action == null)
		{
			return;
		}
		if (!HostThreadRuntime.IsHostThread)
		{
			Control syncContext = _syncContext;
			if (syncContext != null && !syncContext.IsDisposed && syncContext.IsHandleCreated)
			{
				try
				{
					syncContext.BeginInvoke((Delegate)(Action)delegate
					{
						ExecuteHostThreadAction(operation, action);
					});
					return;
				}
				catch (Exception ex)
				{
					LogService.Error("HOST_THREAD dispatch-failed operation=" + operation, ex);
					return;
				}
			}
			LogService.Warn("HOST_THREAD dispatch-dropped operation=" + operation + ", reason=ui-context-unavailable");
		}
		else
		{
			ExecuteHostThreadAction(operation, action);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ExecuteHostThreadAction(string operation, Action action)
	{
		try
		{
			lock (_startupSignalLock)
			{
				if (_shutdownStarted)
				{
					return;
				}
			}
			HostThreadRuntime.AssertAccess("ThisAddIn." + operation);
			action();
		}
		catch (Exception ex)
		{
			LogService.Error("HOST_THREAD action-failed operation=" + operation, ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void RunStartupStep(string name, StartupStepKind kind, Action action)
	{
		if (_startupCoordinator != null)
		{
			_startupCoordinator.RunStep(name, kind, action);
			return;
		}
		try
		{
			action?.Invoke();
		}
		catch (Exception ex)
		{
			lock (_startupSignalLock)
			{
				if (!_startupFailuresBeforeCoordinator.Contains(name))
				{
					_startupFailuresBeforeCoordinator.Add(name);
				}
			}
			LogService.Error("STARTUP_STEP name=" + name + " kind=" + kind.ToString() + " state=FAIL", ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void GetHostIdentity(out string hostName, out string hostVersion)
	{
		hostName = "unknown";
		hostVersion = "unknown";
		try
		{
			using Process process = Process.GetCurrentProcess();
			string a = process.ProcessName ?? string.Empty;
			if (string.Equals(a, "wps", StringComparison.OrdinalIgnoreCase))
			{
				hostName = "WPS Writer";
			}
			else if (string.Equals(a, "WINWORD", StringComparison.OrdinalIgnoreCase))
			{
				hostName = "Microsoft Word";
			}
		}
		catch (Exception ex)
		{
			LogService.Warn("ThisAddIn.GetHostIdentity.Process", ex);
		}
		try
		{
			if (string.Equals(hostName, "unknown", StringComparison.OrdinalIgnoreCase) && Application != null && !string.IsNullOrWhiteSpace(Application.Name))
			{
				hostName = Application.Name;
			}
		}
		catch (Exception ex2)
		{
			LogService.Warn("ThisAddIn.GetHostIdentity.Name", ex2);
		}
		try
		{
			if (Application != null && !string.IsNullOrWhiteSpace(Application.Version))
			{
				hostVersion = Application.Version;
			}
		}
		catch (Exception ex3)
		{
			LogService.Warn("ThisAddIn.GetHostIdentity.Version", ex3);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal void NotifyRibbonLoadStarted()
	{
		lock (_startupSignalLock)
		{
			_ribbonLoadStarted = true;
		}
		LogService.Info("STARTUP_STATE ribbon-load-start");
		if (_startupCoordinator != null)
		{
			StartupHealthService.Mark("ribbon-loading", "ribbon", string.Empty);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal void RunRibbonStartupStep(string name, Action action)
	{
		RunStartupStep("ribbon-" + name, StartupStepKind.Ribbon, action);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal void RunDeferredRibbonStartupStep(string name, Action action)
	{
		AddInStartupCoordinator startupCoordinator = _startupCoordinator;
		if (startupCoordinator != null)
		{
			startupCoordinator.RunDeferredStep("ribbon-" + name, action);
			return;
		}
		try
		{
			action?.Invoke();
		}
		catch (Exception ex)
		{
			LogService.Error("STARTUP_DEFERRED_STEP name=ribbon-" + name + " state=FAIL", ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal void ScheduleDeferredRibbonStartup(Action action)
	{
		if (action == null)
		{
			return;
		}
		lock (_startupSignalLock)
		{
			if (_deferredRibbonStartupScheduled || _shutdownStarted)
			{
				return;
			}
			_deferredRibbonStartupScheduled = true;
		}
		Control syncContext = _syncContext;
		if (syncContext != null && !syncContext.IsDisposed && syncContext.IsHandleCreated)
		{
			try
			{
				syncContext.BeginInvoke((Delegate)(Action)delegate
				{
					lock (_startupSignalLock)
					{
						if (_shutdownStarted)
						{
							return;
						}
					}
					action();
				});
				LogService.Info("STARTUP_DEFERRED scheduled=ui-message-loop");
				return;
			}
			catch (Exception ex)
			{
				LogService.Warn("STARTUP_DEFERRED schedule=ui-message-loop state=FAIL", ex);
			}
		}
		LogService.Warn("STARTUP_DEFERRED schedule=synchronous-fallback");
		action();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal void NotifyRibbonLoaded()
	{
		lock (_startupSignalLock)
		{
			_ribbonLoaded = true;
		}
		if (_startupCoordinator != null)
		{
			_startupCoordinator.MarkRibbonReady();
		}
		else
		{
			LogService.Info("STARTUP_STATE ribbon-ready coordinator=unavailable");
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ReplayPendingRibbonState()
	{
		AddInStartupCoordinator startupCoordinator = _startupCoordinator;
		if (startupCoordinator != null)
		{
			bool ribbonLoadStarted;
			bool ribbonLoaded;
			string[] array;
			lock (_startupSignalLock)
			{
				ribbonLoadStarted = _ribbonLoadStarted;
				ribbonLoaded = _ribbonLoaded;
				array = _startupFailuresBeforeCoordinator.ToArray();
				_startupFailuresBeforeCoordinator.Clear();
			}
			string[] array2 = array;
			foreach (string name in array2)
			{
				startupCoordinator.RecordFailure(name);
			}
			if (ribbonLoadStarted)
			{
				LogService.Info("STARTUP_STATE ribbon-load-replayed");
				StartupHealthService.Mark("ribbon-loading", "ribbon", "replayed-before-coordinator");
			}
			if (ribbonLoaded)
			{
				LogService.Info("STARTUP_STATE ribbon-ready-replayed");
				startupCoordinator.MarkRibbonReady();
			}
		}
	}

	private static void DetectRunMode()
	{
		IsCloudMode = true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ThisAddIn_Shutdown(object sender, EventArgs e)
	{
		lock (_startupSignalLock)
		{
			_shutdownStarted = true;
		}
		try
		{
			if (_documentBeforeCloseRegistered)
			{
				try
				{
					new ComAwareEventInfo(typeof(ApplicationEvents4_Event), "DocumentBeforeClose").RemoveEventHandler(Application, new ApplicationEvents4_DocumentBeforeCloseEventHandler(Application_DocumentBeforeClose));
					_documentBeforeCloseRegistered = false;
				}
				catch (Exception ex)
				{
					LogService.Warn("ThisAddIn.UnsubscribeDocumentBeforeClose", ex);
				}
			}
			TryShutdownAction("document-style-cache", DocumentStyleManager.ResetAllCaches);
			TryShutdownStartupAction("external-format-request-timer", delegate
			{
				DisposeAndClear(ref _externalFormatRequestTimer);
			});
			TryShutdownStartupAction("keyboard-hook", delegate
			{
				if (_keyboardHook != null)
				{
					_keyboardHook.Dispose();
					_keyboardHook = null;
				}
			});
			TryShutdownStartupAction("ui-sync-context", delegate
			{
				if (_syncContext != null)
				{
					((Component)(object)_syncContext).Dispose();
					_syncContext = null;
				}
			});
		}
		finally
		{
			if (_startupCoordinator == null)
			{
				StartupHealthService.Mark("shutdown-complete", "shutdown", "coordinator-unavailable");
			}
			else
			{
				_startupCoordinator.MarkShutdownComplete();
				_startupCoordinator = null;
			}
		}
	}

	private static void DisposeAndClear(ref Timer timer)
	{
		if (timer != null)
		{
			timer.Dispose();
			timer = null;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void TryShutdownAction(string name, Action action)
	{
		try
		{
			action?.Invoke();
			LogService.Info("SHUTDOWN_STEP name=" + name + " state=END");
		}
		catch (Exception ex)
		{
			LogService.Warn("SHUTDOWN_STEP name=" + name + " state=FAIL", ex);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void TryShutdownStartupAction(string name, Action action)
	{
		AddInStartupCoordinator startupCoordinator = _startupCoordinator;
		if (startupCoordinator != null && !startupCoordinator.WasStepSuccessful(name))
		{
			LogService.Info("SHUTDOWN_STEP name=" + name + " state=SKIP reason=not-initialized");
		}
		else
		{
			TryShutdownAction(name, action);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void Application_DocumentBeforeClose(Document document, ref bool cancel)
	{
		if (document == null)
		{
			return;
		}
		try
		{
			DocumentStyleManager.ReleaseDocument(document);
			LogService.Info("Document lifecycle cache released before close.");
		}
		catch (Exception ex)
		{
			LogService.Error("ThisAddIn.Application_DocumentBeforeClose", ex);
		}
	}

	private void InternalStartup()
	{
		base.Startup += ThisAddIn_Startup;
		base.Shutdown += ThisAddIn_Shutdown;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	[DebuggerNonUserCode]
	[EditorBrowsable(EditorBrowsableState.Never)]
	public ThisAddIn(ApplicationFactory factory, IServiceProvider serviceProvider)
		: base((OfficeFactory)factory, serviceProvider, "AddIn", "ThisAddIn")
	{
		Globals.Factory = factory;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	[DebuggerNonUserCode]
	[GeneratedCode("Microsoft.VisualStudio.Tools.Office.ProgrammingModel.dll", "17.0.0.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	protected override void Initialize()
	{
		base.Initialize();
		Application = GetHostItem<Application>(typeof(Application), "Application");
		Globals.ThisAddIn = this;
		System.Windows.Forms.Application.EnableVisualStyles();
		InitializeCachedData();
		InitializeControls();
		InitializeComponents();
		InitializeData();
	}

	[DebuggerNonUserCode]
	[GeneratedCode("Microsoft.VisualStudio.Tools.Office.ProgrammingModel.dll", "17.0.0.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	protected override void FinishInitialization()
	{
		InternalStartup();
		OnStartup();
	}

	[DebuggerNonUserCode]
	[GeneratedCode("Microsoft.VisualStudio.Tools.Office.ProgrammingModel.dll", "17.0.0.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	protected override void InitializeDataBindings()
	{
		BeginInitialization();
		BindToData();
		EndInitialization();
	}

	[DebuggerNonUserCode]
	[GeneratedCode("Microsoft.VisualStudio.Tools.Office.ProgrammingModel.dll", "17.0.0.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	private void InitializeCachedData()
	{
		if (base.DataHost != null && base.DataHost.IsCacheInitialized)
		{
			base.DataHost.FillCachedData((object)this);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("Microsoft.VisualStudio.Tools.Office.ProgrammingModel.dll", "17.0.0.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	private void InitializeData()
	{
	}

	[DebuggerNonUserCode]
	[GeneratedCode("Microsoft.VisualStudio.Tools.Office.ProgrammingModel.dll", "17.0.0.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	private void BindToData()
	{
	}

	[DebuggerNonUserCode]
	[EditorBrowsable(EditorBrowsableState.Advanced)]
	private void StartCaching(string MemberName)
	{
		base.DataHost.StartCaching((object)this, MemberName);
	}

	[DebuggerNonUserCode]
	[EditorBrowsable(EditorBrowsableState.Advanced)]
	private void StopCaching(string MemberName)
	{
		base.DataHost.StopCaching((object)this, MemberName);
	}

	[DebuggerNonUserCode]
	[EditorBrowsable(EditorBrowsableState.Advanced)]
	private bool IsCached(string MemberName)
	{
		return base.DataHost.IsCached((object)this, MemberName);
	}

	[DebuggerNonUserCode]
	[GeneratedCode("Microsoft.VisualStudio.Tools.Office.ProgrammingModel.dll", "17.0.0.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	private void BeginInitialization()
	{
		BeginInit();
		CustomTaskPanes.BeginInit();
		VstoSmartTags.BeginInit();
	}

	[DebuggerNonUserCode]
	[GeneratedCode("Microsoft.VisualStudio.Tools.Office.ProgrammingModel.dll", "17.0.0.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	private void EndInitialization()
	{
		VstoSmartTags.EndInit();
		CustomTaskPanes.EndInit();
		EndInit();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	[DebuggerNonUserCode]
	[GeneratedCode("Microsoft.VisualStudio.Tools.Office.ProgrammingModel.dll", "17.0.0.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	private void InitializeControls()
	{
		CustomTaskPanes = ((OfficeFactory)Globals.Factory).CreateCustomTaskPaneCollection((IServiceProvider)null, (IHostItemProvider)null, "CustomTaskPanes", "CustomTaskPanes", (object)this);
		VstoSmartTags = ((OfficeFactory)Globals.Factory).CreateSmartTagCollection((IServiceProvider)null, (IHostItemProvider)null, "VstoSmartTags", "VstoSmartTags", (object)this);
	}

	[DebuggerNonUserCode]
	[GeneratedCode("Microsoft.VisualStudio.Tools.Office.ProgrammingModel.dll", "17.0.0.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	private void InitializeComponents()
	{
	}

	[DebuggerNonUserCode]
	[EditorBrowsable(EditorBrowsableState.Advanced)]
	private bool NeedsFill(string MemberName)
	{
		return base.DataHost.NeedsFill((object)this, MemberName);
	}

	[DebuggerNonUserCode]
	[GeneratedCode("Microsoft.VisualStudio.Tools.Office.ProgrammingModel.dll", "17.0.0.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	protected override void OnShutdown()
	{
		((IDisposable)VstoSmartTags).Dispose();
		((IDisposable)CustomTaskPanes).Dispose();
		base.OnShutdown();
	}
}
