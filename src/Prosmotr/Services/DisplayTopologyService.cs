using Prosmotr.Infrastructure;
using Prosmotr.Services.Abstractions;

namespace Prosmotr.Services;

/// <summary>Реализация управления топологией дисплеев через CCD API.</summary>
public sealed class DisplayTopologyService : IDisplayTopologyService
{
    private readonly INotificationService _notify;
    private DisplayConfigApi.DISPLAYCONFIG_PATH_INFO[]? _savedPaths;
    private DisplayConfigApi.DISPLAYCONFIG_MODE_INFO[]? _savedModes;

    public bool IsCloned { get; private set; }

    public bool CanClone => DisplayConfigApi.GetSystemMetrics(DisplayConfigApi.SM_CMONITORS) > 1;

    /// <summary>Можно ли переключить режим: выключить если clone активен, или включить если есть 2+ монитора.</summary>
    public bool CanToggle => IsCloned || CanClone;

    public event EventHandler? TopologyChanged;

    public DisplayTopologyService(INotificationService notify)
    {
        _notify = notify;
    }

    public void EnableClone()
    {
        AppLog.Write($"[Clone] EnableClone START: CanClone={CanClone}, IsCloned={IsCloned}, SM_CMONITORS={DisplayConfigApi.GetSystemMetrics(DisplayConfigApi.SM_CMONITORS)}");
        if (!CanClone) { AppLog.Write("[Clone] EnableClone: abort (!CanClone)"); return; }
        if (IsCloned) { AppLog.Write("[Clone] EnableClone: abort (already IsCloned)"); return; }

        try
        {
            SaveCurrentConfig();
            AppLog.Write($"[Clone] SaveCurrentConfig done: savedPaths={_savedPaths != null}, savedModes={_savedModes != null}");

            uint flags = DisplayConfigApi.SDC_TOPOLOGY_CLONE
                       | DisplayConfigApi.SDC_APPLY
                       | DisplayConfigApi.SDC_PATH_PERSIST_IF_REQUIRED
                       | DisplayConfigApi.SDC_VIRTUAL_MODE_AWARE;

            AppLog.Write($"[Clone] Calling SetDisplayConfig(CLONE) flags=0x{flags:X8}");
            int hr = DisplayConfigApi.SetDisplayConfig(0, null, 0, null, flags);
            AppLog.Write($"[Clone] SetDisplayConfig(CLONE) returned: 0x{hr:X8} ({hr})");

            if (hr != DisplayConfigApi.ERROR_SUCCESS)
            {
                AppLog.Write($"[Clone] SetDisplayConfig(CLONE) FAILED: 0x{hr:X8}");
                _notify.Show("Не удалось включить режим дублирования экранов", NotificationKind.Error);
                return;
            }

            IsCloned = true;
            AppLog.Write($"[Clone] IsCloned set to TRUE. CanToggle={CanToggle}");
            TopologyChanged?.Invoke(this, EventArgs.Empty);
            _notify.Show("Экраны дублируются", NotificationKind.Info);
        }
        catch (Exception ex)
        {
            AppLog.Error("[Clone] EnableClone exception", ex);
            _notify.Show("Не удалось переключить режим экранов", NotificationKind.Error);
        }
    }

