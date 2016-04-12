using FM;
using FM.IceLink;
using FM.IceLink.WebRTC;
using Win.Opus;
using Win.AudioProcessing;

namespace webRTC_iceLink_CreateOfferAnswer
{
    public class OpusEchoCanceller
    {
        public AcousticEchoCanceller AcousticEchoCanceller { get; set; }
        public AudioMixer AudioMixer { get; set; }

        public OpusEchoCanceller(int clockRate, int channels)
        {
            AcousticEchoCanceller = new AcousticEchoCanceller(clockRate, channels, 300);
            AudioMixer = new AudioMixer(clockRate, channels, 20);
            AudioMixer.OnFrame += OnAudioMixerFrame;
        }

        public void Start()
        {
            AudioMixer.Start();
        }

        public void Stop()
        {
            AudioMixer.Stop();
        }

        public byte[] Capture(AudioBuffer input)
        {
            return AcousticEchoCanceller.Capture(input.Data, input.Index, input.Length);
        }

        public void Render(string peerId, AudioBuffer echo)
        {
            AudioMixer.AddSourceFrame(peerId, new AudioBuffer(echo.Data, echo.Index, echo.Length));
        }

        private void OnAudioMixerFrame(AudioBuffer echoMixed)
        {
            AcousticEchoCanceller.Render(echoMixed.Data, echoMixed.Index, echoMixed.Length);
        }
    }

    /// <summary>
    /// An implementation of an Opus encoder/decoder.
    /// </summary>
    public class OpusCodec : AudioCodec
    {
        private BasicAudioPadep _Padep;
        private Encoder _Encoder;
        private Decoder _Decoder;

        /// <summary>
        /// Gets or sets the echo canceller.
        /// </summary>
        public OpusEchoCanceller EchoCanceller { get; set; }

