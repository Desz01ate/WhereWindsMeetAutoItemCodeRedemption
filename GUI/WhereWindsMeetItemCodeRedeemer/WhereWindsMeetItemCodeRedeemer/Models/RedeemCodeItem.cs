using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WhereWindsMeetItemCodeRedeemer.Models;

public enum CodeStatus
{
    Pending,
    Redeemed,
    Processing,
    Success,
    Failed,
    Skipped
}

public class RedeemCodeItem : INotifyPropertyChanged
{
    private string _code = string.Empty;
    private string _source = string.Empty;
    private CodeStatus _status = CodeStatus.Pending;
    private bool _isSelected = true;
    private string? _statusMessage;

    public string Code
    {
        get => _code;
        set => SetField(ref _code, value.Trim().ToUpperInvariant());
    }

    public string Source
    {
        get => _source;
        set => SetField(ref _source, value);
    }

    public CodeStatus Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsPending));
                OnPropertyChanged(nameof(IsRedeemed));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public bool IsPending => Status == CodeStatus.Pending;
    public bool IsRedeemed => Status == CodeStatus.Redeemed || Status == CodeStatus.Success;

    public string StatusText => Status switch
    {
        CodeStatus.Pending => "Pending",
        CodeStatus.Redeemed => "Already Redeemed",
        CodeStatus.Processing => "Redeeming...",
        CodeStatus.Success => "Success",
        CodeStatus.Failed => "Failed",
        CodeStatus.Skipped => "Skipped",
        _ => Status.ToString()
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
