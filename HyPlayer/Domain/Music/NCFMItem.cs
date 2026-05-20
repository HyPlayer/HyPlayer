namespace HyPlayer.Domain.Music
{
    public class NCFmItem : NCSong
    {
        public string Description { get; set; }
        public string FMId { get; set; }
        public string RadioId { get; set; }
        public string RadioName { get; set; }
    }
}
