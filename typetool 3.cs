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
    private NotificationForm _notificationForm; // NEU: Eigene Benachrichtigung

    private const int ID_TYPE = 1;
    private const int ID_TOGGLE_ENTER = 2;

    public TypeToolApplication()
    {
        LoadConfig();
        InitializeTray();
        _hotkeyWindow = new HotkeyWindow(this);
        _notificationForm = new NotificationForm(); // NEU: Initialisieren
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
        ShowNotification("Einstellung geändert", $"Enter am Ende: {(Config.EnterKeyEnabled ? "AN" : "AUS")}");
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
            ShowNotification("Info", "Hotkeys erfolgreich gespeichert!");
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
            ShowNotification("Fehler", "Zwischenablage ist leer!");
            return;
        }

        string text = Clipboard.GetText();
        text = text.TrimEnd();

        bool needsWarning = text.Length >= 100;

        if (needsWarning)
        {
            ShowNotification("ACHTUNG: Großer Text",
                $"{text.Length} Zeichen werden getippt.\nDrücke ESC zum Abbrechen (Start in 1.5s)");
        }
        else if (Config.ShowPreviewWindow)
        {
            string preview = text.Length > 60 ? text.Substring(0, 60) + "..." : text;
            ShowNotification($"Tippe {text.Length} Zeichen", preview);
        }

        new Thread(() =>
        {
            try
            {
                if (needsWarning)
                {
                    for (int i = 0; i < 30; i++)
                    {
                        if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8000) != 0) return;
                        Thread.Sleep(50);
                    }
                }

                // Kurz warten, damit das Zielfeld fokussiert ist
                Thread.Sleep(50);

                foreach (char c in text)
                {
                    if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8000) != 0) return;

                    if (c == '\r') continue;

                    // GEÄNDERT (Fix Zeilenumbrüche): "\n" als Unicode-Zeichen über SendInput
                    // erzeugt in den meisten Textfeldern KEINEN echten Zeilenumbruch.
                    // Deshalb hier stattdessen einen echten Enter-Tastendruck (VK_RETURN) simulieren.
                    if (c == '\n')
                    {
                        NativeMethods.SendEnterKey();
                    }
                    else
                    {
                        SendCharByKeyboardAPI(c);
                    }

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

    // GEÄNDERT: Nutzt jetzt die neue schnelle Form statt BalloonTips
    private void ShowNotification(string title, string msg)
    {
        // Auf UI Thread aufrufen falls nötig
        if (_notificationForm.InvokeRequired)
        {
            _notificationForm.Invoke(new Action(() => _notificationForm.ShowMessage(title, msg)));
        }
        else
        {
            _notificationForm.ShowMessage(title, msg);
        }
    }

    // GEÄNDERT (FIX ^/&-Problem):
    // Vorher wurde über VkKeyScan() der virtuelle Tastencode für das Zeichen im
    // AKTUELLEN Tastatur-Layout des Prozesses ermittelt und per keybd_event simuliert.
    // Das schlägt fehl, wenn:
    //  1) das Ziel-Fenster ein anderes Layout aktiv hat als TypeTool selbst, oder
    //  2) das Zeichen (wie "^" auf der deutschen Tastatur) eine "tote Taste" (Dead Key) ist,
    //     die eigentlich Taste+Leertaste braucht, um alleine zu erscheinen.
    // Beides führte dazu, dass z.B. "^" sich mit dem nächsten Zeichen zu "&" o.ä. verband.
    //
    // Fix: Zeichen werden jetzt direkt als UNICODE über SendInput() injiziert.
    // Das ist komplett unabhängig vom aktiven Tastatur-Layout und umgeht Dead-Keys,
    // da Windows das Zeichen nicht mehr aus einem physischen Tastendruck ableiten muss.
    private void SendCharByKeyboardAPI(char c)
    {
        NativeMethods.SendUnicodeChar(c);
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

// NEW: CUSTOM NOTIFICATION FORM (Fix for lag)
public class NotificationForm : Form
{
    private Label _lblTitle;
    private Label _lblText;
    private System.Windows.Forms.Timer _timer;

    public NotificationForm()
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.TopMost = true;
        this.Size = new Size(300, 80);
        this.BackColor = Color.FromArgb(40, 40, 40);
        this.DoubleBuffered = true;

        _lblTitle = new Label
        {
            Left = 10,
            Top = 5,
            Width = 280,
            Height = 20,
            ForeColor = Color.FromArgb(0, 190, 255),
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Text = "Info"
        };

        _lblText = new Label
        {
            Left = 10,
            Top = 30,
            Width = 280,
            Height = 40,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            Text = "...",
            TextAlign = ContentAlignment.TopLeft
        };

        _timer = new System.Windows.Forms.Timer { Interval = 2000 };
        _timer.Tick += (s, e) => this.Hide();

        this.Controls.Add(_lblTitle);
        this.Controls.Add(_lblText);
    }

    // Prevents the form from stealing focus
    protected override bool ShowWithoutActivation => true;

    public void ShowMessage(string title, string message)
    {
        _lblTitle.Text = title;
        _lblText.Text = message;

        Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
        this.Location = new Point(workingArea.Right - this.Width - 10, workingArea.Bottom - this.Height - 10);

        if (!this.Visible)
        {
            this.Show();
        }

        // Timer reset
        _timer.Stop();
        _timer.Start();
    }
}

