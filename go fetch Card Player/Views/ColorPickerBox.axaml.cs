using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;
using System.Runtime.InteropServices;

namespace CardPlayer.Views;

public partial class ColorPickerBox : UserControl
{
    public static readonly StyledProperty<string> ColorProperty =
        AvaloniaProperty.Register<ColorPickerBox, string>(
            nameof(Color), defaultValue: "#FFFFFF",
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay,
            coerce: (_, v) => string.IsNullOrWhiteSpace(v) ? "#FFFFFF" : v);

    public string Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    // HSV is the source of truth while the picker is open. H:0-360 S:0-100 V:0-100.
    private double _h = 0, _s = 0, _v = 100;

    // True only while we are programmatically writing slider values
    // (i.e. when seeding them from a hex as the popup opens). While true,
    // slider ValueChanged handlers do nothing.
    private bool _suppress;

    // True while CommitToColor is pushing Color = hex outward. Prevents the
    // two-way binding echo in OnPropertyChanged from re-seeding the sliders.
    private bool _committing;
    private bool _initialized;  // true after constructor finishes wiring controls

    private Border _swatch = null!;
    private TextBox _hexBox = null!;
    private Popup _picker = null!;
    private Canvas _svCanvas = null!;
    private Image _svImage = null!;
    private Ellipse _svDot = null!;
    private Slider _hSlider = null!;
    private Slider _sSlider = null!;
    private Slider _vSlider = null!;
    private Border _preview = null!;

    private const int SZ = 206;

    public ColorPickerBox()
    {
        AvaloniaXamlLoader.Load(this);

        _swatch = this.FindControl<Border>("Swatch")!;
        _hexBox = this.FindControl<TextBox>("HexBox")!;
        _picker = this.FindControl<Popup>("Picker")!;
        _svCanvas = this.FindControl<Canvas>("SvCanvas")!;
        _svImage = this.FindControl<Image>("SvImage")!;
        _svDot = this.FindControl<Ellipse>("SvDot")!;
        _hSlider = this.FindControl<Slider>("HueSlider")!;
        _sSlider = this.FindControl<Slider>("SatSlider")!;
        _vSlider = this.FindControl<Slider>("ValSlider")!;
        _preview = this.FindControl<Border>("PreviewSwatch")!;

        // Each slider handler reads ONLY its own value and updates ONLY its
        // own backing field, then refreshes the in-popup visuals. It never
        // touches the other sliders, the hex field, or the bound Color.
        _hSlider.ValueChanged += (_, e) =>
        {
            if (_suppress) return;
            _h = e.NewValue;
            RedrawSquare();
            UpdatePreview();
            CommitToColor();
        };
        _sSlider.ValueChanged += (_, e) =>
        {
            if (_suppress) return;
            _s = e.NewValue;
            PlaceDot();
            UpdatePreview();
            CommitToColor();
        };
        _vSlider.ValueChanged += (_, e) =>
        {
            if (_suppress) return;
            _v = e.NewValue;
            PlaceDot();
            UpdatePreview();
            CommitToColor();
        };

        // Also commit when the popup closes (covers the case where the user
        // drags the SV square as the last action before dismissing).
        _picker.Closed += (_, _) => CommitToColor();
        _initialized = true;

        // Whenever the control is (re)attached — e.g. a new entry's DataContext
        // binds to us — force a full sync from the current Color. This does not
        // rely on OnPropertyChanged firing, which Avalonia may suppress when the
        // coerced value happens to equal the existing one.
        this.Loaded += (_, _) => SyncFromColor();

        // Seed now in case the binding already fired during XAML load.
        SyncFromColor();
    }

    // Single source of truth for pushing the bound Color into all visuals.
    private void SyncFromColor()
    {
        if (!_initialized) return;
        var hex = NormalizeHex(Color);
        if (!_picker.IsOpen)
        {
            _hexBox.Text = hex;
            PaintMainSwatch(hex);
            SeedFromHex(hex);
        }
    }

