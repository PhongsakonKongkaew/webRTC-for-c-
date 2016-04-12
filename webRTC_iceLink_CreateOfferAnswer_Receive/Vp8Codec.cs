using System.Collections.Generic;
using FM;
using FM.IceLink;
using FM.IceLink.WebRTC;
using Win.VP8;

namespace webRTC_iceLink_CreateOfferAnswer_Receive
{
    /// <summary>
    /// An implementation of a VP8 encoder/decoder.
    /// </summary>
    public class Vp8Codec : VideoCodec
    {
        private Vp8Padep _Padep;
        private Encoder _Encoder;
        private Decoder _Decoder;

        /// <summary>
        /// Initializes a new instance of the <see cref="Vp8Codec"/> class.
        /// </summary>
        public Vp8Codec()
        {
            _Padep = new Vp8Padep();
        }

        /// <summary>
        /// Encodes a frame.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <returns></returns>
        public override byte[] Encode(VideoBuffer frame)
        {
            if (_Encoder == null)
            {
                _Encoder = new Encoder();
                _Encoder.Quality = 0.5;
                _Encoder.Bitrate = 320;
                _Encoder.Scale = 1.0;
            }

            if (frame.ResetKeyFrame)
            {
                _Encoder.ForceKeyFrame();
            }

            var plane1 = frame.Planes.Length >= 1 ? new Buffer(frame.Planes[0].Data, frame.Planes[0].Stride, frame.Planes[0].Index, frame.Planes[0].Length) : null;
            var plane2 = frame.Planes.Length >= 2 ? new Buffer(frame.Planes[1].Data, frame.Planes[1].Stride, frame.Planes[1].Index, frame.Planes[1].Length) : null;
            var plane3 = frame.Planes.Length >= 3 ? new Buffer(frame.Planes[2].Data, frame.Planes[2].Stride, frame.Planes[2].Index, frame.Planes[2].Length) : null;
            var plane4 = frame.Planes.Length >= 4 ? new Buffer(frame.Planes[3].Data, frame.Planes[3].Stride, frame.Planes[3].Index, frame.Planes[3].Length) : null;
            return _Encoder.Encode(new Image(frame.Width, frame.Height, frame.Rotate, plane1, plane2, plane3, plane4));
        }

        /// <summary>
        /// Decodes an encoded frame.
        /// </summary>
        /// <param name="encodedFrame">The encoded frame.</param>
        /// <returns></returns>
        public override VideoBuffer Decode(byte[] encodedFrame)
        {
            if (_Decoder == null)
            {
                _Decoder = new Decoder();
            }

            if (_Padep.SequenceNumberingViolated)
            {
                _Decoder.NeedsKeyFrame = true;
                return null;
            }

            var frame = _Decoder.Decode(encodedFrame);
            if (frame == null)
            {
                return null;
            }

            var planes = new List<VideoPlane>();
            if (frame.Plane1 != null) planes.Add(new VideoPlane(frame.Plane1.Data, 0, frame.Plane1.Index, frame.Plane1.Length));
            if (frame.Plane2 != null) planes.Add(new VideoPlane(frame.Plane2.Data, 0, frame.Plane2.Index, frame.Plane2.Length));
            if (frame.Plane3 != null) planes.Add(new VideoPlane(frame.Plane3.Data, 0, frame.Plane3.Index, frame.Plane3.Length));
            if (frame.Plane4 != null) planes.Add(new VideoPlane(frame.Plane4.Data, 0, frame.Plane4.Index, frame.Plane4.Length));
            return new VideoBuffer(frame.Width, frame.Height, frame.Rotate, planes.ToArray());
        }

        /// <summary>
        /// Gets whether the decoder needs a keyframe. This
        /// is checked after every failed Decode operation.
        /// </summary>
        /// <returns></returns>
        public override bool DecoderNeedsKeyFrame()
        {
            if (_Decoder == null)
            {
                return false;
            }
            return _Decoder.NeedsKeyFrame;
        }

        /// <summary>
        /// Packetizes an encoded frame.
        /// </summary>
        /// <param name="encodedFrame">The encoded frame.</param>
        /// <returns></returns>
        public override RTPPacket[] Packetize(byte[] encodedFrame)
        {
            return _Padep.Packetize(encodedFrame, ClockRate);
        }

        /// <summary>
        /// Depacketizes a packet.
        /// </summary>
        /// <param name="packet">The packet.</param>
        /// <returns></returns>
        public override byte[] Depacketize(RTPPacket packet)
        {
            return _Padep.Depacketize(packet);
        }

        private int _LossyCount = 0;
        private int _LosslessCount = 0;

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
                    if (packet is RTCPPliPacket)
                    {
                        Log.Info("Received PLI for video stream.");
                        _Encoder.ForceKeyFrame();
                    }
                    else if (packet is RTCPReportPacket)
                    {
                        var report = (RTCPReportPacket)packet;
                        foreach (var block in report.ReportBlocks)
                        {
                            Log.DebugFormat("VP8 report: {0} packet loss ({1} cumulative packets lost)", block.PercentLost.ToString("P2"), block.CumulativeNumberOfPacketsLost.ToString());
                            if (block.PercentLost > 0)
                            {
                                _LosslessCount = 0;
                                _LossyCount++;
                                if (_LossyCount > 5 && (_Encoder.Quality > 0.0 || _Encoder.Bitrate > 64 || _Encoder.Scale > 0.2))
                                {
                                    _LossyCount = 0;
                                    if (_Encoder.Quality > 0.0)
                                    {
                                        _Encoder.Quality = MathAssistant.Max(0.0, _Encoder.Quality - 0.1);
                                        Log.InfoFormat("Decreasing VP8 encoder quality to {0}.", _Encoder.Quality.ToString("P2"));
                                    }
                                    if (_Encoder.Bitrate > 64)
                                    {
                                        _Encoder.Bitrate = MathAssistant.Max(64, _Encoder.Bitrate - 64);
                                        Log.InfoFormat("Decreasing VP8 encoder bitrate to {0}.", _Encoder.Bitrate.ToString());
                                    }
                                    if (_Encoder.Scale > 0.2)
                                    {
                                        _Encoder.Scale = MathAssistant.Max(0.2, _Encoder.Scale - 0.2);
                                        Log.InfoFormat("Decreasing VP8 encoder scale to {0}.", _Encoder.Scale.ToString("P2"));
                                    }
                                }
                            }
                            else
                            {
                                _LossyCount = 0;
                                _LosslessCount++;
                                if (_LosslessCount > 5 && (_Encoder.Quality < 1.0 || _Encoder.Bitrate < 640 || _Encoder.Scale < 1.0))
                                {
                                    _LosslessCount = 0;
                                    if (_Encoder.Quality < 1.0)
                                    {
                                        _Encoder.Quality = MathAssistant.Min(1.0, _Encoder.Quality + 0.1);
                                        Log.InfoFormat("Increasing VP8 encoder quality to {0}.", _Encoder.Quality.ToString("P2"));
                                    }
                                    if (_Encoder.Bitrate < 640)
                                    {
                                        _Encoder.Bitrate = MathAssistant.Min(640, _Encoder.Bitrate + 64);
                                        Log.InfoFormat("Increasing VP8 encoder bitrate to {0}.", _Encoder.Bitrate.ToString());
                                    }
                                    if (_Encoder.Scale < 1.0)
                                    {
                                        _Encoder.Scale = MathAssistant.Min(1.0, _Encoder.Scale + 0.2);
                                        Log.InfoFormat("Increasing VP8 encoder scale to {0}.", _Encoder.Scale.ToString("P2"));
                                    }
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
