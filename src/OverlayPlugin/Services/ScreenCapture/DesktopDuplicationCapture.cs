using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Playnite.SDK;
using Vanara.PInvoke;
using static Vanara.PInvoke.DXGI;
using static Vanara.PInvoke.D3D11;

namespace PlayniteOverlay.Services;

/// <summary>
/// Screen capture implementation using Desktop Duplication API.
/// Provides high-performance capture for windowed and borderless fullscreen modes.
/// </summary>
/// <remarks>
/// Desktop Duplication is only available on Windows 8 and later.
/// Exclusive fullscreen applications cannot be captured with this method.
/// </remarks>
public sealed class DesktopDuplicationCapture : ICapture
{
    private static readonly ILogger logger = LogManager.GetLogger();

    private IDXGIFactory1? _factory;
    private IDXGIAdapter1? _adapter;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGIOutput? _output;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _stagingTexture;

    private int _outputWidth;
    private int _outputHeight;
    private IntPtr _targetMonitorHandle;
    private bool _isInitialized;
    private bool _disposed;

    private static readonly Version Windows8Version = new(6, 2, 9200, 0);

    /// <inheritdoc />
    public bool IsSupported => Environment.OSVersion.Version >= Windows8Version;

    /// <inheritdoc />
    public string? LastError { get; private set; }