// NATIVE METHODS
static class NativeMethods
{
    public const int MOD_ALT = 0x0001;
    public const int MOD_CONTROL = 0x0002;
    public const int VK_CONTROL = 0x11;
    public const int VK_ESCAPE = 0x1B;

    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // NEU (Fix "tippt nicht mehr"): MOUSEINPUT und HARDWAREINPUT müssen mit in der
    // Union stehen, auch wenn wir sie nicht benutzen. Ohne sie ist die Union (und damit
    // die gesamte INPUT-Struktur) kleiner als die von Windows erwartete Größe.
    // SendInput vergleicht das übergebene cbSize exakt mit sizeof(INPUT) des Systems
    // und lehnt den kompletten Aufruf sonst KOMMENTARLOS ab (Rückgabewert 0) -
    // genau das war die Ursache dafür, dass gar nichts mehr getippt wurde.
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // NEU (Fix ^/&-Problem): Sendet ein beliebiges Unicode-Zeichen unabhängig
    // vom aktiven Tastatur-Layout und ohne Dead-Key-Probleme (z.B. "^", "´", "`").
    public static void SendUnicodeChar(char c)
    {
        // Wichtig: cbSize MUSS die Größe einer EINZELNEN INPUT-Struktur sein
        int cbSize = Marshal.SizeOf(typeof(INPUT));

        var down = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = c,
                    dwFlags = KEYEVENTF_UNICODE,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        var up = down;
        up.U.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;

        var inputs = new[] { down, up };

        // Retry-Logik: Bei Fehlschlag kurz warten und erneut versuchen
        uint sent = SendInput((uint)inputs.Length, inputs, cbSize);

        if (sent == 0)
        {
            int err = Marshal.GetLastWin32Error();
            Debug.WriteLine($"SendInput fehlgeschlagen für Zeichen '{c}', Win32-Error: {err}");

            // Fallback: kurz warten und erneut versuchen
            Thread.Sleep(5);
            sent = SendInput((uint)inputs.Length, inputs, cbSize);

            if (sent == 0)
            {
                int err2 = Marshal.GetLastWin32Error();
                Debug.WriteLine($"SendInput Fallback fehlgeschlagen für Zeichen '{c}', Win32-Error: {err2}");
            }
        }
    }

    // NEU (Fix Zeilenumbrüche): Simuliert einen echten Enter-Tastendruck (VK_RETURN),
    // damit Zeilenumbrüche im getippten Text tatsächlich als solche ankommen.
    private const ushort VK_RETURN = 0x0D;

    public static void SendEnterKey()
    {
        // Wichtig: cbSize MUSS die Größe einer EINZELNEN INPUT-Struktur sein
        int cbSize = Marshal.SizeOf(typeof(INPUT));

        var down = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = VK_RETURN,
                    wScan = 0,
                    dwFlags = 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        var up = down;
        up.U.ki.dwFlags = KEYEVENTF_KEYUP;

        var inputs = new[] { down, up };

        // Retry-Logik: Bei Fehlschlag kurz warten und erneut versuchen
        uint sent = SendInput((uint)inputs.Length, inputs, cbSize);

        if (sent == 0)
        {
            int err = Marshal.GetLastWin32Error();
            Debug.WriteLine($"SendInput (Enter) fehlgeschlagen, Win32-Error: {err}");

            // Fallback: kurz warten und erneut versuchen
            Thread.Sleep(5);
            sent = SendInput((uint)inputs.Length, inputs, cbSize);

            if (sent == 0)
            {
                int err2 = Marshal.GetLastWin32Error();
                Debug.WriteLine($"SendInput (Enter) Fallback fehlgeschlagen, Win32-Error: {err2}");
            }
        }
    }

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);
}