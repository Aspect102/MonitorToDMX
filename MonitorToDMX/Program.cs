using Dmx.Net.Common;
using Dmx.Net.Controllers;
using NAudio;
using System.Drawing.Imaging;
using System.Text;
using Terminal.Gui;
using Terminal.Gui.TextValidateProviders;
using Application = Terminal.Gui.Application;
using Button = Terminal.Gui.Button;
using CheckBox = Terminal.Gui.CheckBox;
using Color = Terminal.Gui.Color;
using Label = Terminal.Gui.Label;
using MessageBox = Terminal.Gui.MessageBox;
using Padding = Terminal.Gui.Padding;
using View = Terminal.Gui.View;

// Copyright (c) 2025 Zac Grey
// Licensed under the MIT License. See LICENSE file in the project root for license information.

class Program
{
    private static DmxTimer dmxTimer = new DmxTimer();
    private static OpenDmxController dmxController = new OpenDmxController(dmxTimer);
    private static int partitionAmount = 4;

    static void Main(string[] args)
    {
        Application.Run<ExampleWindow>().Dispose();

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
        // To see this output on the screen it must be done after shutdown,
        // which restores the previous screen.
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
        Application.Shutdown();
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

public class ExampleWindow : Window
{

    public ExampleWindow()
    {
        #region main window
        Title = $"MonitorToDMX ({Application.QuitKey} to quit)";

        var selectRadioBtn = new RadioGroup()
        {
            RadioLabels = ["Manual Colour Mode", "Monitor Colour Mode"],
        };
        Add(selectRadioBtn);

        var licenseLbl = new Label()
        {
            X = Pos.AnchorEnd(),
            Y = Pos.AnchorEnd(),
            Text = "Copyright © 2025 Zac Grey"
        };
        Add(licenseLbl);
        #endregion

        #region manual window

        var manualWindow = new Window()
        {
            Y = Pos.Bottom(selectRadioBtn) + 1,
            Title = "manual controls",
            Width = Dim.Auto(),
        };

        var manualRGB = new ColorPicker()
        {
            Y = Pos.Align(Alignment.Start),
            Style = new ColorPickerStyle { ColorModel = ColorModel.RGB, ShowColorName = true, ShowTextFields = true },
        };
        manualRGB.ApplyStyleChanges();
        manualWindow.Add(manualRGB);

        Add(manualWindow);
        #endregion

        #region monitor window
        var monitorWindow = new Window()
        {
            Y = Pos.Bottom(selectRadioBtn) + 1,
            X = Pos.Right(manualWindow) + 1,
            Title = "monitor settings",
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
            Provider = new TextRegexProvider(@"^(?:0|[1-9][0-9]{0,3}|10000)$"), // regex for 0-10000
            Text = "0",
            Width = Dim.Width(delayTxtWindow),
        };
        delayTxtWindow.Add(delayTxt);

        Add(monitorWindow);
        #endregion
    }
}