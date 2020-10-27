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
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Microsoft.Win32;
namespace MedTest
{
    public partial class MainWindow : Window
    {
        public static MediaElement Player; //Main Player
        public static bool connected = false; 
        static Socket socket;
        Thread Reading; //Thread for reading from socket
        static bool loaded = false;
        static bool paused = true;
        static Slider Play_time;
        static DispatcherTimer delay = new DispatcherTimer(); //Timer for synchronizing playing time
        static int delta = 0; //The number of milliseconds the program should wait before continuing playing video 
        static bool waiting = false; //If true - the playing time is sychronizing
        const int waiting_span = 200; //The value of error of synchronization in milliseconds
        static bool time_changed = true; //If true - we have changed the current time of the video and have to sync it with the others
        static int offset = 0; // The value of the offset of playing time in milliseconds

        public MainWindow()
        {
            /*
             * Initializes everything
            */
            InitializeComponent();
            Play_time = Slider_Play_time;
            DispatcherTimer timer = new DispatcherTimer();
            timer.Tick += timer_Tick;
            timer.Interval = new TimeSpan(100000);
            timer.Start();
            delay.Tick += delay_Tick;
            delay.Interval = new TimeSpan(waiting_span * 10000);
            delay.Start();
        }

        private void delay_Tick(object sender, EventArgs e)
        {
            try
            {
                if (!waiting && paused && time_changed && connected && loaded)
                {
                    if (connected)
                    {
                        //sends the number of seconds to set all players to to the server 
                        Send_msg("SetT " + ((int)Play_time.Value - (offset / 1000)).ToString());
                    }
                    time_changed = false;
                    return;
                }

                if (!waiting || !loaded)
                {
                    return;
                }

                if (delta - waiting_span < 0)
                {
                    Play();
                    waiting = false;
                }
                delta -= waiting_span;
                //waiting delta milliseconds before resuming the video
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error in delay: " + ex.Message);
            }
        }

        private void timer_Tick(object sender, EventArgs e)
        {
            //shows the current playing time
            if (loaded && Player.NaturalDuration.HasTimeSpan)
            {
                Label_Time.Content = String.Format("{0} / {1}", Player.Position.ToString(@"hh\:mm\:ss"), Player.NaturalDuration.TimeSpan.ToString(@"hh\:mm\:ss"));
                TimeSpan pos = Player.Position;
                if (!paused)
                    Play_time.Value = pos.Hours * 3600 + pos.Minutes * 60 + pos.Seconds;
            }
            else
            {
                Play_time.Value = 0;
                Label_Time.Content = "00:00:00 / 00:00:00";
            }
        }

        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            Player = My_el;
        }

        static int GetMilliseconds(TimeSpan span)
        {
            return span.Hours * 3600000 + span.Minutes * 60000 + span.Seconds * 1000 + span.Milliseconds;
        }

        static TimeSpan FromMillisecond(int ms)
        {
            return new TimeSpan(0, ms / 3600000, (ms % 3600000) / 60000, (ms % 60000) / 1000, ms % 1000);
        }

        public static void Pause()
        {
            //pauses the video
            if (!loaded)
            {
                return;
            }
            Play_time.IsEnabled = true;
            Player.Pause();
            paused = true;
        }

        public static void Play()
        {
            //resumes the video
            if (!loaded)
                return;
            Play_time.IsEnabled = false;
            Player.Play();
            paused = false;
        }
        
