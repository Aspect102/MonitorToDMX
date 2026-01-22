using Dmx.Net.Common;
using Dmx.Net.Controllers;
using MonitorToDMX.Models;
using NAudio.Wave;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using static MonitorToDMX.Models.Fixture;

namespace MonitorToDMX.Services
{
    class DMXService
    {
        private static DmxTimer dmxTimer = new DmxTimer();
        private static IController dmxController = ControllerManager.RegisterController<OpenDmxController>(1, dmxTimer);
        private static bool debugMode = false;
        private static int sens = 0; // sensitivity threshold (0-255)
        private static CancellationTokenSource dmxCancel;
        public static byte FixedZoomLevel { get; set; } = 0;

        // 1.0 = Normal, 0.0 = Grey, 2.0 = Vibrant
        public static double SaturationBoost { get; set; } = 1.0;
        private static byte[] GammaLUT = new byte[256];
        public static double ColorMatchRadius { get; set; } = 150.0; // Default "Medium" strictness

        public static void InitializeGamma()
        {
            for (int i = 0; i < 256; i++)
            {
                // Normalize i to 0.0 - 1.0
                double normalized = i / 255.0;

                double corrected = Math.Pow(normalized, GammaValue);

                // Scale back to 0-255
                GammaLUT[i] = (byte)(corrected * 255.0);
            }
        }


        public static int PartitionAmount;

        public static Show show = new Show();

        public static int Rows
        {
            get => _rows;
            set
            {
                _rows = value > 0 ? value : 1;
                PartitionAmount = Rows * Columns;
            }
        }
        private static int _rows = 3;

        public static int Columns
        {
            get => _columns;
            set
            {
                _columns = value > 0 ? value : 1;
                PartitionAmount = Rows * Columns;
            }
        }
        private static int _columns = 4;

        private static double _gammaValue = 1.0;

        public static double GammaValue
        {
            get => _gammaValue;
            set
            {
                _gammaValue = Math.Clamp(value, 0.1, 5.0);

                InitializeGamma();
            }
        }

        private static WasapiLoopbackCapture? loopback;
        private static float currentAudioLevel;
        public static bool UseAudioReactiveMode { get; set; } = false;

        //static void Maind(string[] args)
        //{
        //    if (debugMode)
        //    {
        //        dmxTimer.Start();
        //    }
        //    else
        //    {
        //        try
        //        {
        //            dmxController.Open(0);
        //        }
        //        catch (Exception ex)
        //        {
        //            Console.WriteLine($"Error initializing dmxController (have you plugged in the DMX-USB converter?) {ex.Message}");
        //            return;
        //        }

        //        dmxTimer.Start();
        //    }






        //}

        //public static void WriteGlobalColour(byte r, byte g, byte b)
        //{
        //    dmxTimer.Start();
        //    byte[] pattern = [255, r, g, b, 0, 0, 0, 0, 0]; // 9 channels
        //    byte[] result = Enumerable.Repeat(pattern, 12).SelectMany(x => x).ToArray();
        //    dmxController.SetChannelRange(1, result);
        //}

