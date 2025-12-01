using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using Timer = System.Windows.Forms.Timer;

namespace TypeTool;

// CONFIGURATION CLASS
public class AppConfig
{
    public bool EnterKeyEnabled { get; set; } = false;
    public bool ShowPreviewWindow { get; set; } = true;
    public int TypingDelayMs { get; set; } = 1; // Standard 1ms
    
    public HotkeyDef TypingHotkey { get; set; } = new HotkeyDef(2, 66); // Strg+B
    public HotkeyDef EnterToggleHotkey { get; set; } = new HotkeyDef(3, 66); // Strg+Alt+B
}

public class HotkeyDef
{
    public int FsModifiers { get; set; }
    public int VkCode { get; set; }

    public HotkeyDef() { }
    public HotkeyDef(int mod, int vk) { FsModifiers = mod; VkCode = vk; }
    
    public string ToReadableString()
    {
        var keys = new List<string>();
        if ((FsModifiers & 2) != 0) keys.Add("Strg");
        if ((FsModifiers & 1) != 0) keys.Add("Alt");
        if ((FsModifiers & 4) != 0) keys.Add("Shift");
        keys.Add(((Keys)VkCode).ToString());
        return string.Join(" + ", keys);
    }
}

// MAIN PROGRAM
static class Program
{
    static Mutex? _mutex;

