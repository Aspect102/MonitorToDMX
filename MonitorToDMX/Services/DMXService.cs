using Dmx.Net.Common;
using Dmx.Net.Controllers;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using MonitorToDMX.Models;
using NAudio.Wave;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using static MonitorToDMX.Models.Fixture; // Add this for MainThread

namespace MonitorToDMX.Services
{
    class DMXService
    {
        private static DmxTimer dmxTimer = new DmxTimer();
        private static IController dmxController = ControllerManager.RegisterController<OpenDmxController>(1, dmxTimer);
        private static bool debugMode = false;
        private static int sens = 0; // sensitivity threshold (0-255)
        private static CancellationTokenSource dmxCancel;

        // --- NEW: Gamma Correction Lookup Table ---
        private static readonly byte[] GammaLUT = new byte[] {
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2,
            2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5,
            5, 5, 6, 6, 6, 7, 7, 7, 8, 8, 8, 9, 9, 9, 10, 10,
            11, 11, 11, 12, 12, 13, 13, 13, 14, 14, 15, 15, 16, 16, 17, 17,
            18, 18, 19, 19, 20, 20, 21, 22, 22, 23, 23, 24, 25, 25, 26, 27,
            27, 28, 29, 29, 30, 31, 32, 32, 33, 34, 35, 35, 36, 37, 38, 39,
            40, 41, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54,
            55, 56, 57, 58, 59, 60, 61, 62, 63, 65, 66, 67, 68, 69, 70, 71,
            73, 74, 75, 76, 77, 79, 80, 81, 82, 84, 85, 86, 88, 89, 90, 92,
            93, 94, 96, 97, 99, 100, 101, 103, 104, 106, 107, 109, 110, 112, 113, 115,
            116, 118, 119, 121, 122, 124, 126, 127, 129, 131, 132, 134, 136, 137, 139, 141,
            143, 144, 146, 148, 150, 152, 153, 155, 157, 159, 161, 163, 165, 166, 168, 170,
            172, 174, 176, 178, 180, 182, 184, 186, 188, 191, 193, 195, 197, 199, 201, 203,
            206, 208, 210, 212, 215, 217, 219, 221, 224, 226, 228, 231, 233, 235, 238, 240,
            243, 245, 248, 250, 253, 255
        };

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

            // Precompute regions
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
                            // p[0] is Blue, p[1] is Green, p[2] is Red
                            // We convert to linear BEFORE adding
                            rowSumB += GammaLUT[p[0]];
                            rowSumG += GammaLUT[p[1]];
                            rowSumR += GammaLUT[p[2]];
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

                    static double sRGBtoLin(double colorChannel)
                    {
                        if (colorChannel <= 0.04045)
                        {
                            return colorChannel / 12.92;
                        }
                        else
                        {
                            return Math.Pow(((colorChannel + 0.055) / 1.055), 2.4);
                        }
                    }

                    static double YtoLstar(double Y)
                    {
                        // Send this function a luminance value between 0.0 and 1.0,
                        // and it returns L* which is "perceptual lightness"

                        if (Y <= ((double)216 / 24389))
                        {       // The CIE standard states 0.008856 but 216/24389 is the intent for 0.008856451679036
                            return Y * ((double)24389 / 27);  // The CIE standard states 903.3, but 24389/27 is the intent, making 903.296296296296296
                        }
                        else
                        {
                            return Math.Pow(Y, (1 / 3)) * 116 - 16;
                        }
                    }

                    if (pixelCount > 0)
                    {
                        // --- CHANGED: Simplified Averaging ---
                        // We summed Linear values, so simple average is correct.
                        r = (byte)(sumR / pixelCount);
                        g = (byte)(sumG / pixelCount);
                        b = (byte)(sumB / pixelCount);

                        if (UseAudioReactiveMode)
                        {
                            intensity = GetAudioIntensity();
                        }
                        else
                        {
                            // Better logic for non-audio mode:
                            // Use the brightest color as intensity (so black screen = lights off)
                            intensity = (byte)Math.Max(r, Math.Max(g, b));
                            // intensity = 255; 
                        }
                    }

                    var indigo = (byte)Math.Min(255, r * 0.2 + b * 1.0);
                    var lime = (byte)Math.Min(255, r * 0.5 + g * 0.8 + b * 0.1);

                    // Map fixture modes to values
                    var channelValues = new Dictionary<FixtureMode, byte>
                    {
                        { FixtureMode.Intensity, intensity },
                        { FixtureMode.Red, r },
                        { FixtureMode.Green, g },
                        { FixtureMode.Blue, b },
                        { FixtureMode.Indigo, indigo },
                        { FixtureMode.Lime, lime }
                    };

                    // Assign DMX values based on the mapping
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
                currentAudioLevel = rms; // do NOT multiply by 6f here!
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

