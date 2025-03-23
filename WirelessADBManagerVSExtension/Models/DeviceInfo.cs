using Microsoft.VisualStudio.Extensibility.UI;
using System.ComponentModel;
using WirelessADBManagerVSExtension.ViewModels;

namespace WirelessADBManagerVSExtension.Models;

public class DeviceInfo : NotifyPropertyChangedObject
{
    private string _model;
    public string Model
    {
        get => _model;
        set => SetProperty(ref _model, value);
    }

    private string _ip;
    public string Ip
    {
        get => _ip;
        set => SetProperty(ref _ip, value);
    }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set => SetProperty(ref _isConnected, value);
    }

    private bool _isPaired;
    public bool IsPaired
    {
        get => _isPaired;
        set => SetProperty(ref _isPaired, value);
    }

    private DeviceStates _state;
    public DeviceStates State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }
}