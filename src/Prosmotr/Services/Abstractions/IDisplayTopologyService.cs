namespace Prosmotr.Services.Abstractions;

/// <summary>Управление топологией дисплеев Windows (clone / extend).</summary>
public interface IDisplayTopologyService
{
    bool IsCloned { get; }
    bool CanClone { get; }
    bool CanToggle { get; }
    void EnableClone();
    void RestoreExtend();
    void ToggleClone();
    void OnDisplaySettingsChanged();
    event EventHandler? TopologyChanged;
}
