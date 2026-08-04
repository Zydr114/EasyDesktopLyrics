using System.Runtime.InteropServices;

namespace EasyDesktopLyrics.Services;

/// <summary>
/// WASAPI 环回采集：捕获系统正在播放（render 端点）的音频，输出交错立体声 float 样本
/// （[L0,R0,L1,R1,...]，[-1,1]）。纯 P/Invoke（无第三方依赖），实现对齐 cava input/winscap.c：
/// 事件驱动采集（LOOPBACK | EVENTCALLBACK + SetEventHandle），16ms 缓冲；
/// 事件回调不可用时回退 10ms 轮询；静音帧输出零样本；多声道按 cava 0.7 增益规则下混。
/// 初始化失败时以 IsRunning=false / Error 返回，由频谱引擎回退模拟。
/// 全部 COM 操作集中在专用线程（CoInitialize MTA → 采集循环 → CoUninitialize）。
/// </summary>
public sealed class WasapiLoopbackCapture : IDisposable
{
    private const int EDataFlowRender = 0;
    private const int ERoleConsole = 0;
    private const int CLSCTX_ALL = 0x17;
    private const int AUDCLNT_SHAREMODE_SHARED = 0;
    // audioclient.h（AUDCLNT_STREAMFLAGS_*，实测与本机运行时一致）：
    // LOOPBACK=0x00020000, EVENTCALLBACK=0x00040000, NOPERSIST=0x00080000
    private const int AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
    private const int AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000;
    private const int DEVICE_STATEMASK_ACTIVE = 0x1;
    private const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;
    private const uint COINIT_MULTITHREADED = 0x0;
    private const long REFTIMES_PER_MILLISEC = 10000;

    private static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    private static readonly Guid IID_IAudioCaptureClient = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");

    private static readonly Guid KSDATAFORMAT_SUBTYPE_PCM = new("00000001-0000-0010-8000-00AA00389B71");
    private static readonly Guid KSDATAFORMAT_SUBTYPE_IEEE_FLOAT = new("00000003-0000-0010-8000-00AA00389B71");

    private Thread? _thread;
    private volatile bool _running;
    private Exception? _error;

    /// <summary>采集线程产出的交错立体声 float 样本块（[L0,R0,L1,R1,...]，[-1,1]）。</summary>
    public event Action<float[]>? DataAvailable;

    /// <summary>true = 环回采集已就绪。</summary>
    public bool IsRunning => _running;

    public Exception? Error => _error;

