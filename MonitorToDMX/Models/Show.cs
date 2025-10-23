namespace MonitorToDMX.Models
{
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

        public void AddLightFromExisting(Fixture fixture, int startingAddress, int? x = null, int? y = null)
        {
            var copy = new Fixture(fixture); // make a copy
            copy.StartingAddress = startingAddress;
            copy.Position = (x, y);
            ShowList.Add(copy);
        }

        public Show(List<Fixture>? showList = null)
        {
            ShowList = showList ?? new List<Fixture>();
        }
    }

    public class FixtureConfig
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public int StartingAddress { get; set; }
        public PositionConfig? Position { get; set; } // nullable for global fixtures
    }

    public class PositionConfig
    {
        public int X { get; set; }
        public int Y { get; set; }
    }
    public class ShowConfig
    {
        public List<FixtureConfig> Fixtures { get; set; }
    }
}
