using Dmx.Net.Common;
using Dmx.Net.Controllers;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using Terminal.Gui;
using Terminal.Gui.TextValidateProviders;
using Application = Terminal.Gui.Application;
using Label = Terminal.Gui.Label;

// Copyright (c) 2025 Zac Grey
// Licensed under the MIT License. See LICENSE file in the project root for license information.

class Program
{
    private static DmxTimer dmxTimer = new DmxTimer();
    private static IController dmxController = ControllerManager.RegisterController<OpenDmxController>(1, dmxTimer);
    private static int cols = 4;
    private static int rows = 3;
    private static int partitionAmount = cols * rows;

    public static CancellationTokenSource dmxCancel;
    public static MainWindow mainWindow;
    public static int sens = 0; // sensitivity threshold (0-255)

    static void Main(string[] args)
    {
        dmxController.Open(0);
        dmxTimer.Start();
        mainWindow = Application.Run<MainWindow>();
        Application.Shutdown();
    }

    public static void WriteGlobalColour(byte r, byte g, byte b)
    {
        dmxTimer.Start();
        byte[] pattern = new byte[] { 255, r, g, b, 0, 0, 0, 0, 0 }; // 9 channels
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
                    byte[] dmxBuffer = ComputeDmxBuffer(screenshot);
                    dmxController.SetChannelRange(1, dmxBuffer);

                    Application.Invoke(() =>
                    {
                        MainWindow.dataTbl.Add(new DMXTableEntry(DateTime.Now, AverageToString(dmxBuffer)));

                        var columns = new Dictionary<string, Func<DMXTableEntry, object>>
                        {
                            { "Timestamp", d => d.Timestamp },
                            { "Data", d => d.Data }
                        };

                        MainWindow.outputTbl.Table = new EnumerableTableSource<DMXTableEntry>(MainWindow.dataTbl, columns);
                        MainWindow.outputTbl.Update(); // refresh
                    });
                }

                await Task.Delay(int.Parse(MainWindow.DelayText), token);
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
        var bounds = Screen.PrimaryScreen.Bounds;
        var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using (Graphics g = Graphics.FromImage(bmp))
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
        return bmp;
    }

    static byte[] ComputeDmxBuffer(Bitmap bmp)
    {
        int[] startAddresses = new int[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120 };
        int[] parAddresses = new int[] { 130, 140, 150, 160, 170, 180 };

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

                int startAddr = startAddresses[i] - 1;
                dmxValues[startAddr + 0] = intensity;
                dmxValues[startAddr + 1] = r;
                dmxValues[startAddr + 2] = g;
                dmxValues[startAddr + 3] = b;
                dmxValues[startAddr + 4] = 0;
                dmxValues[startAddr + 5] = 0;
                dmxValues[startAddr + 6] = 0;
                dmxValues[startAddr + 7] = 0;
                dmxValues[startAddr + 8] = 0;
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

            foreach (var addr in parAddresses)
            {
                int startAddr = addr - 1;
                dmxValues[startAddr + 0] = parIntensity;
                dmxValues[startAddr + 1] = parR;
                dmxValues[startAddr + 2] = parG;
                dmxValues[startAddr + 3] = parB;
                dmxValues[startAddr + 4] = 0; // Strobe
            }
        }
        bmp.UnlockBits(bmpData);

        return dmxValues;
    }
}

public class MainWindow : Window
{
    public static string DelayText { get; set; }
    public static TableView outputTbl { get; set; }
    public static ObservableCollection<DMXTableEntry> dataTbl = new ObservableCollection<DMXTableEntry>();

    public MainWindow()
    {
        Title = $"MonitorToDMX ({Application.QuitKey} to quit)";

        var selectRadioBtn = new RadioGroup()
        {
            RadioLabels = ["Manual Colour Mode", "Monitor Colour Mode"],
        };
        Add(selectRadioBtn);

        selectRadioBtn.SelectedItemChanged += (s, e) =>
        {
            if (e.SelectedItem == 0)
                Program.StopDmxLoop();
            else if (e.SelectedItem == 1)
                Program.StartDmxLoop();
        };

        var licenseLbl = new Label()
        {
            X = Pos.AnchorEnd(),
            Y = Pos.AnchorEnd(),
            Text = "Copyright © 2025 Zac Grey"
        };
        Add(licenseLbl);

        #region Manual Controls
        var manualWindow = new Window()
        {
            Y = Pos.Bottom(selectRadioBtn) + 1,
            Title = "Manual Controls",
            Width = Dim.Auto(),
        };

        var manualRGB = new ColorPicker()
        {
            Y = Pos.Align(Alignment.Start),
            Style = new ColorPickerStyle { ColorModel = ColorModel.RGB, ShowColorName = true, ShowTextFields = true },
        };
        manualRGB.ApplyStyleChanges();

        manualRGB.ColorChanged += (s, e) =>
        {
            var color = e.CurrentValue;
            Program.WriteGlobalColour(color.R, color.G, color.B);
        };
        manualWindow.Add(manualRGB);
        Add(manualWindow);
        #endregion

        #region Monitor Controls
        var monitorWindow = new Window()
        {
            Y = Pos.Bottom(selectRadioBtn) + 1,
            X = Pos.Right(manualWindow) + 1,
            Title = "Monitor Settings",
            Width = Dim.Auto(),
        };

        var delayTxtWindow = new Window()
        {
            Y = Pos.Align(Alignment.Start),
            Title = "Delay (0-10000 ms)",
            Width = Dim.Absolute(25),
            Height = Dim.Absolute(3),
        };
        monitorWindow.Add(delayTxtWindow);

        var delayTxt = new TextValidateField()
        {
            Y = Pos.Align(Alignment.Start),
            Provider = new TextRegexProvider(@"^(?:0|[1-9][0-9]{0,3}|10000)$"),
            Text = "0",
            Width = Dim.Width(delayTxtWindow),
        };
        DelayText = "0";
        delayTxt.KeyDownNotHandled += (s, e) =>
        {
            DelayText = delayTxt.Text;
        };
        delayTxtWindow.Add(delayTxt);

        var outputWindow = new Window()
        {
            Y = Pos.Align(Alignment.Start),
            Title = "Raw Output",
            Width = Dim.Absolute(50),
            Height = Dim.Fill(),
        };
        monitorWindow.Add(outputWindow);

        var columns = new Dictionary<string, Func<DMXTableEntry, object>>
        {
            { "Timestamp", d => d.Timestamp },
            { "Data", d => d.Data},
        };
        var table = new EnumerableTableSource<DMXTableEntry>(dataTbl, columns);

        outputTbl = new TableView()
        {
            Y = Pos.Align(Alignment.Start),
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Table = table,
        };
        outputWindow.Add(outputTbl);

        Add(monitorWindow);
        #endregion
    }
}

public class DMXTableEntry
{
    public DateTime Timestamp { get; set; }
    public string Data { get; set; }

    public DMXTableEntry(DateTime timestamp, string data)
    {
        Timestamp = timestamp;
        Data = data;
    }
}
