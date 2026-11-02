using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

internal static class LauncherLayoutTests
{
    static int checks;
    static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    static void Inspect(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            if (!control.Visible) continue;
            if (control is SingleLineLabel && ((Label)control).AutoSize)
            {
                Size text = control.GetPreferredSize(Size.Empty);
                Check(control.Width >= text.Width && control.Height >= text.Height,
                    "Clipped single-line label: " + control.Text + " " + control.Size + " expected " + text);
            }
            if (control is CheckBox || control is RadioButton)
            {
                Size preferred = control.GetPreferredSize(Size.Empty);
                Check(control.Width >= preferred.Width && control.Height >= preferred.Height,
                    "Clipped choice: " + control.Text + " " + control.Size + " expected " + preferred);
            }
            if (!(parent is Panel && ((Panel)parent).AutoScroll) && !(parent is Form) && !(parent is TabControl))
                Check(control.Right <= parent.ClientSize.Width + 1 && control.Bottom <= parent.ClientSize.Height + 1,
                    "Control outside parent: " + control.GetType().Name + " " + control.Text + " " + control.Bounds + " parent " + parent.ClientSize);
            Inspect(control);
        }
    }
    [STAThread]
    static int Main(string[] args)
    {
        WindowsDpi.Initialize(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string root = Path.Combine(Path.GetTempPath(), "ersc-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "SeamlessCoop"));
        File.WriteAllText(Path.Combine(root, "SeamlessCoop", "ersc_settings.ini"), "[NETWORK]\nuse_steam_network=0\n[PASSWORD]\ncooppassword=layout-test\n");
        string output = args.Length > 0 ? args[0] : Path.Combine(root, "images");
        Directory.CreateDirectory(output);
        try
        {
            using (var form = new SettingsWindow(root))
            {
                // Exercise this application only, without sending any desktop
                // input or launching the game. Keep the test window off-screen.
                form.Shown += delegate { form.Location = new Point(-20000, -20000); };
                form.Show(); Application.DoEvents();
                using (Stream iconStream = typeof(SettingsWindow).Assembly.GetManifestResourceStream("SeamlessCoop.LauncherIcon"))
                {
                    // The public source build allows the Windows default icon;
                    // separately provided artwork is optional.
                    if (iconStream != null) using (var expected = new Icon(iconStream))
                    using (var expectedBitmap = expected.ToBitmap())
                    using (var actualBitmap = form.Icon.ToBitmap())
                    {
                        Check(expectedBitmap.Size == actualBitmap.Size, "Window icon size differs from embedded avatar");
                        for (int y = 0; y < expectedBitmap.Height; y++)
                            for (int x = 0; x < expectedBitmap.Width; x++)
                                if (expectedBitmap.GetPixel(x, y) != actualBitmap.GetPixel(x, y))
                                    throw new Exception("Window is not using the embedded avatar");
                        Check(true, "Window icon pixels match embedded avatar");
                    }
                }
                Console.WriteLine("Actual DPI: " + WindowsDpi.ForWindow(form.Handle) + " client=" + form.ClientSize + " min=" + form.MinimumSize + " font=" + form.Font + " scale=" + form.AutoScaleDimensions);
                var layout = (TableLayoutPanel)form.Controls[0];
                var tabs = (TabControl)layout.GetControlFromPosition(0, 1);
                for (int size = 0; size < 2; size++)
                {
                    if (size == 1) form.Size = form.MinimumSize;
                    for (int tab = 0; tab < 4; tab++)
                    {
                        tabs.SelectedIndex = tab; form.PerformLayout(); Application.DoEvents();
                        if (tab == 3) using (var preview = new Bitmap(form.Width, form.Height))
                        { form.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size)); preview.Save(Path.Combine(output, "save-before-check.png"), ImageFormat.Png); }
                        Inspect(form);
                        Control footer = layout.GetControlFromPosition(0, 3);
                        Check(footer.Bottom <= layout.ClientSize.Height, "Footer outside window");
                        if (tab < 2)
                        {
                            var scroll = (Panel)tabs.SelectedTab.Controls[0];
                            var document = (TableLayoutPanel)scroll.Controls[0];
                            Check(document.Width >= document.MinimumSize.Width, "Document compressed");
                            Check(!scroll.HorizontalScroll.Visible, "Unexpected horizontal scrollbar at supported window width");
                            Console.WriteLine("Size=" + size + " tab=" + tab + " viewport=" + scroll.ClientSize + " document=" + document.Size);
                        }
                        using (var bitmap = new Bitmap(form.Width, form.Height))
                        { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, "layout-" + size + "-" + tab + ".png"), ImageFormat.Png); }
                        if (size == 1 && tab < 2)
                        {
                            var scroll = (Panel)tabs.SelectedTab.Controls[0];
                            scroll.AutoScrollPosition = new Point(0, 100000); Application.DoEvents();
                            Check(scroll.Controls[0].Bottom <= scroll.ClientSize.Height, "Bottom of settings cannot be reached");
                            using (var bitmap = new Bitmap(form.Width, form.Height))
                            { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, "scrolled-" + tab + ".png"), ImageFormat.Png); }
                        }
                    }
                }
                tabs.SelectedIndex = 0; form.Steam.Checked = true; Application.DoEvents(); Inspect(form);
                Check(!form.Server.Visible, "Relay fields remain visible in Steam mode");
                form.Close();
            }
            Console.WriteLine("PASS: " + checks + " layout checks"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Directory.Delete(root, true); }
    }
}
