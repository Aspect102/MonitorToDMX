using Dmx.Net.Common;
using Dmx.Net.Controllers;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace MonitorToDMX.Services
{
    class DMXService
    {
        private static DmxTimer dmxTimer = new DmxTimer();
        private static IController dmxController = ControllerManager.RegisterController<OpenDmxController>(1, dmxTimer);
        private static int cols = 4;
        private static int rows = 3;
        private static int partitionAmount = cols * rows;
        private static bool debugMode = false;
        private static int sens = 0; // sensitivity threshold (0-255)
        private static CancellationTokenSource dmxCancel;

        public static Show show = new Show();


        static void Maind(string[] args)
        {
            if (debugMode)
            {
                dmxTimer.Start();
            }
            else
            {
                try
                {
                    dmxController.Open(0);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error initializing dmxController (have you plugged in the DMX-USB converter?) {ex.Message}");
                    return;
                }

                dmxTimer.Start();
            }

            Fixture.Fixtures.Add(new Fixture("Fresnel V", new List<Fixture.FixtureMode>
        {
            { Fixture.FixtureMode.Intensity},
            { Fixture.FixtureMode.Red},
            { Fixture.FixtureMode.Green },
            { Fixture.FixtureMode.Blue },
            { Fixture.FixtureMode.Indigo },
            { Fixture.FixtureMode.Lime },
            { Fixture.FixtureMode.Strobe },
            { Fixture.FixtureMode.Zoom },
            { Fixture.FixtureMode.Fan },
        }, Fixture.ColourMode.Partitioned));

            Fixture.Fixtures.Add(new Fixture("PAR", new List<Fixture.FixtureMode>
        {
            { Fixture.FixtureMode.Intensity },
            { Fixture.FixtureMode.Red },
            { Fixture.FixtureMode.Green },
            { Fixture.FixtureMode.Blue },
            { Fixture.FixtureMode.Lime },
            { Fixture.FixtureMode.Strobe },
        }, Fixture.ColourMode.Global));

            show.AddLight(Fixture.Fixtures[0], 10, 0, 0);
            show.AddLight(Fixture.Fixtures[0], 20, 1, 0);
            show.AddLight(Fixture.Fixtures[0], 30, 2, 0);
            show.AddLight(Fixture.Fixtures[0], 40, 3, 0);
            show.AddLight(Fixture.Fixtures[0], 50, 0, 1);
            show.AddLight(Fixture.Fixtures[0], 60, 1, 1);
            show.AddLight(Fixture.Fixtures[0], 70, 2, 1);
            show.AddLight(Fixture.Fixtures[0], 80, 3, 1);
            show.AddLight(Fixture.Fixtures[0], 90, 0, 2);
            show.AddLight(Fixture.Fixtures[0], 100, 1, 2);
            show.AddLight(Fixture.Fixtures[0], 110, 2, 2);
            show.AddLight(Fixture.Fixtures[0], 120, 3, 2);

            show.AddLight(Fixture.Fixtures[1], 130);
            show.AddLight(Fixture.Fixtures[1], 140);
            show.AddLight(Fixture.Fixtures[1], 150);
            show.AddLight(Fixture.Fixtures[1], 160);
            show.AddLight(Fixture.Fixtures[1], 170);
            show.AddLight(Fixture.Fixtures[1], 180);
        }

        public static void WriteGlobalColour(byte r, byte g, byte b)
        {
            dmxTimer.Start();
            byte[] pattern = [255, r, g, b, 0, 0, 0, 0, 0]; // 9 channels
            byte[] result = Enumerable.Repeat(pattern, 12).SelectMany(x => x).ToArray();
            dmxController.SetChannelRange(1, result);
        }

        public static void StartDmxLoop()
        {
            if (dmxCancel != null)
                return; // Already running

            dmxCancel = new CancellationTokenSource();

            Task.Run(async () =>
            {
                var token = dmxCancel.Token;

                if (!dmxController.IsOpen)
                    dmxController.Open(0);

                dmxTimer.Start();

                while (!token.IsCancellationRequested)
                {
                    using (Bitmap screenshot = CaptureScreen())
                    {
                        byte[] dmxBuffer = ComputeDmxBuffer(screenshot, show);
                        dmxController.SetChannelRange(1, dmxBuffer);
                    }
                }
            }, dmxCancel.Token);
        }

        public static void StopDmxLoop()
        {
            dmxCancel?.Cancel();
            dmxCancel = null;
            dmxTimer.Stop();
            dmxController.SetChannelRange(1, new byte[511]); // reset all channels
            dmxController.WriteBuffer().Wait(); // flush
        }

        static string AverageToString(byte[] averages) => string.Join(",", averages);

        static Bitmap CaptureScreen()
        {
            var displayInfo = DeviceDisplay.MainDisplayInfo; // Use .NET MAUI's DeviceDisplay API
            var bounds = new Rectangle(0, 0, (int)displayInfo.Width, (int)displayInfo.Height);

            var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new System.Drawing.Size(bounds.Width, bounds.Height));
            }
            return bmp;
        }

        static byte[] ComputeDmxBuffer(Bitmap bmp, Show show)
        {
            int partWidth = bmp.Width / cols;
            int partHeight = bmp.Height / rows;

            List<Rectangle> regions = new List<Rectangle>();
            for (int i = 0; i < partitionAmount; i++)
            {
                int row = i / cols;
                int col = i % cols;
                int x = col * partWidth;
                int y = row * partHeight;
                int width = (col == cols - 1) ? bmp.Width - x : partWidth;
                int height = (row == rows - 1) ? bmp.Height - y : partHeight;
                regions.Add(new Rectangle(x, y, width, height));
            }

            byte[] dmxValues = new byte[511];

            // Lock entire screen bitmap once for efficiency
            BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            unsafe
            {
                byte* ptr = (byte*)bmpData.Scan0;
                int stride = bmpData.Stride;


                for (int i = 0; i < partitionAmount; i++)
                {
                    Rectangle rect = regions[i];
                    long sumR = 0, sumG = 0, sumB = 0;
                    int pixelCount = rect.Width * rect.Height;

                    for (int y = rect.Top; y < rect.Bottom; y++)
                    {
                        byte* row = ptr + (y * stride);
                        for (int x = rect.Left; x < rect.Right; x++)
                        {
                            sumB += row[x * 3 + 0];
                            sumG += row[x * 3 + 1];
                            sumR += row[x * 3 + 2];
                        }
                    }

                    byte intensity = 0, r = 0, g = 0, b = 0;
                    if ((sumR + sumG + sumB) / pixelCount > sens)
                    {
                        intensity = (byte)(Math.Max(sumR, Math.Max(sumG, sumB)) / pixelCount);
                        r = (byte)(sumR / pixelCount);
                        g = (byte)(sumG / pixelCount);
                        b = (byte)(sumB / pixelCount);
                    }

                    foreach (Fixture item in show.ShowList)
                    {
                        if (item.Type == Fixture.ColourMode.Partitioned)
                        {
                            for (int j = 0; j < item.Channels.Count; j++)
                            {
                                switch (item.Channels[j])
                                {
                                    case Fixture.FixtureMode.Intensity:
                                        dmxValues[item.StartingAddress - 1 + j] = intensity;
                                        break;
                                    case Fixture.FixtureMode.Red:
                                        dmxValues[item.StartingAddress - 1 + j] = r;
                                        break;
                                    case Fixture.FixtureMode.Green:
                                        dmxValues[item.StartingAddress - 1 + j] = g;
                                        break;
                                    case Fixture.FixtureMode.Blue:
                                        dmxValues[item.StartingAddress - 1 + j] = b;
                                        break;
                                    case Fixture.FixtureMode.Indigo:
                                        dmxValues[item.StartingAddress - 1 + j] = 0;
                                        break;
                                    case Fixture.FixtureMode.Lime:
                                        dmxValues[item.StartingAddress - 1 + j] = 0;
                                        break;
                                    case Fixture.FixtureMode.Strobe:
                                        dmxValues[item.StartingAddress - 1 + j] = 0;
                                        break;
                                    case Fixture.FixtureMode.Zoom:
                                        dmxValues[item.StartingAddress - 1 + j] = 0;
                                        break;
                                    case Fixture.FixtureMode.Fan:
                                        dmxValues[item.StartingAddress - 1 + j] = 0;
                                        break;
                                }
                            }
                        }
                    }
                }

                // Compute total screen average for PARs
                long totalR = 0, totalG = 0, totalB = 0;
                int totalPixels = bmp.Width * bmp.Height;
                for (int y = 0; y < bmp.Height; y++)
                {
                    byte* row = ptr + (y * stride);
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        totalB += row[x * 3 + 0];
                        totalG += row[x * 3 + 1];
                        totalR += row[x * 3 + 2];
                    }
                }

                byte parIntensity = (byte)(Math.Max(totalR, Math.Max(totalG, totalB)) / totalPixels);
                byte parR = (byte)(totalR / totalPixels);
                byte parG = (byte)(totalG / totalPixels);
                byte parB = (byte)(totalB / totalPixels);

                if ((parR + parG + parB) / 3 <= sens)
                    parIntensity = parR = parG = parB = 0;

                foreach (Fixture item in show.ShowList)
                {
                    if (item.Type == Fixture.ColourMode.Global)
                    {
                        for (int j = 0; j < item.Channels.Count; j++)
                        {
                            switch (item.Channels[j])
                            {
                                case Fixture.FixtureMode.Intensity:
                                    dmxValues[item.StartingAddress - 1 + j] = parIntensity;
                                    break;
                                case Fixture.FixtureMode.Red:
                                    dmxValues[item.StartingAddress - 1 + j] = parR;
                                    break;
                                case Fixture.FixtureMode.Green:
                                    dmxValues[item.StartingAddress - 1 + j] = parG;
                                    break;
                                case Fixture.FixtureMode.Blue:
                                    dmxValues[item.StartingAddress - 1 + j] = parB;
                                    break;
                                case Fixture.FixtureMode.Indigo:
                                    dmxValues[item.StartingAddress - 1 + j] = 0;
                                    break;
                                case Fixture.FixtureMode.Lime:
                                    dmxValues[item.StartingAddress - 1 + j] = 0;
                                    break;
                                case Fixture.FixtureMode.Strobe:
                                    dmxValues[item.StartingAddress - 1 + j] = 0;
                                    break;
                                case Fixture.FixtureMode.Zoom:
                                    dmxValues[item.StartingAddress - 1 + j] = 0;
                                    break;
                                case Fixture.FixtureMode.Fan:
                                    dmxValues[item.StartingAddress - 1 + j] = 0;
                                    break;
                            }
                        }
                    }
                }
            }
            bmp.UnlockBits(bmpData);

            return dmxValues;
        }
    }

    public class Fixture
    {
        public enum FixtureMode
        {
            Intensity,
            Red,
            Green,
            Blue,
            Indigo,
            Lime,
            Strobe,
            Zoom,
            Fan
        }

        public enum ColourMode
        {
            Global,
            Partitioned
        }

        public static List<Fixture> Fixtures = new();

        public (int? x, int? y) Position { get; set; }

        public int StartingAddress { get; set; }

        public string Name { get; set; }

        public List<FixtureMode> Channels { get; set; }

        public ColourMode Type { get; set; }

        public Fixture(string name, List<FixtureMode> channels, ColourMode type)
        {
            Name = name;
            Channels = channels;
            Type = type;
        }
    }

    public class Show
    {
        public List<Fixture> ShowList { get; set; }

        public void AddLight(Fixture fixture, int startingAddress, int x, int y)
        {
            fixture.Position = (x, y);
            fixture.StartingAddress = startingAddress;
            ShowList.Add(fixture);
        }
        public void AddLight(Fixture fixture, int startingAddress)
        {
            fixture.Position = (null, null);
            fixture.StartingAddress = startingAddress;
            ShowList.Add(fixture);
        }

        public Show(List<Fixture>? showList = null)
        {
            ShowList = showList ?? new List<Fixture>();
        }
    }
}

