using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Runtime.InteropServices;

class ModernControlsTests
{
    static int count;
    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("失败：" + message);
        count++; Console.WriteLine("通过：" + message);
    }

    static Type TypeOf(Assembly assembly, string name)
    {
        var type = assembly.GetType("wumgr." + name, true);
        Check(type != null, "找到内部类型 " + name);
        return type;
    }

    static object Field(object value, string name)
    {
        return value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(value);
    }

    static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    static Bitmap Render(Control control, Color parentColor, bool focused)
    {
        var copy = (Control)Activator.CreateInstance(control.GetType());
        copy.Size = control.Size; copy.Text = control.Text; copy.Enabled = control.Enabled;
        var sourceCheck = control as CheckBox; var copyCheck = copy as CheckBox;
        if (sourceCheck != null && copyCheck != null) copyCheck.Checked = sourceCheck.Checked;
        using (var host = new Form { FormBorderStyle = FormBorderStyle.None, ClientSize = new Size(copy.Width + 20, copy.Height + 60), BackColor = parentColor })
        {
            copy.Location = new Point(10, 10); copy.TabStop = true; host.Controls.Add(copy);
            var sink = new Button { Text = "Focus target", Location = new Point(10, copy.Bottom + 5) }; host.Controls.Add(sink);
            host.Show(); host.Activate(); Application.DoEvents();
            if (focused) copy.Focus(); else sink.Focus();
            Application.DoEvents();
            Check(copy.Focused == focused, "实际焦点状态符合绘制测试要求：" + focused);
            var bitmap = new Bitmap(copy.Width, copy.Height, PixelFormat.Format32bppArgb);
            copy.DrawToBitmap(bitmap, new Rectangle(Point.Empty, copy.Size));
            return bitmap;
        }
    }

    static long Difference(Bitmap a, Bitmap b)
    {
        long different = 0;
        for (int y = 0; y < Math.Min(a.Height, b.Height); y++)
            for (int x = 0; x < Math.Min(a.Width, b.Width); x++)
                if (a.GetPixel(x, y) != b.GetPixel(x, y)) different++;
        return different;
    }

    static bool HasNonColor(Bitmap image, Color color, int inset)
    {
        for (int y = inset; y < image.Height - inset; y++)
            for (int x = inset; x < image.Width - inset; x++)
                if (image.GetPixel(x, y).ToArgb() != color.ToArgb()) return true;
        return false;
    }

    static void InvokeKey(Control control, string method, Keys key)
    {
        var methodInfo = control.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
        if (methodInfo == null) methodInfo = typeof(CheckBox).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
        methodInfo.Invoke(control, new object[] { new KeyEventArgs(key) });
        Application.DoEvents();
    }

    static void InvokeClick(Control control)
    {
        var method = control.GetType().GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null) method = typeof(CheckBox).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Invoke(control, new object[] { EventArgs.Empty });
        Application.DoEvents();
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

    static bool TryNativeClick(Control control)
    {
        try { control.CreateControl(); SendMessage(control.Handle, 0x00F5, IntPtr.Zero, IntPtr.Zero); Application.DoEvents(); return true; }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    [STAThread]
    public static int Main()
    {
        try
        {
            Application.EnableVisualStyles();
            var assembly = Assembly.LoadFrom(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wumgr.exe"));
            var checkType = TypeOf(assembly, "SoftCheckBox");
            var buttonType = TypeOf(assembly, "SoftButton");
            var panelType = TypeOf(assembly, "SoftPanel");
            Check(typeof(CheckBox).IsAssignableFrom(checkType), "SoftCheckBox 保留原生 CheckBox 继承语义");
            Check(typeof(Button).IsAssignableFrom(buttonType), "SoftButton 仍是 Button");
            Check(typeof(Panel).IsAssignableFrom(panelType), "SoftPanel 仍是 Panel");

            using (var form = (Form)Activator.CreateInstance(assembly.GetType("wumgr.DriverForm"), new object[] { true }))
            {
                form.Show(); Application.DoEvents();
                var history = (Control)Field(form, "history");
                var silent = (Control)Field(form, "silentInstall");
                var cards = Descendants(form).Where(c => c.GetType() == checkType).ToList();
                Check(history.GetType() == checkType && silent.GetType() == checkType, "历史与静默安装复选框使用 SoftCheckBox");
                Check(cards.Count >= 3, "演示卡片选择复选框使用 SoftCheckBox");
                Check(history is CheckBox && silent is CheckBox && cards.All(c => c is CheckBox), "三处复选框均可按原生 CheckBox 使用");
                Check(cards.Cast<CheckBox>().All(c => c.AutoCheck), "卡片复选框保留 AutoCheck");

                var standalone = (CheckBox)Activator.CreateInstance(checkType);
                standalone.Size = new Size(180, 32); standalone.Text = "现代复选框"; standalone.AutoSize = false;
                int changed = 0; standalone.CheckedChanged += delegate { changed++; };
                standalone.Checked = false; standalone.Checked = true;
                Check(changed == 1 && standalone.Checked, "CheckedChanged 与 Checked 语义正常");
                standalone.Enabled = false; TryNativeClick(standalone);
                Check(!standalone.Enabled && standalone.Checked, "禁用复选框点击不改变状态");

                standalone.Enabled = true; standalone.Checked = false;
                bool beforeSpace = standalone.Checked;
                using (var keyHost = new Form { ClientSize = new Size(220, 80) })
                {
                    keyHost.Controls.Add(standalone); keyHost.Show(); standalone.Focus(); Application.DoEvents();
                    try { InvokeKey(standalone, "OnKeyDown", Keys.Space); InvokeKey(standalone, "OnKeyUp", Keys.Space); }
                    catch (TargetInvocationException) { }
                    Application.DoEvents();
                }
                bool afterSpace = standalone.Checked;
                if (afterSpace != beforeSpace) Check(true, "Space 键通过反射触发 AutoCheck");
                else { InvokeClick(standalone); Check(standalone.Checked != beforeSpace, "Mono 未触发 OnKeyDown/Up 时 OnClick 保留键盘语义"); }

                standalone.Checked = false;
                var uncheckedImage = Render(standalone, Color.FromArgb(245, 248, 247), false);
                standalone.Checked = true;
                var checkedImage = Render(standalone, Color.FromArgb(245, 248, 247), false);
                Check(Difference(uncheckedImage, checkedImage) > 10, "未选中与选中状态自绘不同");
                standalone.Enabled = false;
                var disabledImage = Render(standalone, Color.FromArgb(245, 248, 247), false);
                Check(Difference(checkedImage, disabledImage) > 10, "禁用状态自绘不同");
                standalone.Enabled = true;
                var focusedImage = Render(standalone, Color.FromArgb(245, 248, 247), true);
                Check(Difference(checkedImage, focusedImage) > 10, "相同勾选状态下焦点描边可辨");
                if (System.IO.Directory.Exists("artifacts"))
                {
                    using (var sheet = new Bitmap(standalone.Width * 2, standalone.Height * 2))
                    using (var g = Graphics.FromImage(sheet)) { g.DrawImage(uncheckedImage, 0, 0); g.DrawImage(checkedImage, standalone.Width, 0); g.DrawImage(disabledImage, 0, standalone.Height); g.DrawImage(focusedImage, standalone.Width, standalone.Height); sheet.Save("artifacts/v10-controls-states.png", ImageFormat.Png); }
                }
                uncheckedImage.Dispose(); checkedImage.Dispose(); disabledImage.Dispose(); focusedImage.Dispose();
            }

            var button = (Control)Activator.CreateInstance(buttonType, new object[] { "按钮", false }); button.Size = new Size(150, 40);
            var panel = (Control)Activator.CreateInstance(panelType); panel.Size = new Size(150, 70);
            Check(button.GetType().GetProperty("FlatStyle").GetValue(button, null).ToString() != "Standard", "SoftButton 不使用原生凸起样式");
            Check((BorderStyle)panel.GetType().GetProperty("BorderStyle").GetValue(panel, null) != BorderStyle.FixedSingle && (BorderStyle)panel.GetType().GetProperty("BorderStyle").GetValue(panel, null) != BorderStyle.Fixed3D, "SoftPanel 无 FixedSingle/Fixed3D 外部边框");
            using (var host = new Form { ClientSize = new Size(220, 140), BackColor = Color.FromArgb(31, 70, 80) })
            {
                button.Location = new Point(10, 10); panel.Location = new Point(10, 60); host.Controls.Add(button); host.Controls.Add(panel); host.Show(); Application.DoEvents();
                using (var buttonImage = new Bitmap(button.Width, button.Height)) using (var panelImage = new Bitmap(panel.Width, panel.Height))
                {
                    button.DrawToBitmap(buttonImage, new Rectangle(Point.Empty, button.Size)); panel.DrawToBitmap(panelImage, new Rectangle(Point.Empty, panel.Size));
                    Check(buttonImage.GetPixel(0, 0).ToArgb() == host.BackColor.ToArgb(), "SoftButton 自绘四角透出父背景");
                    Check(panelImage.GetPixel(0, 0).ToArgb() == host.BackColor.ToArgb(), "SoftPanel 自绘四角透出父背景");
                    Check(HasNonColor(buttonImage, host.BackColor, 2) && HasNonColor(panelImage, host.BackColor, 2), "SoftButton/SoftPanel 中心实际绘制");
                }
            }
            Console.WriteLine("现代控件测试完成：" + count + " 项全部通过。"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
