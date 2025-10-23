# MonitorToDMX
MonitorToDMX is a Blazor Hybrid web-app that outputs lighting data through a DSD TECH SH-RS09B USB-to-DMX interface. It replicates on-screen colour by assigning light fixtures to the average colour of defined screen partitions, or using a global average colour for the entire display.

This program was initially built for my own setup of cheap UKing fixtures, later evolving into a C# Console app for the light fixtures in the ALT (Edge) Theatre of the University of Bath.

# How to use
- Download VS solution or the latest release (**I advise you download the solution!**)
- Rows and Columns are how many splits you apply to your screen. E.g, if I wanted 4 equal rectangular regions of my screen (for me thats 1920/4 x 1080/4), I would set the rows/cols to 2,2.
- Then, the position is the 0-based position in the 2d array that your regions construct. E.g, with the 2x2 setup, the positions I can possibly assign are:
- (0,0) TOP LEFT, (0,1) BOTTOM LEFT, (1,0) TOP RIGHT, (1,1) BOTTOM RIGHT
- You can save the current "Show" by clicking the Save button which will save a JSON config file, which you can load at any time.
- Prebuilt JSON files are found in the Config folder for setups that I use.

<img src="https://github.com/user-attachments/assets/36dfe587-3a8b-42df-a76e-3705c492e427" width="756" height="1008">
<img src="https://github.com/user-attachments/assets/a3dead1a-d7de-4ec1-96c4-02eab099f9db" width="1008" height="756">

https://github.com/user-attachments/assets/fe73c314-1984-4376-9b00-0d3b86d3cca9

