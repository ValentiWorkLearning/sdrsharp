using SDRSharp.Radio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Net.Sockets;
using System.Net;

namespace SDRSharp.KnureSDR
{
    public unsafe sealed class EthernetDummySamplesReceiver
    {

        private const uint DefaultFrequency = 105500000;
        private const int DefaultSamplerate = 2048000;
        private const int SocketDefaultTimeout = 1000;

        // placeholders
        private readonly string _name = "Test Ethernet dummy receiver";
        private readonly int[] _supportedGains= { };
        private bool _useTunerAGC = true;
        private bool _useRtlAGC;
        private int _tunerGain;
        private uint _centerFrequency = DefaultFrequency;
        private uint _sampleRate = DefaultSamplerate;
        private int _frequencyCorrection;
        private SamplingMode _samplingMode;
        private bool _useOffsetTuning;
        private readonly bool _supportsOffsetTuning = false;


        private static readonly float* _lutPtr;
        private static readonly UnsafeBuffer _lutBuffer = UnsafeBuffer.Create(256, sizeof(float));

        private Complex* _iqPtr;
        private UnsafeBuffer _iqBuffer;


        private Thread _worker;
        CancellationTokenSource _cancellationToken;


        private UdpClient _udpClient;
        private static readonly uint _readLength = (uint)Utils.GetIntSetting("RTLBufferLength", 16 * 1024);

        private readonly SamplesAvailableEventArgs _eventArgs = new SamplesAvailableEventArgs();
        public event SamplesAvailableDelegate SamplesAvailable;


        static EthernetDummySamplesReceiver()
        {
            _lutPtr = (float*)_lutBuffer;

            const float scale = 1.0f / 127.0f;
            for (var i = 0; i < 256; i++)
            {
                _lutPtr[i] = (i - 128) * scale;
            }
        }

        public EthernetDummySamplesReceiver()
        {
            _supportedGains = new int[]{
                            0,
                            9,
                            14,
                            27,
                            37,
                            77,
                            87,
                            125,
                            144,
                            157,
                            166,
                            197,
                            207,
                            229,
                            254,
                            280,
                            297,
                            328,
                            338,
                            364,
                            372,
                            386,
                            402,
                            421,
                            434,
                            439,
                            445,
                            480,
                            496

            };
        }
        ~EthernetDummySamplesReceiver()
        {
            Dispose();
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }


        public void Start()
        {
            _udpClient = new UdpClient();
            _udpClient.Connect("192.168.0.174", 5555);
            _udpClient.Client.SendTimeout = SocketDefaultTimeout;
            _udpClient.Client.ReceiveTimeout = SocketDefaultTimeout;

            _cancellationToken = new CancellationTokenSource();
            _worker = new Thread(StreamProc);
            _worker.Priority = ThreadPriority.Highest;
            _worker.Start();
        }

        public void Stop()
        {
            if (_worker == null)
            {
                return;
            }
       
            if (_worker.ThreadState == ThreadState.Running)
            {
                _cancellationToken.Cancel();
                _worker.Join();
            }
            _worker = null;
        }


        private void RtlSdrSamplesAvailable(byte* buf, uint len)
        {
            var sampleCount = (int)len / 2;
            if (_iqBuffer == null || _iqBuffer.Length != sampleCount)
            {
                _iqBuffer = UnsafeBuffer.Create(sampleCount, sizeof(Complex));
                _iqPtr = (Complex*)_iqBuffer;
            }

            var ptr = _iqPtr;
            for (var i = 0; i < sampleCount; i++)
            {
                ptr->Imag = _lutPtr[*buf++];
                ptr->Real = _lutPtr[*buf++];
                ptr++;
            }

            ComplexSamplesAvailable(_iqPtr, _iqBuffer.Length);
        }

        private void ComplexSamplesAvailable(Complex* buffer, int length)
        {
            if (SamplesAvailable != null)
            {
                _eventArgs.Buffer = buffer;
                _eventArgs.Length = length;
                SamplesAvailable(this, _eventArgs);
            }
        }

       private void StreamProc()
        {
            while (!_cancellationToken.IsCancellationRequested)
            {
               
                try
                {
                    IPEndPoint remoteEP = null;
                    byte[] receivedBytes = _udpClient.Receive(ref remoteEP);

                    ProcessReceivedBytes(receivedBytes);
                }
                catch(Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }
        }

        private unsafe void ProcessReceivedBytes(byte[] receivedBytes)
        {
            fixed (byte* p = receivedBytes)
            {
                RtlSdrSamplesAvailable(p, (uint)receivedBytes.Length);
            }
        }

        public uint Index
        {
            get { return 0; }
        }

        public string Name
        {
            get { return _name; }
        }
        public uint Samplerate
        {
            get
            {
                return _sampleRate;
            }
            set
            {
                _sampleRate = value;
            }
        }

        public uint Frequency
        {
            get
            {
                return _centerFrequency;
            }
            set
            {
                _centerFrequency = value;
            }
        }

        public bool UseRtlAGC
        {
            get { return _useRtlAGC; }
            set
            {
                _useRtlAGC = value;
            }
        }

        public bool UseTunerAGC
        {
            get { return _useTunerAGC; }
            set
            {
                _useTunerAGC = value;
            }
        }

        public SamplingMode SamplingMode
        {
            get { return _samplingMode; }
            set
            {
                _samplingMode = value;
            }
        }

        public bool SupportsOffsetTuning
        {
            get { return _supportsOffsetTuning; }
        }

        public bool UseOffsetTuning
        {
            get { return _useOffsetTuning; }
            set
            {
                _useOffsetTuning = value;
            }
        }

        public int[] SupportedGains
        {
         
            get { return _supportedGains; }
        }

        public int Gain
        {
            get { return _tunerGain; }
            set
            {
                _tunerGain = value;
            }
        }

        public int FrequencyCorrection
        {
            get
            {
                return _frequencyCorrection;
            }
            set
            {
                _frequencyCorrection = value;
            }
        }

        public RtlSdrTunerType TunerType
        {
            get
            {
                return RtlSdrTunerType.R820T; //return _dev == IntPtr.Zero ? RtlSdrTunerType.Unknown : NativeMethods.rtlsdr_get_tuner_type(_dev);
            }
        }

        public bool IsStreaming
        {
            get { return _worker != null; }
        }

    }
}