    // Returns a valid #RRGGBB string, falling back to white for blank/invalid.
    private static string NormalizeHex(string? hex)
        => TryParseHex(hex ?? "", out _, out _, out _) ? hex! : "#FFFFFF";

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ColorProperty) return;
        if (!_initialized) return; // controls not wired yet
        if (_committing) return;   // echo of our own CommitToColor — ignore

        // Popup closed: genuine external change. Popup open: ignore, sliders rule.
        SyncFromColor();
    }

    internal void Swatch_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_picker.IsOpen) { _picker.IsOpen = false; return; }
        // Ensure HSV/sliders match the current color before showing the popup.
        SeedFromHex(NormalizeHex(Color));
        _picker.IsOpen = true;
    }

    // Seeds HSV backing fields + slider controls from a hex string.
    // Called from OnPropertyChanged (on any external Color change while popup
    // is closed) and from Swatch_PointerPressed at open time.
    private void SeedFromHex(string hex)
    {
        if (!TryParseHex(hex, out var r, out var g, out var b)) return;
        HsvFromRgb(r, g, b, out _h, out _s, out _v);
        _suppress = true;
        try
        {
            _hSlider.Value = _h;
            _sSlider.Value = _s;
            _vSlider.Value = _v;
        }
        finally { _suppress = false; }
        RedrawSquare();
        PlaceDot();
        UpdatePreview();
    }

    // Writes the current HSV out to the bound Color + main swatch + text field.
    // Called only when the popup closes.
    private void CommitToColor()
    {
        var hex = HsvToHex(_h, _s, _v);
        _committing = true;
        try
        {
            Color = hex;
            _hexBox.Text = hex;
            PaintMainSwatch(hex);
        }
        finally { _committing = false; }
    }

    // ---- SV square interaction: drives S and V only, popup visuals only. ----
    internal void SvCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Pointer.Capture(_svCanvas);
        HandleSvPointer(e);
    }

    internal void SvCanvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.GetCurrentPoint(_svCanvas).Properties.IsLeftButtonPressed)
            HandleSvPointer(e);
    }

    private void HandleSvPointer(PointerEventArgs e)
    {
        var pt = e.GetPosition(_svCanvas);
        _s = Math.Clamp(pt.X / (SZ - 1) * 100.0, 0, 100);
        _v = Math.Clamp((1.0 - pt.Y / (SZ - 1)) * 100.0, 0, 100);
        _suppress = true;
        try { _sSlider.Value = _s; _vSlider.Value = _v; }
        finally { _suppress = false; }
        PlaceDot();
        UpdatePreview();
        CommitToColor();
    }

    // ---- Main hex field (popup closed only). ----
    internal void HexBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_picker.IsOpen) return;               // ignore while picking
        var txt = _hexBox.Text ?? "";
        if (TryParseHex(txt, out _, out _, out _))
            Color = txt;   // OnPropertyChanged handles swatch + SeedFromHex
    }

    internal void HexBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (!TryParseHex(_hexBox.Text ?? "", out _, out _, out _)) _hexBox.Text = Color;
    }

    private void PaintMainSwatch(string hex)
    {
        if (Avalonia.Media.Color.TryParse(hex, out var ac))
            _swatch.Background = new SolidColorBrush(ac);
    }

    // The bottom-of-popup preview swatch. Reflects live HSV while picking.
    private void UpdatePreview()
    {
        var hex = HsvToHex(_h, _s, _v);
        if (Avalonia.Media.Color.TryParse(hex, out var ac))
            _preview.Background = new SolidColorBrush(ac);
    }

    private void PlaceDot()
    {
        Canvas.SetLeft(_svDot, _s / 100.0 * (SZ - 1) - 5);
        Canvas.SetTop(_svDot, (1.0 - _v / 100.0) * (SZ - 1) - 5);
    }

    private void RedrawSquare()
    {
        var buf = new byte[SZ * SZ * 4];
        for (int y = 0; y < SZ; y++)
        {
            double v = 100.0 * (1.0 - (double)y / (SZ - 1));
            int row = y * SZ * 4;
            for (int x = 0; x < SZ; x++)
            {
                HsvToRgb(_h, 100.0 * x / (SZ - 1), v, out var pr, out var pg, out var pb);
                int i = row + x * 4;
                buf[i] = pb; buf[i + 1] = pg; buf[i + 2] = pr; buf[i + 3] = 255;
            }
        }
        var bmp = new WriteableBitmap(new PixelSize(SZ, SZ), new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
        using (var fb = bmp.Lock())
            Marshal.Copy(buf, 0, fb.Address, buf.Length);
        _svImage.Source = bmp;
    }

    private static bool TryParseHex(string hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrEmpty(hex)) return false;
        hex = hex.TrimStart('#');
        if (hex.Length == 3) hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        if (hex.Length != 6) return false;
        if (!byte.TryParse(hex[0..2], System.Globalization.NumberStyles.HexNumber, null, out r)) return false;
        if (!byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out g)) return false;
        if (!byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out b)) return false;
        return true;
    }

    private static string HsvToHex(double h, double s, double v)
    { HsvToRgb(h, s, v, out var r, out var g, out var b); return $"#{r:X2}{g:X2}{b:X2}"; }

    private static void HsvToRgb(double h, double s, double v, out byte r, out byte g, out byte b)
    {
        s /= 100.0; v /= 100.0;
        if (s <= 0) { var c = (byte)Math.Round(v * 255); r = g = b = c; return; }
        h = ((h % 360) + 360) % 360;
        double sec = h / 60.0; int i = (int)sec; double f = sec - i;
        double p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        double rd, gd, bd;
        switch (i)
        {
            case 0: rd = v; gd = t; bd = p; break;
            case 1: rd = q; gd = v; bd = p; break;
            case 2: rd = p; gd = v; bd = t; break;
            case 3: rd = p; gd = q; bd = v; break;
            case 4: rd = t; gd = p; bd = v; break;
            default: rd = v; gd = p; bd = q; break;
        }
        r = (byte)Math.Round(rd * 255); g = (byte)Math.Round(gd * 255); b = (byte)Math.Round(bd * 255);
    }

    private static void HsvFromRgb(byte r, byte g, byte b, out double h, out double s, out double v)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd)), min = Math.Min(rd, Math.Min(gd, bd)), delta = max - min;
        v = max * 100.0; s = max == 0 ? 0 : delta / max * 100.0;
        if (delta == 0) { h = 0; return; }
        if (max == rd) h = 60 * (((gd - bd) / delta) % 6);
        else if (max == gd) h = 60 * ((bd - rd) / delta + 2);
        else h = 60 * ((rd - gd) / delta + 4);
        if (h < 0) h += 360;
    }
}