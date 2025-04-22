namespace SteamCmdWeb.Models
{
    public class ClientAutoRegisterRequest
    {
        public string ClientId { get; set; }
        public string Description { get; set; }
        public string Address { get; set; }
        public int Port { get; set; } = 61188;
        public string AuthToken { get; set; }
    }

    public class ClientAddressUpdateRequest
    {
        public string ClientId { get; set; }
        public string NewAddress { get; set; }
        public int Port { get; set; } = 61188;
        public string AuthToken { get; set; }
    }
}