        /// <summary>
        /// Gets or sets the loss percentage (0-100)
        /// before forward error correction (FEC) is
        /// activated (only if supported by the remote peer).
        /// Affects encoded data only.
        /// Defaults to 5.
        /// </summary>
        public int PercentLossToTriggerFEC { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to
        /// disable forward error correction (FEC) completely.
        /// If set to true, FEC will never activate.
        /// Affects encoded data only.
        /// Defaults to false.
        /// </summary>
        public bool DisableFEC { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether forward
        /// error correction (FEC) is currently active.
        /// </summary>
        public bool FecActive { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="OpusCodec"/> class.
        /// </summary>
        public OpusCodec()
            : this(null)
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="OpusCodec"/> class.
        /// </summary>
        /// <param name="acousticEchoCanceller">The acoustic echo canceller.</param>
        public OpusCodec(OpusEchoCanceller echoCanceller)
            : base(20)
        {
            EchoCanceller = echoCanceller;
            DisableFEC = false;
            PercentLossToTriggerFEC = 5;

            _Padep = new BasicAudioPadep();
        }

        /// <summary>
        /// Encodes a frame.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <returns></returns>
        public override byte[] Encode(AudioBuffer frame)
        {
            if (_Encoder == null)
            {
                _Encoder = new Encoder(ClockRate, Channels, PacketTime);
                _Encoder.Quality = 0.5;
                _Encoder.Bitrate = 125;
            }

            var echoCanceller = EchoCanceller;
            var buffer = (echoCanceller == null)
                ? new Buffer(frame.Data, frame.Index, frame.Length)
                : new Buffer(echoCanceller.Capture(frame));

            return _Encoder.Encode(buffer);
        }

        private int _CurrentRTPSequenceNumber = -1;
        private int _LastRTPSequenceNumber = -1;

        /// <summary>
        /// Decodes an encoded frame.
        /// </summary>
        /// <param name="encodedFrame">The encoded frame.</param>
        /// <returns></returns>
        public override AudioBuffer Decode(byte[] encodedFrame)
        {
            if (_Decoder == null)
            {
                _Decoder = new Decoder(ClockRate, Channels, PacketTime);
                Link.GetRemoteStream().DisablePLC = true;
            }

            if (_LastRTPSequenceNumber == -1)
            {
                _LastRTPSequenceNumber = _CurrentRTPSequenceNumber;
                return DecodeNormal(encodedFrame);
            }
            else
            {
                var sequenceNumberDelta = RTPPacket.GetSequenceNumberDelta(_CurrentRTPSequenceNumber, _LastRTPSequenceNumber);
                _LastRTPSequenceNumber = _CurrentRTPSequenceNumber;

                var missingPacketCount = sequenceNumberDelta - 1;
                var previousFrames = new AudioBuffer[missingPacketCount];

                var plcFrameCount = (missingPacketCount > 1) ? missingPacketCount - 1 : 0;
                if (plcFrameCount > 0)
                {
                    Log.InfoFormat("Adding {0} frames of loss concealment to incoming audio stream. Packet sequence violated.", plcFrameCount.ToString());
                    for (var i = 0; i < plcFrameCount; i++)
                    {
                        previousFrames[i] = DecodePLC();
                    }
                }

                var fecFrameCount = (missingPacketCount > 0) ? 1 : 0;
                if (fecFrameCount > 0)
                {
                    var fecFrame = DecodeFEC(encodedFrame);
                    var fecFrameIndex = missingPacketCount - 1;
                    if (fecFrame == null)
                    {
                        previousFrames[fecFrameIndex] = DecodePLC();
                    }
                    else
                    {
                        previousFrames[fecFrameIndex] = fecFrame;
                    }
                }

                var frame = DecodeNormal(encodedFrame);
                frame.PreviousBuffers = previousFrames;
                return frame;
            }
        }

        private AudioBuffer DecodePLC()
        {
            return Decode(null, false);
        }

        private AudioBuffer DecodeFEC(byte[] encodedFrame)
        {
            return Decode(encodedFrame, true);
        }

        private AudioBuffer DecodeNormal(byte[] encodedFrame)
        {
            return Decode(encodedFrame, false);
        }

        private AudioBuffer Decode(byte[] encodedFrame, bool fec)
        {
            var buffer = _Decoder.Decode(encodedFrame, fec);
            if (buffer == null)
            {
                return null;
            }

            var frame = new AudioBuffer(buffer.Data, buffer.Index, buffer.Length);
            var echoCanceller = EchoCanceller;
            if (echoCanceller != null)
            {
                echoCanceller.Render(PeerId, frame);
            }
            return frame;
        }

        /// <summary>
        /// Packetizes an encoded frame.
        /// </summary>
        /// <param name="encodedFrame">The encoded frame.</param>
        /// <returns></returns>
        public override RTPPacket[] Packetize(byte[] encodedFrame)
        {
            return _Padep.Packetize(encodedFrame, ClockRate, PacketTime, ResetTimestamp);
        }

        /// <summary>
        /// Depacketizes a packet.
        /// </summary>
        /// <param name="packet">The packet.</param>
        /// <returns></returns>
        public override byte[] Depacketize(RTPPacket packet)
        {
            _CurrentRTPSequenceNumber = packet.SequenceNumber;

            return _Padep.Depacketize(packet);
        }

        private int _LossyCount = 0;
        private int _LosslessCount = 0;

        private int _MinimumReportsBeforeFEC = 1;
        private long _ReportsReceived = 0;

        /// <summary>
        /// Processes RTCP packets.
        /// </summary>
        /// <param name="packets">The packets to process.</param>
        public override void ProcessRTCP(RTCPPacket[] packets)
        {
            if (_Encoder != null)
            {
                foreach (var packet in packets)
                {
                    if (packet is RTCPReportPacket)
                    {
                        _ReportsReceived++;

                        var report = (RTCPReportPacket)packet;
                        foreach (var block in report.ReportBlocks)
                        {
                            Log.DebugFormat("Opus report: {0} packet loss ({1} cumulative packets lost)", block.PercentLost.ToString("P2"), block.CumulativeNumberOfPacketsLost.ToString());
                            if (block.PercentLost > 0)
                            {
                                _LosslessCount = 0;
                                _LossyCount++;
                                if (_LossyCount > 5 && _Encoder.Quality > 0.0)
                                {
                                    _LossyCount = 0;
                                    _Encoder.Quality = MathAssistant.Max(0.0, _Encoder.Quality - 0.1);
                                    Log.InfoFormat("Decreasing Opus encoder quality to {0}.", _Encoder.Quality.ToString("P2"));
                                }
                            }
                            else
                            {
                                _LossyCount = 0;
                                _LosslessCount++;
                                if (_LosslessCount > 5 && _Encoder.Quality < 1.0)
                                {
                                    _LosslessCount = 0;
                                    _Encoder.Quality = MathAssistant.Min(1.0, _Encoder.Quality + 0.1);
                                    Log.InfoFormat("Increasing Opus encoder quality to {0}.", _Encoder.Quality.ToString("P2"));
                                }
                            }

                            if (!DisableFEC && !FecActive && _ReportsReceived > _MinimumReportsBeforeFEC)
                            {
                                if ((block.PercentLost * 100) > PercentLossToTriggerFEC)
                                {
                                    Log.Info("Activating FEC for Opus audio stream.");
                                    _Encoder.ActivateFEC(PercentLossToTriggerFEC);
                                    FecActive = true;
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Destroys the codec.
        /// </summary>
        public override void Destroy()
        {
            if (_Encoder != null)
            {
                _Encoder.Destroy();
                _Encoder = null;
            }

            if (_Decoder != null)
            {
                _Decoder.Destroy();
                _Decoder = null;
            }
        }
    }
}
