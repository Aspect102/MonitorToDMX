using Dmx.Net.Common;
using Dmx.Net.Controllers;
using System.Drawing.Imaging;

class Program
{
    private static DmxTimer dmxTimer = new DmxTimer();
    private static OpenDmxController dmxController = new OpenDmxController(dmxTimer);
    private static int partitionAmount = 4;
    static void Main(string[] args)
    {
        if (dmxController.IsOpen == false)
        {
            try
            {
                dmxController.Open(0);
            }
            catch (Exception e)
            {
                // try again later
            }
        }

        dmxTimer.Start();

        // This will send DMX data from channel 1, and then all parameters afterwards--
        // -- will be the DMX value for each channel in order afterwards.
        while (true)
        {
            Bitmap screenshot = CaptureScreen();
            byte[] averages = ComputeAverages(screenshot).ToArray();
            dmxController.SetChannelRange(1, averages);
            Console.WriteLine("Writing: " + AverageToString(averages));
            //Thread.Sleep(30);
        }
    }
    static string AverageToString(byte[] averages)
    {
        string str = String.Empty;
        for (int i = 0; i < averages.Length; i++)
        {
            str += averages[i] + ",";
        }
        return str;
    }
    static Bitmap CaptureScreen()
    {
        var bounds = Screen.PrimaryScreen.Bounds;
        var bmpScreenshot = new Bitmap(bounds.Width,
                                       bounds.Height,
                                       PixelFormat.Format24bppRgb);
        using (Graphics g = Graphics.FromImage(bmpScreenshot))
        {
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
        }
        return bmpScreenshot;
    }
    static List<byte> ComputeAverages(Bitmap bmp)
    {
        int partWidth = bmp.Width / 2;
        int partHeight = bmp.Height / 2;

        // Map partitions according to your light setup
        List<Rectangle> regions = new List<Rectangle>
    {
        new Rectangle(partWidth, 0, partWidth, partHeight),
        new Rectangle(partWidth, partHeight, partWidth, partHeight),
        new Rectangle(0, partHeight, partWidth, partHeight),
        new Rectangle(0, 0, partWidth, partHeight),
    };

        List<byte> averagesList = new List<byte>();

        foreach (var rect in regions)
        {
            Bitmap partBmp = bmp.Clone(rect, bmp.PixelFormat);

            long sumR = 0, sumG = 0, sumB = 0;
            int pixelCount = partBmp.Width * partBmp.Height;

            BitmapData bmpData = partBmp.LockBits(
                new Rectangle(0, 0, partBmp.Width, partBmp.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);

            unsafe
            {
                byte* ptr = (byte*)bmpData.Scan0;
                int stride = bmpData.Stride;

                for (int y = 0; y < bmpData.Height; y++)
                {
                    byte* row = ptr + (y * stride);
                    for (int x = 0; x < bmpData.Width; x++)
                    {
                        byte b = row[x * 3 + 0];
                        byte g = row[x * 3 + 1];
                        byte r = row[x * 3 + 2];

                        sumR += r;
                        sumG += g;
                        sumB += b;
                    }
                }
            }

            partBmp.UnlockBits(bmpData);

            if ((sumR + sumG + sumB) / pixelCount <= 0)
            {
                averagesList.Add(0); averagesList.Add(0); averagesList.Add(0);
                averagesList.Add(0); averagesList.Add(0); averagesList.Add(0);
                averagesList.Add(0);
            }
            else
            {
                long sumIntensity = Math.Max(sumR, Math.Max(sumG, sumB));
                averagesList.Add((byte)(sumIntensity / pixelCount));
                averagesList.Add((byte)(sumR / pixelCount));
                averagesList.Add((byte)(sumG / pixelCount));
                averagesList.Add((byte)(sumB / pixelCount));
                averagesList.Add(0); averagesList.Add(0); averagesList.Add(0);
            } 
            partBmp.Dispose();
        }

        return averagesList;
    }

}
