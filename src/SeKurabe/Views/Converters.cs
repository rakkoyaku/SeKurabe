using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace SeKurabe.Views;

/// <summary>int ⇔ decimal?（NumericUpDown 用）。</summary>
public sealed class IntDecimalConverter : IValueConverter
{
    public static readonly IntDecimalConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i ? (decimal?)i : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is decimal d ? (int)Math.Round(d) : BindingOperations.DoNothing;
}

/// <summary>値の文字列表現が ConverterParameter と等しいか（セグメントの選択表示用）。</summary>
public sealed class EqualsConverter : IValueConverter
{
    public static readonly EqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        BindingOperations.DoNothing;
}

/// <summary>渡された値がすべて等しいとき true（選択中の項目に印を付けるのに使う）。</summary>
public sealed class AllEqualConverter : Avalonia.Data.Converters.IMultiValueConverter
{
    public static readonly AllEqualConverter Instance = new();
    public object? Convert(IList<object?> values, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        values.Count > 1 && values.All(v => Equals(v, values[0]));
}

/// <summary>フォント名 → FontFamily（「（標準）」などの不明な名前は標準のフォント）。</summary>
public sealed class FontFamilyNameConverter : IValueConverter
{
    public static readonly FontFamilyNameConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string name && !name.StartsWith('（') ? new Avalonia.Media.FontFamily(name) : Avalonia.Media.FontFamily.Default;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}

/// <summary>double ⇔ decimal?（NumericUpDown 用）。</summary>
public sealed class DoubleDecimalConverter : IValueConverter
{
    public static readonly DoubleDecimalConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? (decimal?)Math.Round((decimal)d, 1) : null;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is decimal m ? (double)m : BindingOperations.DoNothing;
}

/// <summary>"#RRGGBB" → ブラシ。</summary>
public sealed class HexBrushConverter : IValueConverter
{
    public static readonly HexBrushConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && Avalonia.Media.Color.TryParse(s, out var c) ? new Avalonia.Media.SolidColorBrush(c) : Avalonia.Media.Brushes.Gray;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}
