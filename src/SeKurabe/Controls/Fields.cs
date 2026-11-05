using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using SkiaSharp;

namespace SeKurabe.Controls;

/// <summary>「見出し ─ スライダー ─ 数値」の 1 行。</summary>
public sealed class SliderField : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<SliderField, string>(nameof(Label), "");
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<SliderField, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<SliderField, double>(nameof(Minimum), 0);
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<SliderField, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<string> SuffixProperty =
        AvaloniaProperty.Register<SliderField, string>(nameof(Suffix), "");

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public string Suffix { get => GetValue(SuffixProperty); set => SetValue(SuffixProperty, value); }

    readonly TextBlock _value = new() { VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };

    public SliderField()
    {
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        label.Classes.Add("field-label");
        label.Bind(TextBlock.TextProperty, this.GetObservable(LabelProperty));

        var slider = new Slider { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -8) };
        slider.Bind(RangeBase.MinimumProperty, this.GetObservable(MinimumProperty));
        slider.Bind(RangeBase.MaximumProperty, this.GetObservable(MaximumProperty));
        slider.Bind(RangeBase.ValueProperty, new Binding(nameof(Value)) { Source = this, Mode = BindingMode.TwoWay });

        _value.Classes.Add("caption");
        _value.Classes.Add("num");

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("84,*,46"), ColumnSpacing = 8, MinHeight = 30 };
        grid.Children.Add(label);
        Grid.SetColumn(slider, 1);
        grid.Children.Add(slider);
        Grid.SetColumn(_value, 2);
        grid.Children.Add(_value);
        Content = grid;
        UpdateText();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == SuffixProperty) UpdateText();
    }

    void UpdateText() => _value.Text = $"{Math.Round(Value):0}{Suffix}";
}

/// <summary>「見出し ─ 色」の 1 行。色は "#RRGGBB" / "#AARRGGBB" の文字列で受け渡す。</summary>
public sealed class ColorField : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<ColorField, string>(nameof(Label), "");
    public static readonly StyledProperty<string?> HexProperty =
        AvaloniaProperty.Register<ColorField, string?>(nameof(Hex), defaultBindingMode: BindingMode.TwoWay);

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Hex { get => GetValue(HexProperty); set => SetValue(HexProperty, value); }

    readonly ColorPicker _picker = new() { IsAlphaEnabled = true, HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBox _text = new() { MinWidth = 0, PlaceholderText = "自動" };
    bool _sync;

    public ColorField()
    {
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        label.Classes.Add("field-label");
        label.Bind(TextBlock.TextProperty, this.GetObservable(LabelProperty));

        _picker.ColorChanged += (_, e) =>
        {
            if (_sync) return;
            var c = e.NewColor;
            SetHex(c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}");
        };
        _text.LostFocus += (_, _) => CommitText();
        _text.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) CommitText(); };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("84,Auto,*"), ColumnSpacing = 8, MinHeight = 30 };
        grid.Children.Add(label);
        Grid.SetColumn(_picker, 1);
        grid.Children.Add(_picker);
        Grid.SetColumn(_text, 2);
        grid.Children.Add(_text);
        Content = grid;
    }

    void CommitText()
    {
        var t = _text.Text?.Trim() ?? "";
        if (t.Length == 0) { SetHex(""); return; }   // 空欄 = 自動（項目によって意味が変わる）
        if (!t.StartsWith('#')) t = "#" + t;
        if (SKColor.TryParse(t, out _)) SetHex(t.ToUpperInvariant());
        else _text.Text = Hex;
    }

    void SetHex(string hex)
    {
        Hex = hex;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != HexProperty) return;
        _sync = true;
        try
        {
            _text.Text = Hex;
            if (SKColor.TryParse(Hex ?? "", out var c))
                _picker.Color = Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue);
        }
        finally { _sync = false; }
    }
}

/// <summary>
/// 選択肢を並べたボタン群（1 つだけ選べる）。Options は "値=表示名,値=表示名" の形で書く。
/// Columns を指定すると等幅の段組み（区切りボタン）、0 なら折り返すチップ。
/// </summary>
public sealed class ChoiceField : UserControl
{
    public static readonly StyledProperty<string> OptionsProperty =
        AvaloniaProperty.Register<ChoiceField, string>(nameof(Options), "");
    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<ChoiceField, string?>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> ColumnsProperty =
        AvaloniaProperty.Register<ChoiceField, int>(nameof(Columns), 0);

    public string Options { get => GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }
    public string? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int Columns { get => GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }

    readonly List<(string Value, Button Button)> _buttons = new();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == OptionsProperty || change.Property == ColumnsProperty) Build();
        else if (change.Property == ValueProperty) UpdateSelection();
    }

    void Build()
    {
        _buttons.Clear();
        var items = Options.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(o => o.Split('=', 2)).Select(p => (Value: p[0], Text: p.Length > 1 ? p[1] : p[0])).ToList();
        Panel panel = Columns > 0
            ? new UniformGrid { Columns = Columns }
            : new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        foreach (var (value, text) in items)
        {
            var b = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            b.Classes.Add(Columns > 0 ? "seg" : "pick");
            b.Click += (_, _) => Value = value;
            _buttons.Add((value, b));
            panel.Children.Add(b);
        }
        if (Columns > 0)
        {
            var border = new Border { Child = panel };
            border.Classes.Add("segmented");
            Content = border;
        }
        else Content = panel;
        UpdateSelection();
    }

    void UpdateSelection()
    {
        foreach (var (value, b) in _buttons)
            b.Classes.Set("on", value == Value);
    }
}
