using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

// เรียกใช้งาน Library iceLink .DLL ที่ทำการ Add เข้ามา
using FM;
using FM.IceLink;
using FM.IceLink.WebRTC;
using FM.IceLink.WebRTC.NAudio;
using FM.IceLink.WebRTC.AForge;

namespace webRTC_iceLink_CreateOfferAnswer_Receive
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // กำหนด localmedia ของเครื่อง 
        private LocalMediaStream LocalMediaReceiver ;

        // กำหนด Conference ในการสนทนา
        // ศึกษา Class conference ได้จากลิงค์ http://docs.frozenmountain.com/icelink2/#class=IceLink.DotNet45.FM.IceLink.Conference
        private Conference Receiver = null;

        // กำหนด codec Audio 
        private OpusEchoCanceller OpusEchoCanceller = null;
        private int OpusClockRate = 48000;
        private int OpusChannels = 2;

        // กำหนด Audio และ Video Device ที่ใช้งานในการทำ steam
        private AudioStream audioStream_Receiver;
        private VideoStream videoStream_Receiver;

        // ประกาศตัวแปรเพื่อที่จะแสดง Video ในขณะที่สนทนากันในรูปแบบของ WPF 
        public WpfLayoutManager LayoutManager { get; private set; }

        // กำหนด peerID ในการสนทนาทั้งสองฝั่งจะต้องมี peerID เดียวกัน
        private String peerID_link = "room1";

        public MainWindow()
        {
            InitializeComponent();

            // Log to the console.
            Log.Provider = new ConsoleLogProvider(LogLevel.Info);

            // WebRTC has chosen VP8 as its mandatory video codec.
            // Since video encoding is best done using native code,
            // reference the video codec at the application-level.
            // This is required when using a WebRTC video stream.
            // ทำการ Register Codec Video
            VideoStream.RegisterCodec("VP8", () =>
            {
                // class Vp8Codec.cs
                return new Vp8Codec();
            }, true);

            // For improved audio quality, we can use Opus. By
            // setting it as the preferred audio codec, it will
            // override the default PCMU/PCMA codecs.
            // ทำการ Register Codec Audio
            AudioStream.RegisterCodec("opus", OpusClockRate, OpusChannels, () =>
            {
                // class OpusCodec.cs
                return new OpusCodec(OpusEchoCanceller);
            }, true);

            // Since this example can create MessageBox alerts, we have to
            // wait until the form has finished loading before proceeding.
            // เมื่อ program Run ทำการจะทำการ Load เพื่อ access user media ของเครื่อง 
            Loaded += (s, unused) =>
            {
                // Start เพื่อทำการ Access User media ของเครื่อง
                StartMedia();
            };

        }

        private void StartMedia()
        {
            // Get a video-based local media stream. This
            // conference will be send-only.
            // ทำการ Get user media ของเครื่อง (Audio and Video Device)
            UserMedia.GetMedia(new GetMediaArgs(true, true)
            {
                AudioCaptureProvider = new NAudioCaptureProvider(),
                VideoCaptureProvider = new WpfAForgeVideoCaptureProvider(Dispatcher),

                CreateAudioRenderProvider = (e) =>
                {
                    return new NAudioRenderProvider();
                },
                CreateVideoRenderProvider = (e) =>
                {
                    return new ImageVideoRenderProvider(Dispatcher, LayoutScale.Contain);
                },

                // กำหนดขนาด Video ที่จะแสดงพร้อมทั้ง Frame rate ของ Video 
                VideoWidth = 352,    // optional
                VideoHeight = 288,   // optional
                VideoFrameRate = 30, // optional

                // เมื่อไม่สามารถ Access user media ของเครื่องได้ จะแสดง message error 
                OnFailure = (e) =>
                {
                    Alert("Could not get media. {0}", e.Exception.Message);
                },

                // เมื่อสามารถ Access user media ของเครื่องได้
                OnSuccess = (e) =>
                {
                    // พิมพ์แสดงผลลัพธ์ว่าสามารถ Access user media ของเครื่องได้
                    Console.WriteLine("Get User media ok...");

                    // กำหนด localmedia audio and video ที่ใช้งาน
                    LocalMediaReceiver = e.LocalStream;

                    // Create a WebRTC audio stream description (requires a
                    // reference to the local audio feed).
                    // กำหนด Audio ในการสนทนา
                    audioStream_Receiver = new AudioStream(LocalMediaReceiver);

                    // Create a WebRTC video stream description (requires a
                    // reference to the local video feed). Whenever a P2P link
                    // initializes using this description, position and display
                    // the remote video control on-screen by passing it to the
                    // layout manager created above. Whenever a P2P link goes
                    // down, remove it.
                    // กำหนด Video ในการสนทนา
                    videoStream_Receiver = new VideoStream(LocalMediaReceiver);

                    // กำหนดแสดง Video เมื่อมีการสนทนากับเลิกสนทนา
                    videoStream_Receiver.OnLinkInit += AddRemoteVideoControl;
                    videoStream_Receiver.OnLinkDown += RemoveRemoteVideoControl;

                    // แสดงรายชื่อ Audio และ Video Device ที่สามารถ Access ได้โดยแสดงใน ComboBox 
                    AudioDevices.ItemsSource = LocalMediaReceiver.GetAudioDeviceNames();
                    VideoDevices.ItemsSource = LocalMediaReceiver.GetVideoDeviceNames();

                    // เลือก Audio and Video อันแรกของอุปกรณ์เพื่อใช้งาน
                    AudioDevices.SelectedIndex = LocalMediaReceiver.GetAudioDeviceNumber();
                    VideoDevices.SelectedIndex = LocalMediaReceiver.GetVideoDeviceNumber();

                    // เปลี่ยน Localmedia เมื่อมีการเปลี่ยน Audio and Video Device
                    AudioDevices.SelectionChanged += SwitchAudioDevice;
                    VideoDevices.SelectionChanged += SwitchVideoDevice;

                    // อัพเดตแสดง Video Device เมื่อมีการเลือกหรือเปลี่ยนอุปกรณ์การใช้งาน
                    LocalMediaReceiver.OnAudioDeviceNumberChanged += UpdateSelectedAudioDevice;
                    LocalMediaReceiver.OnVideoDeviceNumberChanged += UpdateSelectedVideoDevice;

                    // This is our local video control, a WinForms control
                    // that displays video coming from the capture source.
                    // กำหนดการ capture source ของ Video device
                    var localVideoControl = (FrameworkElement)e.LocalVideoControl;

                    // Create an IceLink layout manager, which makes the task
                    // of arranging video controls easy. Give it a reference
                    // to a WinForms control that can be filled with video feeds.
                    // Use WpfLayoutManager for WPF-based applications.
                    // กำหนดให้ Video แสดงผลที่ canvas ใน UI ของ Sender
                    LayoutManager = new WpfLayoutManager(containerLocal);

                    // แสดงผล Video ที่ได้จากการ capture source
                    LayoutManager.SetLocalVideoControl(localVideoControl);

                    // เริ่มทำการ Conference 
                    StartConference();
                }

            });
        }

        // แสดง Video เมื่อมีการสนทนากัน
        private void AddRemoteVideoControl(StreamLinkInitArgs e)
        {
            try
            {
                var remoteVideoControl = (FrameworkElement)e.Link.GetRemoteVideoControl();
                LayoutManager.AddRemoteVideoControl(e.PeerId, remoteVideoControl);
            }
            catch (Exception ex)
            {
                Log.Error("Could not add remote video control.", ex);
            }
        }

        // เลิกแสดงวีดีโอสนทนากัน 
        private void RemoveRemoteVideoControl(StreamLinkDownArgs e)
        {
            try
            {
                if (LocalMediaReceiver != null && LayoutManager != null)
                {
                    LayoutManager.RemoveRemoteVideoControl(e.PeerId);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not remove remote video control.", ex);
            }
        }

        // เปลี่ยน localmedia เมื่อมีการเปลี่ยน Audio Device
        private void SwitchAudioDevice(object sender, EventArgs e)
        {
            if (AudioDevices.SelectedIndex >= 0)
            {
                LocalMediaReceiver.SetAudioDeviceNumber(this.AudioDevices.SelectedIndex);
            }
        }

        // เปลี่ยน localmedia เมื่อมีการเปลี่ยน Video Device
        private void SwitchVideoDevice(object sender, EventArgs e)
        {
            if (VideoDevices.SelectedIndex >= 0)
            {
                LocalMediaReceiver.SetVideoDeviceNumber(this.VideoDevices.SelectedIndex);
            }
        }

        // Update Audio ComboBox เมื่อมีการเปลี่ยน Audio Device
        private void UpdateSelectedAudioDevice(AudioDeviceNumberChangedArgs e)
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                this.AudioDevices.SelectedIndex = e.DeviceNumber;
            }));
        }

        // Update Video ComboBox เมื่อมีการเปลี่ยน Video Device
        private void UpdateSelectedVideoDevice(VideoDeviceNumberChangedArgs e)
        {
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                this.VideoDevices.SelectedIndex = e.DeviceNumber;
            }));
        }

        // เริ่มทำการ Conference 
        private void StartConference()
        {
            // Create a new IceLink conference 
            // สามารถศึกษา Conference ได้จากลิงค์ http://docs.frozenmountain.com/icelink2/#class=IceLink.DotNet45.FM.IceLink.Conference
            // Conference parameter : serverAddress : System.String , serverPort : System.Int32 , stream : FM.IceLink.Stream
            Receiver = new FM.IceLink.Conference("172.18.6.226", 8888, new Stream[]
            {
                audioStream_Receiver,
                videoStream_Receiver
            });

            //**** Event การทำงาน Conference ****//
            Receiver.OnLinkInit += Receiver_OnLinkInit;
            Receiver.OnLinkUp += Receiver_OnLinkUp;
            Receiver.OnLinkDown += Receiver_OnLinkDown;

            // Stop automatically when the application closes.
            // หยุด Conference เมื่อมีการปิดโปรแกรม
            Closing += (ss, e) =>
            {
                // Stop localMedia ที่กำลังใช้งาน 
                LocalMediaReceiver.Stop();

                if (Receiver != null)
                {
                    // ยกเลิก Link ทั้งหมดในการทำ Conference
                    Receiver.UnlinkAll();
                }

            };

            //**** Event การทำงาน Conference ****//
            // In-memory signalling.
            Receiver.OnLinkOfferAnswer += Receiver_OnLinkOfferAnswer;
            Receiver.OnLinkCandidate += Receiver_OnLinkCandidate;

            // Start echo canceller
            OpusEchoCanceller = new OpusEchoCanceller(OpusClockRate, OpusChannels);
            OpusEchoCanceller.Start();

        }

        // เมื่อทำ Conference ทำการเริ่มต้น (Link to peer initializing...)
        void Receiver_OnLinkInit(LinkInitArgs p)
        {
            Log.Info("Link to peer initializing...");
        }

        // เมื่อทำ Conference สำเร็จ (Link to peer is UP.)
        void Receiver_OnLinkUp(LinkUpArgs p)
        {
            Log.Info("Link to peer is UP.");
        }

        // เมื่อทำ Conference ไม่สำเร็จหรือเลิกการสนทนา (Link to peer is DOWN Sender)
        void Receiver_OnLinkDown(LinkDownArgs p)
        {
            Log.Info(string.Format("Link to peer is DOWN Receiver. {0}", p.Exception.Message));
            Alert("Link to peer is DOWN Receiver. {0}", p.Exception.Message);
        }

        // สร้าง Offer/Answer จาก Link peerID ใน Event ของ Conference
        void Receiver_OnLinkOfferAnswer(LinkOfferAnswerArgs p)
        {
            Console.WriteLine("Answer : "+p.OfferAnswer.ToJson());
            TextBox_Answer.AppendText(p.OfferAnswer.ToJson());
            //TextBox_log.AppendText("\n******* Generate answer *******\n" + p.OfferAnswer.ToJson());
        }

        // สร้าง Candidate จาก Link peerID ใน Event ของ Conference
        void Receiver_OnLinkCandidate(LinkCandidateArgs p)
        {
            TextBox_log.AppendText("\n\n*************** Candidate ***********\n");
            TextBox_log.AppendText(p.Candidate.ToJson());
        }

        // แสดง Alert Message 
        private void Alert(string format, params object[] args)
        {
            Dispatcher.Invoke(new Action(() =>
            {
                MessageBox.Show(string.Format(format, args));
            }));
        }

        // เมื่อมีการปิดโปรแกรม
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (LocalMediaReceiver != null)
            {
                LocalMediaReceiver.Stop();
            }
        }

        // Button รับ Offer text ที่เป็นรูปแบบ JSON พร้อมทั้ง peerID Link นั้น
        private void btn_createAnswer_Click(object sender, RoutedEventArgs e)
        {
            btn_createAnswer.IsEnabled = false;
            Receiver.ReceiveOfferAnswer(OfferAnswer.FromJson(TextBox_senderOffer.Text), peerID_link);
            //TextBox_log.AppendText("\n\n******* Recieve offer *******\n" + TextBox_senderOffer.Text);
        }

        // Button รับ candidate จาก candidate text ที่เป็นรูปแบบ JSON พร้อมทั้ง peerID Link นั้น
        private void btn_Getcandidate_Click(object sender, RoutedEventArgs e)
        {
            Receiver.ReceiveCandidate(Candidate.FromJson(TextBox_candidate.Text), peerID_link);

            //TextBox_log.AppendText("\n*************** Receiver candidate ***********\n");
            //TextBox_log.AppendText(TextBox_candidate.Text);
        }

        // button reset ล้างข้อมูล
        private void btn_reset_Click(object sender, RoutedEventArgs e)
        {
            Receiver.UnlinkAll();

            btn_createAnswer.IsEnabled = true;

            TextBox_senderOffer.Clear();
            TextBox_Answer.Clear();
            TextBox_candidate.Clear();
            TextBox_log.Clear();
        }
    }
}
