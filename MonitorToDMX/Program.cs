using Dmx.Net.Common;
using Dmx.Net.Controllers;
using System.Collections.ObjectModel;
using System.Drawing.Imaging;
using Terminal.Gui;
using Terminal.Gui.TextValidateProviders;
using Application = Terminal.Gui.Application;
using Button = Terminal.Gui.Button;
using ComboBox = Terminal.Gui.ComboBox;
using Label = Terminal.Gui.Label;
using Shortcut = Terminal.Gui.Shortcut;

// Copyright (c) 2025 Zac Grey
// Licensed under the MIT License. See LICENSE file in the project root for license information.

class Program
{
    private static DmxTimer dmxTimer = new DmxTimer();
    private static IController dmxController = ControllerManager.RegisterController<OpenDmxController>(1, dmxTimer);
    private static int cols = 4;
    private static int rows = 3;
    private static int partitionAmount = cols * rows;
    private static bool debugMode = false;
    private static int sens = 0; // sensitivity threshold (0-255)
    private static CancellationTokenSource dmxCancel;

    public static MainWindow mainWindow;
    

    static void Main(string[] args)
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

        mainWindow = new MainWindow();
        Application.Run<MainWindow>();
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
                    byte[] dmxBuffer = ComputeDmxBuffer(screenshot);
                    dmxController.SetChannelRange(1, dmxBuffer);

                    Application.Invoke(() =>
                    {
                        mainWindow.dataTbl.Add(new DMXTableEntry(DateTime.Now, AverageToString(dmxBuffer)));

                        var columns = new Dictionary<string, Func<DMXTableEntry, object>>
                        {
                            { "Timestamp", d => d.Timestamp },
                            { "Data", d => d.Data }
                        };

                        mainWindow.outputTbl.Table = new EnumerableTableSource<DMXTableEntry>(mainWindow.dataTbl, columns);
                        mainWindow.outputTbl.Update(); // refresh
                    });
                }

                await Task.Delay(int.Parse(mainWindow.DelayText), token);
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
        int[] startAddresses = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120];
        int[] parAddresses = [130, 140, 150, 160, 170, 180];

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
    public string DelayText { get; set; }
    public TableView outputTbl { get; set; }

    public ObservableCollection<DMXTableEntry> dataTbl = new ObservableCollection<DMXTableEntry>();

    public MainWindow()
    {
        #region Main Window

        Title = $"MonitorToDMX ({Application.QuitKey} to quit)";

        var selectRadioBtn = new RadioGroup()
        {
            Y = Pos.Align(Alignment.Start) + 2,
            RadioLabels = ["Manual Colour Mode", "Monitor Colour Mode"],
        };

        selectRadioBtn.SelectedItemChanged += (s, e) =>
        {
            if (e.SelectedItem == 0)
                Program.StopDmxLoop();
            else if (e.SelectedItem == 1)
                Program.StartDmxLoop();
        };

        var loadShortcut = new Shortcut()
        {
            X = Pos.Align(Alignment.Start),
            HelpText = "Load Config",
            Key = Key.L,
            Action = () =>
            {
                var openDialog = new OpenDialog()
                {
                    AllowedTypes = new List<IAllowedType> { new JsonFileType() },
                };
                Application.Run(openDialog);
                var jsonFile = openDialog.FilePaths;
            }
        };

        var licenseLbl = new Label()
        {
            X = Pos.AnchorEnd(),
            Y = Pos.AnchorEnd(),
            Text = "Copyright © 2025 Zac Grey"
        };

        #endregion

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

        var addFixtureButton = new ComboBox()
        {
            Y = Pos.Align(Alignment.Start),
            Text = "Add Fixture",
            Width = Dim.Absolute(15),
            Height = Dim.Absolute(15),
        };

        var tblWindow = new Window()
        {
            Y = Pos.Bottom(delayTxtWindow) + 1,
            Title = "Raw Output",
            Width = Dim.Absolute(50),
            Height = Dim.Fill(),
        };

        var fixtureWindow = new Window()
        {
            X = Pos.Right(tblWindow) + 1,
            Y = Pos.Top(delayTxtWindow),
            Title = "Fixtures",
            Width = Dim.Absolute(50),
            Height = Dim.Fill(),
        };

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

        delayTxtWindow.Add(delayTxt);
        fixtureWindow.Add(addFixtureButton);
        tblWindow.Add(outputTbl);
        monitorWindow.Add(delayTxtWindow, tblWindow, fixtureWindow);
        #endregion

        Add(selectRadioBtn, loadShortcut, licenseLbl, manualWindow, monitorWindow);
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
public class JsonFileType : IAllowedType
{
    public bool IsAllowed(string filePath)
    {
        return filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }
}

public class Fixture
{
    public static IEnumerable<string> ChannelDefs = ["Intensity", "Red", "Green", "Blue"];
    public string Name { get; set; }
    public int StartAddress { get; set; }

    public Dictionary<string, int> ChannelMap = new();
    public int ChannelCount { get; set; }
    public string Type { get; set; }

    public Fixture(string name, int startAddress, int channelCount, string type)
    {
        Name = name;
        StartAddress = startAddress;
        ChannelCount = channelCount;
        Type = type;
    }
    
}
