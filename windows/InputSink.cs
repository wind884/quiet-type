using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace QuietType
{
    // A native edit control owns composition and candidate selection; only committed text leaves it.
    public sealed class InputSink : TextBox
    {
        bool internalChange;
        public bool IsComposing { get; private set; }
        public bool CaptureTab { get; set; }
        public event Action<string> Committed;
        public event Action<string> CompositionChanged;
        public event Action<Keys> InputCommand;
        [DllImport("imm32.dll")] static extern IntPtr ImmGetContext(IntPtr window);
        [DllImport("imm32.dll")] static extern bool ImmReleaseContext(IntPtr window, IntPtr context);
        [DllImport("imm32.dll", CharSet = CharSet.Unicode)] static extern int ImmGetCompositionStringW(IntPtr context, int index, byte[] buffer, int size);
        [DllImport("imm32.dll")] static extern bool ImmSetCandidateWindow(IntPtr context, ref CandidateForm form);
        [DllImport("imm32.dll")] static extern bool ImmNotifyIME(IntPtr context, int action, int index, int value);
        [DllImport("user32.dll")] static extern bool HideCaret(IntPtr window);
        [StructLayout(LayoutKind.Sequential)] struct CandidateForm
        { public int Index, Style; public Point Position; public Rectangle Area; }
        public InputSink()
        {
            Name = "InputSink";
            BorderStyle = BorderStyle.None;
            AutoSize = false;
            BackColor = ForeColor = Color.White;
            ShortcutsEnabled = false;
            ContextMenuStrip = new ContextMenuStrip();
            TabStop = false;
            ImeMode = ImeMode.NoControl;
            MaxLength = 4096;
            Width = 1;
            AccessibleName = "打字输入";
        }
        public void ClearPending()
        { internalChange = true; Clear(); internalChange = false; }
        public void CancelComposition()
        {
            internalChange = true;
            try
            {
                if (IsHandleCreated && IsComposing)
                {
                    IntPtr context = ImmGetContext(Handle);
                    if (context != IntPtr.Zero)
                    {
                        try { ImmNotifyIME(context, 0x15, 4, 0); }
                        finally { ImmReleaseContext(Handle, context); }
                    }
                }
                IsComposing = false; Clear();
                if (CompositionChanged != null) CompositionChanged("");
            }
            finally { internalChange = false; }
        }
        void Flush()
        {
            if (IsComposing || internalChange || TextLength == 0) return;
            if (char.IsHighSurrogate(Text[TextLength - 1])) return;
            string value = Text;
            ClearPending();
            if (Committed != null) Committed(value);
        }
        protected override void OnTextChanged(EventArgs e)
        { base.OnTextChanged(e); Flush(); }
        protected override bool IsInputKey(Keys data)
        {
            Keys key = data & Keys.KeyCode;
            return key == Keys.Enter || (key == Keys.Tab && CaptureTab) || base.IsInputKey(data);
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys data)
        {
            if (!IsComposing && (data == (Keys.Control | Keys.V) || data == (Keys.Shift | Keys.Insert)
                || data == (Keys.Control | Keys.X) || data == (Keys.Control | Keys.A))) return true;
            if (!IsComposing && data == (Keys.Control | Keys.Back))
            { if (InputCommand != null) InputCommand(data); return true; }
            return base.ProcessCmdKey(ref msg, data);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!IsComposing && (e.KeyCode == Keys.Back || e.KeyCode == Keys.Enter || (e.KeyCode == Keys.Tab && CaptureTab)))
            {
                if (InputCommand != null) InputCommand(e.KeyData);
                e.SuppressKeyPress = e.Handled = true;
            }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e)
        { base.OnGotFocus(e); HideCaret(Handle); }
        string ReadComposition()
        {
            IntPtr context = ImmGetContext(Handle);
            if (context == IntPtr.Zero) return "";
            try
            {
                int bytes = ImmGetCompositionStringW(context, 8, null, 0);
                if (bytes <= 0 || bytes > 8192) return "";
                var buffer = new byte[bytes];
                int read = ImmGetCompositionStringW(context, 8, buffer, bytes);
                return read > 0 ? Encoding.Unicode.GetString(buffer, 0, read) : "";
            }
            finally { ImmReleaseContext(Handle, context); }
        }
        void PlaceCandidates()
        {
            IntPtr context = ImmGetContext(Handle);
            if (context == IntPtr.Zero) return;
            try
            {
                var form = new CandidateForm { Index = 0, Style = 0x40, Position = new Point(0, Height + 2) };
                ImmSetCandidateWindow(context, ref form);
            }
            finally { ImmReleaseContext(Handle, context); }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x10d) IsComposing = true; // WM_IME_STARTCOMPOSITION
            int message = m.Msg;
            base.WndProc(ref m);
            if (message == 0x10f)
            {
                if (CompositionChanged != null) CompositionChanged(ReadComposition());
                PlaceCandidates();
            }
            if (message == 0x10e)
            {
                IsComposing = false;
                if (CompositionChanged != null) CompositionChanged("");
                Flush();
            }
            if ((message == 0x7 || message == 0xf) && IsHandleCreated) HideCaret(Handle);
        }
    }
}
