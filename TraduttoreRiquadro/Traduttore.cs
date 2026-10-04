using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace TraduttoreRiquadro
{
    class Block
    {
        public RectangleF Rect;
        public float LineHeight;
        public float LastBottom;
        public float MaxHeight;
        public float FontPx;
        public string Text;
        public string Translated;
        public Color Bg;
        public Color Fg;
    }

    class MainForm : Form
    {
        [DllImport("user32.dll")]
        static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
        [DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")]
        static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")]
        static extern uint GetClipboardSequenceNumber();

        // messaggio con cui un secondo avvio del programma fa comparire il riquadro di quello gia' aperto
        public static readonly int ShowMsg = RegisterWindowMessage("TraduttoreRiquadro.Mostra");

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "TraduttoreRiquadro";

        static readonly Color KeyColor = Color.FromArgb(255, 1, 254);
        static readonly Color BarColor = Color.FromArgb(32, 99, 199);

        // codici di Google Traduttore
        static readonly string[] Langs = {
            "af|Afrikaans", "sq|Albanese", "ar|Arabo", "hy|Armeno", "eu|Basco", "bn|Bengalese", "bg|Bulgaro",
            "ca|Catalano", "cs|Ceco", "zh-CN|Cinese (semplificato)", "zh-TW|Cinese (tradizionale)", "ko|Coreano",
            "hr|Croato", "da|Danese", "iw|Ebraico", "et|Estone", "tl|Filippino", "fi|Finlandese", "fr|Francese",
            "gl|Galiziano", "cy|Gallese", "ka|Georgiano", "ja|Giapponese", "el|Greco", "hi|Hindi", "id|Indonesiano",
            "en|Inglese", "ga|Irlandese", "is|Islandese", "it|Italiano", "la|Latino", "lv|Lettone", "lt|Lituano",
            "mk|Macedone", "ms|Malese", "mt|Maltese", "no|Norvegese", "nl|Olandese", "fa|Persiano", "pl|Polacco",
            "pt|Portoghese", "ro|Rumeno", "ru|Russo", "sr|Serbo", "sk|Slovacco", "sl|Sloveno", "es|Spagnolo",
            "sv|Svedese", "sw|Swahili", "th|Thailandese", "de|Tedesco", "tr|Turco", "uk|Ucraino", "hu|Ungherese",
            "ur|Urdu", "vi|Vietnamita" };

        static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TraduttoreRiquadro");
        static readonly string SettingsFile = Path.Combine(SettingsDir, "lingue.txt");
        static readonly string HotkeyFile = Path.Combine(SettingsDir, "tasti.txt");

        readonly Button btnTranslate = new Button();
        readonly Button btnClear = new Button();
        readonly Button btnClose = new Button();
        readonly Button btnSettings = new Button();
        readonly ToolTip tip = new ToolTip();
        readonly CheckBox chkAuto = new CheckBox();
        readonly ComboBox cmbFrom = new ComboBox();
        readonly ComboBox cmbTo = new ComboBox();
        readonly Timer timer = new Timer();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ToolStripMenuItem miToggle = new ToolStripMenuItem();

        // tasti rapidi: [0] mostra o nasconde il riquadro, [1] traduce il testo selezionato
        readonly Keys[] hotkeys = { Keys.Control | Keys.Alt | Keys.T, Keys.Control | Keys.Alt | Keys.S };
        bool trayIcon = true;
        PopupForm popup;
        bool selBusy;
        bool startHidden;
        bool settingsOpen;
        bool hideTipShown;

        readonly List<Block> blocks = new List<Block>();
        readonly Dictionary<string, string> cache = new Dictionary<string, string>();
        OcrEngine engine;
        bool excluded;
        bool busy;
        bool retranslate;
        string lastHash;
        string status = "Posiziona il riquadro sul testo e premi Traduci";
        int statusLeft;

        int S(int v) { return (int)Math.Round(v * DeviceDpi / 96.0); }
        int Bar { get { return S(34); } }
        int Edge { get { return S(6); } }

        public MainForm(bool hidden)
        {
            startHidden = hidden;
            Icon = MakeIcon();
            Text = "Traduttore riquadro";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            BackColor = KeyColor;
            TransparencyKey = KeyColor;
            DoubleBuffered = true;
            ResizeRedraw = true;
            Font = new Font("Segoe UI", 9f);
            Size = new Size(S(700), S(400));
            MinimumSize = new Size(S(620), S(120));

            int x = S(10), y = S(5), h = Bar - S(10);
            SetupButton(btnTranslate, "Traduci", ref x, y, S(70), h);
            SetupButton(btnClear, "Pulisci", ref x, y, S(64), h);

            chkAuto.Text = "Auto";
            chkAuto.ForeColor = Color.White;
            chkAuto.BackColor = BarColor;
            chkAuto.SetBounds(x, y, S(58), h);
            x += S(62);
            Controls.Add(chkAuto);

            cmbFrom.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbFrom.Items.Add("Rilevamento automatico");
            cmbTo.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (string l in Langs)
            {
                string name = l.Substring(l.IndexOf('|') + 1);
                cmbFrom.Items.Add(name);
                cmbTo.Items.Add(name);
            }
            cmbFrom.SetBounds(x, y + S(1), S(150), h);
            x += S(156);
            cmbTo.SetBounds(x, y + S(1), S(150), h);
            x += S(158);
            Controls.Add(cmbFrom);
            Controls.Add(cmbTo);
            statusLeft = x;
            tip.SetToolTip(cmbFrom, "Lingua originale");
            tip.SetToolTip(cmbTo, "Traduci in");
            LoadLanguages();

            int cx = 0;
            SetupButton(btnClose, "✕", ref cx, y, S(30), h);
            btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClose.Left = ClientSize.Width - S(30) - S(8);
            int sx = 0;
            SetupButton(btnSettings, "⚙", ref sx, y, S(30), h);
            btnSettings.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSettings.Left = btnClose.Left - S(36);
            tip.SetToolTip(btnSettings, "Impostazioni");

            LoadHotkey();
            ContextMenuStrip menu = new ContextMenuStrip();
            miToggle.Text = "Mostra o nascondi il riquadro";
            miToggle.Click += delegate { ToggleFrame(); };
            menu.Items.Add(miToggle);
            menu.Items.Add("Impostazioni…", null, delegate { OpenSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Esci", null, delegate { Close(); });
            tray.Icon = Icon;
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ToggleFrame(); };
            UpdateTray();
            // se l'exe e' stato spostato, l'avvio automatico deve puntare alla nuova posizione
            try { if (AutoStart) SetAutoStart(true); } catch { }

            btnTranslate.Click += async delegate { await TranslateNow(true); };
            btnClear.Click += delegate { chkAuto.Checked = false; ClearBlocks(); retranslate = false; SetStatus(""); };
            btnClose.Click += delegate { if (StaysRunning) HideFrame(); else Close(); };
            btnSettings.Click += delegate { OpenSettings(); };
            chkAuto.CheckedChanged += async delegate { if (chkAuto.Checked) await TranslateNow(true); };
            cmbTo.SelectedIndexChanged += async delegate { if (blocks.Count > 0 || chkAuto.Checked) await TranslateNow(true); };
            cmbFrom.SelectedIndexChanged += async delegate
            {
                CreateEngine();
                if (blocks.Count > 0 || chkAuto.Checked) await TranslateNow(true);
            };

            timer.Interval = 1500;
            timer.Tick += async delegate { if (chkAuto.Checked) await TranslateNow(false); };
            timer.Start();

            CreateEngine();
            if (engine == null) status = "OCR di Windows non disponibile";
        }

        static string Code(int index) { return Langs[index].Substring(0, Langs[index].IndexOf('|')); }
        string FromCode { get { return cmbFrom.SelectedIndex <= 0 ? "auto" : Code(cmbFrom.SelectedIndex - 1); } }
        string ToCode { get { return Code(cmbTo.SelectedIndex); } }

        static int IndexOfCode(string code)
        {
            for (int i = 0; i < Langs.Length; i++)
                if (Code(i) == code) return i;
            return -1;
        }

        // lingua di Windows espressa con i codici di Google
        static string SystemLanguage()
        {
            System.Globalization.CultureInfo c = System.Globalization.CultureInfo.CurrentUICulture;
            string two = c.TwoLetterISOLanguageName;
            if (two == "zh") return (c.Name.Contains("TW") || c.Name.Contains("HK") || c.Name.Contains("Hant")) ? "zh-TW" : "zh-CN";
            if (two == "he") return "iw";
            if (two == "nb" || two == "nn") return "no";
            if (c.Name.StartsWith("fil")) return "tl";
            return two;
        }

        void LoadLanguages()
        {
            string from = "auto", to = SystemLanguage();
            try
            {
                string[] parts = File.ReadAllText(SettingsFile).Trim().Split('|');
                if (parts.Length == 2) { from = parts[0]; to = parts[1]; }
            }
            catch { }
            cmbFrom.SelectedIndex = IndexOfCode(from) + 1;
            int t = IndexOfCode(to);
            cmbTo.SelectedIndex = t >= 0 ? t : IndexOfCode("en");
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                File.WriteAllText(SettingsFile, FromCode + "|" + ToCode);
            }
            catch { }
            tray.Visible = false;
            tray.Dispose();
            base.OnFormClosed(e);
        }

        // icona disegnata al volo: un riquadro bianco con la sua barra, su fondo blu
        static Icon MakeIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(BarColor);
                    using (Pen p = new Pen(Color.White, 3))
                        g.DrawRectangle(p, 5, 8, 21, 18);
                    g.FillRectangle(Brushes.White, 4, 5, 24, 7);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        public static string HotkeyText(Keys k)
        {
            return k == Keys.None ? "Nessuno" : new KeysConverter().ConvertToString(k);
        }

        void LoadHotkey()
        {
            try
            {
                string[] parts = File.ReadAllText(HotkeyFile).Trim().Split('|');
                for (int i = 0; i < hotkeys.Length && i < parts.Length; i++) hotkeys[i] = (Keys)int.Parse(parts[i]);
                if (parts.Length > hotkeys.Length) trayIcon = parts[hotkeys.Length] != "0";
            }
            catch { }
        }

        void SaveHotkey()
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                File.WriteAllText(HotkeyFile, (int)hotkeys[0] + "|" + (int)hotkeys[1] + "|" + (trayIcon ? "1" : "0"));
            }
            catch { }
        }

        void UnregisterHotkeys()
        {
            for (int i = 0; i < hotkeys.Length; i++) UnregisterHotKey(Handle, i + 1);
        }

        // registra le combinazioni; restituisce l'indice di quella gia' usata da un altro programma, -1 se tutto bene
        int TryRegister(Keys[] keys)
        {
            UnregisterHotkeys();
            for (int i = 0; i < keys.Length; i++)
            {
                Keys k = keys[i];
                if (k == Keys.None) continue;
                uint mod = 0x4000; // MOD_NOREPEAT
                if ((k & Keys.Alt) != 0) mod |= 1;
                if ((k & Keys.Control) != 0) mod |= 2;
                if ((k & Keys.Shift) != 0) mod |= 4;
                if (!RegisterHotKey(Handle, i + 1, mod, (uint)(k & Keys.KeyCode))) return i;
            }
            return -1;
        }

        static bool AutoStart
        {
            get
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(RunName) != null;
            }
        }

        // all'avvio di Windows parte nascosto: resta solo l'icona vicino all'orologio
        static void SetAutoStart(bool on)
        {
            using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\" /avvio");
                else k.DeleteValue(RunName, false);
            }
        }

        // senza icona e senza tasti rapidi il riquadro nascosto non si potrebbe piu' riaprire: allora la X chiude
        bool StaysRunning { get { return trayIcon || hotkeys[0] != Keys.None || hotkeys[1] != Keys.None; } }

        void UpdateTray()
        {
            tray.Visible = trayIcon;
            tip.SetToolTip(btnClose, !StaysRunning ? "Chiudi" :
                trayIcon ? "Nascondi: il programma resta nell'icona vicino all'orologio" :
                "Nascondi: i tasti rapidi continuano a funzionare");
            Keys k = hotkeys[0];
            tray.Text = k == Keys.None ? "Traduttore riquadro" : "Traduttore riquadro (" + HotkeyText(k) + ")";
            miToggle.ShortcutKeyDisplayString = k == Keys.None ? "" : HotkeyText(k);
        }

        void ShowFrame()
        {
            Show();
            Activate();
        }

        void HideFrame()
        {
            ClearBlocks();
            retranslate = false;
            Hide();
            if (hideTipShown || !trayIcon) return;
            hideTipShown = true;
            tray.ShowBalloonTip(4000, "Traduttore riquadro", "Il programma resta attivo in questa icona" +
                (hotkeys[0] == Keys.None ? "." : ": premi " + HotkeyText(hotkeys[0]) + " per riaprire il riquadro."), ToolTipIcon.Info);
        }

        static bool ModifierDown()
        {
            return GetAsyncKeyState(0x10) < 0 || GetAsyncKeyState(0x11) < 0 || GetAsyncKeyState(0x12) < 0;
        }

        // legge il testo selezionato nel programma in primo piano facendogli fare Ctrl+C, poi rimette a posto gli appunti
        async Task<string> CopySelection()
        {
            // i tasti della combinazione devono essere rilasciati, altrimenti si sommano a Ctrl+C
            for (int i = 0; i < 40 && ModifierDown(); i++) await Task.Delay(25);

            DataObject keep = new DataObject();
            bool had = false;
            try
            {
                IDataObject old = Clipboard.GetDataObject();
                if (old != null)
                {
                    foreach (string f in old.GetFormats(false))
                    {
                        try
                        {
                            object d = old.GetData(f, false);
                            if (d != null) { keep.SetData(f, d); had = true; }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            uint seq = GetClipboardSequenceNumber();
            keybd_event(0x11, 0, 0, UIntPtr.Zero);
            keybd_event(0x43, 0, 0, UIntPtr.Zero);
            keybd_event(0x43, 0, 2, UIntPtr.Zero);
            keybd_event(0x11, 0, 2, UIntPtr.Zero);
            for (int i = 0; i < 20 && GetClipboardSequenceNumber() == seq; i++) await Task.Delay(25);
            // appunti invariati: non c'era niente di selezionato
            if (GetClipboardSequenceNumber() == seq) return "";
            await Task.Delay(50);

            string text = "";
            try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); } catch { }
            try { if (had) Clipboard.SetDataObject(keep, true); else Clipboard.Clear(); } catch { }
            return text.Trim();
        }

        async void TranslateSelection()
        {
            if (selBusy || settingsOpen) return;
            selBusy = true;
            Point at = Cursor.Position;
            try
            {
                string text = await CopySelection();
                if (text.Length == 0) { ShowPopup("Nessun testo selezionato.", at); return; }
                string from = FromCode, to = ToCode;
                string key = from + ">" + to + "|" + text;
                string tr;
                if (!cache.TryGetValue(key, out tr))
                {
                    List<string> res = await Task.Run(delegate { return TranslateAll(new List<string> { text }, from, to); });
                    tr = res[0];
                    cache[key] = tr;
                }
                ShowPopup(tr, at);
            }
            catch (WebException ex)
            {
                ShowPopup("Errore di rete: " + ex.Message, at);
            }
            catch (Exception ex)
            {
                ShowPopup("Errore: " + ex.Message, at);
            }
            finally
            {
                selBusy = false;
            }
        }

        void ShowPopup(string text, Point at)
        {
            if (popup != null && !popup.IsDisposed) popup.Close();
            popup = new PopupForm(text.Replace("\r\n", "\n").Replace("\n", "\r\n"), "Traduzione - " + cmbTo.Text, at);
            popup.Show();
            popup.Activate();
        }

        void ToggleFrame()
        {
            if (Visible) HideFrame(); else ShowFrame();
        }

        void OpenSettings()
        {
            if (settingsOpen) return;
            settingsOpen = true;
            bool quit = false;
            // finche' si scelgono le combinazioni quelle attuali non devono scattare
            UnregisterHotkeys();
            try
            {
                bool auto = false;
                try { auto = AutoStart; } catch { }
                using (SettingsForm f = new SettingsForm(hotkeys, auto, trayIcon, TryRegister))
                {
                    DialogResult r = f.ShowDialog();
                    quit = r == DialogResult.Abort;
                    if (r != DialogResult.OK) return;
                    f.Hotkeys.CopyTo(hotkeys, 0);
                    trayIcon = f.TrayIcon;
                    SaveHotkey();
                    try { SetAutoStart(f.AutoStart); }
                    catch (Exception ex) { MessageBox.Show("Impossibile cambiare l'avvio automatico: " + ex.Message, "Traduttore riquadro"); }
                }
            }
            finally
            {
                settingsOpen = false;
                TryRegister(hotkeys);
                UpdateTray();
                if (quit) Close();
            }
        }

        // primo avvio nascosto: la finestra viene creata, perche' i tasti rapidi ne hanno bisogno, ma non mostrata
        protected override void SetVisibleCore(bool value)
        {
            if (startHidden)
            {
                startHidden = false;
                if (!IsHandleCreated) CreateHandle();
                value = false;
            }
            base.SetVisibleCore(value);
        }

        // usa l'OCR della lingua originale se il suo pacchetto e' installato, altrimenti quello di sistema
        void CreateEngine()
        {
            engine = null;
            try
            {
                string code = FromCode;
                if (code != "auto")
                {
                    string tag = code == "iw" ? "he" : code == "no" ? "nb" : code == "zh-CN" ? "zh-Hans" : code == "zh-TW" ? "zh-Hant" : code;
                    Windows.Globalization.Language lang = new Windows.Globalization.Language(tag);
                    if (OcrEngine.IsLanguageSupported(lang)) engine = OcrEngine.TryCreateFromLanguage(lang);
                }
            }
            catch { }
            try { if (engine == null) engine = OcrEngine.TryCreateFromUserProfileLanguages(); } catch { }
        }

        void SetupButton(Button b, string text, ref int x, int y, int w, int h)
        {
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = Color.White;
            b.ForeColor = Color.Black;
            b.SetBounds(x, y, w, h);
            x += w + S(6);
            Controls.Add(b);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // WDA_EXCLUDEFROMCAPTURE: la cattura dello schermo vede cosa c'e' sotto il riquadro
            excluded = SetWindowDisplayAffinity(Handle, 0x11);
            int bad = TryRegister(hotkeys);
            if (bad >= 0)
                tray.ShowBalloonTip(4000, "Traduttore riquadro", "I tasti rapidi " + HotkeyText(hotkeys[bad]) +
                    " sono già usati da un altro programma: scegline altri nelle impostazioni.", ToolTipIcon.Warning);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterHotkeys();
            base.OnHandleDestroyed(e);
        }

        Rectangle Interior()
        {
            return new Rectangle(Edge, Bar, ClientSize.Width - 2 * Edge, ClientSize.Height - Bar - Edge);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84, WM_HOTKEY = 0x312;
            if (m.Msg == WM_HOTKEY)
            {
                if ((int)m.WParam == 2) TranslateSelection(); else ToggleFrame();
                return;
            }
            if (m.Msg == ShowMsg)
            {
                ShowFrame();
                return;
            }
            if (m.Msg == WM_NCHITTEST)
            {
                long lp = m.LParam.ToInt64();
                Point p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                bool l = p.X < Edge, r = p.X >= ClientSize.Width - Edge;
                bool t = p.Y < S(4), b = p.Y >= ClientSize.Height - Edge;
                int hit;
                if (t && l) hit = 13;
                else if (t && r) hit = 14;
                else if (b && l) hit = 16;
                else if (b && r) hit = 17;
                else if (l) hit = 10;
                else if (r) hit = 11;
                else if (t) hit = 12;
                else if (b) hit = 15;
                else if (p.Y < Bar) hit = 2; // HTCAPTION: trascina per spostare
                else hit = 1;
                m.Result = (IntPtr)hit;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            ClearBlocks();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ClearBlocks();
        }

        void ClearBlocks()
        {
            lastHash = null;
            if (blocks.Count == 0) return;
            blocks.Clear();
            retranslate = true;
            Invalidate();
        }

        // finito di spostare o ridimensionare: se c'era una traduzione la rifa' nella nuova posizione
        protected override async void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            if (!retranslate) return;
            retranslate = false;
            await TranslateNow(true);
        }

        void SetStatus(string s)
        {
            status = s;
            Text = s.Length == 0 ? "Traduttore riquadro" : "Traduttore riquadro - " + s;
            Invalidate(new Rectangle(0, 0, ClientSize.Width, Bar));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int w = ClientSize.Width, h = ClientSize.Height;
            using (SolidBrush bar = new SolidBrush(BarColor))
            {
                g.FillRectangle(bar, 0, 0, w, Bar);
                g.FillRectangle(bar, 0, 0, Edge, h);
                g.FillRectangle(bar, w - Edge, 0, Edge, h);
                g.FillRectangle(bar, 0, h - Edge, w, Edge);
            }
            Rectangle sr = new Rectangle(statusLeft, 0, btnSettings.Left - statusLeft - S(6), Bar);
            TextRenderer.DrawText(g, status, Font, sr, Color.White, BarColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            Rectangle inner = Interior();
            g.SetClip(inner);
            foreach (Block b in blocks)
            {
                if (b.Translated == null) continue;
                DrawBlock(g, b, inner);
            }
        }

        void DrawBlock(Graphics g, Block b, Rectangle inner)
        {
            const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping;
            Rectangle r = Rectangle.Round(b.Rect);
            r.Offset(inner.X, inner.Y);
            r.Inflate(S(3), S(2));
            // stessa grandezza del testo originale: se la traduzione e' piu' lunga cresce il riquadro, non si riduce il carattere
            using (Font f = new Font("Segoe UI", b.FontPx, GraphicsUnit.Pixel))
            {
                Size big = new Size(int.MaxValue, int.MaxValue);
                if (b.Rect.Height < 1.5f * b.LineHeight)
                {
                    // riga singola: si allarga verso destra prima di andare a capo
                    int w = TextRenderer.MeasureText(g, b.Translated, f, big, flags).Width + S(6);
                    r.Width = Math.Max(r.Width, Math.Min(w, inner.Right - r.Left));
                }
                Size sz = TextRenderer.MeasureText(g, b.Translated, f, new Size(r.Width - S(6), int.MaxValue), flags | TextFormatFlags.WordBreak);
                if (sz.Height + S(4) > r.Height) r.Height = sz.Height + S(4);
                if (sz.Width + S(6) > r.Width) r.Width = sz.Width + S(6);
                using (SolidBrush bg = new SolidBrush(b.Bg))
                    g.FillRectangle(bg, r);
                Rectangle tr = new Rectangle(r.X + S(3), r.Y + S(2), r.Width - S(6), r.Height - S(4));
                TextRenderer.DrawText(g, b.Translated, f, tr, b.Fg, b.Bg, flags | TextFormatFlags.WordBreak);
            }
        }

        async Task TranslateNow(bool force)
        {
            if (busy || engine == null || !Visible) return;
            busy = true;
            try
            {
                Rectangle area = RectangleToScreen(Interior());
                if (area.Width < 20 || area.Height < 20) return;
                if (!excluded && blocks.Count > 0)
                {
                    // senza esclusione dalla cattura bisogna togliere le traduzioni prima di leggere lo schermo
                    blocks.Clear();
                    Invalidate();
                    Update();
                    await Task.Delay(80);
                }
                using (Bitmap bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                        g.CopyFromScreen(area.Location, Point.Empty, area.Size);
                    string hash = Hash(bmp);
                    if (!force && excluded && hash == lastHash) return;
                    Point origin = Location;
                    Size size = Size;
                    if (force) SetStatus("Lettura del testo…");
                    List<Block> found = await Recognize(bmp);
                    string from = FromCode, to = ToCode;
                    string lang = from + ">" + to;

                    List<Block> pending = new List<Block>();
                    foreach (Block b in found)
                    {
                        string tr;
                        if (cache.TryGetValue(lang + "|" + b.Text, out tr)) b.Translated = tr;
                        else pending.Add(b);
                    }
                    if (pending.Count > 0)
                    {
                        SetStatus("Traduzione…");
                        List<string> texts = new List<string>();
                        foreach (Block b in pending) texts.Add(b.Text);
                        List<string> res = await Task.Run(delegate { return TranslateAll(texts, from, to); });
                        for (int i = 0; i < pending.Count; i++)
                        {
                            pending[i].Translated = res[i];
                            cache[lang + "|" + pending[i].Text] = res[i];
                        }
                    }
                    // se nel frattempo il riquadro e' stato spostato le posizioni non valgono piu'
                    if (origin != Location || size != Size) return;

                    blocks.Clear();
                    foreach (Block b in found)
                    {
                        if (b.Translated == null) continue;
                        if (string.Equals(b.Translated.Trim(), b.Text.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                        blocks.Add(b);
                    }
                    lastHash = hash;
                    SetStatus(found.Count == 0 ? "Nessun testo trovato" :
                        blocks.Count == 0 ? "Testo già in " + cmbTo.Text :
                        "Tradotto in " + cmbTo.Text);
                    Invalidate();
                }
            }
            catch (WebException ex)
            {
                SetStatus("Errore di rete: " + ex.Message);
            }
            catch (Exception ex)
            {
                SetStatus("Errore: " + ex.Message);
            }
            finally
            {
                busy = false;
            }
        }

        static string Hash(Bitmap bmp)
        {
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] buf = new byte[d.Stride * d.Height];
                Marshal.Copy(d.Scan0, buf, 0, buf.Length);
                using (MD5 md5 = MD5.Create())
                    return Convert.ToBase64String(md5.ComputeHash(buf));
            }
            finally
            {
                bmp.UnlockBits(d);
            }
        }

        async Task<OcrResult> RunOcr(Bitmap bmp, int scale, bool invert)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (Bitmap img = new Bitmap(bmp.Width * scale, bmp.Height * scale, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(img))
                    using (ImageAttributes att = new ImageAttributes())
                    {
                        att.SetWrapMode(WrapMode.TileFlipXY);
                        if (invert)
                        {
                            att.SetColorMatrix(new ColorMatrix(new float[][] {
                                new float[] { -1, 0, 0, 0, 0 }, new float[] { 0, -1, 0, 0, 0 }, new float[] { 0, 0, -1, 0, 0 },
                                new float[] { 0, 0, 0, 1, 0 }, new float[] { 1, 1, 1, 0, 1 } }));
                        }
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(bmp, new Rectangle(0, 0, img.Width, img.Height), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, att);
                    }
                    img.Save(ms, ImageFormat.Bmp);
                }
                ms.Position = 0;
                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
                using (SoftwareBitmap sb = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied))
                    return await engine.RecognizeAsync(sb);
            }
        }

        // aggiunge le righe di una passata che non coprono righe gia' trovate
        static void AddLines(List<KeyValuePair<RectangleF, string>> lines, OcrResult result, int scale)
        {
            foreach (OcrLine line in result.Lines)
            {
                string text = line.Text.Trim();
                if (line.Words.Count == 0 || text.Length == 0) continue;
                RectangleF r = RectangleF.Empty;
                foreach (OcrWord w in line.Words)
                {
                    Windows.Foundation.Rect wr = w.BoundingRect;
                    RectangleF f = new RectangleF((float)wr.X / scale, (float)wr.Y / scale, (float)wr.Width / scale, (float)wr.Height / scale);
                    r = r.IsEmpty ? f : RectangleF.Union(r, f);
                }
                bool known = false;
                foreach (KeyValuePair<RectangleF, string> o in lines)
                {
                    RectangleF i = RectangleF.Intersect(o.Key, r);
                    if (i.Width > 0 && i.Height > 0 &&
                        i.Width * i.Height > 0.3f * Math.Min(o.Key.Width * o.Key.Height, r.Width * r.Height)) { known = true; break; }
                }
                if (!known) lines.Add(new KeyValuePair<RectangleF, string>(r, text));
            }
        }

        async Task<List<Block>> Recognize(Bitmap bmp)
        {
            // l'OCR di Windows a volte salta del testo secondo grandezza e contrasto: piu' passate
            // (ingrandita per il testo piccolo, normale per i titoli, in negativo per il chiaro su scuro) e si uniscono i risultati
            int max = (int)OcrEngine.MaxImageDimension;
            int big = (bmp.Width * 2 <= max && bmp.Height * 2 <= max) ? 2 : 1;
            List<KeyValuePair<RectangleF, string>> lines = new List<KeyValuePair<RectangleF, string>>();
            AddLines(lines, await RunOcr(bmp, big, false), big);
            if (big > 1) AddLines(lines, await RunOcr(bmp, 1, false), 1);
            AddLines(lines, await RunOcr(bmp, big, true), big);
            lines.Sort(delegate(KeyValuePair<RectangleF, string> x, KeyValuePair<RectangleF, string> y) { return x.Key.Top.CompareTo(y.Key.Top); });

            List<Block> list = new List<Block>();
            foreach (KeyValuePair<RectangleF, string> line in lines)
            {
                RectangleF r = line.Key;
                string text = line.Value;

                // righe vicine e allineate formano un paragrafo: tradotte insieme la resa e' migliore
                Block cur = null;
                for (int k = list.Count - 1; k >= 0 && cur == null; k--)
                {
                    Block c = list[k];
                    float gap = r.Top - c.LastBottom;
                    float hr = r.Height / c.LineHeight;
                    bool overlap = r.Left < c.Rect.Right && r.Right > c.Rect.Left;
                    if (gap > -0.5f * r.Height && gap < 0.8f * r.Height && hr > 0.7f && hr < 1.4f
                        && overlap && Math.Abs(r.Left - c.Rect.Left) < 4 * r.Height) cur = c;
                }
                if (cur != null)
                {
                    cur.Text = cur.Text.EndsWith("-") ? cur.Text.Substring(0, cur.Text.Length - 1) + text : cur.Text + " " + text;
                    cur.Rect = RectangleF.Union(cur.Rect, r);
                    cur.LastBottom = r.Bottom;
                    cur.MaxHeight = Math.Max(cur.MaxHeight, r.Height);
                }
                else
                {
                    cur = new Block();
                    cur.Text = text;
                    cur.Rect = r;
                    cur.LineHeight = r.Height;
                    cur.LastBottom = r.Bottom;
                    cur.MaxHeight = r.Height;
                    list.Add(cur);
                }
            }
            foreach (Block b in list) PickColors(bmp, b);

            // blocchi di altezza simile ricevono lo stesso carattere, altrimenti la pagina tradotta cambia aspetto
            List<Block> bySize = new List<Block>(list);
            bySize.Sort(delegate(Block x, Block y) { return x.MaxHeight.CompareTo(y.MaxHeight); });
            int start = 0;
            for (int k = 1; k <= bySize.Count; k++)
            {
                if (k < bySize.Count && bySize[k].MaxHeight <= bySize[start].MaxHeight * 1.2f) continue;
                float px = Math.Max(S(11), bySize[(start + k - 1) / 2 + (k - start) / 4].MaxHeight);
                for (int j = start; j < k; j++) bySize[j].FontPx = px;
                start = k;
            }
            return list;
        }

        // colore di sfondo = colore piu' frequente attorno al testo, cosi' la traduzione si fonde con la pagina
        static void PickColors(Bitmap bmp, Block b)
        {
            Rectangle r = Rectangle.Round(b.Rect);
            r.Inflate(3, 3);
            r.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
            Dictionary<int, int> counts = new Dictionary<int, int>();
            int best = Color.White.ToArgb(), bestN = 0;
            if (r.Width > 1 && r.Height > 1)
            {
                for (int i = 0; i < 24; i++)
                {
                    int x = r.Left + (r.Width - 1) * i / 23;
                    int y = r.Top + (r.Height - 1) * i / 23;
                    int[] samples = { bmp.GetPixel(x, r.Top).ToArgb(), bmp.GetPixel(x, r.Bottom - 1).ToArgb(),
                                      bmp.GetPixel(r.Left, y).ToArgb(), bmp.GetPixel(r.Right - 1, y).ToArgb() };
                    foreach (int c in samples)
                    {
                        int n;
                        counts.TryGetValue(c, out n);
                        counts[c] = ++n;
                        if (n > bestN) { bestN = n; best = c; }
                    }
                }
            }
            Color bg = Color.FromArgb(255, Color.FromArgb(best));
            if (bg.ToArgb() == KeyColor.ToArgb()) bg = Color.FromArgb(255, 2, 254);
            b.Bg = bg;
            // colore del testo = il pixel piu' lontano dallo sfondo dentro il blocco
            Color fg = (bg.R * 299 + bg.G * 587 + bg.B * 114) / 1000 < 128 ? Color.White : Color.Black;
            int bestDist = 120;
            int step = Math.Max(1, (int)Math.Sqrt(r.Width * (double)r.Height / 3000));
            for (int y = r.Top; y < r.Bottom; y += step)
            {
                for (int x = r.Left; x < r.Right; x += step)
                {
                    Color c = bmp.GetPixel(x, y);
                    int d = Math.Abs(c.R - bg.R) + Math.Abs(c.G - bg.G) + Math.Abs(c.B - bg.B);
                    if (d > bestDist) { bestDist = d; fg = Color.FromArgb(255, c); }
                }
            }
            b.Fg = fg;
        }

        static List<string> TranslateAll(List<string> texts, string from, string to)
        {
            List<string> result = new List<string>();
            int i = 0;
            while (i < texts.Count)
            {
                // un'unica richiesta per piu' blocchi (un parametro q per blocco)
                StringBuilder body = new StringBuilder();
                int n = 0;
                while (i < texts.Count && (n == 0 || body.Length < 6000))
                {
                    if (n > 0) body.Append('&');
                    body.Append("q=").Append(Uri.EscapeDataString(texts[i]));
                    i++;
                    n++;
                }
                List<string> part = GoogleTranslate(body.ToString(), from, to);
                if (part.Count != n) throw new InvalidOperationException("risposta del traduttore non valida");
                result.AddRange(part);
            }
            return result;
        }

        static List<string> GoogleTranslate(string body, string from, string to)
        {
            using (WebClient wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded";
                wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0";
                string json = wc.UploadString(
                    "https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl=" + from + "&tl=" + to, body);
                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = int.MaxValue;
                object[] root = (object[])ser.DeserializeObject(json);
                List<string> list = new List<string>();
                foreach (object item in root)
                {
                    // con il rilevamento automatico ogni voce e' [traduzione, lingua rilevata]
                    object[] pair = item as object[];
                    string t = pair != null && pair.Length > 0 ? pair[0] as string : item as string;
                    list.Add((t ?? "").Trim());
                }
                return list;
            }
        }
    }

    class SettingsForm : Form
    {
        readonly CheckBox chkStart = new CheckBox();
        readonly CheckBox chkIcon = new CheckBox();
        readonly Keys[] hotkeys;

        public Keys[] Hotkeys { get { return hotkeys; } }
        public bool AutoStart { get { return chkStart.Checked; } }
        public bool TrayIcon { get { return chkIcon.Checked; } }

        int S(int v) { return (int)Math.Round(v * DeviceDpi / 96.0); }

        // etichetta, casella che cattura la combinazione premuta e pulsante per toglierla
        void AddHotkeyRow(int index, string label, int y)
        {
            Label lbl = new Label();
            lbl.Text = label;
            lbl.SetBounds(S(16), y, S(368), S(20));

            TextBox txt = new TextBox();
            txt.ReadOnly = true;
            txt.BackColor = SystemColors.Window;
            txt.ShortcutsEnabled = false;
            txt.Text = MainForm.HotkeyText(hotkeys[index]);
            txt.SetBounds(S(16), y + S(24), S(262), S(24));
            txt.KeyDown += delegate(object s, KeyEventArgs e)
            {
                e.SuppressKeyPress = true;
                Keys code = e.KeyCode;
                if (code == Keys.ControlKey || code == Keys.ShiftKey || code == Keys.Menu || code == Keys.LWin || code == Keys.RWin) return;
                // un tasto da solo non va bene: scatterebbe mentre si scrive (fanno eccezione i tasti funzione)
                bool fkey = code >= Keys.F1 && code <= Keys.F24;
                if ((e.Modifiers & (Keys.Control | Keys.Alt)) == 0 && !fkey) return;
                hotkeys[index] = e.KeyData;
                txt.Text = MainForm.HotkeyText(e.KeyData);
            };

            Button btnNone = new Button();
            btnNone.Text = "Nessuno";
            btnNone.SetBounds(S(286), y + S(23), S(98), S(26));
            btnNone.Click += delegate { hotkeys[index] = Keys.None; txt.Text = MainForm.HotkeyText(Keys.None); };

            Controls.AddRange(new Control[] { lbl, txt, btnNone });
        }

        public SettingsForm(Keys[] current, bool autoStart, bool trayIcon, Func<Keys[], int> tryRegister)
        {
            hotkeys = (Keys[])current.Clone();
            Text = "Impostazioni - Traduttore riquadro";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(S(400), S(272));

            chkStart.Text = "Avvia all'avvio di Windows";
            chkStart.Checked = autoStart;
            chkStart.SetBounds(S(16), S(14), S(368), S(24));
            chkIcon.Text = "Mostra l'icona vicino all'orologio";
            chkIcon.Checked = trayIcon;
            chkIcon.SetBounds(S(16), S(42), S(368), S(24));
            Controls.AddRange(new Control[] { chkStart, chkIcon });

            AddHotkeyRow(0, "Tasti rapidi per mostrare o nascondere il riquadro:", S(80));
            AddHotkeyRow(1, "Tasti rapidi per tradurre il testo selezionato:", S(140));

            Label hint = new Label();
            hint.Text = "Clicca in una casella e premi la combinazione, per esempio Ctrl+Alt+T.";
            hint.ForeColor = SystemColors.GrayText;
            hint.SetBounds(S(16), S(196), S(368), S(20));

            Button btnOk = new Button();
            btnOk.Text = "OK";
            btnOk.SetBounds(S(206), S(232), S(84), S(28));
            btnOk.Click += delegate
            {
                if (hotkeys[0] != Keys.None && hotkeys[0] == hotkeys[1])
                {
                    MessageBox.Show(this, "Le due combinazioni di tasti devono essere diverse.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                int bad = tryRegister(hotkeys);
                if (bad >= 0)
                {
                    MessageBox.Show(this, "I tasti " + MainForm.HotkeyText(hotkeys[bad]) + " sono già usati da un altro programma: scegline altri.",
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                DialogResult = DialogResult.OK;
            };

            Button btnCancel = new Button();
            btnCancel.Text = "Annulla";
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.SetBounds(S(300), S(232), S(84), S(28));

            // senza icona e' l'unico modo di chiudere il programma quando resta attivo solo con i tasti rapidi
            Button btnQuit = new Button();
            btnQuit.Text = "Chiudi il programma";
            btnQuit.DialogResult = DialogResult.Abort;
            btnQuit.SetBounds(S(16), S(232), S(140), S(28));

            Controls.AddRange(new Control[] { hint, btnOk, btnCancel, btnQuit });
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }
    }

    // finestrella con la traduzione del testo selezionato: si chiude con Esc o cliccando altrove
    class PopupForm : Form
    {
        bool closing;

        int S(int v) { return (int)Math.Round(v * DeviceDpi / 96.0); }

        public PopupForm(string text, string title, Point at)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Font = new Font("Segoe UI", 10f);
            BackColor = SystemColors.Window;
            Padding = new Padding(S(10));

            TextBox txt = new TextBox();
            txt.Multiline = true;
            txt.ReadOnly = true;
            txt.BorderStyle = BorderStyle.None;
            txt.BackColor = SystemColors.Window;
            txt.Dock = DockStyle.Fill;
            txt.Text = text;

            Size sz = TextRenderer.MeasureText(text, Font, new Size(S(420), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
            int w = Math.Max(S(220), Math.Min(sz.Width + S(8), S(420)));
            int h = Math.Max(S(40), Math.Min(sz.Height + S(6), S(320)));
            if (sz.Height + S(6) > S(320))
            {
                txt.ScrollBars = ScrollBars.Vertical;
                w += SystemInformation.VerticalScrollBarWidth;
            }
            ClientSize = new Size(w + S(20), h + S(20));
            Controls.Add(txt);
            txt.Select(0, 0);

            // vicino al puntatore, senza uscire dallo schermo
            Rectangle wa = Screen.FromPoint(at).WorkingArea;
            Location = new Point(
                Math.Max(wa.Left, Math.Min(at.X + S(12), wa.Right - Width)),
                Math.Max(wa.Top, Math.Min(at.Y + S(16), wa.Bottom - Height)));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            closing = true;
            base.OnFormClosing(e);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!closing) Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [STAThread]
        static void Main(string[] args)
        {
            // "/avvio" e' l'avvio automatico con Windows: parte nascosto nell'icona vicino all'orologio
            bool hidden = args.Length > 0 && args[0] == "/avvio";
            bool first;
            using (System.Threading.Mutex mutex = new System.Threading.Mutex(true, "TraduttoreRiquadro", out first))
            {
                if (!first)
                {
                    // gia' aperto: fa comparire il riquadro di quello in esecuzione (HWND_BROADCAST)
                    if (!hidden) PostMessage((IntPtr)0xFFFF, MainForm.ShowMsg, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                SetProcessDPIAware();
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(hidden));
            }
        }
    }
}
