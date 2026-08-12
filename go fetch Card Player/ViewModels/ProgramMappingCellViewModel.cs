using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CardPlayer.Config;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CardPlayer.ViewModels;

public partial class ProgramMappingCellViewModel : ViewModelBase
{
    private readonly PerProfileMappings _profileMappings;
    private readonly bool               _isShift;
    private bool                        _initialized;

    public int    Row         { get; }
    public int    Col         { get; }
    public string RemoteLabel { get; }
    public bool   IsEmpty     => string.IsNullOrWhiteSpace(RemoteLabel);

    [ObservableProperty] private bool _isShiftKey;
    [ObservableProperty] private bool _isSelectingShift;

    [ObservableProperty]
    private ObservableCollection<ProgramFunctionViewModel> _availableFunctions = new();

    [ObservableProperty]
    private ProgramFunctionViewModel? _selectedFunction;

    // Fired only on actual user edits, not initialization or refresh
    public event Action? DataChanged;

    private List<CellMapping> MappingList =>
        _isShift ? _profileMappings.ShiftMappings : _profileMappings.Mappings;

    partial void OnSelectedFunctionChanged(ProgramFunctionViewModel? oldValue,
                                           ProgramFunctionViewModel? newValue)
    {
        if (oldValue != null) oldValue.PropertyChanged -= OnFunctionNameChanged;
        if (newValue != null) newValue.PropertyChanged += OnFunctionNameChanged;

        SyncMapping(newValue);

        if (_initialized) DataChanged?.Invoke();
    }

    private void OnFunctionNameChanged(object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProgramFunctionViewModel.Name))
            SyncMapping(SelectedFunction);
    }

    private void SyncMapping(ProgramFunctionViewModel? fn)
    {
        var existing = MappingList.FirstOrDefault(m => m.Row == Row && m.Col == Col);
        if (fn == null || string.IsNullOrEmpty(fn.Name))
        {
            if (existing != null) MappingList.Remove(existing);
        }
        else
        {
            if (existing != null) existing.FunctionName = fn.Name;
            else MappingList.Add(new CellMapping { Row = Row, Col = Col, FunctionName = fn.Name });
        }
    }

    /// <summary>
    /// Called before the VM is discarded during a grid rebuild.
    /// Prevents the ComboBox detach from writing null back into the mapping list.
    /// </summary>
    public void Dispose()
    {
        _initialized = false;
        if (_selectedFunction != null)
            _selectedFunction.PropertyChanged -= OnFunctionNameChanged;
    }

    public ProgramMappingCellViewModel(int row, int col, string remoteLabel,
        PerProfileMappings profileMappings,
        ObservableCollection<ProgramFunctionViewModel> functions,
        bool isShift = false)
    {
        Row              = row;
        Col              = col;
        RemoteLabel      = remoteLabel;
        _profileMappings = profileMappings;
        _isShift         = isShift;

        BuildFunctions(functions);

        var saved = MappingList.FirstOrDefault(m => m.Row == row && m.Col == col);
        if (saved != null)
        {
            var match = AvailableFunctions.FirstOrDefault(f => f.Name == saved.FunctionName);
            if (match != null)
            {
                _selectedFunction = match;
                match.PropertyChanged += OnFunctionNameChanged;
            }
        }

        _initialized = true;
    }

    public void RefreshFunctions(ObservableCollection<ProgramFunctionViewModel> functions)
    {
        bool wasInit = _initialized;
        _initialized = false;

        string? currentName = SelectedFunction?.Name;
        BuildFunctions(functions);

        var match = AvailableFunctions.FirstOrDefault(f => f.Name == currentName);
        SelectedFunction = match;

        _initialized = wasInit;
    }

    private void BuildFunctions(ObservableCollection<ProgramFunctionViewModel> functions)
    {
        AvailableFunctions = new ObservableCollection<ProgramFunctionViewModel>(
            new[] { new ProgramFunctionViewModel(new ProgramFunction { Name = "", KeySend = "" }) }
            .Concat(functions));
    }
}
