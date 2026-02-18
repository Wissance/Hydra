namespace Wissance.Hydra.Common.Data
{
    public enum OperationType
    {
        StartServer,
        StopServer,
        RestartServer,
        SendDataToClient
    }

    public class OperationResult
    {
        public OperationType Operation { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}