    /// <inheritdoc />
    public void Initialize(IntPtr monitorHandle)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DesktopDuplicationCapture));
        }

        if (!IsSupported)
        {
            LastError = "Desktop Duplication is not supported on this version of Windows. Requires Windows 8 or later.";
            logger.Warn(LastError);
            return;
        }

        _targetMonitorHandle = monitorHandle;

        try
        {
            CleanupResources();
            InitializeDevice();
            InitializeOutput();
            InitializeDuplication();
            _isInitialized = true;
            LastError = null;
            logger.Info($"Desktop Duplication capture initialized successfully for monitor {monitorHandle}");
        }
        catch (COMException ex)
        {
            HandleInitializationError(ex);
        }
        catch (Exception ex)
        {
            LastError = $"Failed to initialize Desktop Duplication: {ex.Message}";
            logger.Error(ex, LastError);
            _isInitialized = false;
        }
    }

    /// <inheritdoc />
    public Bitmap? CaptureFrame()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DesktopDuplicationCapture));
        }

        if (!_isInitialized || _duplication == null || _device == null || _context == null || _stagingTexture == null)
        {
            LastError ??= "Capture not initialized. Call Initialize() first.";
            return null;
        }

        try
        {
            var deviceReason = _device.GetDeviceRemovedReason();
            if (deviceReason.Failed)
            {
                logger.Warn("Device removed detected, attempting reinitialization");
                HandleDeviceRemoved();
                return null;
            }

            var result = _duplication.AcquireNextFrame(100, out var frameInfo, out var desktopResource);

            if (result == HRESULT.DXGI_ERROR_WAIT_TIMEOUT)
            {
                return null;
            }

            if (result == HRESULT.DXGI_ERROR_ACCESS_LOST)
            {
                logger.Warn("Access lost to desktop duplication, session may have changed");
                LastError = "Desktop access lost. The session may have changed or another application has taken control.";
                return null;
            }

            if (result.Failed)
            {
                logger.Error($"AcquireNextFrame failed with HRESULT: 0x{(int)result:X}");
                LastError = $"Failed to acquire frame: 0x{(int)result:X}";
                return null;
            }

            if (desktopResource == null || frameInfo.LastPresentTime == 0)
            {
                if (desktopResource != null)
                    Marshal.ReleaseComObject(desktopResource);
                _duplication.ReleaseFrame();
                return null;
            }

            Bitmap? bitmap = null;
            try
            {
                var iidTexture2D = typeof(ID3D11Texture2D).GUID;
                var ptr = IntPtr.Zero;
                var qiResult = Marshal.QueryInterface(Marshal.GetIUnknownForObject(desktopResource), ref iidTexture2D, out ptr);
                if (qiResult != 0 || ptr == IntPtr.Zero)
                {
                    LastError = "Failed to query texture interface from desktop resource";
                    Marshal.ReleaseComObject(desktopResource);
                    _duplication.ReleaseFrame();
                    return null;
                }

                var sourceTexture = Marshal.GetObjectForIUnknown(ptr) as ID3D11Resource;
                Marshal.Release(ptr);

                if (sourceTexture == null)
                {
                    LastError = "Failed to get source texture";
                    Marshal.ReleaseComObject(desktopResource);
                    _duplication.ReleaseFrame();
                    return null;
                }

                var stagingResource = _stagingTexture as ID3D11Resource;
                if (stagingResource == null)
                {
                    LastError = "Failed to get staging texture resource";
                    Marshal.ReleaseComObject(desktopResource);
                    _duplication.ReleaseFrame();
                    return null;
                }

                _context.CopyResource(sourceTexture, stagingResource);

                var mapResult = _context.Map(stagingResource, 0, D3D11_MAP.D3D11_MAP_READ, 0, out var mapped);
                if (mapResult.Failed || mapped.pData == IntPtr.Zero)
                {
                    LastError = "Failed to map staging texture";
                    _context.Unmap(stagingResource, 0);
                    Marshal.ReleaseComObject(desktopResource);
                    _duplication.ReleaseFrame();
                    return null;
                }

                try
                {
                    bitmap = CreateBitmapFromMappedResource(mapped);
                }
                finally
                {
                    _context.Unmap(stagingResource, 0);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(desktopResource);
                _duplication.ReleaseFrame();
            }

            return bitmap;
        }
        catch (COMException ex) when ((HRESULT)ex.ErrorCode == HRESULT.DXGI_ERROR_DEVICE_REMOVED)
        {
            logger.Warn("Device removed during capture, attempting reinitialization");
            HandleDeviceRemoved();
            return null;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error during frame capture");
            LastError = $"Capture error: {ex.Message}";
            return null;
        }
    }

    private void InitializeDevice()
    {
        var iidFactory1 = typeof(IDXGIFactory1).GUID;
        var result = DXGI.CreateDXGIFactory1(in iidFactory1, out var factoryObj);
        if (result.Failed)
        {
            throw new COMException("Failed to create DXGI factory", (int)result);
        }
        _factory = factoryObj as IDXGIFactory1;

        result = _factory!.EnumAdapters1(0, out var adapter);
        if (result.Failed)
        {
            throw new COMException("Failed to enumerate graphics adapter", (int)result);
        }
        _adapter = adapter;

        var featureLevels = new[]
        {
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1,
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0,
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_1,
            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_0
        };

        result = D3D11.D3D11CreateDevice(
            _adapter,
            D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_UNKNOWN,
            default,
            D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            featureLevels,
            (uint)featureLevels.Length,
            D3D11.D3D11_SDK_VERSION,
            out _device,
            out var featureLevel,
            out _context);

        if (result.Failed)
        {
            throw new COMException($"Failed to create D3D11 device (result: 0x{(int)result:X8})", (int)result);
        }

        logger.Debug($"D3D11 device created with feature level: {featureLevel}");
    }

    private void InitializeOutput()
    {
        if (_adapter == null)
        {
            throw new InvalidOperationException("Adapter not initialized");
        }

        uint outputIndex = 0;
        HRESULT result;

        while (true)
        {
            result = _adapter.EnumOutputs(outputIndex, out var output);
            if (result == HRESULT.DXGI_ERROR_NOT_FOUND)
            {
                if (outputIndex == 0)
                {
                    throw new COMException("No display outputs found", (int)result);
                }
                outputIndex = 0;
                continue;
            }

            if (result.Failed)
            {
                throw new COMException($"Failed to enumerate output {outputIndex}", (int)result);
            }

            _output = output;

            var desc = _output.GetDesc();
            _outputWidth = desc.DesktopCoordinates.right - desc.DesktopCoordinates.left;
            _outputHeight = desc.DesktopCoordinates.bottom - desc.DesktopCoordinates.top;

            logger.Debug($"Using output {outputIndex}: {_outputWidth}x{_outputHeight}");
            break;
        }
    }

    private void InitializeDuplication()
    {
        if (_output == null || _device == null)
        {
            throw new InvalidOperationException("Output or device not initialized");
        }

        var output1 = _output as IDXGIOutput1;
        if (output1 == null)
        {
            throw new COMException("Failed to cast to IDXGIOutput1", (int)HRESULT.E_NOINTERFACE);
        }

        var result = output1.DuplicateOutput(_device, out _duplication);
        if (result == HRESULT.DXGI_ERROR_UNSUPPORTED)
        {
            LastError = "Desktop Duplication is not supported in the current display mode. This may occur with exclusive fullscreen applications.";
            logger.Warn(LastError);
            throw new COMException(LastError, (int)result);
        }

        if (result == HRESULT.E_ACCESSDENIED)
        {
            LastError = "Access denied. Another application may be using Desktop Duplication.";
            logger.Warn(LastError);
            throw new COMException(LastError, (int)result);
        }

        if (result.Failed)
        {
            throw new COMException($"Failed to duplicate output (result: 0x{(int)result:X8})", (int)result);
        }

        CreateStagingTexture();
    }

    private void CreateStagingTexture()
    {
        if (_device == null)
        {
            throw new InvalidOperationException("Device not initialized");
        }

        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)_outputWidth,
            Height = (uint)_outputHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
            Usage = D3D11_USAGE.D3D11_USAGE_STAGING,
            BindFlags = 0,
            CPUAccessFlags = D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ,
            MiscFlags = 0
        };

        var result = _device.CreateTexture2D(in desc, null, out _stagingTexture);
        if (result.Failed || _stagingTexture == null)
        {
            throw new InvalidOperationException("Failed to create staging texture");
        }

        logger.Debug($"Staging texture created: {_outputWidth}x{_outputHeight}");
    }

    private Bitmap CreateBitmapFromMappedResource(D3D11_MAPPED_SUBRESOURCE mapped)
    {
        var bitmap = new Bitmap(_outputWidth, _outputHeight, PixelFormat.Format32bppArgb);
        var bmpData = bitmap.LockBits(
            new Rectangle(0, 0, _outputWidth, _outputHeight),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            var sourcePtr = mapped.pData;
            var destPtr = bmpData.Scan0;
            var sourceStride = (int)mapped.RowPitch;
            var destStride = bmpData.Stride;
            var rowBytes = _outputWidth * 4;

            for (var y = 0; y < _outputHeight; y++)
            {
                var srcRow = IntPtr.Add(sourcePtr, y * sourceStride);
                var dstRow = IntPtr.Add(destPtr, y * destStride);
                CopyMemory(dstRow, srcRow, rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(bmpData);
        }

        return bitmap;
    }

    [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
    private static extern void CopyMemory(IntPtr destination, IntPtr source, int length);

    private void HandleInitializationError(COMException ex)
    {
        if ((HRESULT)ex.ErrorCode == HRESULT.DXGI_ERROR_UNSUPPORTED)
        {
            LastError = "Desktop Duplication is not supported in the current display mode. Exclusive fullscreen applications cannot be captured.";
            logger.Warn(LastError);
        }
        else if ((HRESULT)ex.ErrorCode == HRESULT.E_ACCESSDENIED)
        {
            LastError = "Access denied. Another application may already be using Desktop Duplication.";
            logger.Warn(LastError);
        }
        else
        {
            LastError = $"Failed to initialize Desktop Duplication: {ex.Message} (0x{ex.ErrorCode:X8})";
            logger.Error(ex, LastError);
        }
        _isInitialized = false;
    }

    private void HandleDeviceRemoved()
    {
        LastError = "Graphics device was removed. Attempting to reinitialize...";
        logger.Warn(LastError);

        try
        {
            CleanupResources();
            InitializeDevice();
            InitializeOutput();
            InitializeDuplication();
            _isInitialized = true;
            LastError = null;
            logger.Info("Device reinitialized successfully after removal");
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to reinitialize device after removal");
            LastError = $"Failed to reinitialize graphics device: {ex.Message}";
            _isInitialized = false;
        }
    }

    private void CleanupResources()
    {
        ReleaseComObject(ref _stagingTexture);
        ReleaseComObject(ref _duplication);
        ReleaseComObject(ref _output);
        ReleaseComObject(ref _context);
        ReleaseComObject(ref _device);
        ReleaseComObject(ref _adapter);
        ReleaseComObject(ref _factory);

        _isInitialized = false;
    }

    private static void ReleaseComObject<T>(ref T? obj) where T : class
    {
        if (obj != null)
        {
            Marshal.ReleaseComObject(obj);
            obj = null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CleanupResources();
        _disposed = true;
        logger.Debug("DesktopDuplicationCapture disposed");
    }
}
