using System;
using MissionPlanner.Utilities;
using System.IO;
using System.Windows.Forms;
using GMap.NET.WindowsForms;
using GMap.NET;
using System.Drawing;
using System.Drawing.Drawing2D;
using MissionPlanner.Maps;
using System.Collections.Generic;
using System.Net;
using System.Drawing.Imaging;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;

namespace MissionPlanner.Maps
{

    public class GMapMarkerWeather : GMapMarker
    {
        Bitmap weather;
        private RectLatLng rect;

        public GMapMarkerWeather(String imageName, RectLatLng rect, PointLatLng currentloc)
        : base(currentloc)
        {
            this.rect = rect;
            weather?.Dispose();
            weather = new Bitmap(imageName);
            weather.MakeTransparent();
        }


        public override void OnRender(IGraphics g)
        {
            base.OnRender(g);

            if (weather != null)
            {

                var tlll = Overlay.Control.FromLatLngToLocal(rect.LocationTopLeft);
                var brll = Overlay.Control.FromLatLngToLocal(rect.LocationRightBottom);

                var old = g.Transform;
                g.ResetTransform();
                g.CompositingMode = CompositingMode.SourceOver;
                g.DrawImage(weather, tlll.X, tlll.Y, brll.X - tlll.X, brll.Y - tlll.Y);
                g.Transform = old;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            weather.Dispose();

        }


    }

}



namespace MissionPlanner.WeatherRadarPlugin
{


    public class WeatherRadarPlugin : MissionPlanner.Plugin.Plugin
    {


        string weatherDir = Settings.GetUserDataDirectory() + "weather" + Path.DirectorySeparatorChar;
        string[] imagesToDisplay;
        int imageIndex = 0;
        DateTime lastImageDownload = new DateTime(1972, 1, 1);

        RectLatLng rect = RectLatLng.FromLTRB(14.256696, 49.558357, 24.002804, 44.827050);

        GMapOverlay weatherOverlay = new GMapOverlay("weather");

        Label weatherImageDate;
        System.Windows.Forms.ToolStripMenuItem men = new System.Windows.Forms.ToolStripMenuItem();
        bool weatherEnabled = false;


        public override string Name
        {
            get { return "Stats"; }
        }

        public override string Version
        {
            get { return "0.1"; }
        }

        public override string Author
        {
            get { return "Michael Oborne"; }
        }

        //[DebuggerHidden]
        public override bool Init()
        {


            //Check internet connectivity, if no connecttion the disable the plugin. And add a message about it
            var networkAvailable = PingNetwork("1.1.1.1");
            if (!networkAvailable)
            {
                Console.WriteLine("No network available, exit plugin");
                loopratehz = 0;
                Host.comPort.MAV.cs.messageHigh = "Weather radar not available";
                return false;
            }


            loopratehz = 1f;

            ////weather

            Host.FDGMapControl.Overlays.Add(weatherOverlay);


            MainV2.instance.BeginInvoke((MethodInvoker)(() =>
            {

                var FDRightSide = Host.MainForm.FlightData.Controls.Find("splitContainer1", true).FirstOrDefault() as SplitContainer;
                men.Click += enable_Click;
                Host.FDMenuMap.Items.Add(men);
                weatherImageDate = new Label();
                weatherImageDate.Name = "lbl_alt";
                weatherImageDate.Location = new System.Drawing.Point(0, 100);
                weatherImageDate.Text = "";
                weatherImageDate.AutoSize = true;
                weatherImageDate.Font = new Font("Tahoma", 15, FontStyle.Bold);
                weatherImageDate.Anchor = (AnchorStyles.Top | AnchorStyles.Left);
                weatherImageDate.ForeColor = System.Drawing.Color.DarkCyan;

                FDRightSide.Panel2.Controls.Add(weatherImageDate);
                FDRightSide.Panel2.Controls.SetChildIndex(weatherImageDate, 1);

            }));


            weatherEnabled = Settings.Instance.GetBoolean("weatheroverlay", false);

            if (weatherEnabled) enableOverlay();
            else disableOverlay();


            // Check directory
            if (!Directory.Exists(weatherDir))
                Directory.CreateDirectory(weatherDir);

            clearImageDir(weatherDir);

            return true;
        }


