using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SeKurabe.Controls;

namespace SeKurabe.Views;

public enum DialogKind { Info, Question, Warning }

/// <summary>
/// アイコン・見出し・本文・詳細リスト・ボタンを持つ確認ダイアログ。押したボタンの番号（buttons の添字）を返す。
/// buttons[0] を主ボタンとして強調する。macOS では主ボタンを右端に置く（OS の慣習に合わせる）。
/// </summary>
public sealed class ChoiceDialog : Window
{
    public ChoiceDialog(string title, string message, IReadOnlyList<string>? details, string[] buttons, DialogKind kind)
    {
        Title = title;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 680;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        this[!BackgroundProperty] = this.GetResourceObservable("Surface1").ToBinding();

        var root = new DockPanel { Margin = new Thickness(24, 22, 24, 20), LastChildFill = true };

        // ボタン（主ボタン = 0 番）
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 20, 0, 0),
        };
        var order = Enumerable.Range(0, buttons.Length).ToList();
        if (OperatingSystem.IsMacOS()) order.Reverse();
        foreach (var index in order)
        {
            var b = new Button
            {
                Content = buttons[index],
                MinWidth = 96,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                IsDefault = index == 0,
                IsCancel = buttons[index] is "キャンセル" or "閉じる",
            };
            b.Classes.Add(index == 0 ? "primary" : "secondary");
            b.Click += (_, _) => Close(index);
            buttonPanel.Children.Add(b);
        }
        DockPanel.SetDock(buttonPanel, Dock.Bottom);
        root.Children.Add(buttonPanel);

        // アイコン + 本文
        var (iconKind, fg, bg) = kind switch
        {
            DialogKind.Warning => ("alert", "Warning", "WarningSoft"),
            DialogKind.Question => ("help", "AccentText", "AccentSoft"),
            _ => ("info", "AccentText", "AccentSoft"),
        };
        var icon = new Icon { Kind = iconKind, Width = 20, Height = 20 };
        icon[!Avalonia.Controls.Documents.TextElement.ForegroundProperty] = this.GetResourceObservable(fg).ToBinding();
        var iconHolder = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(20),
            VerticalAlignment = VerticalAlignment.Top,
            Child = icon,
        };
        iconHolder[!Border.BackgroundProperty] = this.GetResourceObservable(bg).ToBinding();

        var body = new StackPanel { Spacing = 8 };
        var heading = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap };
        heading.Classes.Add("title");
        body.Children.Add(heading);
        var msg = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
        msg.Classes.Add("caption");
        msg.FontSize = 12.5;
        body.Children.Add(msg);

        if (details is { Count: > 0 })
        {
            var list = new SelectableTextBlock
            {
                Text = string.Join("\n", details),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                LineHeight = 19,
            };
            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10),
                Margin = new Thickness(0, 6, 0, 0),
                BorderThickness = new Thickness(1),
                Child = new ScrollViewer { MaxHeight = 240, Content = list },
            };
            card[!Border.BackgroundProperty] = this.GetResourceObservable("Surface0").ToBinding();
            card[!Border.BorderBrushProperty] = this.GetResourceObservable("Stroke").ToBinding();
            body.Children.Add(card);
        }

        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 16 };
        content.Children.Add(iconHolder);
        Grid.SetColumn(body, 1);
        content.Children.Add(body);
        root.Children.Add(content);
        Content = root;
    }
}
