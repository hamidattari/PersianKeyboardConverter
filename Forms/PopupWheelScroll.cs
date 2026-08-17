using System.Runtime.InteropServices;

namespace PersianKeyboardConverter.Forms
{
    /// <summary>
    /// Scrolls the control under the cursor while a WS_EX_NOACTIVATE popup is
    /// visible.
    ///
    /// Such popups never take keyboard focus, so Windows delivers WM_MOUSEWHEEL to
    /// whichever window <i>is</i> focused (the application underneath the popup)
    /// instead of to the popup itself — the wheel would scroll the app behind the
    /// popup rather than the popup's own text. For the popup's lifetime this
    /// installs a low-level mouse hook; while the cursor is over the popup the
    /// hovered TextBox/ListBox is scrolled directly (via WM_VSCROLL, which works
    /// even without focus) and the message is swallowed so the window below is
    /// left untouched.
    /// </summary>
    internal sealed class PopupWheelScroll : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_VSCROLL = 0x0115;
        private const int SB_LINEUP = 0;
        private const int SB_LINEDOWN = 1;
        private const int WheelDelta = 120;
        private const uint GA_ROOT = 2;

        private readonly Form _form;
        private readonly LowLevelMouseProc _proc;
        private readonly IntPtr _hook;
        private readonly int _linesPerNotch;
        private int _wheelRemainder;

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        public PopupWheelScroll(Form form)
        {
            _form = form;
            _proc = HookCallback;
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);

            // 0 means "no scrolling" and -1 means "scroll one screen" under the raw
            // SPI_GETWHEELSCROLLLINES value — both fall back to the 3-line default.
            int lines = SystemInformation.MouseWheelScrollLines;
            _linesPerNotch = lines > 0 ? lines : 3;
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
                UnhookWindowsHookEx(_hook);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEWHEEL
                && !_form.IsDisposed && _form.IsHandleCreated)
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                // Only act when the popup is the window actually under the cursor
                // (it is topmost, but this keeps the redirect honest on overlap).
                if (GetAncestor(WindowFromPoint(info.pt), GA_ROOT) == _form.Handle)
                {
                    Control? target = FindScrollTarget(new Point(info.pt.X, info.pt.Y));
                    if (target != null && target.IsHandleCreated)
                    {
                        ScrollByWheel(target, unchecked((short)((info.mouseData >> 16) & 0xFFFF)));
                        return new IntPtr(1); // consumed — don't scroll the app underneath
                    }
                }
            }

            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        /// <summary>
        /// Accumulates wheel deltas (high-resolution wheels/trackpads send many
        /// sub-120 deltas) and scrolls the target by the system's lines-per-notch
        /// amount for each full notch.
        /// </summary>
        private void ScrollByWheel(Control target, int delta)
        {
            _wheelRemainder += delta;
            int notches = _wheelRemainder / WheelDelta;
            _wheelRemainder -= notches * WheelDelta;
            if (notches == 0) return;

            int lines = notches * _linesPerNotch;
            IntPtr code = lines > 0 ? (IntPtr)SB_LINEUP : (IntPtr)SB_LINEDOWN;

            for (int i = 0; i < Math.Abs(lines); i++)
                SendMessage(target.Handle, WM_VSCROLL, code, IntPtr.Zero);
        }

        /// <summary>
        /// Descends to the deepest control under the cursor, then accepts it (or an
        /// ancestor) if it is a scrollable text box or list.
        /// </summary>
        private Control? FindScrollTarget(Point screenPoint)
        {
            Control current = _form;
            for (;;)
            {
                Control? child = current.GetChildAtPoint(
                    current.PointToClient(screenPoint), GetChildAtPointSkip.Invisible);
                if (child == null) break;
                current = child;
            }

            for (Control? c = current; c != null && c != _form; c = c.Parent)
            {
                if (c is TextBox || c is ListBox) return c;
            }
            return null;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT pt);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
