using LibVLCSharp.Shared;

namespace Prosmotr.Services;

/// <summary>
/// Ленивый singleton-провайдер LibVLC. Вызывает Core.Initialize() один раз
/// (находит нативные libvlc в подпапке libvlc\win-x64 рядом с приложением).
/// </summary>
public sealed class LibVlcProvider : IDisposable
{
    private readonly object _gate = new();
    private LibVLC? _libVlc;
    private bool _coreInitialized;

    public LibVLC LibVlc
    {
        get
        {
            lock (_gate)
            {
                if (_libVlc == null)
                {
                    EnsureCoreInitialized();
                    _libVlc = new LibVLC(
                        "--no-video-title-show", // не показывать название поверх видео
                        "--quiet");
                }
                return _libVlc;
            }
        }
    }

    private void EnsureCoreInitialized()
    {
        if (_coreInitialized) return;
        Core.Initialize();
        _coreInitialized = true;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _libVlc?.Dispose();
            _libVlc = null;
        }
    }
}
