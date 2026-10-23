using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace QuietType
{
    public static class InputTests
    {
        private static int passed;
        private static int failed;
        private static string outputDirectory;

#if INPUT_TESTS
        [STAThread]
        public static int Main() { return Run(); }
#endif

        public static int Run()
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) return RunOnUiThread();
            int result = 1;
            Thread uiThread = new Thread(delegate() { result = RunOnUiThread(); });
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start(); uiThread.Join();
            return result;
        }

        private static int RunOnUiThread()
        {
            passed = 0; failed = 0;
            string basePath = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string checksPath = basePath.EndsWith(Path.Combine("_checks", "input-tests"), StringComparison.OrdinalIgnoreCase)
                ? basePath : Path.Combine(basePath, "_checks", "input-tests");
            outputDirectory = Path.Combine(checksPath, "run-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outputDirectory);
            try
            {
                Application.EnableVisualStyles();
                Check("Native committed Chinese, accent and emoji batches reach the session", CommittedBatches);
                Check("Incomplete surrogate input waits for its second half", SplitSurrogate);
                Check("Composition message gate holds preedit and flushes final text once", CompositionMessageGate);
                Check("Changing a round cancels pending composition before new input", CancelOnLoad);
                Check("Native Backspace and Control Backspace edit text elements and words", BackspaceCommands);
                Check("Native Enter and Tab preserve code lines and indentation", CodeCommands);
                Check("Disabled input cannot change the session", DisabledInput);
                Check("Black Latin glyphs occupy prior gray glyph pixels at fonts 13 and 21", LatinAlignment);
                Check("Black CJK glyphs occupy prior gray glyph pixels at fonts 13 and 21", CjkAlignment);
                Check("Memory masking hides reference glyphs and preserves typed black pixels", MemoryMask);
                Check("Narrow multiline Unicode retains the full glyph ink at fonts 13 and 21", NarrowMultiline);
                Console.WriteLine("Input tests: " + passed.ToString(CultureInfo.InvariantCulture) + " passed, " + failed.ToString(CultureInfo.InvariantCulture) + " failed");
                Console.WriteLine("Composition coverage is a message gate test, not physical input method certification");
                Console.WriteLine("Render checks: " + outputDirectory);
                return failed == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL " + ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        private static void Check(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex)
            {
                failed++;
                Exception detail = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                Console.Error.WriteLine("FAIL " + name + ": " + detail.Message);
            }
        }
        private static void Require(bool value, string failure)
        {
            if (!value) throw new InvalidOperationException(failure);
        }
        private static PracticeControl View(string text, bool code, int size, int width, int height)
        {
            PracticeControl view = new PracticeControl();
            view.Size = new Size(width, height); view.TextSize = size;
            view.Load(new TypingSession(text, new SessionOptions { IsCode = code }), code);
            view.CreateControl(); Sink(view).CreateControl();
            Require(!view.HasInputFocus, "Pixel checks require an unfocused control so no caret is drawn");
            return view;
        }
        private static InputSink Sink(PracticeControl view)
        {
            foreach (Control child in view.Controls)
            {
                InputSink sink = child as InputSink;
                if (sink != null) return sink;
            }
            throw new InvalidOperationException("The practice control has no native input child");
        }
        private static void Key(InputSink sink, Keys key)
        {
            KeyEventArgs args = new KeyEventArgs(key);
            MethodInfo handler = typeof(InputSink).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);
            handler.Invoke(sink, new object[] { args });
            if (!sink.IsComposing && (args.KeyCode == Keys.Back || args.KeyCode == Keys.Enter || (args.KeyCode == Keys.Tab && sink.CaptureTab)))
                Require(args.Handled && args.SuppressKeyPress, "A handled editing key could also reach the native text buffer");
        }
        private static void ImeMessage(InputSink sink, int id)
        {
            Message message = Message.Create(sink.Handle, id, IntPtr.Zero, IntPtr.Zero);
            MethodInfo handler = typeof(InputSink).GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic);
            object[] args = new object[] { message };
            handler.Invoke(sink, args);
        }

        private static void CommittedBatches()
        {
            using (PracticeControl view = View("你好，café 😀！ extra", false, 15, 500, 160))
            {
                InputSink sink = Sink(view); int commits = 0, changes = 0;
                sink.Committed += delegate { commits++; };
                view.SessionChanged += delegate { changes++; };
                sink.Text = "你好，";
                Require(view.Session.Typed == "你好，" && view.Session.Attempts == 3, "Chinese native TextChanged was not committed once");
                sink.Text = "cafe\u0301 😀";
                sink.Text = "！";
                Require(view.Session.Typed == "你好，café 😀！" && view.Session.Position == 10,
                    "Accent normalization or emoji text element counting failed through the native input event");
                Require(view.Session.CorrectCount == 10 && view.Session.Attempts == 10 && commits == 3 && changes == 3,
                    "One native text batch produced duplicate scoring or change events");
                Require(sink.TextLength == 0, "Committed native input was not cleared");
            }
        }
        private static void SplitSurrogate()
        {
            using (PracticeControl view = View("😀é👩‍💻Z", false, 15, 400, 160))
            {
                InputSink sink = Sink(view); int commits = 0;
                sink.Committed += delegate { commits++; };
                sink.Text = "\ud83d";
                Require(view.Session.Attempts == 0 && sink.TextLength == 1, "An incomplete surrogate was committed or discarded");
                sink.AppendText("\ude00");
                Require(view.Session.Typed == "😀" && view.Session.Position == 1 && commits == 1, "The completed surrogate pair was not committed once");
                sink.Text = "e\u0301👩‍💻";
                Require(view.Session.Typed == "😀é👩‍💻" && view.Session.Position == 3 && view.Session.CorrectCount == 3,
                    "Accented or joined emoji batches did not reach the session as text elements");
            }
        }
        private static void CompositionMessageGate()
        {
            using (PracticeControl view = View("你好世界", false, 15, 400, 160))
            {
                InputSink sink = Sink(view); int commits = 0;
                sink.Committed += delegate { commits++; };
                ImeMessage(sink, 0x10d);
                Require(sink.IsComposing, "Composition START did not close the commit gate");
                sink.Text = "ni";
                Key(sink, Keys.Back);
                Require(view.Session.Attempts == 0 && view.Session.Typed == "" && commits == 0,
                    "Preedit text or editing keys escaped the composition gate");
                sink.Text = "你";
                Require(view.Session.Position == 0, "Composition result was scored before END");
                ImeMessage(sink, 0x10e);
                Require(!sink.IsComposing && view.Session.Typed == "你" && view.Session.Attempts == 1 && commits == 1,
                    "Composition END did not flush its final text exactly once");
                ImeMessage(sink, 0x10e);
                Require(view.Session.Attempts == 1 && commits == 1 && sink.TextLength == 0, "A repeated END duplicated committed text");
                ImeMessage(sink, 0x10d); sink.Text = ""; ImeMessage(sink, 0x10e);
                Require(view.Session.Attempts == 1 && commits == 1, "Cancelled composition counted as input");
            }
        }
        private static void BackspaceCommands()
        {
            using (PracticeControl view = View("alpha beta 中国 😀Z", false, 15, 500, 160))
            {
                InputSink sink = Sink(view); sink.Text = "alpha beta 中国 😀";
                Key(sink, Keys.Back);
                Require(view.Session.Typed == "alpha beta 中国 ", "Native Backspace split or failed to remove the emoji");
                Key(sink, Keys.Control | Keys.Back);
                Require(view.Session.Typed == "alpha beta 中", "Control Backspace did not remove whitespace and one CJK text element");
                Key(sink, Keys.Control | Keys.Back);
                Key(sink, Keys.Control | Keys.Back);
                Require(view.Session.Typed == "alpha ", "Control Backspace did not remove the preceding Latin word");
                Require(sink.TextLength == 0, "Editing commands leaked characters into the native buffer");
            }
        }
        private static void CancelOnLoad()
        {
            using (PracticeControl view = View("旧文本", false, 15, 400, 160))
            {
                InputSink sink = Sink(view);
                ImeMessage(sink,0x10d); sink.Text = "旧";
                view.Load(new TypingSession("新文本",new SessionOptions()),false);
                Require(!sink.IsComposing && sink.TextLength == 0 && view.Session.Attempts == 0,"Old composition survived the round change");
                sink.Text = "新";
                Require(view.Session.Typed == "新" && view.Session.Attempts == 1,"New text was still trapped in old composition");
            }
        }
        private static void CodeCommands()
        {
            using (PracticeControl view = View("x\n    y\n  z", true, 15, 400, 220))
            {
                InputSink sink = Sink(view);
                Require(sink.CaptureTab, "Code mode did not capture Tab");
                sink.Text = "x"; Key(sink, Keys.Enter); Key(sink, Keys.Tab);
                Require(view.Session.Typed == "x\n    ", "Enter or four-space code indentation was lost");
                sink.Text = "y"; Key(sink, Keys.Enter); Key(sink, Keys.Tab);
                Require(view.Session.Typed == "x\n    y\n  ", "Tab did not respect the next line's two-space indentation");
                sink.Text = "z";
                Require(view.Session.Reason == FinishReason.Completed && view.Session.TotalMistakes == 0 && view.Session.Position == 11,
                    "Native code commands produced a wrong result or duplicate characters");
            }
            using (PracticeControl view = View("abc", false, 15, 300, 140))
            {
                InputSink sink = Sink(view); Key(sink, Keys.Enter); Key(sink, Keys.Tab);
                Require(!sink.CaptureTab && view.Session.Position == 0, "Prose mode accepted code-only indentation or newlines");
            }
        }
        private static void DisabledInput()
        {
            using (PracticeControl view = View("abc", false, 15, 300, 140))
            {
                InputSink sink = Sink(view); view.InputAllowed = false;
                sink.Text = "a"; Key(sink, Keys.Back);
                Require(view.Session.Attempts == 0 && view.Session.Position == 0, "Locked input changed the session");
                view.InputAllowed = true; sink.Text = "a";
                Require(view.Session.Typed == "a" && view.Session.Attempts == 1, "Re-enabled input retained a stale native batch");
            }
        }

        private static Bitmap Render(PracticeControl view, string name)
        {
            Require(!view.HasInputFocus, "A focused caret would contaminate the glyph pixel check");
            Bitmap bitmap = new Bitmap(view.Width, view.Height);
            view.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            if (name != null) bitmap.Save(Path.Combine(outputDirectory, name + ".png"));
            return bitmap;
        }
        private static int Minimum(Color color) { return Math.Min(color.R, Math.Min(color.G, color.B)); }
        private static int Maximum(Color color) { return Math.Max(color.R, Math.Max(color.G, color.B)); }
        private static void LatinAlignment() { CheckAlignment("MWQ8é", "latin"); }
        private static void CjkAlignment() { CheckAlignment("中文汉字", "cjk"); }
        private static void CheckAlignment(string sample, string name)
        {
            string failures = "";
            foreach (int size in new int[] { 13, 21 })
            using (PracticeControl view = View(sample + " Z", false, size, 500, 180))
            using (Bitmap reference = Render(view, name + "-" + size + "-gray"))
            {
                Sink(view).Text = sample;
                Require(view.Session.Typed == sample && view.Session.TotalMistakes == 0, "The alignment sample was not committed through the input sink");
                using (Bitmap entered = Render(view, name + "-" + size + "-black"))
                {
                    int dark = 0, misplaced = 0;
                    for (int y = 0; y < entered.Height; y++)
                    for (int x = 0; x < entered.Width; x++)
                    {
                        Color after = entered.GetPixel(x, y);
                        if (Maximum(after) < 180)
                        {
                            dark++;
                            if (Minimum(reference.GetPixel(x, y)) >= 254) misplaced++;
                        }
                    }
                    if (dark < sample.Length * 6) failures += "No substantial black glyph ink was rendered at font " + size + "; ";
                    if (misplaced != 0) failures += name + " font " + size + " has " + misplaced + " dark pixels outside the prior gray glyph footprint; ";
                }
            }
            Require(failures.Length == 0, failures);
        }
        private static void MemoryMask()
        {
            using (PracticeControl view = View("A中文测试Z", false, 21, 500, 180))
            {
                Sink(view).Text = "A";
                using (Bitmap visible = Render(view, "memory-visible"))
                {
                    view.MaskReference = true;
                    using (Bitmap masked = Render(view, "memory-masked"))
                    {
                        int typedInk = 0, changedTypedInk = 0, grayInk = 0, removedGrayInk = 0;
                        for (int y = 0; y < visible.Height; y++)
                        for (int x = 0; x < visible.Width; x++)
                        {
                            Color before = visible.GetPixel(x, y), after = masked.GetPixel(x, y);
                            if (Maximum(before) < 180)
                            {
                                typedInk++;
                                if (before.ToArgb() != after.ToArgb()) changedTypedInk++;
                            }
                            if (Minimum(before) >= 190 && Maximum(before) < 254)
                            {
                                grayInk++;
                                if (Minimum(after) >= 254) removedGrayInk++;
                            }
                        }
                        Require(typedInk > 10 && changedTypedInk == 0, "Memory masking hid or moved typed black glyphs");
                        Require(grayInk > 60 && removedGrayInk > grayInk * 0.6,
                            "Memory masking left target glyph shapes visible instead of hiding them");
                    }
                }
            }
        }
        private static void NarrowMultiline()
        {
            string sample = "中A😀文 café\n  日本語\n世界 end";
            foreach (int size in new int[] { 13, 21 })
            using (PracticeControl wide = View(sample, true, size, 700, 650))
            using (PracticeControl narrow = View(sample, true, size, 135, 650))
            {
                Sink(wide).Text = sample; Sink(narrow).Text = sample;
                Require(wide.Session.Typed == sample && narrow.Session.Typed == sample,
                    "Multiline native text did not reach the code session intact");
                using (Bitmap a = Render(wide, "unicode-" + size + "-wide"))
                using (Bitmap b = Render(narrow, "unicode-" + size + "-narrow"))
                {
                    float dpi;
                    using (Graphics g = wide.CreateGraphics()) dpi = g.DpiX / 96F;
                    int gutter = (int)(36 * dpi);
                    Rectangle wideBounds, narrowBounds;
                    int wideInk = CountDarkInk(a, gutter, out wideBounds);
                    int narrowInk = CountDarkInk(b, gutter, out narrowBounds);
                    Require(wideInk > 100 && Math.Abs(wideInk - narrowInk) <= 2,
                        "Narrow Unicode layout clipped glyph ink at font " + size + " (wide " + wideInk + ", narrow " + narrowInk + ")");
                    Require(narrowBounds.Bottom > wideBounds.Bottom && narrowBounds.Bottom < narrow.Height - 4,
                        "Unicode lines did not wrap fully within the narrow viewport at font " + size);
                    Require(narrowBounds.Right < narrow.Width - 4, "Unicode glyphs reached the clipped right edge at font " + size);
                }
            }
        }
        private static int CountDarkInk(Bitmap bitmap, int left, out Rectangle bounds)
        {
            int count = 0, minX = bitmap.Width, minY = bitmap.Height, maxX = -1, maxY = -1;
            for (int y = 0; y < bitmap.Height; y++)
            for (int x = left; x < bitmap.Width; x++)
            {
                if (Maximum(bitmap.GetPixel(x, y)) >= 180) continue;
                count++; minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
            bounds = count == 0 ? Rectangle.Empty : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            return count;
        }
    }
}
