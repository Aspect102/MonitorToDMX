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

        public static List<Fixture> Fixtures = new();

        public (int? x, int? y) Position { get; set; }

        public int StartingAddress { get; set; }

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