    [STAThread]
    static void Main(string[] args)
    {
        // TRICK: Icon generieren, wenn angefordert
        if (args.Length > 0 && args[0] == "--generate-icon")
        {
            try 
            {
                using (var icon = IconGenerator.CreateDarkIcon())
                using (var stream = new FileStream("icon.ico", FileMode.Create))
                {
                    icon.Save(stream);
                }
                MessageBox.Show("Icon 'icon.ico' wurde erfolgreich erstellt!", "TypeTool Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Fehler beim Erstellen des Icons: " + ex.Message);
            }
            return; // Programm beenden
        }

        const string appName = "TypeTool_Unique_Mutex_Name";
        bool createdNew;
        _mutex = new Mutex(true, appName, out createdNew);

        if (!createdNew)
        {
            MessageBox.Show("TypeTool läuft bereits!", "TypeTool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TypeToolApplication());
    }
}

// APPLICATION CONTEXT
public class TypeToolApplication : ApplicationContext
{
    private NotifyIcon _trayIcon;
    private ContextMenuStrip _contextMenu;
    public AppConfig Config = new(); 
    private const string ConfigFile = "config.json";
    
    private HotkeyWindow _hotkeyWindow;
    private const int ID_TYPE = 1;
    private const int ID_TOGGLE_ENTER = 2;

    public TypeToolApplication()
    {
        LoadConfig();
        InitializeTray();
        _hotkeyWindow = new HotkeyWindow(this);
        RegisterHotKeys();
    }

    private void InitializeTray()
    {
        _contextMenu = new ContextMenuStrip();
        UpdateMenu();

        Icon customIcon = IconGenerator.CreateDarkIcon();

        _trayIcon = new NotifyIcon
        {
            Icon = customIcon, 
            ContextMenuStrip = _contextMenu,
            Visible = true,
            Text = "TypeTool"
        };
        UpdateTrayTooltip();
    }

    private void UpdateTrayTooltip()
    {
        string hkType = Config.TypingHotkey.ToReadableString();
        _trayIcon.Text = $"TypeTool ({hkType})";
    }

    public void UpdateMenu()
    {
        _contextMenu.Items.Clear();

        var itemPreview = new ToolStripMenuItem("Benachrichtigungen", null, (s, e) => TogglePreview()) { Checked = Config.ShowPreviewWindow };
        var itemEnter = new ToolStripMenuItem("Enter am Ende", null, (s, e) => ToggleEnter()) { Checked = Config.EnterKeyEnabled };
        
        var itemHotkeys = new ToolStripMenuItem("Hotkeys ändern...", null, (s, e) => OpenHotkeySettings());
        var itemSpeed = new ToolStripMenuItem("Geschwindigkeit...", null, (s, e) => ChangeSpeed());
        
        var itemRestart = new ToolStripMenuItem("Neustarten", null, (s, e) => RestartApp());
        var itemExit = new ToolStripMenuItem("Beenden", null, (s, e) => ExitApp());

        _contextMenu.Items.Add(itemPreview);
        _contextMenu.Items.Add(itemEnter);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(itemHotkeys);
        _contextMenu.Items.Add(itemSpeed);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(new ToolStripMenuItem("Hotkeys neu laden", null, (s, e) => RegisterHotKeys()));
        _contextMenu.Items.Add(itemRestart);
        _contextMenu.Items.Add(itemExit);
    }

    private void TogglePreview()
    {
        Config.ShowPreviewWindow = !Config.ShowPreviewWindow;
        SaveConfig();
        UpdateMenu();
    }

    private void ToggleEnter()
    {
        Config.EnterKeyEnabled = !Config.EnterKeyEnabled;
        SaveConfig();
        UpdateMenu();
        ShowNotification("Einstellung geändert", $"Enter am Ende: {(Config.EnterKeyEnabled ? "AN" : "AUS")}", ToolTipIcon.Info);
    }

    private void ChangeSpeed()
    {
        using var dialog = new InputDialog("Geschwindigkeit (ms pro Zeichen):", Config.TypingDelayMs.ToString());
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            if (int.TryParse(dialog.InputValue, out int newDelay) && newDelay >= 0)
            {
                Config.TypingDelayMs = newDelay;
                SaveConfig();
            }
        }
    }

    private void OpenHotkeySettings()
    {
        using var form = new HotkeySettingsForm(this);
        form.Icon = _trayIcon.Icon;
        
        if (form.ShowDialog() == DialogResult.OK)
        {
            SaveConfig();
            RegisterHotKeys(); 
            UpdateMenu();
            UpdateTrayTooltip();
            ShowNotification("Info", "Hotkeys erfolgreich gespeichert!", ToolTipIcon.Info);
        }
    }

    public void RegisterHotKeys()
    {
        try
        {
            NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, ID_TYPE);
            NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, ID_TOGGLE_ENTER);

            NativeMethods.RegisterHotKey(_hotkeyWindow.Handle, ID_TYPE, (uint)Config.TypingHotkey.FsModifiers, (uint)Config.TypingHotkey.VkCode);
            NativeMethods.RegisterHotKey(_hotkeyWindow.Handle, ID_TOGGLE_ENTER, (uint)Config.EnterToggleHotkey.FsModifiers, (uint)Config.EnterToggleHotkey.VkCode);
        }
        catch { }
    }

    public void HandleHotkey(int id)
    {
        switch (id)
        {
            case ID_TYPE:
                StartTypingProcess();
                break;
            case ID_TOGGLE_ENTER:
                ToggleEnter();
                break;
        }
    }

    private void StartTypingProcess()
    {
        if (!WaitForCtrlRelease()) return;

        if (!Clipboard.ContainsText())
        {
            ShowNotification("Fehler", "Zwischenablage ist leer!", ToolTipIcon.Error);
            return;
        }

        string text = Clipboard.GetText();
        text = text.TrimEnd(); 

        bool needsWarning = text.Length >= 100;

        if (needsWarning)
        {
            ShowNotification("ACHTUNG: Großer Text", 
                $"{text.Length} Zeichen werden getippt.\nDrücke ESC zum Abbrechen (Start in 1.5s)", 
                ToolTipIcon.Warning);
        }
        else if (Config.ShowPreviewWindow)
        {
            string preview = text.Length > 60 ? text.Substring(0, 60) + "..." : text;
            ShowNotification($"Tippe {text.Length} Zeichen", preview, ToolTipIcon.Info);
        }

        new Thread(() =>
        {
            try
            {
                if (needsWarning)
                {
                    for(int i=0; i < 30; i++) 
                    {
                        if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8000) != 0) return; 
                        Thread.Sleep(50);
                    }
                }

                foreach (char c in text)
                {
                    if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8000) != 0) return;
                    
                    if (c == '\r') continue; 
                    string keys = c.ToString();
                    if ("+^%~(){}[]".Contains(c)) keys = "{" + c + "}";
                    
                    SendKeys.SendWait(keys);
                    Thread.Sleep(Config.TypingDelayMs);
                }

                if (Config.EnterKeyEnabled)
                {
                    Thread.Sleep(50);
                    SendKeys.SendWait("{ENTER}");
                }
            }
            catch { }
        }).Start();
    }

    private bool WaitForCtrlRelease()
    {
        int timeout = 0;
        while ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0)
        {
            Thread.Sleep(50);
            timeout += 50;
            if (timeout > 3000) return false;
        }
        Thread.Sleep(100);
        return true;
    }

    private void ShowNotification(string title, string msg, ToolTipIcon icon)
    {
        _trayIcon.Visible = true;
        _trayIcon.BalloonTipTitle = title;
        _trayIcon.BalloonTipText = msg;
        _trayIcon.BalloonTipIcon = icon;
        _trayIcon.ShowBalloonTip(2000);
    }

    private void RestartApp()
    {
        _trayIcon.Visible = false;
        Application.Restart();
        Environment.Exit(0);
    }

    private void ExitApp()
    {
        _trayIcon.Visible = false;
        Application.Exit();
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigFile))
            {
                string json = File.ReadAllText(ConfigFile);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json);
                if (loaded != null) Config = loaded;
            }
        }
        catch { }
    }

    private void SaveConfig()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(Config, options);
            File.WriteAllText(ConfigFile, json);
        }
        catch { }
    }
}

