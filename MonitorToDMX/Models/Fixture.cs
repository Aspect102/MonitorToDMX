namespace MonitorToDMX.Models
{
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

        public static List<Fixture> Fixtures { get; } = new()
{
    new Fixture("Fresnel V", new Dictionary<Fixture.FixtureMode, int>
    {
        { Fixture.FixtureMode.Intensity, 0 },
        { Fixture.FixtureMode.Red, 1 },
        { Fixture.FixtureMode.Green, 2 },
        { Fixture.FixtureMode.Blue, 3 },
        { Fixture.FixtureMode.Indigo, 4 },
        { Fixture.FixtureMode.Lime, 5 },
        { Fixture.FixtureMode.Strobe, 6 },
        { Fixture.FixtureMode.Zoom, 7 },
        { Fixture.FixtureMode.Fan, 8 },
    }, Fixture.ColourMode.Partitioned),

    new Fixture("PAR", new Dictionary<Fixture.FixtureMode, int>
    {
        { Fixture.FixtureMode.Intensity, 0 },
        { Fixture.FixtureMode.Red, 1 },
        { Fixture.FixtureMode.Green, 2 },
        { Fixture.FixtureMode.Blue, 3 },
        { Fixture.FixtureMode.Strobe, 4 },
    }, Fixture.ColourMode.Global),

    new Fixture("UKing", new Dictionary<Fixture.FixtureMode, int>
    {
        { Fixture.FixtureMode.Intensity, 0 },
        { Fixture.FixtureMode.Red, 1 },
        { Fixture.FixtureMode.Green, 2 },
        { Fixture.FixtureMode.Blue, 3 },
        { Fixture.FixtureMode.Strobe, 4 },
    }, Fixture.ColourMode.Partitioned)
};


        public (int? x, int? y) Position { get; set; }


        private int _startingAddress = 1;
        public int StartingAddress
        {
            get => _startingAddress;
            set
            {
                if (value < 1 || value > 512)
                    throw new ArgumentOutOfRangeException(nameof(StartingAddress), "StartingAddress must be between 1 and 512");

                _startingAddress = value;
            }
        }

        public string Name { get; set; }

        public Dictionary<FixtureMode, int> ChannelMapping { get; set; }

        public ColourMode Type { get; set; }

        public Fixture(string name, Dictionary<FixtureMode, int> channels, ColourMode type)
        {
            Name = name;
            ChannelMapping = channels;
            Type = type;
        }

        public Fixture(Fixture other) //copies a fixture, usually from the list
        {
            Name = other.Name;
            ChannelMapping = new Dictionary<FixtureMode, int>(other.ChannelMapping);
            Type = other.Type;
            Position = other.Position;
            StartingAddress = other.StartingAddress;
        }
    }
}