    public void RestoreExtend()
    {
        AppLog.Write($"[Clone] RestoreExtend START: IsCloned={IsCloned}, savedPaths={_savedPaths != null}, savedModes={_savedModes != null}");
        if (!IsCloned) { AppLog.Write("[Clone] RestoreExtend: abort (!IsCloned)"); return; }

        try
        {
            int hr;
            bool restored = false;

            if (_savedPaths != null && _savedModes != null)
            {
                uint flags = DisplayConfigApi.SDC_APPLY
                           | DisplayConfigApi.SDC_USE_SUPPLIED_DISPLAY_CONFIG
                           | DisplayConfigApi.SDC_ALLOW_CHANGES
                           | DisplayConfigApi.SDC_VIRTUAL_MODE_AWARE;

                AppLog.Write($"[Clone] RestoreExtend: trying SDC_USE_SUPPLIED_DISPLAY_CONFIG flags=0x{flags:X8}, paths={_savedPaths.Length}, modes={_savedModes.Length}");
                hr = DisplayConfigApi.SetDisplayConfig((uint)_savedPaths.Length, _savedPaths,
                    (uint)_savedModes.Length, _savedModes, flags);
                AppLog.Write($"[Clone] RestoreExtend USE_SUPPLIED returned: 0x{hr:X8} ({hr})");

                if (hr == DisplayConfigApi.ERROR_SUCCESS)
                {
                    restored = true;
                    AppLog.Write("[Clone] RestoreExtend: USE_SUPPLIED succeeded");
                }
                else
                {
                    AppLog.Write($"[Clone] RestoreExtend: USE_SUPPLIED failed, will fallback to EXTEND topology");
                }
            }

            if (!restored)
            {
                uint flags = DisplayConfigApi.SDC_TOPOLOGY_EXTEND
                           | DisplayConfigApi.SDC_APPLY
                           | DisplayConfigApi.SDC_ALLOW_CHANGES
                           | DisplayConfigApi.SDC_PATH_PERSIST_IF_REQUIRED
                           | DisplayConfigApi.SDC_VIRTUAL_MODE_AWARE;

                AppLog.Write($"[Clone] RestoreExtend: fallback to SDC_TOPOLOGY_EXTEND flags=0x{flags:X8}");
                hr = DisplayConfigApi.SetDisplayConfig(0, null, 0, null, flags);
                AppLog.Write($"[Clone] RestoreExtend EXTEND returned: 0x{hr:X8} ({hr})");

                if (hr != DisplayConfigApi.ERROR_SUCCESS)
                {
                    AppLog.Write($"[Clone] RestoreExtend EXTEND FAILED: 0x{hr:X8}");
                    _notify.Show("Не удалось восстановить расширенный режим", NotificationKind.Error);
                    return;
                }
            }

            IsCloned = false;
            _savedPaths = null;
            _savedModes = null;
            AppLog.Write($"[Clone] IsCloned set to FALSE. CanToggle={CanToggle}");
            TopologyChanged?.Invoke(this, EventArgs.Empty);
            _notify.Show("Расширенный режим восстановлен", NotificationKind.Info);
        }
        catch (Exception ex)
        {
            AppLog.Error("[Clone] RestoreExtend exception", ex);
            _notify.Show("Не удалось восстановить режим экранов", NotificationKind.Error);
        }
    }

    public void ToggleClone()
    {
        AppLog.Write($"[Clone] ToggleClone: IsCloned={IsCloned}, CanClone={CanClone}, CanToggle={CanToggle}");
        if (IsCloned)
            RestoreExtend();
        else
            EnableClone();
    }

