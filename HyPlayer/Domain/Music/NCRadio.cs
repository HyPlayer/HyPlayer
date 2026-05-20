namespace HyPlayer.Domain.Music
{
    public class NCRadio
    {
        public string Cover { get; set; }
        public string Description { get; set; }
        public NCUser DJ { get; set; }
        public string Id { get; set; }
        public string LastProgramName { get; set; }
        public string Name { get; set; }
        public bool HasSubscribed { get; set; }
    }

}