        private void Label_Play_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!loaded)
            {
                return;
            }
            if (!connected)
            {
                Play();
                return;
            }
            Send_msg("GetT");
        }
        
        private void Label_Pause_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!loaded)
                return;
            if (!connected)
            {
                Pause();
            }
            Send_msg("Pause");
        }
        
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //closes threads and socket just before closing the app
            if (connected)
            {
                Send_msg("$off$");
                if (Reading.IsAlive)
                {
                    Reading.Abort();
                }
                socket.Shutdown(SocketShutdown.Both);
                socket.Close();
            }
        }

        private void Label_Source_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            //opens OpenFileDialog to get the path uri of the video file
            loaded = false;
            Play_time.IsEnabled = false;
            try
            {
                OpenFileDialog dialog = new OpenFileDialog();
                bool? result = dialog.ShowDialog();
                string uri = "";
                if (result == true)
                {
                    uri = dialog.FileName;
                }
                
                Player.Pause();
                if (Player.Source != null && Player.Source == new Uri(uri))
                {
                    loaded = true;
                    if (Player.NaturalDuration.HasTimeSpan)
                    {
                        TimeSpan total = Player.NaturalDuration.TimeSpan;
                        Play_time.Maximum = total.Hours * 3600 + total.Minutes * 60 + total.Seconds;
                    }
                    MessageBox.Show("Your file has been loaded successfully.\nWait for other clients to get the same message.");
                    Player.Source = new Uri(uri);
                    return;
                }
                Player.Source = new Uri(uri);
                Player.Play();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Loading error");
            }
        }
        
        private void My_el_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            //is called when errors in playing occurs
            MessageBox.Show("Media playing failed.\nTry again.\n" + e.ErrorException.Message);
        }

        private void My_el_MediaOpened(object sender, RoutedEventArgs e)
        {
            //is called when the file is loaded to the player
            Player.Pause();
            loaded = true;
            if (Player.NaturalDuration.HasTimeSpan)
            {
                TimeSpan total = Player.NaturalDuration.TimeSpan;
                Play_time.Maximum = total.Hours * 3600 + total.Minutes * 60 + total.Seconds;
            }
            MessageBox.Show("Your file has been loaded successfully.\nWait for other clients to get the same message.");
        }

        private void My_el_MediaEnded(object sender, RoutedEventArgs e)
        {
            //is called when the media ends
            MessageBox.Show("Media has ended");
            loaded = false;
        }

        private void Play_time_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            //is called when you change the position of the Slider Play_time
            if (!loaded)
                return;
            if (!paused)
                return;
            int val = (int)Play_time.Value;
            if (connected)
            {
                time_changed = true;
                return;
            }
            TimeSpan now_span = new TimeSpan(val / 3600, (val % 3600) / 60, val % 60);
            Player.Position = now_span;
            offset = GetMilliseconds(Player.Position);
        }

        private void Label_FullScreen_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            //enters or exits FullScreen mode
            if (window_Main.WindowState == WindowState.Maximized)
            {
                window_Main.WindowState = WindowState.Normal;
                window_Main.WindowStyle = WindowStyle.SingleBorderWindow;
            } else
            {
                window_Main.WindowState = WindowState.Maximized;
                window_Main.WindowStyle = WindowStyle.None;
            }
        }

        private void Slider_Volume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            //changes Player's volume when the slider position is changed
            if (!loaded)
                return;
            Player.Volume = Slider_Volume.Value;
        }

        #region Connection 
        //the region of code, that connects the client to the server
        public static void Send_msg(string req)
        {
            //sends message req to the server
            if (!connected)
                return;
            byte[] data = Encoding.Default.GetBytes(req);
            socket.Send(data);
        }

        public static void Parse(string command)
        {
            //parses command and does the action, that the string command represents
            if (!loaded)
                return;
            try
            {
                string[] splitted  = command.Split(' ');
                switch (splitted[0])
                {
                    case "Play":
                        Play();
                        break;
                    case "Pause":
                        Pause();
                        break;
                    case "GetT":
                        //sends the current time of playing in milliseconds
                        Send_msg((GetMilliseconds(Player.Position) - offset).ToString());
                        break;
                    case "SetT":
                        //sets the video time to the time, received with command
                        int val = int.Parse(splitted[1]);
                        val = Math.Max(0, Math.Min(val * 1000 + offset, GetMilliseconds(Player.NaturalDuration.TimeSpan)));
                        Player.Position = FromMillisecond(val);
                        break;
                    default:
                        //sets the value to wait before resuming the video
                        int low = int.Parse(command);
                        low += offset;
                        int now = GetMilliseconds(Player.Position);
                        delta = now - low;
                        waiting = true;
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error in Parse");
                MessageBox.Show(ex.Message);
            }
        }

        private void Label_Connect_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (connected)
            {
                //disconnects the client, aborts the thread
                Label_Connect.Background = new SolidColorBrush(Color.FromArgb(150, 89, 147, 86));
                Label_Connect.Content = "Connect";
                Send_msg("$off$");
                try
                {
                    if (Reading.IsAlive)
                    {
                        Reading.Abort();
                    }
                }
                catch (Exception ex)
                {

                }
                socket.Shutdown(SocketShutdown.Both);
                socket.Close();
                connected = !connected;
            }
            else
            {
                try
                {
                    //connects the client to the 766 port to the IP address
                    if (loaded)
                    {
                        offset = GetMilliseconds(Player.Position);
                    }

                    
                    int port = 766;
                    string address = TextBox_Ip.Text;
                    IPEndPoint ipPoint = new IPEndPoint(IPAddress.Parse(address), port);

                    socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    socket.Connect(ipPoint);
                    Reading = new Thread(() =>
                    {
                        //sets new Thread of reading messages from the server
                        try
                        {
                            Action<String> Parse_inv = (string str) => { Parse(str); };
                            while (true)
                            {
                                byte[] dat = new byte[1024];
                                StringBuilder builder = new StringBuilder();
                                int bytes = 0;
                                bytes = socket.Receive(dat);
                                builder.Append(Encoding.Default.GetString(dat, 0, bytes));
                                string msg = builder.ToString();
                                Dispatcher.Invoke(Parse_inv, msg); //Invokes Parse_inv in the main thread
                            }
                        }
                        catch (SocketException ex)
                        {
                            //in case of errors closes connection
                            Action act = () =>
                            {
                                MessageBox.Show(ex.Message);
                                Label_Connect.Content = "Connect";
                                Label_Connect.Background = new SolidColorBrush(Color.FromArgb(200, 89, 147, 86));
                                if (Reading != null && Reading.IsAlive)
                                {
                                    Reading.Abort();
                                }
                                if (socket != null && socket.Connected)
                                {
                                    socket.Shutdown(SocketShutdown.Both);
                                    socket.Close();
                                }
                                connected = false;
                            };
                            Dispatcher.Invoke(act);
                        }
                    });
                    Reading.Start(); //starts that thread
                    MessageBox.Show("You have connected to the server");
                    Label_Connect.Background = new SolidColorBrush(Color.FromArgb(100, 193, 69, 69));
                    Label_Connect.Content = "Disconnect";
                    connected = true;
                }
                catch (Exception ex)
                {
                    //in case of errors closes connection
                    MessageBox.Show(ex.Message);
                    Label_Connect.Content = "Connect";
                    Label_Connect.Background = new SolidColorBrush(Color.FromArgb(200, 89, 147, 86));
                    MessageBox.Show("Connection failed.\nCheck your settings and try again.");
                    if (Reading != null && Reading.IsAlive)
                    {
                        Reading.Abort();
                    }
                    if (socket != null && socket.Connected)
                    {
                        socket.Shutdown(SocketShutdown.Both);
                        socket.Close();
                    }
                    connected = false;
                }
            }
        }

        #endregion

        #region Interface
        //the region of code of changing the interface
        private void Label_FullScreen_MouseEnter(object sender, MouseEventArgs e)
        {
            Label_FullScreen.Background = new SolidColorBrush(Color.FromArgb(200, 66, 66, 147));
        }

        private void Label_FullScreen_MouseLeave(object sender, MouseEventArgs e)
        {
            Label_FullScreen.Background = new SolidColorBrush(Color.FromArgb(200, 35, 35, 95));
        }

        private void Label_Source_MouseLeave(object sender, MouseEventArgs e)
        {
            Label_Source.Background = new SolidColorBrush(Color.FromArgb(200, 35, 35, 95));
        }

        private void Label_Source_MouseEnter(object sender, MouseEventArgs e)
        {
            Label_Source.Background = new SolidColorBrush(Color.FromArgb(200, 66, 66, 147));
        }

        private void Window_Main_MouseMove(object sender, MouseEventArgs e)
        {
            Point pos = e.GetPosition(window_Main);
            double k = 0.28d;
            double h = ActualHeight;
            int allowed = (int)Math.Floor((h) / (k + 1)) - 40;
            if (pos.Y >= allowed || !loaded)
            {
                Label_Connect.Visibility = Visibility.Visible;
                Label_Ip.Visibility = Visibility.Visible;
                Label_Pause.Visibility = Visibility.Visible;
                Label_Play.Visibility = Visibility.Visible;
                Label_Source.Visibility = Visibility.Visible;
                TextBox_Ip.Visibility = Visibility.Visible;
                Label_Time.Visibility = Visibility.Visible;
                Play_time.Visibility = Visibility.Visible;
                Label_FullScreen.Visibility = Visibility.Visible;
                Slider_Volume.Visibility = Visibility.Visible;
                Label_Volume.Visibility = Visibility.Visible;
                Label_BackGroundVolume.Visibility = Visibility.Visible;
            } 
            else
            {
                Label_Connect.Visibility = Visibility.Hidden;
                Label_Ip.Visibility = Visibility.Hidden;
                Label_Pause.Visibility = Visibility.Hidden;
                Label_Play.Visibility = Visibility.Hidden;
                Label_Source.Visibility = Visibility.Hidden;
                TextBox_Ip.Visibility = Visibility.Hidden;
                Label_Time.Visibility = Visibility.Hidden;
                Play_time.Visibility = Visibility.Hidden;
                Label_FullScreen.Visibility = Visibility.Hidden;
                Slider_Volume.Visibility = Visibility.Hidden;
                Label_Volume.Visibility = Visibility.Hidden;
                Label_BackGroundVolume.Visibility = Visibility.Hidden;
            }
        }

        private void Label_Pause_MouseEnter(object sender, MouseEventArgs e)
        {
            Label_Pause.Background = new SolidColorBrush(Color.FromArgb(200, 66, 66, 147));
        }

        private void Label_Pause_MouseLeave(object sender, MouseEventArgs e)
        {
            Label_Pause.Background = new SolidColorBrush(Color.FromArgb(200, 35, 35, 95));
        }

        private void Label_Play_MouseEnter(object sender, MouseEventArgs e)
        {
            Label_Play.Background = new SolidColorBrush(Color.FromArgb(200, 66, 66, 147));
        }

        private void Label_Play_MouseLeave(object sender, MouseEventArgs e)
        {
            Label_Play.Background = new SolidColorBrush(Color.FromArgb(200, 35, 35, 95));
        }

        private void Label_Connect_MouseEnter(object sender, MouseEventArgs e)
        {
            if (connected)
            {
                Label_Connect.Background = new SolidColorBrush(Color.FromArgb(150, 193, 69, 69));
            } 
            else
            {
                Label_Connect.Background = new SolidColorBrush(Color.FromArgb(200, 89, 147, 86));
            }
        }

        private void Label_Connect_MouseLeave(object sender, MouseEventArgs e)
        {
            if (connected)
            {
                Label_Connect.Background = new SolidColorBrush(Color.FromArgb(100, 193, 69, 69));
            }
            else
            {
                Label_Connect.Background = new SolidColorBrush(Color.FromArgb(150, 89, 147, 86));
            }
        }

        #endregion

    }
}
