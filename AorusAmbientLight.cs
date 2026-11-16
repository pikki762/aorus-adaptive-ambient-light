using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AorusAmbientLightApp
{
    public static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        public static void Main()
        {
            try
            {
                SetProcessDPIAware();
            }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                File.WriteAllText("crash.log", ex.ToString());
            }
        }
    }

    #region Custom Dark Controls

    public class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            this.UpdateStyles();
        }
    }

    public class DarkSlider : Control
    {
        private int _min = 0;
        private int _max = 100;
        private int _val = 50;
        private bool _isDragging = false;
        private bool _isHovered = false;

        public event EventHandler ValueChanged;

        public int Minimum
        {
            get { return _min; }
            set { _min = value; if (_val < _min) _val = _min; Invalidate(); }
        }

        public int Maximum
        {
            get { return _max; }
            set { _max = value; if (_val > _max) _val = _max; Invalidate(); }
        }

        public int Value
        {
            get { return _val; }
            set
            {
                int clamped = Math.Max(_min, Math.Min(_max, value));
                if (_val != clamped)
                {
                    _val = clamped;
                    Invalidate();
                    if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
                }
            }
        }

        public DarkSlider()
        {
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable, true);
            this.Height = 28;
            this.Cursor = Cursors.Hand;
            this.DoubleBuffered = true;
        }

        protected override void OnMouseEnter(EventArgs e) { _isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _isHovered = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                UpdateValueFromX(e.X);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_isDragging)
            {
                UpdateValueFromX(e.X);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _isDragging = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        private void UpdateValueFromX(int x)
        {
            int pad = 12;
            int trackW = Width - pad * 2;
            if (trackW <= 0) return;

            float ratio = (float)(x - pad) / trackW;
            ratio = Math.Max(0f, Math.Min(1f, ratio));
            this.Value = _min + (int)Math.Round(ratio * (_max - _min));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int pad = 12;
            int trackH = 6;
            int trackY = (Height - trackH) / 2;
            int trackW = Width - pad * 2;

            float ratio = (_max > _min) ? (float)(_val - _min) / (_max - _min) : 0f;
            int thumbX = pad + (int)(ratio * trackW);
            int thumbY = Height / 2;
            int thumbR = _isDragging ? 9 : (_isHovered ? 8 : 7);

            // Empty Track
            using (GraphicsPath p = GetRoundPath(new Rectangle(pad, trackY, trackW, trackH), 3))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(45, 45, 55)))
            {
                g.FillPath(b, p);
            }

            // Filled Track
            int fillW = thumbX - pad;
            if (fillW > 0)
            {
                using (GraphicsPath p = GetRoundPath(new Rectangle(pad, trackY, fillW, trackH), 3))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 110, 20)))
                {
                    g.FillPath(b, p);
                }
            }

            // Thumb
            Color thumbColor = _isDragging ? Color.White : (_isHovered ? Color.FromArgb(255, 160, 90) : Color.FromArgb(255, 110, 20));
            using (SolidBrush b = new SolidBrush(thumbColor))
            {
                g.FillEllipse(b, thumbX - thumbR, thumbY - thumbR, thumbR * 2, thumbR * 2);
            }
            using (Pen p = new Pen(Color.FromArgb(25, 25, 30), 2))
            {
                g.DrawEllipse(p, thumbX - thumbR, thumbY - thumbR, thumbR * 2, thumbR * 2);
            }
        }

        private static GraphicsPath GetRoundPath(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (r.Width <= d) d = r.Width - 1;
            if (r.Height <= d) d = r.Height - 1;
            if (d < 1) d = 1;

            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    #endregion

    #region Fast Screen Capture (DIBSection)

    public class FastScreenCapture : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

        [DllImport("gdi32.dll")]
        private static extern bool StretchBlt(IntPtr hdcDest, int nXOriginDest, int nYOriginDest, int nWidthDest, int nHeightDest,
                                              IntPtr hdcSrc, int nXOriginSrc, int nYOriginSrc, int nWidthSrc, int nHeightSrc, int dwRop);

        [DllImport("gdi32.dll")]
        private static extern int SetStretchBltMode(IntPtr hdc, int iStretchMode);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private const int HALFTONE = 4;
        private const int SRCCOPY = 0x00CC0020;

        public readonly int DestWidth;
        public readonly int DestHeight;

        private IntPtr _hdcScreen = IntPtr.Zero;
        private IntPtr _hdcMem = IntPtr.Zero;
        private IntPtr _hBitmap = IntPtr.Zero;
        private IntPtr _hOldBitmap = IntPtr.Zero;
        private IntPtr _pBits = IntPtr.Zero;
        private bool _disposed;

        public FastScreenCapture(int width = 96, int height = 54)
        {
            DestWidth = width;
            DestHeight = height;

            _hdcScreen = GetDC(IntPtr.Zero);
            _hdcMem = CreateCompatibleDC(_hdcScreen);

            BITMAPINFO bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bmi.bmiHeader.biWidth = DestWidth;
            bmi.bmiHeader.biHeight = -DestHeight; // Top-down
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0;

            _hBitmap = CreateDIBSection(_hdcMem, ref bmi, 0, out _pBits, IntPtr.Zero, 0);
            _hOldBitmap = SelectObject(_hdcMem, _hBitmap);
            SetStretchBltMode(_hdcMem, HALFTONE);
        }

        public bool Capture(int[] pixelBuffer)
        {
            if (_disposed || _pBits == IntPtr.Zero) return false;

            int sw = GetSystemMetrics(0);
            int sh = GetSystemMetrics(1);

            bool ok = StretchBlt(_hdcMem, 0, 0, DestWidth, DestHeight, _hdcScreen, 0, 0, sw, sh, SRCCOPY);
            if (!ok) return false;

            Marshal.Copy(_pBits, pixelBuffer, 0, pixelBuffer.Length);
            return true;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                if (_hdcMem != IntPtr.Zero && _hOldBitmap != IntPtr.Zero)
                {
                    SelectObject(_hdcMem, _hOldBitmap);
                }
                if (_hBitmap != IntPtr.Zero)
                {
                    DeleteObject(_hBitmap);
                }
                if (_hdcMem != IntPtr.Zero)
                {
                    DeleteDC(_hdcMem);
                }
                if (_hdcScreen != IntPtr.Zero)
                {
                    ReleaseDC(IntPtr.Zero, _hdcScreen);
                }
            }
        }
    }

    #endregion

    #region Hardware Controller

    public class AorusHardwareController
    {
        private MethodInfo _methodSetLightEffect;
        private MethodInfo _methodSetZoneColors;
        private MethodInfo _methodSetLightEffectLightbar;
        private MethodInfo _methodSetLightbarCustomColor;
        private bool _isInitialized = false;

        public bool IsInitialized
        {
            get { return _isInitialized; }
        }

        public bool Initialize()
        {
            try
            {
                string dllPath = @"C:\Program Files\GIGABYTE\Control Center\Lib\GBT_Notebook\KeyboardDomainLogic2023.dll";
                if (!File.Exists(dllPath)) return false;

                Assembly asm = Assembly.LoadFrom(dllPath);
                Type fusionCtrl = asm.GetType("KeyboardDomainLogic.FusionController");
                Type fusionLight = asm.GetType("KeyboardDomainLogic.FusionLightService");
                if (fusionCtrl == null || fusionLight == null) return false;

                MethodInfo initMethod = fusionCtrl.GetMethod("InitKeyboard", new Type[0]);
                if (initMethod == null) return false;

                bool success = (bool)initMethod.Invoke(null, null);
                if (!success) return false;

                _methodSetLightEffect = fusionLight.GetMethod("SetLightEffect", new Type[] { typeof(int) });
                _methodSetZoneColors = fusionLight.GetMethod("SetZoneColors", new Type[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(int) });
                _methodSetLightEffectLightbar = fusionLight.GetMethod("SetLightEffect_LightBar", new Type[] { typeof(byte), typeof(byte), typeof(byte), typeof(byte), typeof(byte), typeof(byte) });
                _methodSetLightbarCustomColor = fusionLight.GetMethod("SetLightbarCustomColor", new Type[] { typeof(byte), typeof(byte), typeof(byte) });

                // Активируем Zone RGB Mode (Режим 9) на клавиатуре
                if (_methodSetLightEffect != null)
                {
                    _methodSetLightEffect.Invoke(null, new object[] { 9 });
                    Thread.Sleep(100);
                }

                // Активируем статичный режим (Режим 1) на световой полосе (Lightbar)
                if (_methodSetLightEffectLightbar != null)
                {
                    _methodSetLightEffectLightbar.Invoke(null, new object[] { (byte)1, (byte)50, (byte)100, (byte)7, (byte)0, (byte)0 });
                    Thread.Sleep(80);
                }

                _isInitialized = (_methodSetZoneColors != null);
                return _isInitialized;
            }
            catch
            {
                _isInitialized = false;
                return false;
            }
        }

        public void ActivateZoneRgbMode()
        {
            try
            {
                if (_methodSetLightEffect != null)
                {
                    _methodSetLightEffect.Invoke(null, new object[] { 9 });
                }
            }
            catch { }
        }

        // Zone 0 (Left/WASD) -> 3
        // Zone 1 (Center)     -> 4
        // Zone 2 (Right)      -> 5
        public void SetZoneColor(int userZoneIndex, int r, int g, int b, int brightnessPercent)
        {
            if (!_isInitialized || _methodSetZoneColors == null) return;

            int realHwZone = userZoneIndex + 3; // Map 0->3 (WASD), 1->4 (Center), 2->5 (Right)
            // Аппаратный максимум скважности ITE контроллера равен 50 (в GCC шкала 0..100 делится на 2: _ledBrightness / 2)
            // Значения выше 50 вызывали 8-битное переполнение регистров и искажение цветов в фиолетовый
            int hwBri = Math.Max(0, Math.Min(50, (int)Math.Round(brightnessPercent * 0.5f)));

            try
            {
                _methodSetZoneColors.Invoke(null, new object[] {
                    realHwZone,
                    Clamp(r),
                    Clamp(g),
                    Clamp(b),
                    hwBri
                });
            }
            catch { }
        }

        private byte _lastSentLbColor = 255;
        private byte _lastSentLbBri = 255;

        public void SetLightbarColor(int r, int g, int b, int brightnessPercent)
        {
            if (!_isInitialized || _methodSetLightEffectLightbar == null) return;

            byte colorCode = RgbToLightbarColorCode(r, g, b);
            byte bri = (byte)Math.Max(0, Math.Min(100, brightnessPercent));
            if (colorCode == 0) bri = 0;

            if (colorCode != _lastSentLbColor || Math.Abs(bri - _lastSentLbBri) > 10)
            {
                try
                {
                    _methodSetLightEffectLightbar.Invoke(null, new object[] {
                        (byte)1,     // Статичный режим
                        (byte)50,    // Скорость
                        bri,         // Яркость 0..100
                        colorCode,   // Цветовой код (1..7)
                        (byte)0,     // Направление
                        (byte)0      // Синхронизация
                    });
                    _lastSentLbColor = colorCode;
                    _lastSentLbBri = bri;
                }
                catch { }
            }
        }

        public static float GetHue(int r, int g, int b)
        {
            float rf = r / 255.0f;
            float gf = g / 255.0f;
            float bf = b / 255.0f;

            float max = Math.Max(rf, Math.Max(gf, bf));
            float min = Math.Min(rf, Math.Min(gf, bf));
            float delta = max - min;

            if (delta < 0.001f) return 0f;

            float hue;
            if (max == rf)
            {
                hue = (gf - bf) / delta;
                if (hue < 0) hue += 6f;
            }
            else if (max == gf)
            {
                hue = 2f + (bf - rf) / delta;
            }
            else
            {
                hue = 4f + (rf - gf) / delta;
            }

            return hue * 60f;
        }

        public static byte RgbToLightbarColorCode(int r, int g, int b)
        {
            int max = Math.Max(r, Math.Max(g, b));
            if (max < 18) return 0; // Black / Off

            int min = Math.Min(r, Math.Min(g, b));
            int delta = max - min;

            // Если насыщенность очень низкая -> Белый
            if (delta < 25 && max > 55) return 7; // White (Пресет 7)

            float hue = GetHue(r, g, b);

            // Красный (Пресет 1): 345°..360° и 0°..20° (чистый красный, контейнер в CS2)
            if (hue >= 345f || hue < 20f) return 1;

            // Оранжевый (Пресет 5): 20°..48° (Aorus Orange, пламя, закат)
            if (hue >= 20f && hue < 48f) return 5;

            // Желтый (Пресет 3): 48°..70° (золото, лимонный)
            if (hue >= 48f && hue < 70f) return 3;

            // Зеленый (Пресет 2): 70°..165° (салатовый, изумрудный, трава)
            if (hue >= 70f && hue < 165f) return 2;

            // Голубой / Небесный (Пресет 7): 165°..205° (циан, светлое небо)
            if (hue >= 165f && hue < 205f) return 7;

            // Синий (Пресет 4): 205°..265° (кобальт, глубокий синий)
            if (hue >= 205f && hue < 265f) return 4;

            // Пурпурный (Пресет 6): 265°..345° (фиолетовый, сиреневый, маджента)
            return 6;
        }

        public void TurnOff()
        {
            SetZoneColor(0, 0, 0, 0, 0);
            SetZoneColor(1, 0, 0, 0, 0);
            SetZoneColor(2, 0, 0, 0, 0);
            SetLightbarColor(0, 0, 0, 0);
        }

        private static int Clamp(int val, int min = 0, int max = 255)
        {
            if (val < min) return min;
            if (val > max) return max;
            return val;
        }
    }

    #endregion

    public struct RgbColor
    {
        public float R;
        public float G;
        public float B;

        public RgbColor(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }

        public Color ToColor()
        {
            return Color.FromArgb(
                (int)Math.Min(255, Math.Max(0, R)),
                (int)Math.Min(255, Math.Max(0, G)),
                (int)Math.Min(255, Math.Max(0, B)));
        }
    }

    public class MainForm : Form
    {
        private AorusHardwareController _hardware = new AorusHardwareController();
        private Thread _workerThread;
        private volatile bool _isRunning = false;
        private volatile bool _isPaused = false;
        private volatile bool _isTestMode = false;

        // Settings
        public volatile int Brightness = 100;
        public volatile float SmoothingAlpha = 0.065f;
        public volatile float SmoothingMaxStep = 2.5f;
        public volatile float SaturationFactor = 1.35f;
        public volatile bool LightbarBottomOnly = false;

        // Live Colors
        public RgbColor CurZone0 = new RgbColor(0, 0, 0);
        public RgbColor CurZone1 = new RgbColor(0, 0, 0);
        public RgbColor CurZone2 = new RgbColor(0, 0, 0);
        public RgbColor CurLightbar = new RgbColor(0, 0, 0);

        // Controls
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private Label _lblStatus;
        private DoubleBufferedPanel _previewPanel;
        private DarkSlider _sliderBrightness;
        private DarkSlider _sliderSmoothness;
        private DarkSlider _sliderVibrancy;
        private Label _lblBrightnessVal;
        private Label _lblSmoothnessVal;
        private Label _lblVibrancyVal;
        private RadioButton _rbLightbarBottom;
        private RadioButton _rbLightbarAll;
        private CheckBox _cbAutoStart;
        private CheckBox _cbMinimizeToTray;
        private Button _btnToggle;
        private Button _btnTest;
        private System.Windows.Forms.Timer _guiTimer;

        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRegistryName = "AorusAmbientLight";

        public MainForm()
        {
            InitializeComponent();
            LoadRegistrySettings();

            // Background hardware connection
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = _hardware.Initialize();
                this.BeginInvoke((MethodInvoker)delegate
                {
                    if (ok)
                    {
                        _lblStatus.Text = "● Контроллер Aorus подключен (Zone RGB Mode)";
                        _lblStatus.ForeColor = Color.FromArgb(76, 175, 80);
                        StartWorker();
                    }
                    else
                    {
                        _lblStatus.Text = "● Ошибка подключения к контроллеру Aorus";
                        _lblStatus.ForeColor = Color.FromArgb(244, 67, 54);
                    }
                });
            });
        }

        private void InitializeComponent()
        {
            this.Text = "Aorus Ambient Light — Адаптивная подсветка";
            this.ClientSize = new Size(540, 690);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(20, 20, 24);
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // Tray Menu
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("Открыть окно", null, delegate { RestoreFromTray(); });
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Пауза / Старт", null, delegate { TogglePause(); });
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Выход", null, delegate { ExitApplication(); });

            _trayIcon = new NotifyIcon();
            _trayIcon.Text = "Aorus Ambient Light";
            _trayIcon.ContextMenuStrip = _trayMenu;
            _trayIcon.Icon = CreateAppIcon();
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += delegate { RestoreFromTray(); };

            // Header Banner
            Panel pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 72,
                BackColor = Color.FromArgb(28, 28, 34)
            };

            Label lblTitle = new Label
            {
                Text = "AORUS AMBIENT LIGHT",
                Font = new Font("Segoe UI", 12.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 110, 20),
                Location = new Point(18, 12),
                AutoSize = true
            };

            _lblStatus = new Label
            {
                Text = "● Подключение к контроллеру...",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(255, 193, 7),
                Location = new Point(20, 42),
                AutoSize = true
            };

            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(_lblStatus);
            this.Controls.Add(pnlHeader);

            int startY = 82;
            int margin = 16;
            int width = this.ClientSize.Width - margin * 2;

            // 1. Live Visualization Card
            Label lblPreviewTitle = new Label
            {
                Text = "ВИЗУАЛИЗАЦИЯ ПОДСВЕТКИ В РЕАЛЬНОМ ВРЕМЕНИ",
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(160, 160, 175),
                Location = new Point(margin, startY),
                AutoSize = true
            };
            this.Controls.Add(lblPreviewTitle);
            startY += 22;

            _previewPanel = new DoubleBufferedPanel
            {
                Location = new Point(margin, startY),
                Size = new Size(width, 135),
                BackColor = Color.FromArgb(12, 12, 15)
            };
            _previewPanel.Paint += PreviewPanel_Paint;
            this.Controls.Add(_previewPanel);
            startY += 145;

            // 2. Sliders
            // Brightness
            Label lblBri = new Label
            {
                Text = "Яркость подсветки:",
                Location = new Point(margin, startY),
                AutoSize = true
            };
            _lblBrightnessVal = new Label
            {
                Text = "100%",
                ForeColor = Color.FromArgb(255, 110, 20),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(this.ClientSize.Width - margin - 60, startY),
                TextAlign = ContentAlignment.TopRight,
                AutoSize = false,
                Width = 60
            };
            this.Controls.Add(lblBri);
            this.Controls.Add(_lblBrightnessVal);
            startY += 24;

            _sliderBrightness = new DarkSlider
            {
                Location = new Point(margin, startY),
                Size = new Size(width, 26),
                Minimum = 10,
                Maximum = 100,
                Value = 100
            };
            _sliderBrightness.ValueChanged += delegate
            {
                Brightness = _sliderBrightness.Value;
                _lblBrightnessVal.Text = Brightness + "%";
            };
            this.Controls.Add(_sliderBrightness);
            startY += 36;

            // Smoothness
            Label lblSm = new Label
            {
                Text = "Плавность смены кадров:",
                Location = new Point(margin, startY),
                AutoSize = true
            };
            _lblSmoothnessVal = new Label
            {
                Text = "Сбалансированная (Плавная)",
                ForeColor = Color.FromArgb(255, 110, 20),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(this.ClientSize.Width - margin - 220, startY),
                TextAlign = ContentAlignment.TopRight,
                AutoSize = false,
                Width = 220
            };
            this.Controls.Add(lblSm);
            this.Controls.Add(_lblSmoothnessVal);
            startY += 24;

            _sliderSmoothness = new DarkSlider
            {
                Location = new Point(margin, startY),
                Size = new Size(width, 26),
                Minimum = 1,
                Maximum = 10,
                Value = 6
            };
            _sliderSmoothness.ValueChanged += delegate
            {
                int val = _sliderSmoothness.Value;
                float t = (val - 1) / 9.0f; // 0.0 .. 1.0

                // Экспоненциальное масштабирование: от бодрого 0.35 до шелковистого кино-эффекта
                SmoothingAlpha = 0.35f * (float)Math.Pow(0.045f, t);   // 0.35 -> 0.016
                SmoothingMaxStep = 18.0f * (float)Math.Pow(0.04f, t);  // 18.0 -> 0.72

                if (val <= 3) _lblSmoothnessVal.Text = "Динамичная (Игры)";
                else if (val <= 6) _lblSmoothnessVal.Text = "Сбалансированная (Плавная)";
                else _lblSmoothnessVal.Text = "Шелковая (Ультра-плавно)";
            };
            this.Controls.Add(_sliderSmoothness);
            startY += 36;

            // Vibrancy
            Label lblVib = new Label
            {
                Text = "Насыщенность цветов (Vivid):",
                Location = new Point(margin, startY),
                AutoSize = true
            };
            _lblVibrancyVal = new Label
            {
                Text = "1.35x",
                ForeColor = Color.FromArgb(255, 110, 20),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(this.ClientSize.Width - margin - 60, startY),
                TextAlign = ContentAlignment.TopRight,
                AutoSize = false,
                Width = 60
            };
            this.Controls.Add(lblVib);
            this.Controls.Add(_lblVibrancyVal);
            startY += 24;

            _sliderVibrancy = new DarkSlider
            {
                Location = new Point(margin, startY),
                Size = new Size(width, 26),
                Minimum = 10,
                Maximum = 20,
                Value = 13
            };
            _sliderVibrancy.ValueChanged += delegate
            {
                SaturationFactor = _sliderVibrancy.Value / 10.0f;
                _lblVibrancyVal.Text = SaturationFactor.ToString("0.00") + "x";
            };
            this.Controls.Add(_sliderVibrancy);
            startY += 40;

            // Lightbar Mode
            Label lblBarTitle = new Label
            {
                Text = "Источник цвета для ленты под экраном (Lightbar):",
                Location = new Point(margin, startY),
                AutoSize = true
            };
            this.Controls.Add(lblBarTitle);
            startY += 24;

            _rbLightbarBottom = new RadioButton
            {
                Text = "Нижняя кромка экрана (продолжение кадра)",
                Checked = false,
                Location = new Point(margin + 5, startY),
                AutoSize = true,
                ForeColor = Color.FromArgb(220, 220, 230)
            };
            _rbLightbarBottom.CheckedChanged += delegate { LightbarBottomOnly = _rbLightbarBottom.Checked; };
            this.Controls.Add(_rbLightbarBottom);
            startY += 26;

            _rbLightbarAll = new RadioButton
            {
                Text = "Общая атмосфера кадра (гармония с клавиатурой)",
                Checked = true,
                Location = new Point(margin + 5, startY),
                AutoSize = true,
                ForeColor = Color.FromArgb(220, 220, 230)
            };
            this.Controls.Add(_rbLightbarAll);
            startY += 34;

            // Checkboxes
            _cbAutoStart = new CheckBox
            {
                Text = "Автозапуск вместе с Windows",
                Location = new Point(margin + 5, startY),
                AutoSize = true,
                ForeColor = Color.FromArgb(220, 220, 230)
            };
            _cbAutoStart.CheckedChanged += delegate { SetAutoStart(_cbAutoStart.Checked); };
            this.Controls.Add(_cbAutoStart);

            _cbMinimizeToTray = new CheckBox
            {
                Text = "Сворачивать в трей при закрытии",
                Checked = true,
                Location = new Point(margin + 260, startY),
                AutoSize = true,
                ForeColor = Color.FromArgb(220, 220, 230)
            };
            this.Controls.Add(_cbMinimizeToTray);
            startY += 38;

            // Action Buttons
            int btnWidth = (width - 12) / 2;

            _btnToggle = new Button
            {
                Text = "ПАУЗА",
                Location = new Point(margin, startY),
                Size = new Size(btnWidth, 42),
                BackColor = Color.FromArgb(255, 110, 20),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnToggle.FlatAppearance.BorderSize = 0;
            _btnToggle.Click += delegate { TogglePause(); };
            this.Controls.Add(_btnToggle);

            _btnTest = new Button
            {
                Text = "ТЕСТ ЦВЕТОВ",
                Location = new Point(margin + btnWidth + 12, startY),
                Size = new Size(btnWidth, 42),
                BackColor = Color.FromArgb(40, 40, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnTest.FlatAppearance.BorderSize = 0;
            _btnTest.Click += delegate { RunHardwareSelfTest(); };
            this.Controls.Add(_btnTest);

            // GUI Timer (30 FPS)
            _guiTimer = new System.Windows.Forms.Timer();
            _guiTimer.Interval = 33;
            _guiTimer.Tick += delegate
            {
                if (this.Visible && this.WindowState != FormWindowState.Minimized)
                {
                    _previewPanel.Invalidate();
                }
            };
            _guiTimer.Start();
        }

        private void PreviewPanel_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = _previewPanel.ClientSize.Width;
            int h = _previewPanel.ClientSize.Height;

            // 1. Lightbar Strip (Top)
            int barY = 8;
            int barH = 20;
            Rectangle barRect = new Rectangle(12, barY, w - 24, barH);
            Color barColor = CurLightbar.ToColor();

            using (SolidBrush b = new SolidBrush(barColor))
            {
                g.FillRectangle(b, barRect);
            }
            using (Pen p = new Pen(Color.FromArgb(80, 80, 95)))
            {
                g.DrawRectangle(p, barRect);
            }

            Color barTextColor = (barColor.R * 0.299 + barColor.G * 0.587 + barColor.B * 0.114 > 130) ? Color.Black : Color.White;
            using (Font f = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            using (SolidBrush tb = new SolidBrush(barTextColor))
            {
                string txt = "LED-ЛЕНТА ПОД ДИСПЛЕЕМ (LIGHTBAR)";
                SizeF sz = g.MeasureString(txt, f);
                g.DrawString(txt, f, tb, (w - sz.Width) / 2, barY + 3);
            }

            // 2. Keyboard Zones (3 Boxes)
            int kbY = 36;
            int kbH = h - kbY - 8;
            int totalW = w - 24;
            int spacing = 8;
            int zoneW = (totalW - spacing * 2) / 3;

            Rectangle r0 = new Rectangle(12, kbY, zoneW, kbH);
            Rectangle r1 = new Rectangle(12 + zoneW + spacing, kbY, zoneW, kbH);
            Rectangle r2 = new Rectangle(12 + (zoneW + spacing) * 2, kbY, zoneW, kbH);

            DrawZoneBox(g, r0, CurZone0.ToColor(), "ЛЕВАЯ ЗОНА\n(WASD)");
            DrawZoneBox(g, r1, CurZone1.ToColor(), "ЦЕНТР\n(Клавиатура)");
            DrawZoneBox(g, r2, CurZone2.ToColor(), "ПРАВАЯ ЗОНА\n(Numpad / Стрелки)");
        }

        private void DrawZoneBox(Graphics g, Rectangle r, Color col, string title)
        {
            using (SolidBrush b = new SolidBrush(col))
            {
                g.FillRectangle(b, r);
            }
            using (Pen p = new Pen(Color.FromArgb(70, 70, 85)))
            {
                g.DrawRectangle(p, r);
            }

            int bannerH = 34;
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(200, 15, 15, 20)))
            {
                g.FillRectangle(bg, r.X, r.Bottom - bannerH, r.Width, bannerH);
            }

            using (Font f = new Font("Segoe UI", 7.5f, FontStyle.Regular))
            using (SolidBrush tb = new SolidBrush(Color.White))
            {
                StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString(title, f, tb, new RectangleF(r.X, r.Bottom - bannerH, r.Width, bannerH), sf);
            }
        }

        private void StartWorker()
        {
            if (_isRunning) return;

            _isRunning = true;
            _isPaused = false;
            _btnToggle.Text = "ПАУЗА";
            _btnToggle.BackColor = Color.FromArgb(255, 110, 20);

            _workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _workerThread.Start();
        }

        private void TogglePause()
        {
            if (!_hardware.IsInitialized)
            {
                _hardware.Initialize();
            }

            _isPaused = !_isPaused;
            if (_isPaused)
            {
                _btnToggle.Text = "ВОЗОБНОВИТЬ";
                _btnToggle.BackColor = Color.FromArgb(76, 175, 80);
                _lblStatus.Text = "● Приостановлено";
                _lblStatus.ForeColor = Color.FromArgb(170, 170, 180);
            }
            else
            {
                _hardware.ActivateZoneRgbMode();
                _btnToggle.Text = "ПАУЗА";
                _btnToggle.BackColor = Color.FromArgb(255, 110, 20);
                _lblStatus.Text = "● Активно (Адаптивная синхронизация)";
                _lblStatus.ForeColor = Color.FromArgb(76, 175, 80);
            }
        }

        private void RunHardwareSelfTest()
        {
            if (_isTestMode) return;

            _isTestMode = true;
            _btnTest.Enabled = false;
            _btnTest.Text = "ТЕСТИРОВАНИЕ...";

            ThreadPool.QueueUserWorkItem(delegate
            {
                bool wasPaused = _isPaused;
                _isPaused = true;
                Thread.Sleep(50);

                _hardware.ActivateZoneRgbMode();
                Thread.Sleep(100);

                // Фаза 1: Раздельный тест всех зон (Левая=Красный, Центр=Зеленый, Правая=Синий, Лайтбар=Желтый)
                CurZone0 = new RgbColor(255, 0, 0);
                CurZone1 = new RgbColor(0, 255, 0);
                CurZone2 = new RgbColor(0, 0, 255);
                CurLightbar = new RgbColor(255, 255, 0);

                _hardware.SetZoneColor(0, 255, 0, 0, 100);
                _hardware.SetZoneColor(1, 0, 255, 0, 100);
                _hardware.SetZoneColor(2, 0, 0, 255, 100);
                _hardware.SetLightbarColor(255, 255, 0, 100);
                Thread.Sleep(3500);

                // Фаза 2: Общая синхронная смена цветов
                Color[] testColors = new Color[] {
                    Color.Red,
                    Color.FromArgb(255, 75, 0), // Aorus Orange
                    Color.FromArgb(255, 210, 0), // Gold / Yellow
                    Color.FromArgb(0, 230, 70),  // Vivid Green
                    Color.FromArgb(0, 210, 255), // Cyan
                    Color.Blue,
                    Color.FromArgb(220, 0, 255), // Magenta / Purple
                    Color.White
                };

                for (int i = 0; i < testColors.Length; i++)
                {
                    Color c = testColors[i];
                    CurZone0 = new RgbColor(c.R, c.G, c.B);
                    CurZone1 = new RgbColor(c.R, c.G, c.B);
                    CurZone2 = new RgbColor(c.R, c.G, c.B);
                    CurLightbar = new RgbColor(c.R, c.G, c.B);

                    _hardware.SetZoneColor(0, c.R, c.G, c.B, 100);
                    _hardware.SetZoneColor(1, c.R, c.G, c.B, 100);
                    _hardware.SetZoneColor(2, c.R, c.G, c.B, 100);
                    _hardware.SetLightbarColor(c.R, c.G, c.B, 100);

                    Thread.Sleep(1500);
                }

                _isPaused = wasPaused;
                this.BeginInvoke((MethodInvoker)delegate
                {
                    _isTestMode = false;
                    _btnTest.Enabled = true;
                    _btnTest.Text = "ТЕСТ ЦВЕТОВ";
                });
            });
        }

        private void WorkerLoop()
        {
            using (FastScreenCapture capture = new FastScreenCapture(96, 54))
            {
                int dw = capture.DestWidth;
                int dh = capture.DestHeight;
                int[] pixels = new int[dw * dh];

                RgbColor s0 = new RgbColor(0, 0, 0);
                RgbColor s1 = new RgbColor(0, 0, 0);
                RgbColor s2 = new RgbColor(0, 0, 0);
                RgbColor sBar = new RgbColor(0, 0, 0);

                int lastSentR0 = -1, lastSentG0 = -1, lastSentB0 = -1;
                int lastSentR1 = -1, lastSentG1 = -1, lastSentB1 = -1;
                int lastSentR2 = -1, lastSentG2 = -1, lastSentB2 = -1;
                int lastSentRBar = -1, lastSentGBar = -1, lastSentBBar = -1;

                int loopCycle = 0;

                while (_isRunning)
                {
                    if (_isPaused || _isTestMode)
                    {
                        Thread.Sleep(80);
                        continue;
                    }

                    try
                    {
                        loopCycle++;
                        if (loopCycle % 120 == 0)
                        {
                            _hardware.ActivateZoneRgbMode();
                        }

                        bool captured = capture.Capture(pixels);
                        if (!captured)
                        {
                            Thread.Sleep(30);
                            continue;
                        }

                        // Zone 0: Left third [0, 31]
                        RgbColor raw0 = ComputeZoneAverage(pixels, dw, dh, 0, 32, 0, dh);
                        // Zone 1: Center third [32, 63]
                        RgbColor raw1 = ComputeZoneAverage(pixels, dw, dh, 32, 64, 0, dh);
                        // Zone 2: Right third [64, 95]
                        RgbColor raw2 = ComputeZoneAverage(pixels, dw, dh, 64, dw, 0, dh);

                        // Lightbar
                        RgbColor rawBar;
                        if (LightbarBottomOnly)
                        {
                            // Bottom 25% of display
                            rawBar = ComputeZoneAverage(pixels, dw, dh, 0, dw, (int)(dh * 0.75f), dh);
                        }
                        else
                        {
                            // Атмосфера всей сцены (гармония с клавиатурой)
                            rawBar = new RgbColor((raw0.R + raw1.R + raw2.R) / 3f,
                                                  (raw0.G + raw1.G + raw2.G) / 3f,
                                                  (raw0.B + raw1.B + raw2.B) / 3f);
                        }

                        // Vibrancy
                        raw0 = BoostVibrancy(raw0, SaturationFactor);
                        raw1 = BoostVibrancy(raw1, SaturationFactor);
                        raw2 = BoostVibrancy(raw2, SaturationFactor);
                        rawBar = BoostVibrancy(rawBar, SaturationFactor);

                        // Smooth
                        float alpha = SmoothingAlpha;
                        float maxStep = SmoothingMaxStep;
                        s0 = Smooth(s0, raw0, alpha, maxStep);
                        s1 = Smooth(s1, raw1, alpha, maxStep);
                        s2 = Smooth(s2, raw2, alpha, maxStep);
                        sBar = Smooth(sBar, rawBar, alpha, maxStep);

                        CurZone0 = s0;
                        CurZone1 = s1;
                        CurZone2 = s2;
                        CurLightbar = sBar;

                        int bri = Brightness;

                        int outR0 = (int)(s0.R * bri / 100.0f);
                        int outG0 = (int)(s0.G * bri / 100.0f);
                        int outB0 = (int)(s0.B * bri / 100.0f);

                        int outR1 = (int)(s1.R * bri / 100.0f);
                        int outG1 = (int)(s1.G * bri / 100.0f);
                        int outB1 = (int)(s1.B * bri / 100.0f);

                        int outR2 = (int)(s2.R * bri / 100.0f);
                        int outG2 = (int)(s2.G * bri / 100.0f);
                        int outB2 = (int)(s2.B * bri / 100.0f);

                        int outRBar = (int)(sBar.R * bri / 100.0f);
                        int outGBar = (int)(sBar.G * bri / 100.0f);
                        int outBBar = (int)(sBar.B * bri / 100.0f);

                        // Применяем аппаратную калибровку светодиодов ноутбука (убирает розоватость и паразитный синий в теплых тонах)
                        CalibrateForKeyboardLeds(ref outR0, ref outG0, ref outB0);
                        CalibrateForKeyboardLeds(ref outR1, ref outG1, ref outB1);
                        CalibrateForKeyboardLeds(ref outR2, ref outG2, ref outB2);

                        if (ColorDelta(outR0, outG0, outB0, lastSentR0, lastSentG0, lastSentB0) >= 1)
                        {
                            _hardware.SetZoneColor(0, outR0, outG0, outB0, bri);
                            lastSentR0 = outR0; lastSentG0 = outG0; lastSentB0 = outB0;
                        }

                        if (ColorDelta(outR1, outG1, outB1, lastSentR1, lastSentG1, lastSentB1) >= 1)
                        {
                            _hardware.SetZoneColor(1, outR1, outG1, outB1, bri);
                            lastSentR1 = outR1; lastSentG1 = outG1; lastSentB1 = outB1;
                        }

                        if (ColorDelta(outR2, outG2, outB2, lastSentR2, lastSentG2, lastSentB2) >= 1)
                        {
                            _hardware.SetZoneColor(2, outR2, outG2, outB2, bri);
                            lastSentR2 = outR2; lastSentG2 = outG2; lastSentB2 = outB2;
                        }

                        if (ColorDelta(outRBar, outGBar, outBBar, lastSentRBar, lastSentGBar, lastSentBBar) >= 1)
                        {
                            _hardware.SetLightbarColor(outRBar, outGBar, outBBar, bri);
                            lastSentRBar = outRBar; lastSentGBar = outGBar; lastSentBBar = outBBar;
                        }
                    }
                    catch
                    {
                    }

                    Thread.Sleep(20); // 50 FPS - ультра-плавная анимация без рывков
                }
            }
        }

        private static void CalibrateForKeyboardLeds(ref int r, ref int g, ref int b)
        {
            if (r > g && r > b)
            {
                // Теплые тона (оранжевый, желтый, закатный)
                // 1. Подавляем паразитный синий шум, который превращает оранжевый в розовый
                if (b < 120)
                {
                    b = (int)(b * 0.25f);
                }

                // 2. Калибровка зеленого: у светодиодов зеленый субъективно намного ярче красного.
                // Корректируем пропорцию, чтобы оранжевый оставался насыщенным фирменным Aorus Orange
                float gr = (r > 0) ? (float)g / r : 0f;
                if (gr > 0.12f && gr < 0.70f)
                {
                    g = (int)(g * 0.45f);
                }
            }
            else if (g > r && g > b)
            {
                if (b < 100) b = (int)(b * 0.3f);
            }
        }

        private static int ColorDelta(int r1, int g1, int b1, int r2, int g2, int b2)
        {
            return Math.Max(Math.Abs(r1 - r2), Math.Max(Math.Abs(g1 - g2), Math.Abs(b1 - b2)));
        }

        private static RgbColor ComputeZoneAverage(int[] pixels, int dw, int dh, int x0, int x1, int y0, int y1)
        {
            double sumR = 0, sumG = 0, sumB = 0;
            double totalWeight = 0;

            for (int y = y0; y < y1; y++)
            {
                int rowOffset = y * dw;
                for (int x = x0; x < x1; x++)
                {
                    int c = pixels[rowOffset + x];
                    int r = (c >> 16) & 0xFF;
                    int g = (c >> 8) & 0xFF;
                    int b = c & 0xFF;

                    int max = Math.Max(r, Math.Max(g, b));
                    int min = Math.Min(r, Math.Min(g, b));

                    // Игнорируем темные пиксели интерфейса, панели задач и рамок
                    if (max < 20) continue;

                    // Ambilight Vibrant Peak Weighting: отдаем приоритет сочным цветам
                    double sat = (max > 0) ? (double)(max - min) / max : 0;
                    double bri = max / 255.0;
                    double weight = 0.2 + sat * 1.6 + bri * 0.6;

                    sumR += r * weight;
                    sumG += g * weight;
                    sumB += b * weight;
                    totalWeight += weight;
                }
            }

            if (totalWeight < 0.001) return new RgbColor(0, 0, 0);
            return new RgbColor((float)(sumR / totalWeight), (float)(sumG / totalWeight), (float)(sumB / totalWeight));
        }

        private static RgbColor BoostVibrancy(RgbColor c, float factor)
        {
            float r = c.R / 255.0f;
            float g = c.G / 255.0f;
            float b = c.B / 255.0f;

            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            float delta = max - min;

            if (delta < 0.005f || max < 0.05f) return c;

            float mean = (r + g + b) / 3.0f;
            r = mean + (r - mean) * factor;
            g = mean + (g - mean) * factor;
            b = mean + (b - mean) * factor;

            return new RgbColor(
                Math.Min(255f, Math.Max(0f, r * 255f)),
                Math.Min(255f, Math.Max(0f, g * 255f)),
                Math.Min(255f, Math.Max(0f, b * 255f)));
        }

        private static RgbColor Smooth(RgbColor current, RgbColor target, float alpha, float maxStep)
        {
            float dR = (target.R - current.R) * alpha;
            float dG = (target.G - current.G) * alpha;
            float dB = (target.B - current.B) * alpha;

            if (dR > maxStep) dR = maxStep;
            else if (dR < -maxStep) dR = -maxStep;

            if (dG > maxStep) dG = maxStep;
            else if (dG < -maxStep) dG = -maxStep;

            if (dB > maxStep) dB = maxStep;
            else if (dB < -maxStep) dB = -maxStep;

            return new RgbColor(
                current.R + dR,
                current.G + dG,
                current.B + dB);
        }

        private void SetAutoStart(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            key.SetValue(AppRegistryName, "\"" + Application.ExecutablePath + "\"");
                        }
                        else
                        {
                            key.DeleteValue(AppRegistryName, false);
                        }
                    }
                }
            }
            catch { }
        }

        private void LoadRegistrySettings()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false))
                {
                    if (key != null)
                    {
                        _cbAutoStart.Checked = (key.GetValue(AppRegistryName) != null);
                    }
                }
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && _cbMinimizeToTray.Checked)
            {
                e.Cancel = true;
                this.Hide();
                _trayIcon.ShowBalloonTip(1500, "Aorus Ambient Light", "Приложение свернуто в трей и продолжает работать.", ToolTipIcon.Info);
                return;
            }

            ExitApplication();
            base.OnFormClosing(e);
        }

        private void RestoreFromTray()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.BringToFront();
            this.Activate();
        }

        private void ExitApplication()
        {
            _isRunning = false;
            if (_workerThread != null && _workerThread.IsAlive)
            {
                _workerThread.Join(400);
            }

            _hardware.TurnOff();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            Application.Exit();
        }

        private static Icon CreateAppIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 110, 20)))
                    {
                        g.FillEllipse(b, 2, 2, 28, 28);
                    }
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(25, 25, 30)))
                    {
                        g.FillEllipse(b, 7, 7, 18, 18);
                    }
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 190, 50)))
                    {
                        g.FillEllipse(b, 12, 12, 8, 8);
                    }
                }
                IntPtr hIcon = bmp.GetHicon();
                return Icon.FromHandle(hIcon);
            }
        }
    }
}
