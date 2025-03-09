using WirelessADBManagerVSExtension.ViewModels;

namespace WirelessADBManagerVSExtension.Models;

public class DeviceInfo : BaseNotify
{
    private string _model;
    public string Model
    {
        get => _model;
        set
        {
            _model = value;
            OnPropertyChanged();
        }
    }

    private string _ip;
    public string Ip
    {
        get => _ip;
        set
        {
            _ip = value;
            OnPropertyChanged();
        }
    }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            _isConnected = value;
            OnPropertyChanged();
        }
    }

    private bool _isPaired;
    public bool IsPaired
    {
        get => _isPaired;
        set
        {
            _isPaired = value;
            OnPropertyChanged();
        }
    }

    private DeviceStates _state;
    public DeviceStates State
    {
        get => _state;
        set
        {
            _state = value;
            OnPropertyChanged();
        }
    }
}