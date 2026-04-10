using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using ReSilico.UI.ViewModels;
using System;

namespace ReSilico.UI.Views;

public partial class CorrelationMatrixView : UserControl
{
    public CorrelationMatrixView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is CorrelationMatrixViewModel vm)
            RebuildMatrix(vm);
    }

    private void RebuildMatrix(CorrelationMatrixViewModel vm)
    {
        var grid = this.FindControl<Grid>("PART_MatrixGrid")!;
        grid.Children.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();

        int n = vm.DemandNames.Count;
        if (n == 0) return;

        const double headerWidth = 80;
        const double cellWidth = 110;
        const double cellHeight = 44;

        // Column definitions: label col + n data cols
        grid.ColumnDefinitions.Add(new ColumnDefinition(headerWidth, GridUnitType.Pixel));
        for (int c = 0; c < n; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(cellWidth, GridUnitType.Pixel));

        // Row definitions: header row + n data rows
        grid.RowDefinitions.Add(new RowDefinition(cellHeight, GridUnitType.Pixel));
        for (int r = 0; r < n; r++)
            grid.RowDefinitions.Add(new RowDefinition(cellHeight, GridUnitType.Pixel));

        // Column headers (row 0, cols 1..n)
        for (int c = 0; c < n; c++)
        {
            var tb = MakeHeader(vm.DemandNames[c]);
            Grid.SetRow(tb, 0);
            Grid.SetColumn(tb, c + 1);
            grid.Children.Add(tb);
        }

        // Data rows
        for (int r = 0; r < n; r++)
        {
            // Row label (col 0)
            var lbl = MakeHeader(vm.DemandNames[r]);
            lbl.HorizontalAlignment = HorizontalAlignment.Right;
            lbl.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetRow(lbl, r + 1);
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            // Cells (cols 1..n)
            for (int c = 0; c < n; c++)
            {
                var cellVm = vm.Rows[r][c];
                bool isDiag = r == c;

                var spinner = new NumericUpDown
                {
                    Minimum = -1,
                    Maximum = 1,
                    Increment = 0.05M,
                    FormatString = "N2",
                    IsReadOnly = isDiag,
                    Width = cellWidth - 4,
                    Margin = new Thickness(2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = isDiag
                        ? new SolidColorBrush(Color.Parse("#313244"))
                        : new SolidColorBrush(Color.Parse("#45475A")),
                    Foreground = isDiag
                        ? new SolidColorBrush(Color.Parse("#A6ADC8"))
                        : new SolidColorBrush(Color.Parse("#CDD6F4")),
                };

                // Two-way bind to the cell ViewModel's Value property
                spinner.Bind(
                    NumericUpDown.ValueProperty,
                    new Binding(nameof(CorrelationCellViewModel.Value))
                    {
                        Source = cellVm,
                        Mode = BindingMode.TwoWay,
                        Converter = new DoubleToDecimalConverter()
                    });

                Grid.SetRow(spinner, r + 1);
                Grid.SetColumn(spinner, c + 1);
                grid.Children.Add(spinner);
            }
        }
    }

    private static TextBlock MakeHeader(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.Parse("#89B4FA")),
        FontSize = 12,
        FontWeight = FontWeight.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = Avalonia.Media.TextAlignment.Center,
    };
}

/// <summary>
/// NumericUpDown uses decimal; our ViewModel uses double.
/// This converter bridges the gap without changing the VM.
/// </summary>
internal sealed class DoubleToDecimalConverter : Avalonia.Data.Converters.IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is double d ? (decimal?)((decimal)d) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is decimal dec ? (double)dec : (object?)null;
}
