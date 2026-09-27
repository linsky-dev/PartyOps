using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DocumentRepository.Services.Logging;

namespace DocumentRepository;

public class KeyboardHook : NativeWindow, IDisposable
{
	private const int WM_HOTKEY = 786;

	private const uint HOTKEY_ID_NUMPAD_PLUS = 16384u;

	private const uint HOTKEY_ID_MAIN_PLUS = 16385u;

	private readonly Action _callback;

	private bool _registeredNumpadPlus;

	private bool _registeredMainPlus;

	private const uint MOD_ALT = 1u;

	private const uint MOD_CONTROL = 2u;

	private const uint MOD_SHIFT = 4u;

	private const uint MOD_WIN = 8u;

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool RegisterHotKey(IntPtr hWnd, uint id, uint fsModifiers, uint vk);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool UnregisterHotKey(IntPtr hWnd, uint id);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public KeyboardHook(Action callback)
	{
		_callback = callback;
		((NativeWindow)this).CreateHandle(new CreateParams
		{
			ExStyle = 128,
			Style = int.MinValue
		});
		_registeredNumpadPlus = RegisterHotKey(((NativeWindow)this).Handle, 16384u, 5u, 107u);
		_registeredMainPlus = RegisterHotKey(((NativeWindow)this).Handle, 16385u, 5u, 187u);
		if (!_registeredNumpadPlus && !_registeredMainPlus)
		{
			LogService.Error("RegisterHotKey failed: " + Marshal.GetLastWin32Error());
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	protected override void WndProc(ref Message m)
	{
		uint num = (uint)m.WParam.ToInt32();
		if (m.Msg == 786 && (num == 16384 || num == 16385))
		{
			try
			{
				_callback?.Invoke();
			}
			catch (Exception ex)
			{
				LogService.Error("HotKey callback", ex);
			}
		}
		base.WndProc(ref m);
	}

	public void Dispose()
	{
		if (_registeredNumpadPlus)
		{
			UnregisterHotKey(((NativeWindow)this).Handle, 16384u);
			_registeredNumpadPlus = false;
		}
		if (_registeredMainPlus)
		{
			UnregisterHotKey(((NativeWindow)this).Handle, 16385u);
			_registeredMainPlus = false;
		}
		if (((NativeWindow)this).Handle != IntPtr.Zero)
		{
			((NativeWindow)this).DestroyHandle();
		}
	}

	void IDisposable.Dispose()
	{
		this.Dispose();
	}
}
