using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QuietType
{
    public sealed class PracticeControl : Control
    {
        public static readonly Color ReferenceColor = Color.FromArgb(205, 205, 205);
        public static readonly Color InputColor = Color.FromArgb(24, 24, 24);
        const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
            | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping;
        readonly InputSink sink = new InputSink();
        readonly Timer blinkTimer = new Timer();
        readonly float scale;
        Font latinFont, cjkFont, numberFont;
        int textSize = 15, cellWidth, lineHeight, scrollRow, visibleRows, totalRows, cachedColumns = -1;
        List<GlyphCell> cells;
        bool code, blink = true, follow = true, inputAllowed = true, mask;
        string composing = "";
        public TypingSession Session { get; private set; }
        public event EventHandler SessionChanged;
        public bool IsCode { get { return code; } }
        public bool HasInputFocus { get { return sink.Focused; } }
        public bool IsComposing { get { return sink.IsComposing; } }
        public int GhostPosition { get; set; }
        public bool MaskReference { get { return mask; } set { mask = value; Invalidate(); } }
        public bool InputAllowed
        {
            get { return inputAllowed; }
            set { inputAllowed = value; sink.Enabled = value; Invalidate(); }
        }
        public int TextSize
        {
            get { return textSize; }
            set { textSize = Math.Max(13, Math.Min(21, value)); RebuildFont(); follow = true; Invalidate(); }
        }
        public PracticeControl()
        {
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Cursor = Cursors.IBeam;
            TabStop = true;
            AccessibleName = "打字练习区";
            AccessibleRole = AccessibleRole.Text;
            Controls.Add(sink);
            sink.Committed += delegate(string text) { if (inputAllowed && Session != null && Session.Commit(text)) Changed(); };
            sink.CompositionChanged += delegate(string text) { composing = text; blink = true; Invalidate(); };
            sink.InputCommand += Command;
            sink.GotFocus += delegate { SetHostActive(Form.ActiveForm == FindForm()); blink = true; Invalidate(); };
            sink.LostFocus += delegate { SetHostActive(false); Invalidate(); };
            RebuildFont();
            blinkTimer.Interval = 530;
            blinkTimer.Tick += delegate { blink = !blink; if (HasInputFocus) Invalidate(); };
            blinkTimer.Start();
        }
        void RebuildFont()
        {
            if (latinFont != null) { latinFont.Dispose(); cjkFont.Dispose(); numberFont.Dispose(); }
            latinFont = new Font("Consolas", textSize * scale, GraphicsUnit.Pixel);
            if (!string.Equals(latinFont.FontFamily.Name, "Consolas", StringComparison.OrdinalIgnoreCase))
            { latinFont.Dispose(); latinFont = new Font(FontFamily.GenericMonospace, textSize * scale, GraphicsUnit.Pixel); }
            cjkFont = new Font("Microsoft YaHei UI", textSize * scale, GraphicsUnit.Pixel);
            numberFont = new Font("Consolas", 11 * scale, GraphicsUnit.Pixel);
            using (var g = CreateGraphics()) cellWidth = Math.Max(1, TextRenderer.MeasureText(g, "M", latinFont, new Size(10000,10000), Flags).Width);
            lineHeight = (int)((textSize + 14) * scale);
            sink.Font = latinFont;
            cachedColumns = -1;
        }
        public void Load(TypingSession session, bool isCode)
        {
            sink.CancelComposition();
            Session = session; code = isCode;
            sink.CaptureTab = code;
            composing = ""; MaskReference = false; GhostPosition = -1;
            cachedColumns = -1; scrollRow = 0; follow = true;
            Session.SetActive(HasInputFocus && Form.ActiveForm == FindForm());
            Changed();
        }
        public void SetHostActive(bool active)
        { if (Session != null) Session.SetActive(active && inputAllowed); }
        public void FocusInput()
        { if (inputAllowed) { sink.Focus(); SetHostActive(Form.ActiveForm == FindForm()); blink = true; Invalidate(); } }
        void Changed()
        {
            blink = true; follow = true; Invalidate();
            if (SessionChanged != null) SessionChanged(this, EventArgs.Empty);
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        }
        void Command(Keys keys)
        {
            if (!inputAllowed || Session == null) return;
            Keys key = keys & Keys.KeyCode;
            bool changed = false;
            if (key == Keys.Back) changed = Session.Backspace((keys & Keys.Control) != 0);
            if (code && key == Keys.Enter) changed = Session.Commit("\n");
            if (code && key == Keys.Tab)
            {
                int spaces = 0;
                while (spaces < 4 && Session.Position + spaces < Session.TargetUnits.Count && Session.TargetUnits[Session.Position + spaces] == " ") spaces++;
                changed = Session.Commit(new string(' ', Math.Max(1, spaces)));
            }
            if (changed) Changed();
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); FocusInput(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); FocusInput(); }
        protected override void OnResize(EventArgs e) { cachedColumns = -1; follow = true; base.OnResize(e); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scrollRow = Math.Max(0, Math.Min(Math.Max(0, totalRows - visibleRows), scrollRow - e.Delta / 120 * 3));
            follow = false; Invalidate(); base.OnMouseWheel(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.White);
            if (Session == null) return;
            var g = e.Graphics;
            int edge = (int)(5 * scale), gutter = code ? (int)(36 * scale) : 0;
            int columns = Math.Max(1, (Width - gutter - (int)(25 * scale)) / cellWidth);
            if (columns != cachedColumns)
            { cells = WordLayout.Build(Session.TargetUnits, columns); cachedColumns = columns; }
            visibleRows = Math.Max(1, (Height - edge * 2) / lineHeight);
            totalRows = cells[cells.Count - 1].Row + 1;
            var caret = cells[Session.Position];
            if (follow)
            {
                if (caret.Row < scrollRow) scrollRow = caret.Row;
                if (caret.Row >= scrollRow + visibleRows) scrollRow = caret.Row - visibleRows + 1;
                follow = false;
            }
            scrollRow = Math.Max(0, Math.Min(scrollRow, Math.Max(0, totalRows - visibleRows)));
            int line = 1;
            for (int i = 0; i < Session.TargetUnits.Count; i++)
            {
                var cell = cells[i];
                bool show = cell.Row >= scrollRow && cell.Row < scrollRow + visibleRows;
                if (code && show && (i == 0 || Session.TargetUnits[i - 1] == "\n"))
                    TextRenderer.DrawText(g, line.ToString(), numberFont,
                        new Rectangle(0, edge + (cell.Row - scrollRow) * lineHeight + (int)(3 * scale), gutter - (int)(12 * scale), lineHeight),
                        Color.FromArgb(182,182,182), Flags | TextFormatFlags.Right);
                if (Session.TargetUnits[i] == "\n") line++;
                if (!show) continue;
                int x = gutter + cell.Column * cellWidth, y = edge + (cell.Row - scrollRow) * lineHeight;
                bool entered = i < Session.Position;
                string unit = entered ? Session.InputUnits[i] : Session.TargetUnits[i];
                if (unit == "\n") unit = "↵";
                if (!entered && mask && unit != " ") unit = "_";
                Font font = PracticeText.CellSpan(unit) > 1 ? cjkFont : latinFont;
                var clip = new Rectangle(x, y, Math.Max(cellWidth, cell.Span * cellWidth), lineHeight);
                var state = g.Save();
                g.SetClip(clip, CombineMode.Intersect);
                TextRenderer.DrawText(g, unit, font, new Point(x, y), entered ? InputColor : ReferenceColor, Color.White, Flags);
                g.Restore(state);
                if (entered && Session.InputUnits[i] != Session.TargetUnits[i])
                    using (var pen = new Pen(Color.FromArgb(193,104,104), Math.Max(1, scale)))
                        g.DrawLine(pen, x, y + Math.Max(latinFont.Height,cjkFont.Height) + 1, x + cell.Span * cellWidth - 2, y + Math.Max(latinFont.Height,cjkFont.Height) + 1);
            }
            int caretX = gutter + caret.Column * cellWidth;
            int caretY = edge + (caret.Row - scrollRow) * lineHeight;
            // Keep the native IME context and candidate window at the visual insertion position.
            // The one-pixel native receiver stays in the empty line gap, never on top of reference ink.
            if (!sink.IsComposing) sink.SetBounds(Math.Max(0, Math.Min(Width - 2, caretX)),
                Math.Max(0, Math.Min(Height - 1, caretY + Math.Max(latinFont.Height,cjkFont.Height) + 4)), 1, 1);
            if (HasInputFocus && !Session.IsFinished && inputAllowed && caret.Row >= scrollRow && caret.Row < scrollRow + visibleRows)
            {
                if (!string.IsNullOrEmpty(composing))
                {
                    var size = TextRenderer.MeasureText(g, composing, cjkFont, new Size(10000,10000), Flags);
                    var rect = new Rectangle(caretX, caretY, Math.Max(size.Width + cellWidth, cellWidth * 5), lineHeight);
                    using (var brush = new SolidBrush(Color.White)) g.FillRectangle(brush, rect);
                    TextRenderer.DrawText(g, composing, cjkFont, rect, InputColor, Flags);
                    using (var pen = new Pen(Color.FromArgb(90,115,138), scale)) g.DrawLine(pen, rect.Left, rect.Bottom - 5, rect.Right, rect.Bottom - 5);
                }
                else if (blink)
                    using (var pen = new Pen(InputColor, Math.Max(1, scale))) g.DrawLine(pen, caretX, caretY + 2, caretX, caretY + latinFont.Height - 1);
            }
            if (GhostPosition >= 0 && GhostPosition < cells.Count)
            {
                var ghost = cells[GhostPosition];
                if (ghost.Row >= scrollRow && ghost.Row < scrollRow + visibleRows)
                    using (var pen = new Pen(Color.FromArgb(120,145,165), Math.Max(1,scale)))
                    { pen.DashStyle = DashStyle.Dot; float x = gutter + ghost.Column * cellWidth; float y = edge + (ghost.Row - scrollRow) * lineHeight; g.DrawLine(pen,x,y,x,y+latinFont.Height); }
            }
            if (totalRows > visibleRows)
            {
                float track = Height - edge * 2, thumb = Math.Max(18 * scale, track * visibleRows / totalRows);
                float y = edge + (track - thumb) * scrollRow / Math.Max(1, totalRows - visibleRows);
                using (var brush = new SolidBrush(Color.FromArgb(222,222,222))) g.FillRectangle(brush, Width - 3 * scale, y, 2 * scale, thumb);
            }
            base.OnPaint(e);
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new PracticeAccessible(this); }
        sealed class PracticeAccessible : ControlAccessibleObject
        {
            readonly PracticeControl owner;
            public PracticeAccessible(PracticeControl control) : base(control) { owner = control; }
            public override string Value { get { return owner.Session == null ? "" : owner.Session.Typed; } }
            public override string Description { get { return owner.Session == null ? "" : "原文 " + owner.Session.Target; } }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { blinkTimer.Dispose(); if (latinFont != null) { latinFont.Dispose(); cjkFont.Dispose(); numberFont.Dispose(); } }
            base.Dispose(disposing);
        }
    }
}