        public override bool Loaded()
        {
            return true;
        }

        public override bool Loop()
        {

            if (weatherEnabled)
            {

                TimeSpan t = DateTime.Now - lastImageDownload;
                if (t.Minutes > 6)
                {
                    weatherOverlay.Markers.Clear();
                    downloadLatestImages();
                    updateDisplayImageList();
                }


                if (imagesToDisplay?.Length > 0)
                {

                    weatherOverlay.Markers.Clear();
                    GMapMarkerWeather weather = new GMapMarkerWeather(imagesToDisplay[imageIndex], rect, new PointLatLng(0, 0));
                    weatherOverlay.Markers.Add(weather);

                    var l = imagesToDisplay[imageIndex].Substring(imagesToDisplay[imageIndex].Length - 9, 5).Replace("_", ":");

                    setTimeLabel(l);

                    imageIndex++;
                    if (imageIndex == imagesToDisplay.Length)
                    {
                        if (imagesToDisplay.Length >= 6) imageIndex = imagesToDisplay.Length - 6;
                        else imageIndex = 0;
                    }
                }
            }

            return true;
        }

        public override bool Exit()
        {

            return true;
        }


        public void setTimeLabel(string s)
        {
            MainV2.instance.BeginInvoke((MethodInvoker)(() =>
            {

                weatherImageDate.Text = s;
            }));
        }


        public void downloadLatestImages()
        {
            String imagesJS;

            //Get the JS for image list
            using (WebClient web1 = new WebClient())
                imagesJS = web1.DownloadString("https://idokep.hu/radar/radar.js");
            String[] scriptLines = imagesJS.Split('\n');

            List<String> imagesToDownload = new List<String>();

            foreach (String s in scriptLines)
            {
                if (s.Contains("radar/"))
                {
                    //generate filename
                    String str = s;
                    str = str.Replace("'", string.Empty);
                    str = str.Replace(",", string.Empty);
                    str = str.Replace("/radar/", string.Empty);
                    if (!File.Exists(weatherDir + str)) imagesToDownload.Add(str);
                }
            }

            using (WebClient webClient = new WebClient())
            {
                foreach (String s in imagesToDownload)
                {
                    webClient.Headers.Add("Referer", "https://www.idokep.hu/radar");
                    byte[] data = webClient.DownloadData("https://www.idokep.hu/radar/" + s);

                    using (MemoryStream mem = new MemoryStream(data))
                    {
                        using (var yourImage = Image.FromStream(mem))
                        {
                            yourImage.Save(weatherDir + s, ImageFormat.Png);
                        }
                    }
                }

            }

            lastImageDownload = DateTime.Now;

        }

        public void updateDisplayImageList()
        {

            imagesToDisplay = Directory.GetFiles(weatherDir, "*.png");
            Array.Sort(imagesToDisplay, StringComparer.InvariantCulture);
            imageIndex = 0;

        }


        public void clearImageDir(string path)
        {
            System.IO.DirectoryInfo di = new DirectoryInfo(path);
            foreach (FileInfo file in di.GetFiles())
            {
                file.Delete();
            }
        }


        void enable_Click(object sender, EventArgs e)
        {

            if (weatherEnabled)
            {
                disableOverlay();
            }
            else
            {
                enableOverlay();
            }
        }


        void disableOverlay()
        {
            weatherEnabled = false;
            Settings.Instance["weatheroverlay"] = false.ToString();
            weatherOverlay.Markers.Clear();
            setTimeLabel("");
            men.Text = "Enable Radar overlay";

        }

        void enableOverlay()
        {
            weatherEnabled = true;
            Settings.Instance["weatheroverlay"] = true.ToString();
            men.Text = "Disable Radar overlay";

        }


        public static bool PingNetwork(string hostNameOrAddress)
        {
            bool pingStatus;

            using (var p = new Ping())
            {
                byte[] buffer = Encoding.ASCII.GetBytes("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
                int timeout = 5000; // 5sg

                try
                {
                    var reply = p.Send(hostNameOrAddress, timeout, buffer);
                    pingStatus = reply.Status == IPStatus.Success;
                }
                catch (Exception)
                {
                    pingStatus = false;
                }
            }

            return pingStatus;
        }



    }

}