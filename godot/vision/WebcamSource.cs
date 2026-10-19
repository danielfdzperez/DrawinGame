using OpenCvSharp;

namespace DrawingGame.Vision;

/// <summary>Webcam del PC (DirectShow). Se abre en la primera lectura, es decir, en el hilo de la cámara.</summary>
public sealed class WebcamSource : ICameraSource
{
    private readonly int _index;
    private VideoCapture? _capture;
    private volatile string? _error;

    public WebcamSource(int index) => _index = index;

    public bool Connected => _capture != null && _error == null;
    public string? Error => _error;

    public bool TryGetFrame(Mat destination, int timeoutMs)
    {
        if (_error != null) return false;

        if (_capture == null)
        {
            _capture = new VideoCapture(_index, VideoCaptureAPIs.DSHOW);
            if (!_capture.IsOpened())
            {
                _error = $"No se pudo abrir la cámara {_index}.";
                return false;
            }
        }

        if (!_capture.Read(destination) || destination.Empty())
        {
            Thread.Sleep(10);
            return false;
        }
        return true;
    }

    public void Dispose()
    {
        _capture?.Dispose();
        _capture = null;
    }
}