// ICON GENERATOR
public static class IconGenerator
{
    public static Icon CreateDarkIcon()
    {
        using (Bitmap bmp = new Bitmap(64, 64))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Hintergrund
            Rectangle rect = new Rectangle(2, 2, 60, 60);
            using (GraphicsPath path = new GraphicsPath())
            {
                int radius = 15;
                path.AddArc(rect.X, rect.Y, radius, radius, 180, 90);
                path.AddArc(rect.Right - radius, rect.Y, radius, radius, 270, 90);
                path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90);
                path.AddArc(rect.X, rect.Bottom - radius, radius, radius, 90, 90);
                path.CloseFigure();

                using (LinearGradientBrush brush = new LinearGradientBrush(rect, 
                    Color.FromArgb(50, 50, 50), Color.FromArgb(10, 10, 10), 45f))
                {
                    g.FillPath(brush, path);
                }
                using (Pen pen = new Pen(Color.FromArgb(80, 80, 80), 2))
                {
                    g.DrawPath(pen, path);
                }
            }

            // Akzent-Balken
            Rectangle barRect = new Rectangle(15, 50, 34, 4);
            using (Brush brush = new SolidBrush(Color.FromArgb(0, 190, 255)))
            {
               g.FillRectangle(brush, barRect);
            }

            // Text "TT"
            using (Font f = new Font("Segoe UI", 28, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                
                g.DrawString("TT", f, Brushes.Black, new Rectangle(3, 1, 60, 60), sf);
                g.DrawString("TT", f, Brushes.White, new Rectangle(0, -2, 60, 60), sf);
            }

            return Icon.FromHandle(bmp.GetHicon());
        }
    }
}

// HOTKEY SETTINGS FORM 
public class HotkeySettingsForm : Form
{
    private TypeToolApplication _app;
    private Button _btnType;
    private Button _btnEnter;
    private Label _lblStatus;

    private HotkeyDef _tempType;
    private HotkeyDef _tempEnter;

    private bool _listening = false;
    private Button? _activeButton = null;