    public void OnDisplaySettingsChanged()
    {
        bool wasCloned = IsCloned;
        bool wasCanClone = CanClone;
        bool wasCanToggle = CanToggle;

        // НЕ сбрасываем IsCloned автоматически при !CanClone:
        // в clone-режиме GetSystemMetrics(SM_CMONITORS) часто возвращает 1 (один логический монитор),
        // поэтому сброс здесь сломал бы восстановление при shutdown.
        AppLog.Write($"[Clone] OnDisplaySettingsChanged: IsCloned={IsCloned}, CanClone={CanClone}(SM_CMONITORS={DisplayConfigApi.GetSystemMetrics(DisplayConfigApi.SM_CMONITORS)}), CanToggle={CanToggle}");

        if (wasCloned != IsCloned || wasCanClone != CanClone || wasCanToggle != CanToggle)
        {
            AppLog.Write($"[Clone] OnDisplaySettingsChanged: RAISING TopologyChanged (wasCloned={wasCloned},wasCanClone={wasCanClone},wasCanToggle={wasCanToggle})");
            TopologyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SaveCurrentConfig()
    {
        try
        {
            // QDC_ONLY_ACTIVE_PATHS возвращает текущую активную конфигурацию (extend).
            // В отличие от QDC_DATABASE_CURRENT, он НЕ требует currentTopologyId != NULL.
            uint flags = DisplayConfigApi.QDC_ONLY_ACTIVE_PATHS | DisplayConfigApi.QDC_VIRTUAL_MODE_AWARE;
            uint pathCount = 0, modeCount = 0;
            int hr = DisplayConfigApi.GetDisplayConfigBufferSizes(flags, out pathCount, out modeCount);
            AppLog.Write($"[Clone] SaveCurrentConfig BufferSizes: pathCount={pathCount}, modeCount={modeCount}, hr=0x{hr:X8} ({hr})");

            if (hr != DisplayConfigApi.ERROR_SUCCESS)
            {
                AppLog.Write($"[Clone] SaveCurrentConfig GetDisplayConfigBufferSizes FAILED: 0x{hr:X8}");
                _savedPaths = null;
                _savedModes = null;
                return;
            }

            // Цикл: между GetDisplayConfigBufferSizes и QueryDisplayConfig конфигурация
            // может измениться, поэтому размер буфера может оказаться недостаточным.
            const int maxRetries = 3;
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                var paths = new DisplayConfigApi.DISPLAYCONFIG_PATH_INFO[pathCount];
                var modes = new DisplayConfigApi.DISPLAYCONFIG_MODE_INFO[modeCount];
                uint pc = pathCount, mc = modeCount;
                hr = DisplayConfigApi.QueryDisplayConfig(flags, ref pc, paths, ref mc, modes, IntPtr.Zero);
                AppLog.Write($"[Clone] SaveCurrentConfig QueryDisplayConfig attempt={attempt}, returned=0x{hr:X8} ({hr}), pc={pc}, mc={mc}");

                if (hr == DisplayConfigApi.ERROR_INSUFFICIENT_BUFFER && attempt < maxRetries - 1)
                {
                    AppLog.Write("[Clone] SaveCurrentConfig: insufficient buffer, retrying…");
                    hr = DisplayConfigApi.GetDisplayConfigBufferSizes(flags, out pathCount, out modeCount);
                    if (hr != DisplayConfigApi.ERROR_SUCCESS)
                    {
                        AppLog.Write($"[Clone] SaveCurrentConfig GetDisplayConfigBufferSizes(retry) FAILED: 0x{hr:X8}");
                        break;
                    }
                    continue;
                }

                if (hr != DisplayConfigApi.ERROR_SUCCESS)
                {
                    AppLog.Write($"[Clone] SaveCurrentConfig QueryDisplayConfig FAILED: 0x{hr:X8}");
                    _savedPaths = null;
                    _savedModes = null;
                    return;
                }

                // Обрезаем до актуального количества записей (QueryDisplayConfig обновляет out-параметры).
                if (pc < paths.Length)
                {
                    AppLog.Write($"[Clone] SaveCurrentConfig resizing paths from {paths.Length} to {pc}");
                    Array.Resize(ref paths, (int)pc);
                }
                if (mc < modes.Length)
                {
                    AppLog.Write($"[Clone] SaveCurrentConfig resizing modes from {modes.Length} to {mc}");
                    Array.Resize(ref modes, (int)mc);
                }

                _savedPaths = paths;
                _savedModes = modes;
                AppLog.Write($"[Clone] SaveCurrentConfig SUCCESS: paths={_savedPaths.Length}, modes={_savedModes.Length}");
                return;
            }

            AppLog.Write("[Clone] SaveCurrentConfig: exceeded max retries");
            _savedPaths = null;
            _savedModes = null;
        }
        catch (Exception ex)
        {
            AppLog.Error("[Clone] SaveCurrentConfig exception", ex);
            _savedPaths = null;
            _savedModes = null;
        }
    }
}
