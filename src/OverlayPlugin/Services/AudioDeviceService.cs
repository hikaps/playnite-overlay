using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using Playnite.SDK;
using PlayniteOverlay.Models;

namespace PlayniteOverlay.Services;

/// <summary>
/// Service for managing Windows audio output devices.
/// Uses NAudio for device enumeration and COM interop for switching default device.
/// </summary>
public sealed class AudioDeviceService : IDisposable
{
    private readonly ILogger logger;
    private readonly MMDeviceEnumerator? enumerator;
    private bool disposed;

    public AudioDeviceService()
    {
        logger = LogManager.GetLogger();
        try
        {
            enumerator = new MMDeviceEnumerator();
            logger.Debug("AudioDeviceService initialized successfully");
        }
        catch (Exception ex)
        {
            logger.Warn(ex, "Failed to initialize MMDeviceEnumerator");
            enumerator = null;
        }
    }

    /// <summary>
    /// Gets a list of all active audio output devices.
    /// Returns null if enumeration fails or no devices found.
    /// </summary>
    public List<AudioDevice>? GetOutputDevices()
    {
        if (enumerator == null)
        {
            logger.Debug("Enumerator not available, cannot get output devices");
            return null;
        }

        try
        {
            var deviceCollection = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            if (deviceCollection == null || deviceCollection.Count == 0)
            {
                logger.Debug("No active audio output devices found");
                return null;
            }

            var defaultDevice = GetDefaultDevice();
            var defaultId = defaultDevice?.Id;

            var devices = new List<AudioDevice>();
            foreach (var device in deviceCollection)
            {
                try
                {
                    devices.Add(new AudioDevice
                    {
                        Id = device.ID,
                        Name = device.FriendlyName,
                        IsDefault = device.ID == defaultId
                    });
                }
                catch (Exception ex)
                {
                    logger.Debug(ex, $"Error reading device info for {device.ID}");
                }
            }

            logger.Debug($"Found {devices.Count} active audio output devices");
            return devices.Count > 0 ? devices : null;
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Error enumerating audio output devices");
            return null;
        }
    }

    /// <summary>
    /// Gets the current default audio output device.
    /// Returns null if no default device is set or enumeration fails.
    /// </summary>
    public AudioDevice? GetDefaultDevice()
    {
        if (enumerator == null)
        {
            return null;
        }

        try
        {
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (defaultDevice == null)
            {
                return null;
            }

            return new AudioDevice
            {
                Id = defaultDevice.ID,
                Name = defaultDevice.FriendlyName,
                IsDefault = true
            };
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Error getting default audio device");
            return null;
        }
    }

    /// <summary>
    /// Sets the specified device as the default audio output device for all roles.
    /// Returns true if successful, false otherwise.
    /// </summary>
    public bool SetDefaultDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            logger.Warn("Cannot set default device: deviceId is null or empty");
            return false;
        }

        try
        {
            var policyConfig = new PolicyConfigClient();
            var config = (IPolicyConfig)policyConfig;

            // Set as default for all three roles to ensure consistent behavior
            var result1 = config.SetDefaultEndpoint(deviceId, Role.Multimedia);
            var result2 = config.SetDefaultEndpoint(deviceId, Role.Console);
            var result3 = config.SetDefaultEndpoint(deviceId, Role.Communications);

            if (result1 == 0 && result2 == 0 && result3 == 0)
            {
                logger.Info($"Successfully set default audio device to {deviceId}");
                return true;
            }
            else
            {
                logger.Warn($"Failed to set default audio device (results: {result1}, {result2}, {result3})");
                return false;
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"Error setting default audio device to {deviceId}");
            return false;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        try
        {
            enumerator?.Dispose();
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "Error disposing AudioDeviceService");
        }

        disposed = true;
    }
}

#region COM Interop

/// <summary>
/// COM class for PolicyConfig interface.
/// GUID: 870af99c-171d-4f9e-af0d-e63df40c2bc9
/// </summary>
[ComImport]
[Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
internal class PolicyConfigClient
{
}

/// <summary>
/// IPolicyConfig interface for setting default audio endpoints.
/// GUID: f8679f50-850a-41cf-9c72-430f290290c8
/// </summary>
[ComImport]
[Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig]
    int GetMixFormat(string pszDeviceName, IntPtr ppFormat);

    [PreserveSig]
    int GetDeviceFormat(string pszDeviceName, bool bDefault, IntPtr ppFormat);

    [PreserveSig]
    int ResetDeviceFormat(string pszDeviceName);

    [PreserveSig]
    int SetDeviceFormat(string pszDeviceName, IntPtr pEndpointFormat, IntPtr mixFormat);

    [PreserveSig]
    int GetProcessingPeriod(string pszDeviceName, bool bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);

    [PreserveSig]
    int SetProcessingPeriod(string pszDeviceName, IntPtr pmftPeriod);

    [PreserveSig]
    int GetShareMode(string pszDeviceName, IntPtr pMode);

    [PreserveSig]
    int SetShareMode(string pszDeviceName, IntPtr mode);

    [PreserveSig]
    int GetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);

    [PreserveSig]
    int SetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);

    [PreserveSig]
    int SetDefaultEndpoint(string pszDeviceName, Role role);

    [PreserveSig]
    int SetEndpointVisibility(string pszDeviceName, bool bVisible);
}

#endregion