    public HotkeySettingsForm(TypeToolApplication app)
    {
        _app = app;
        _tempType = new HotkeyDef(app.Config.TypingHotkey.FsModifiers, app.Config.TypingHotkey.VkCode);
        _tempEnter = new HotkeyDef(app.Config.EnterToggleHotkey.FsModifiers, app.Config.EnterToggleHotkey.VkCode);

        this.Text = "Hotkeys anpassen";
        this.Size = new Size(350, 220);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.KeyPreview = true; 

        var lbl1 = new Label { Text = "Hotkey zum Tippen:", Left = 20, Top = 20, AutoSize = true };
        _btnType = new Button { Text = _tempType.ToReadableString(), Left = 150, Top = 15, Width = 150 };
        _btnType.Click += (s, e) => StartListening(_btnType);

        var lbl2 = new Label { Text = "Hotkey für Enter:", Left = 20, Top = 60, AutoSize = true };
        _btnEnter = new Button { Text = _tempEnter.ToReadableString(), Left = 150, Top = 55, Width = 150 };
        _btnEnter.Click += (s, e) => StartListening(_btnEnter);

        _lblStatus = new Label { Text = "Klicke auf einen Button zum Ändern...", Left = 20, Top = 100, Width = 300, ForeColor = Color.Gray };

        var btnSave = new Button { Text = "Speichern", Left = 180, Top = 140, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Abbrechen", Left = 90, Top = 140, DialogResult = DialogResult.Cancel };

        btnSave.Click += (s, e) => ApplyChanges();

        this.Controls.AddRange(new Control[] { lbl1, _btnType, lbl2, _btnEnter, _lblStatus, btnSave, btnCancel });

        this.KeyDown += OnKeyDown;
    }

    private void StartListening(Button btn)
    {
        _listening = true;
        _activeButton = btn;
        _lblStatus.Text = "Drücke jetzt die neue Tastenkombination...";
        _lblStatus.ForeColor = Color.Red;
        btn.Text = "Drücken...";
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_listening || _activeButton == null) return;

        if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu) return;

        int mods = 0;
        if (e.Control) mods |= 2; 
        if (e.Alt) mods |= 1;     
        if (e.Shift) mods |= 4;   

        int vk = (int)e.KeyCode;

        var newDef = new HotkeyDef(mods, vk);
        
        if (_activeButton == _btnType)
        {
            _tempType = newDef;
            _btnType.Text = newDef.ToReadableString();
        }
        else if (_activeButton == _btnEnter)
        {
            _tempEnter = newDef;
            _btnEnter.Text = newDef.ToReadableString();
        }

        _listening = false;
        _activeButton = null;
        _lblStatus.Text = "Klicke auf einen Button zum Ändern...";
        _lblStatus.ForeColor = Color.Gray;

        e.SuppressKeyPress = true; 
    }

    private void ApplyChanges()
    {
        _app.Config.TypingHotkey = _tempType;
        _app.Config.EnterToggleHotkey = _tempEnter;
    }
}

// INPUT DIALOG
public class InputDialog : Form
{
    public string InputValue { get; private set; } = "";
    private TextBox _txtInput;

    public InputDialog(string title, string defaultVal)
    {
        this.Text = "Einstellung";
        this.Size = new Size(300, 150);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;

        var lbl = new Label { Text = title, Left = 10, Top = 10, Width = 260 };
        _txtInput = new TextBox { Text = defaultVal, Left = 10, Top = 40, Width = 260 };
        var btnOk = new Button { Text = "OK", Left = 190, Top = 70, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Abbrechen", Left = 100, Top = 70, DialogResult = DialogResult.Cancel };

        this.Controls.AddRange(new Control[] { lbl, _txtInput, btnOk, btnCancel });
        this.AcceptButton = btnOk;
        this.CancelButton = btnCancel;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        InputValue = _txtInput.Text;
        base.OnFormClosing(e);
    }
}

// HOTKEY WINDOW 
public class HotkeyWindow : Form
{
    private TypeToolApplication _app;
    public HotkeyWindow(TypeToolApplication app)
    {
        _app = app;
        this.ShowInTaskbar = false;
        this.WindowState = FormWindowState.Minimized;
        this.FormBorderStyle = FormBorderStyle.None;
        this.CreateHandle();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312) _app.HandleHotkey(m.WParam.ToInt32());
        base.WndProc(ref m);
    }
}

// NATIVE METHODS
static class NativeMethods
{
    public const int MOD_ALT = 0x0001;
    public const int MOD_CONTROL = 0x0002;
    public const int VK_CONTROL = 0x11;
    public const int VK_ESCAPE = 0x1B;

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);
}