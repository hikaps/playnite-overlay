using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Playnite.SDK;

namespace PlayniteOverlay;

public class SendInputService : ICaptureService
{
    private static readonly ILogger Logger = LogManager.GetLogger();
    private static readonly Dictionary<string, ushort> KeyNameMap = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
    {
        { "PrintScreen", 0x2C },
        { "PrtSc", 0x2C },
        { "PrtScr", 0x2C },
        { "Snapshot", 0x2C },
        { "F1", 0x70 },
        { "F2", 0x71 },
        { "F3", 0x72 },
        { "F4", 0x73 },
        { "F5", 0x74 },
        { "F6", 0x75 },
        { "F7", 0x76 },
        { "F8", 0x77 },
        { "F9", 0x78 },
        { "F10", 0x79 },
        { "F11", 0x7A },
        { "F12", 0x7B },
        { "0", 0x30 },
        { "1", 0x31 },
        { "2", 0x32 },
        { "3", 0x33 },
        { "4", 0x34 },
        { "5", 0x35 },
        { "6", 0x36 },
        { "7", 0x37 },
        { "8", 0x38 },
        { "9", 0x39 },
        { "A", 0x41 },
        { "B", 0x42 },
        { "C", 0x43 },
        { "D", 0x44 },
        { "E", 0x45 },
        { "F", 0x46 },
        { "G", 0x47 },
        { "H", 0x48 },
        { "I", 0x49 },
        { "J", 0x4A },
        { "K", 0x4B },
        { "L", 0x4C },
        { "M", 0x4D },
        { "N", 0x4E },
        { "O", 0x4F },
        { "P", 0x50 },
        { "Q", 0x51 },
        { "R", 0x52 },
        { "S", 0x53 },
        { "T", 0x54 },
        { "U", 0x55 },
        { "V", 0x56 },
        { "W", 0x57 },
        { "X", 0x58 },
        { "Y", 0x59 },
        { "Z", 0x5A }
    };

    private readonly ushort _screenshotVk;
    private readonly ushort _recordVk;
    private bool _isRecording;

    public string Name => "SendInput Hotkey";
    
    public bool IsRecording => _isRecording;
    
    public event EventHandler<bool>? RecordingStateChanged;

    public SendInputService(string screenshotKey, string recordKey)
    {
        if (string.IsNullOrEmpty(screenshotKey))
        {
            throw new ArgumentException("Screenshot key cannot be null or empty.", nameof(screenshotKey));
        }
        
        if (string.IsNullOrEmpty(recordKey))
        {
            throw new ArgumentException("Record key cannot be null or empty.", nameof(recordKey));
        }
        
        if (!KeyNameMap.TryGetValue(screenshotKey, out _screenshotVk))
        {
            throw new ArgumentException($"Unknown screenshot key: {screenshotKey}", nameof(screenshotKey));
        }
        
        if (!KeyNameMap.TryGetValue(recordKey, out _recordVk))
        {
            throw new ArgumentException($"Unknown record key: {recordKey}", nameof(recordKey));
        }
    }

    public bool IsAvailable()
    {
        return true;
    }

    public Task TakeScreenshotAsync()
    {
        try
        {
            NativeInput.SimulateKeyPress(_screenshotVk);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to send screenshot key press");
            return Task.CompletedTask;
        }
    }

    public Task StartRecordingAsync()
    {
        try
        {
            if (!_isRecording)
            {
                NativeInput.SimulateKeyPress(_recordVk);
                _isRecording = true;
                OnRecordingStateChanged(true);
            }
            
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to send record start key press");
            return Task.CompletedTask;
        }
    }

    public Task StopRecordingAsync()
    {
        try
        {
            if (_isRecording)
            {
                NativeInput.SimulateKeyPress(_recordVk);
                _isRecording = false;
                OnRecordingStateChanged(false);
            }
            
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to send record stop key press");
            return Task.CompletedTask;
        }
    }

    public Task ToggleRecordingAsync()
    {
        try
        {
            NativeInput.SimulateKeyPress(_recordVk);
            _isRecording = !_isRecording;
            OnRecordingStateChanged(_isRecording);
            
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to send record toggle key press");
            return Task.CompletedTask;
        }
    }

    private void OnRecordingStateChanged(bool isRecording)
    {
        RecordingStateChanged?.Invoke(this, isRecording);
    }
}
