using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace ReSilico.UI.ViewModels;

public partial class CorrelationCellViewModel : ObservableObject
{
    public int Row { get; }
    public int Col { get; }
    public bool IsReadOnly => Row == Col;

    [ObservableProperty]
    private double _value;

    public CorrelationCellViewModel(int row, int col, double value)
    {
        Row = row;
        Col = col;
        _value = value;
    }
}

public partial class CorrelationMatrixViewModel : ViewModelBase
{
    private int _dimension = 4;
    private static readonly string[] _defaultNames = ["PID-1", "PID-2", "PID-3", "PFA-1"];

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    public ObservableCollection<string> DemandNames { get; } = [];
    public ObservableCollection<ObservableCollection<CorrelationCellViewModel>> Rows { get; } = [];

    public CorrelationMatrixViewModel()
    {
        Title = "Correlation Matrix";
        BuildMatrix(_dimension);
    }

    private void BuildMatrix(int n)
    {
        DemandNames.Clear();
        Rows.Clear();

        foreach (var name in _defaultNames.Take(n))
            DemandNames.Add(name);
        while (DemandNames.Count < n)
            DemandNames.Add($"EDP-{DemandNames.Count + 1}");

        for (int r = 0; r < n; r++)
        {
            var row = new ObservableCollection<CorrelationCellViewModel>();
            for (int c = 0; c < n; c++)
            {
                double val = r == c ? 1.0 : 0.5;
                var cell = new CorrelationCellViewModel(r, c, val);
                cell.PropertyChanged += OnCellChanged;
                row.Add(cell);
            }
            Rows.Add(row);
        }
    }

    private void OnCellChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not CorrelationCellViewModel cell || e.PropertyName != nameof(CorrelationCellViewModel.Value)) return;
        if (cell.Row == cell.Col) return;

        // Enforce symmetry
        Rows[cell.Col][cell.Row].PropertyChanged -= OnCellChanged;
        Rows[cell.Col][cell.Row].Value = cell.Value;
        Rows[cell.Col][cell.Row].PropertyChanged += OnCellChanged;

        Validate();
    }

    [RelayCommand]
    private void FixMatrix()
    {
        var m = ExtractMatrix();
        var spd = NearestPositiveDefinite(m, _dimension);
        ApplyMatrix(spd);
        ValidationMessage = string.Empty;
    }

    /// <summary>Higham 1988 nearest positive-definite approximation via symmetric averaging.</summary>
    private static double[,] NearestPositiveDefinite(double[,] A, int n)
    {
        // Symmetrize
        var B = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                B[i, j] = (A[i, j] + A[j, i]) / 2.0;

        // Set diagonal to 1
        for (int i = 0; i < n; i++) B[i, i] = 1.0;

        // Iterative Higham: clamp off-diagonal until PD
        const double epsilon = 1e-8;
        for (int iter = 0; iter < 100; iter++)
        {
            if (IsPositiveDefinite(B, n)) break;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    if (i != j)
                        B[i, j] = Math.Max(-1 + epsilon, Math.Min(1 - epsilon, B[i, j] * 0.99));
            for (int i = 0; i < n; i++) B[i, i] = 1.0;
        }
        return B;
    }

    private void Validate()
    {
        var m = ExtractMatrix();
        bool isSpd = IsPositiveDefinite(m, _dimension);
        ValidationMessage = isSpd ? string.Empty : "Matrix is not positive definite. Click \"Fix Matrix\" to correct it.";
    }

    public double[,] ExtractMatrix()
    {
        int n = _dimension;
        var m = new double[n, n];
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
                m[r, c] = Rows[r][c].Value;
        return m;
    }

    private void ApplyMatrix(double[,] m)
    {
        int n = _dimension;
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                Rows[r][c].PropertyChanged -= OnCellChanged;
                Rows[r][c].Value = Math.Round(m[r, c], 4);
                Rows[r][c].PropertyChanged += OnCellChanged;
            }
        }
    }

    private static bool IsPositiveDefinite(double[,] m, int n)
    {
        // Simple Cholesky attempt
        var L = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double sum = m[i, j];
                for (int k = 0; k < j; k++) sum -= L[i, k] * L[j, k];
                if (i == j)
                {
                    if (sum <= 0) return false;
                    L[i, j] = Math.Sqrt(sum);
                }
                else
                {
                    L[i, j] = sum / L[j, j];
                }
            }
        }
        return true;
    }
}
