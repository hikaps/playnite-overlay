using System;
using System.Runtime.InteropServices;
using System.Text;

namespace PlayniteOverlay;

/// <summary>
/// Media Foundation P/Invoke definitions for video encoding.
/// Minimal set required for H.264/AAC MP4 recording.
/// </summary>
internal static class MediaFoundation
{
    // Media Foundation API version (Windows 7+)
    public const int MF_VERSION = 0x0002;
    public const int MF_API_VERSION = MF_VERSION;

    #region P/Invoke - mfplat.dll

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFStartup(int version, int dwFlags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateMediaType(out IMFMediaType ppMFType);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateSample(out IMFSample ppIMFSample);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateMemoryBuffer(int cbMaxLength, out IMFMediaBuffer ppBuffer);

    #endregion

    #region P/Invoke - mfreadwrite.dll

    [DllImport("mfreadwrite.dll", ExactSpelling = true)]
    public static extern int MFCreateSinkWriterFromURL(
        [MarshalAs(UnmanagedType.LPWStr)] string pwszOutputURL,
        IntPtr pByteStream,
        IntPtr pAttributes,
        out IMFSinkWriter ppSinkWriter);

    #endregion

    #region P/Invoke - mf.dll

    [DllImport("mf.dll", ExactSpelling = true)]
    public static extern int MFCreateAttributes(out IntPtr ppAttributes, int cInitialSize);

    #endregion

    #region GUIDs - Media Subtypes

    // MF_MT_SUBTYPE - used to set media type subtype
    public static readonly Guid MF_MT_SUBTYPE = new("F72E1919-E9EC-11D0-AA4B-00A0C9223196");

    // H.264 video format
    public static readonly Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00AA00389B71");

    // AAC audio format
    public static readonly Guid MFAudioFormat_AAC = new("00001610-0000-0010-8000-00AA00389B71");

    // MF_TRANSCODE_CONTAINERTYPE - container type attribute
    public static readonly Guid MF_TRANSCODE_CONTAINERTYPE = new("1500A814-1D9D-4AEC-BF01-9F213B27A4F4");

    // MP4 container
    public static readonly Guid MFTranscodeContainerType_MPEG4 = new("8C2A61D7-90A4-4AB3-A609-7E4A2DE0A7AF");

    #endregion

    #region GUIDs - Media Type Attributes

    public static readonly Guid MF_MT_MAJOR_TYPE = new("48EB18F8-E9EC-451B-A12A-1D9B4AB07D9A");
    public static readonly Guid MF_MT_FRAME_SIZE = new("1652C33D-D6B2-4012-B834-59052FD16745");
    public static readonly Guid MF_MT_FRAME_RATE = new("C459B284-6838-4958-A3CF-500F7EC08176");
    public static readonly Guid MF_MT_AVG_BITRATE = new("20332624-FB0D-4D9E-BD0D-CBF6654A24BF");
    public static readonly Guid MF_MT_INTERLACE_MODE = new("E2724BB8-E679-4A78-9B48-89D5E34B1305");
    public static readonly Guid MF_MT_AUDIO_NUM_CHANNELS = new("37E48BF5-676E-4A5B-8B65-8EC3772B238D");
    public static readonly Guid MF_MT_AUDIO_SAMPLES_PER_SECOND = new("5FAEE7C0-E82E-40DD-A10A-A4831B718B9E");
    public static readonly Guid MF_MT_AUDIO_BITS_PER_SAMPLE = new("F2DEBFCF-4DDE-4650-9E4D-23D09CDC329D");
    public static readonly Guid MF_MT_AUDIO_BLOCK_ALIGNMENT = new("08CE78B4-1D35-4CA0-9B65-7A794CB5CE5E");

    // Major type GUIDs
    public static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFMediaType_Audio = new("73647561-0000-0010-8000-00AA00389B71");

    #endregion

    #region Constants

    public const int MFVideoInterlace_Progressive = 2;

    #endregion

    #region COM Interfaces

    [ComImport]
    [Guid("44AE0FA8-EA31-4109-8D2E-4CA465D1102A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFMediaType
    {
        // IMFAttributes methods
        [PreserveSig]
        int GetItem(ref Guid guidKey, IntPtr pValue);

        [PreserveSig]
        int SetItem(ref Guid guidKey, IntPtr Value);

        [PreserveSig]
        int DeleteItem(ref Guid guidKey);

        [PreserveSig]
        int DeleteAllItems();

        [PreserveSig]
        int SetUINT32(ref Guid guidKey, int unValue);

        [PreserveSig]
        int GetUINT32(ref Guid guidKey, out int punValue);

        [PreserveSig]
        int SetUINT64(ref Guid guidKey, long unValue);

        [PreserveSig]
        int GetUINT64(ref Guid guidKey, out long punValue);

        [PreserveSig]
        int SetDouble(ref Guid guidKey, double fValue);

        [PreserveSig]
        int GetDouble(ref Guid guidKey, out double pfValue);

        [PreserveSig]
        int SetGUID(ref Guid guidKey, ref Guid guidValue);

        [PreserveSig]
        int GetGUID(ref Guid guidKey, out Guid pguidValue);

        [PreserveSig]
        int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string pwszValue);

        [PreserveSig]
        int GetString(ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszValue, int cchBufSize, out int pcchLength);

        [PreserveSig]
        int SetBlob(ref Guid guidKey, byte[] pBuf, int cbBufSize);

        [PreserveSig]
        int GetBlob(ref Guid guidKey, byte[] pBuf, int cbBufSize, out int pcbActual);

        // Additional IMFAttributes methods
        [PreserveSig]
        int GetAllocatedString(ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out int pcchLength);

        [PreserveSig]
        int GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out int pcbSize);

        // IMFMediaType methods
        [PreserveSig]
        int GetMajorType(out Guid pguidMajorType);

        [PreserveSig]
        int IsCompressedFormat(out int pfCompressed);

        [PreserveSig]
        int IsEqual(IntPtr pIMediaType, out int pdwFlags);

        [PreserveSig]
        int GetRepresentation(Guid guidRepresentation, out IntPtr ppvRepresentation);

        [PreserveSig]
        int FreeRepresentation(Guid guidRepresentation, IntPtr pvRepresentation);
    }

    [ComImport]
    [Guid("32152229-3351-48c6-9B52-0E9342AF5356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSinkWriter
    {
        [PreserveSig]
        int AddStream(IMFMediaType pMediaTypeOut, out int pdwStreamIndex);

        [PreserveSig]
        int SetInputMediaType(int dwStreamIndex, IMFMediaType pInputMediaType, IntPtr pEncodingParameters);

        [PreserveSig]
        int BeginWriting();

        [PreserveSig]
        int WriteSample(int dwStreamIndex, IMFSample pSample);

        [PreserveSig]
        int SendStreamTick(int dwStreamIndex, long hnsTimestamp);

        [PreserveSig]
        int PlaceMarker(int dwStreamIndex, IntPtr pvContext);

        [PreserveSig]
        int NotifyEndOfSegment(int dwStreamIndex);

        [PreserveSig]
        int Flush(int dwStreamIndex);

        [PreserveSig]
        int Finalize();

        [PreserveSig]
        int GetServiceForStream(int dwStreamIndex, ref Guid guidService, ref Guid riid, out IntPtr ppvObject);

        [PreserveSig]
        int GetStatistics(int dwStreamIndex, out IntPtr pStats);
    }

    [ComImport]
    [Guid("CFA9DC33-75AC-4D0C-A67C-5318974E7F3F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSample
    {
        // IMFAttributes methods
        [PreserveSig]
        int GetItem(ref Guid guidKey, IntPtr pValue);

        [PreserveSig]
        int SetItem(ref Guid guidKey, IntPtr Value);

        [PreserveSig]
        int DeleteItem(ref Guid guidKey);

        [PreserveSig]
        int DeleteAllItems();

        [PreserveSig]
        int SetUINT32(ref Guid guidKey, int unValue);

        [PreserveSig]
        int GetUINT32(ref Guid guidKey, out int punValue);

        [PreserveSig]
        int SetUINT64(ref Guid guidKey, long unValue);

        [PreserveSig]
        int GetUINT64(ref Guid guidKey, out long punValue);

        [PreserveSig]
        int SetDouble(ref Guid guidKey, double fValue);

        [PreserveSig]
        int GetDouble(ref Guid guidKey, out double pfValue);

        [PreserveSig]
        int SetGUID(ref Guid guidKey, ref Guid guidValue);

        [PreserveSig]
        int GetGUID(ref Guid guidKey, out Guid pguidValue);

        [PreserveSig]
        int SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string pwszValue);

        [PreserveSig]
        int GetString(ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszValue, int cchBufSize, out int pcchLength);

        [PreserveSig]
        int SetBlob(ref Guid guidKey, byte[] pBuf, int cbBufSize);

        [PreserveSig]
        int GetBlob(ref Guid guidKey, byte[] pBuf, int cbBufSize, out int pcbActual);

        [PreserveSig]
        int GetAllocatedString(ref Guid guidKey, [Out, MarshalAs(UnmanagedType.LPWStr)] out string ppwszValue, out int pcchLength);

        [PreserveSig]
        int GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out int pcbSize);

        // IMFSample methods
        [PreserveSig]
        int GetSampleFlags(out int pdwSampleFlags);

        [PreserveSig]
        int SetSampleFlags(int dwSampleFlags);

        [PreserveSig]
        int GetSampleTime(out long phnsSampleTime);

        [PreserveSig]
        int SetSampleTime(long hnsSampleTime);

        [PreserveSig]
        int GetSampleDuration(out long phnsSampleDuration);

        [PreserveSig]
        int SetSampleDuration(long hnsSampleDuration);

        [PreserveSig]
        int GetBufferCount(out int pdwBufferCount);

        [PreserveSig]
        int GetBufferByIndex(int dwIndex, out IMFMediaBuffer ppBuffer);

        [PreserveSig]
        int ConvertToContiguousBuffer(out IMFMediaBuffer ppBuffer);

        [PreserveSig]
        int AddBuffer(IMFMediaBuffer pBuffer);

        [PreserveSig]
        int RemoveBufferByIndex(int dwIndex);

        [PreserveSig]
        int RemoveAllBuffers();

        [PreserveSig]
        int GetTotalLength(out int pcbTotalLength);

        [PreserveSig]
        int CopyToBuffer(IMFMediaBuffer pBuffer);
    }

    [ComImport]
    [Guid("045FA593-8799-42B8-BC8D-89651D25BC54")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFMediaBuffer
    {
        [PreserveSig]
        int Lock(out IntPtr ppbBuffer, out int pcbMaxLength, out int pcbCurrentLength);

        [PreserveSig]
        int Unlock();

        [PreserveSig]
        int GetCurrentLength(out int pcbCurrentLength);

        [PreserveSig]
        int SetCurrentLength(int cbCurrentLength);

        [PreserveSig]
        int GetMaxLength(out int pcbMaxLength);
    }

    #endregion
}