        public static void StartDmxLoop()
        {
            if (dmxCancel != null)
                return; // Already running

            dmxCancel = new CancellationTokenSource();

            if (UseAudioReactiveMode)
                StartAudioCapture();

            InitializeGamma();

            Task.Run(async () =>
            {
                var token = dmxCancel.Token;

                if (!dmxController.IsOpen)
                    dmxController.Open(0);

                dmxTimer.Start();

                while (!token.IsCancellationRequested)
                {
                    using (Bitmap screenshot = await CaptureScreenAsync())
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
            if (UseAudioReactiveMode)
                StopAudioCapture();
        }

        static string AverageToString(byte[] averages) => string.Join(",", averages);

        private static void ApplySaturation(ref byte r, ref byte g, ref byte b, double saturation)
        {
            // Calculate luminance (perceived brightness)
            // Or simple average: double avg = (r + g + b) / 3.0;
            double gray = r * 0.299 + g * 0.587 + b * 0.114;

            // Interpolate between the gray value and the color value
            double newR = gray + (r - gray) * saturation;
            double newG = gray + (g - gray) * saturation;
            double newB = gray + (b - gray) * saturation;

            r = (byte)Math.Clamp(newR, 0, 255);
            g = (byte)Math.Clamp(newG, 0, 255);
            b = (byte)Math.Clamp(newB, 0, 255);
        }

        static async Task<Bitmap> CaptureScreenAsync()
        {
            DisplayInfo displayInfo = await MainThread.InvokeOnMainThreadAsync(() => DeviceDisplay.MainDisplayInfo);
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
            PartitionAmount = Rows * Columns;
            int partWidth = bmp.Width / Columns;
            int partHeight = bmp.Height / Rows;

            Dictionary<(int col, int row), Rectangle> regionMap = new();
            for (int i = 0; i < PartitionAmount; i++)
            {
                int row = i / Columns;
                int col = i % Columns;
                int x = col * partWidth;
                int y = row * partHeight;
                int width = (col == Columns - 1) ? bmp.Width - x : partWidth;
                int height = (row == Rows - 1) ? bmp.Height - y : partHeight;
                regionMap[(col, row)] = new Rectangle(x, y, width, height);
            }

            byte[] dmxValues = new byte[511];
            Dictionary<(int col, int row), (long sumR, long sumG, long sumB, int count)> regionSums
                = new Dictionary<(int col, int row), (long, long, long, int)>();

            BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                                              ImageLockMode.ReadOnly,
                                              PixelFormat.Format24bppRgb);

            unsafe
            {
                byte* ptr = (byte*)bmpData.Scan0;
                int stride = bmpData.Stride;

                var regionKeys = regionMap.Keys.ToArray();

                Parallel.For(0, regionKeys.Length, i =>
                {
                    var key = regionKeys[i];
                    var rect = regionMap[key];

                    long localSumR = 0, localSumG = 0, localSumB = 0;

                    Parallel.For(rect.Top, rect.Bottom, y =>
                    {
                        long rowSumR = 0, rowSumG = 0, rowSumB = 0;

                        byte* rowPtr = ptr + y * stride;
                        byte* p = rowPtr + rect.Left * 3;

                        for (int x = 0; x < rect.Width; x++)
                        {
                            rowSumB += p[0];
                            rowSumG += p[1];
                            rowSumR += p[2];
                            p += 3;
                        }
                        Interlocked.Add(ref localSumR, rowSumR);
                        Interlocked.Add(ref localSumG, rowSumG);
                        Interlocked.Add(ref localSumB, rowSumB);
                    });
                    lock (regionSums)
                    {
                        regionSums[key] = (localSumR, localSumG, localSumB, rect.Width * rect.Height);
                    }
                });

                // Compute global sum by reusing region sums
                long globalR = 0, globalG = 0, globalB = 0;
                int globalCount = 0;
                foreach (var sums in regionSums.Values)
                {
                    globalR += sums.sumR;
                    globalG += sums.sumG;
                    globalB += sums.sumB;
                    globalCount += sums.count;
                }

                foreach (var fixture in show.ShowList)
                {
                    long sumR = 0, sumG = 0, sumB = 0;
                    int pixelCount = 0;

                    if (fixture.Type == Fixture.ColourMode.Partitioned)
                    {
                        var pos = fixture.Position;
                        if (pos.x.HasValue && pos.y.HasValue)
                        {
                            var sums = regionSums[(pos.x.Value, pos.y.Value)];
                            sumR = sums.sumR;
                            sumG = sums.sumG;
                            sumB = sums.sumB;
                            pixelCount = sums.count;
                        }
                    }
                    else if (fixture.Type == Fixture.ColourMode.Global)
                    {
                        sumR = globalR;
                        sumG = globalG;
                        sumB = globalB;
                        pixelCount = globalCount;
                    }

                    byte r = 0, g = 0, b = 0, intensity = 0;

                    if (pixelCount > 0)
                    {
                        r = (byte)(sumR / pixelCount);
                        g = (byte)(sumG / pixelCount);
                        b = (byte)(sumB / pixelCount);

                        if (SaturationBoost != 1.0)
                        {
                            ApplySaturation(ref r, ref g, ref b, SaturationBoost);
                        }

                        r = GammaLUT[r];
                        g = GammaLUT[g];
                        b = GammaLUT[b];

                        if (UseAudioReactiveMode)
                        {
                            intensity = GetAudioIntensity();
                        }
                        else
                        {
                            intensity = (byte)Math.Max(r, Math.Max(g, b));
                        }
                    }

                    (byte r, byte g, byte b) trueLime = (191, 255, 0);
                    (byte r, byte g, byte b) trueIndigo = (75, 0, 130);
                    
                    //byte trueLimeAvg = (byte)((trueLime.Item1 + trueLime.Item2 + trueLime.Item3) / 3);
                    //byte trueIndigoAvg = (byte)((trueIndigo.Item1 + trueIndigo.Item2 + trueIndigo.Item3) / 3);
                    //byte indigo = (byte)(Math.Abs((r - trueIndigo.Item1) + (g - trueIndigo.Item2) + (b - trueIndigo.Item3)) / 3) / (trueIndigoAvg);
                    //byte lime = (byte)(Math.Abs((r - trueLime.Item1) + (g - trueLime.Item2) + (b - trueLime.Item3))/3) / (trueLimeAvg);

                    double distIndigo = Math.Sqrt(Math.Pow(r - trueIndigo.r, 2) +
                                  Math.Pow(g - trueIndigo.g, 2) +
                                  Math.Pow(b - trueIndigo.b, 2));

                    double distLime = Math.Sqrt(Math.Pow(r - trueLime.r, 2) +
                                                Math.Pow(g - trueLime.g, 2) +
                                                Math.Pow(b - trueLime.b, 2));

                    // 255.0 means it fades out over the whole color space.
                    // 100.0 means it only lights up if the color is VERY close.
                    double radius = 100;

                    double indigoFactor = Math.Max(0, 1.0 - (distIndigo / radius));
                    double limeFactor = Math.Max(0, 1.0 - (distLime / radius));

                    byte indigo = (byte)(indigoFactor * 255);
                    byte lime = (byte)(limeFactor * 255);

                    // Map fixture modes to values
                    var channelValues = new Dictionary<FixtureMode, byte>
                    {
                        { FixtureMode.Intensity, intensity },
                        { FixtureMode.Red, r },
                        { FixtureMode.Green, g },
                        { FixtureMode.Blue, b },
                        { FixtureMode.Indigo, indigo },
                        { FixtureMode.Lime, lime },
                        { FixtureMode.Zoom, FixedZoomLevel },
                    };

                    foreach (var kvp in fixture.ChannelMapping)
                    {
                        int dmxIndex = fixture.StartingAddress - 1 + kvp.Value;
                        FixtureMode mode = kvp.Key;

                        if (dmxIndex >= 0 && dmxIndex < dmxValues.Length &&
                            channelValues.TryGetValue(mode, out byte value))
                        {
                            dmxValues[dmxIndex] = value;
                        }
                    }
                }
            }

            bmp.UnlockBits(bmpData);
            return dmxValues;
        }

        public static async Task LoadShowConfigAsync(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Config file not found", filePath);

            string json = await File.ReadAllTextAsync(filePath);
            var config = JsonSerializer.Deserialize<ShowConfig>(json);

            if (config == null) return; 
            if (config.Rows > 0) Rows = config.Rows;
            if (config.Columns > 0) Columns = config.Columns;

            FixedZoomLevel = config.GlobalZoom;

            GammaValue = config.Gamma > 0 ? config.Gamma : 1.0;
            SaturationBoost = config.Saturation;
            UseAudioReactiveMode = config.AudioReactive;
            ColorMatchRadius = config.ColorRadius > 0 ? config.ColorRadius : 150.0;

            show.ShowList.Clear();

            foreach (var fc in config.Fixtures)
            {
                var fixtureTemplate = Fixture.Fixtures.FirstOrDefault(f => f.Name == fc.Name);
                if (fixtureTemplate != null)
                {
                    int x = fc.Position?.X ?? 0;
                    int y = fc.Position?.Y ?? 0;
                    show.AddLightFromExisting(fixtureTemplate, fc.StartingAddress, x, y);
                }
            }
        }

        public static void StartAudioCapture()
        {
            if (loopback != null)
                return;

            loopback = new WasapiLoopbackCapture();
            loopback.DataAvailable += (s, e) =>
            {
                int samples = e.BytesRecorded / 4;
                float sumSquares = 0;

                for (int i = 0; i < e.BytesRecorded; i += 4)
                {
                    float sample = BitConverter.ToSingle(e.Buffer, i);
                    sumSquares += sample * sample;
                }

                float rms = (float)Math.Sqrt(sumSquares / samples);

                // Scale RMS to a 0-1 range suitable for DMX intensity
                currentAudioLevel = rms;
            };


            loopback.StartRecording();
        }

        public static void StopAudioCapture()
        {
            loopback?.StopRecording();
            loopback?.Dispose();
            loopback = null;
            currentAudioLevel = 0;
        }

        public static byte GetAudioIntensity()
        {
            // Amplify gently
            return (byte)Math.Clamp(currentAudioLevel * 255f * 2f, 0, 255);
        }
    }
}