    /// <summary>在专用线程上初始化环回采集；true = 成功并已开始捕获。</summary>
    public Task<bool> StartAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        _thread = new Thread(() =>
        {
            var ok = TryInit();
            tcs.TrySetResult(ok);
            if (ok)
                CaptureLoop();
            Cleanup();
        })
        {
            IsBackground = true,
            Name = "wasapi-loopback",
        };
        _thread.Start();
        return tcs.Task;
    }

    public void Stop()
    {
        _running = false;
        _thread = null;
    }

    public void Dispose()
    {
        Stop();
    }

    // ---------- 采集线程 ----------

    private IAudioClient? _client;
    private IAudioCaptureClient? _captureClient;
    private int _channels;
    private int _bytesPerSample;
    private bool _floatFormat;
    private byte[] _work = new byte[0];
    private readonly double[] _chan = new double[8];
    private IntPtr _eventHandle;

    private Exception? _lastDeviceError;

    private bool TryInit()
    {
        try
        {
            CoInitializeEx(IntPtr.Zero, COINIT_MULTITHREADED);

            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

            // 1. 默认 render 端点（用户当前默认播放设备）
            var hr = enumerator.GetDefaultAudioEndpoint(EDataFlowRender, ERoleConsole, out var def);
            if (hr >= 0 && def != null && TryInitDevice(def))
                return true;

            // 2. 遍历全部活动 render 端点逐个尝试（默认设备可能是虚拟设备，不支持环回）
            hr = enumerator.EnumAudioEndpoints(EDataFlowRender, DEVICE_STATEMASK_ACTIVE, out var coll);
            if (hr < 0 || coll == null)
                throw new InvalidOperationException($"EnumAudioEndpoints hr=0x{hr:X8}");
            coll.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                coll.Item(i, out var dev);
                if (dev != null && TryInitDevice(dev))
                    return true;
            }

            _error = _lastDeviceError ?? new InvalidOperationException("no render endpoint supports loopback");
            _running = false;
            return false;
        }
        catch (Exception ex)
        {
            _error = ex;
            _running = false;
            return false;
        }
    }

    /// <summary>在单个 render 端点上初始化环回；失败释放并返回 false。</summary>
    private bool TryInitDevice(IMMDevice device)
    {
        try
        {
            var iidClient = IID_IAudioClient;
            var hr = device.Activate(ref iidClient, CLSCTX_ALL, IntPtr.Zero, out var pAudioClient);
            if (hr < 0 || pAudioClient == IntPtr.Zero)
                throw new InvalidOperationException($"Activate(IAudioClient) hr=0x{hr:X8}");
            _client = (IAudioClient)Marshal.GetObjectForIUnknown(pAudioClient);

            hr = _client.GetMixFormat(out var fmtPtr);
            if (hr < 0 || fmtPtr == IntPtr.Zero)
                throw new InvalidOperationException($"GetMixFormat hr=0x{hr:X8}");

            var fmt = Marshal.PtrToStructure<WaveFormatEx>(fmtPtr);
            _channels = Math.Max(1, (int)fmt.nChannels);
            _bytesPerSample = fmt.wBitsPerSample / 8;
            if (fmt.wFormatTag == 0xFFFE && fmt.cbSize >= 22)
            {
                var ext = Marshal.PtrToStructure<WaveFormatExtensible>(fmtPtr);
                _floatFormat = ext.SubFormat == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
            }
            else
            {
                _floatFormat = fmt.wFormatTag == 0x0003;
            }

            // 仅支持 float32 / 16bit PCM，其余格式跳过该设备
            if ((_floatFormat && fmt.wBitsPerSample != 32) || (!_floatFormat && fmt.wBitsPerSample != 16))
                throw new NotSupportedException($"unsupported format {fmt.wBitsPerSample}bit float={_floatFormat}");

            // 事件驱动环回（对齐 cava：16ms 缓冲）
            hr = _client.Initialize(
                AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
                16 * REFTIMES_PER_MILLISEC, 0, fmtPtr, IntPtr.Zero);
            if (hr < 0)
                throw new InvalidOperationException($"Initialize(loopback|event) hr=0x{hr:X8}");

            hr = _client.GetBufferSize(out _);
            if (hr < 0)
                throw new InvalidOperationException($"GetBufferSize hr=0x{hr:X8}");

            // 注册数据就绪事件；失败则回退轮询（重新以非事件模式初始化）
            _eventHandle = CreateEvent(IntPtr.Zero, true, false, null);
            if (_eventHandle != IntPtr.Zero && _client.SetEventHandle(_eventHandle) < 0)
            {
                CloseHandle(_eventHandle);
                _eventHandle = IntPtr.Zero;
                _client.Reset();
                hr = _client.Initialize(
                    AUDCLNT_SHAREMODE_SHARED,
                    AUDCLNT_STREAMFLAGS_LOOPBACK,
                    16 * REFTIMES_PER_MILLISEC, 0, fmtPtr, IntPtr.Zero);
                if (hr < 0)
                    throw new InvalidOperationException($"Initialize(loopback poll) hr=0x{hr:X8}");
            }

            var iidCapture = IID_IAudioCaptureClient;
            hr = _client.GetService(ref iidCapture, out var pCaptureClient);
            if (hr < 0 || pCaptureClient == IntPtr.Zero)
                throw new InvalidOperationException($"GetService(IAudioCaptureClient) hr=0x{hr:X8}");
            _captureClient = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(pCaptureClient);

            hr = _client.Start();
            if (hr < 0)
                throw new InvalidOperationException($"Start hr=0x{hr:X8}");

            _running = true;
            return true;
        }
        catch (Exception ex)
        {
            _lastDeviceError = ex;
            try { _client?.Stop(); } catch { }
            _client = null;
            _captureClient = null;
            _running = false;
            return false;
        }
    }

    private void CaptureLoop()
    {
        try
        {
            while (_running)
            {
                // 事件驱动：等待数据就绪；事件不可用时退化为 10ms 轮询
                if (_eventHandle != IntPtr.Zero)
                    WaitForSingleObject(_eventHandle, 5000);
                else
                    Thread.Sleep(10);
                Drain();
                if (_eventHandle != IntPtr.Zero)
                    ResetEvent(_eventHandle);
            }
        }
        catch (Exception ex)
        {
            _error = ex;
            _running = false;
        }
    }

    private void Drain()
    {
        while (_captureClient != null && _captureClient.GetNextPacketSize(out var frames) >= 0 && frames > 0)
        {
            var hr = _captureClient.GetBuffer(out var data, out var numFrames, out var flags, out _, out _);
            if (hr < 0)
                return;
            if (numFrames > 0)
            {
                // 静音帧输出零样本，让频谱在静音时正常回落（对齐 cava write_silent_frame）
                DataAvailable?.Invoke((flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0
                    ? ConvertSilence((int)numFrames)
                    : ConvertToStereo(data, (int)numFrames));
            }
            _captureClient.ReleaseBuffer(numFrames);
        }
    }

    /// <summary>把任意声道数下混为交错立体声（[L0,R0,L1,R1,...]），中置/环绕按 cava 0.7 增益规则。</summary>
    private float[] ConvertToStereo(IntPtr data, int frames)
    {
        var total = frames * _channels * _bytesPerSample;
        if (_work.Length < total)
            Array.Resize(ref _work, total);
        Marshal.Copy(data, _work, 0, total);

        var stereo = new float[frames * 2];
        var step = _channels * _bytesPerSample;
        var chans = Math.Min(_channels, _chan.Length);
        for (var i = 0; i < frames; i++)
        {
            var off = i * step;
            for (var c = 0; c < chans; c++)
            {
                var o = off + c * _bytesPerSample;
                _chan[c] = _floatFormat
                    ? BitConverter.ToSingle(_work, o)
                    : (short)(_work[o] | (_work[o + 1] << 8)) / 32768.0;
            }
            // cava 下混规则：中置(3)、环绕(5/6)、后环绕(7/8) 以 0.7 增益混入 L/R
            double left = 0, right = 0;
            if (chans >= 2) { left += _chan[0]; right += _chan[1]; }
            if (chans >= 3) { left += _chan[2] * 0.7; right += _chan[2] * 0.7; }
            if (chans >= 5) { left += _chan[4] * 0.7; right += _chan[5] * 0.7; }
            if (chans >= 7) { left += _chan[6] * 0.7; right += _chan[7] * 0.7; }
            stereo[i * 2] = (float)left;
            stereo[i * 2 + 1] = (float)right;
        }
        return stereo;
    }

    private static float[] ConvertSilence(int frames)
    {
        return new float[frames * 2];
    }

    private void Cleanup()
    {
        try
        {
            if (_client != null)
            {
                _client.Stop();
                _client = null;
            }
        }
        catch
        {
            // ignore
        }
        if (_eventHandle != IntPtr.Zero)
        {
            CloseHandle(_eventHandle);
            _eventHandle = IntPtr.Zero;
        }
        _captureClient = null;
        try
        {
            CoUninitialize();
        }
        catch
        {
            // ignore
        }
    }

    // ---------- P/Invoke ----------

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatEx
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormatExtensible
    {
        public WaveFormatEx Format;
        public ushort wValidBitsPerSample;
        public uint dwChannelMask;
        public Guid SubFormat;
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll")]
    private static extern bool ResetEvent(IntPtr hEvent);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject
    {
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IMMDeviceCollection devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);

        [PreserveSig]
        int GetDevice(string id, out IMMDevice device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint pcDevices);

        [PreserveSig]
        int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IntPtr ppInterface);

        [PreserveSig]
        int OpenPropertyStore(int access, out IntPtr properties);

        [PreserveSig]
        int GetId(out IntPtr pwstrId);

        [PreserveSig]
        int GetState(out uint state);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig]
        int Initialize(int shareMode, int streamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr pFormat, IntPtr audioSessionGuid);

        [PreserveSig]
        int GetBufferSize(out uint pNumBufferFrames);

        [PreserveSig]
        int GetStreamLatency(out long phnsLatency);

        [PreserveSig]
        int GetCurrentPadding(out uint pNumPaddingFrames);

        [PreserveSig]
        int IsFormatSupported(int shareMode, IntPtr pFormat, IntPtr ppClosestMatch);

        [PreserveSig]
        int GetMixFormat(out IntPtr ppDeviceFormat);

        [PreserveSig]
        int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);

        [PreserveSig]
        int Start();

        [PreserveSig]
        int Stop();

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int SetEventHandle(IntPtr eventHandle);

        [PreserveSig]
        int GetService(ref Guid iid, out IntPtr ppInterface);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig]
        int GetBuffer(out IntPtr pData, out uint numFramesReturned, out uint pdwFlags, out ulong pu64DevicePosition, out ulong pu64QPCPosition);

        [PreserveSig]
        int ReleaseBuffer(uint numFramesRead);

        [PreserveSig]
        int GetNextPacketSize(out uint pNumFramesInNextPacket);
    }
}
