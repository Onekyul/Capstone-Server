namespace GameServer.DTO
{
    public class ServerRegisterReq
    {
        public string ServerId { get; set; }
        public int Port { get; set; }
    }

    public class ServerIdleReq
    {
        public string ServerId { get; set; }
    }
}
