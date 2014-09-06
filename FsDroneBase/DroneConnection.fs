//provides basic connectivity to the drone
//including the ability to send commnads and receive telemetry and configuration data
namespace FsDrone
open Extensions
open System
open System.Net.Sockets
open System.Net
open System.Text
open System.IO
open IOUtils
open FsDrone

module private ConnectionServices =

    let droneHost     = "192.168.1.1"
    let controlPort   = 5559
    let videoPort     = 5555
    let commandPort   = 5556
    let telemetryPort = 5554 //navdata

    open FsDrone.CommandUtils

    let createSender token (skt:Socket) endpoint fMonitor fSetTimestamp =
        let seqNum = ref 0
        let buffer = new WriteBuffer(1000)
        Agent.Start(
            (fun inbox -> 
            async {
                while true do
                    try
                        let! msg = inbox.Receive()
                        seqNum := !seqNum + 1
                        buffer.Reset()
                        toATCommand buffer.TextWriter !seqNum msg
                        skt.SendTo(buffer.ByteArray,buffer.Length, SocketFlags.None, endpoint) |> ignore
                        fSetTimestamp()
                    with ex -> 
                        logEx ex
                        fMonitor (CommandPortError ex)
            }),
            token)
    
    let telemetryReceiveLoop seqNum (skt:Socket) endpoint (buffer:ReadBuffer) fTelemetryProcessor fError =
        async {
            while true do 
                try
                    buffer.Reset()
                    let! read = skt.AsyncReceiveFrom(buffer.ByteArray,endpoint)
                    buffer.PrepareForRead read
//                    printfn "Read %d" read
//                    printfn "%A" buffer.ByteArray
                    seqNum := fTelemetryProcessor fError buffer !seqNum 
                with ex ->
                    logEx ex
                    fError (ReceiveError ex)
            }

    let telemtryPortKeepAliveLoop (skt:Socket) endpoint fError =
        async {
            while true do
                try
                    do! Async.Sleep 200
                    skt.SendTo([|1uy|],endpoint) |> ignore
                with ex -> 
                    fError (KeepAliveError ex)
            }

    let startReceiver token (skt:Socket) endpoint fTelemetryProcessor fError =
        let buffer = ReadBuffer(4096)
        let seqNum = ref 0u
        let receiveLoop = telemetryReceiveLoop seqNum skt endpoint buffer fTelemetryProcessor fError
        Async.Start (receiveLoop, token)
        Async.Start (telemtryPortKeepAliveLoop skt !endpoint fError, token)


    let configLoop (cfgClient:TcpClient) str fMonitor fConfiguration =
        async {
            while true do
                try
                    do! Async.Sleep 300
                    if cfgClient.Available > 0 then
                        Configuration.scanConfig str fConfiguration
                with ex -> 
                    ex |> ConfigError |> ConfigPortError |> fMonitor
        }


type DroneConnection(fMonitor, fTelemetry, fConfiguration) = 
    let droneAddr = IPAddress.Parse(ConnectionServices.droneHost)
    let rcvEndpt  = ref (IPEndPoint(droneAddr,ConnectionServices.telemetryPort) :> EndPoint)
    let sndEndpt  = IPEndPoint(droneAddr,ConnectionServices.commandPort)
    let cfgEndpt  = IPEndPoint(droneAddr,ConnectionServices.controlPort)

    let sndSocket = new Socket(AddressFamily.InterNetwork,SocketType.Dgram, ProtocolType.Udp)
    let rcvSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram,ProtocolType.Udp)
    let cfgClient = new TcpClient()

    let connect() =
        try
            sndSocket.Bind(IPEndPoint(IPAddress.Any,ConnectionServices.commandPort))
            sndSocket.SendTo([|1uy|],sndEndpt) |> ignore
            rcvSocket.Bind(IPEndPoint(IPAddress.Any,ConnectionServices.telemetryPort))
            cfgClient.Connect(cfgEndpt)
        with ex ->
            logEx ex
            sndSocket.Dispose()
            rcvSocket.Dispose()
            cfgClient.Close(); (cfgClient :> IDisposable).Dispose()
            fMonitor(ConnectionError ex)
            raise ex

    do connect()

    let mutable lastCommandSent = DateTime.MinValue.ToBinary()

    let setLastCommandSent() = 
        let mutable newTicks = DateTime.Now.ToBinary()
        let _ = System.Threading.Interlocked.Exchange(&lastCommandSent, newTicks)
        ()

    let getLastCommandSent() = 
        let currentTics = System.Threading.Interlocked.Read(&lastCommandSent)
        DateTime.FromBinary(currentTics)

    let cts = new System.Threading.CancellationTokenSource()
    //
    // start telemetry agents
    let fTelemeteryProcessor = Parsing.processTelemeteryData fTelemetry
    let fRecvError = fMonitor<<TelemeteryPortError 
    let receiver = ConnectionServices.startReceiver cts.Token rcvSocket rcvEndpt fTelemeteryProcessor fRecvError
    //
    // start command sender agent
    let sender = ConnectionServices.createSender cts.Token sndSocket sndEndpt fMonitor setLastCommandSent
    //
    // start config reader agent
    let configReader = new BinaryReader(cfgClient.GetStream())
    do Async.Start(ConnectionServices.configLoop cfgClient configReader fMonitor fConfiguration, cts.Token)

    member x.Cmds = sender
    member x.LastCommandSent = getLastCommandSent

    interface IDisposable with 
        member x.Dispose() = 
            try
                cts.Cancel()
                sndSocket.Close(); sndSocket.Dispose()
                rcvSocket.Close(); rcvSocket.Dispose()
            with ex ->
                logEx ex
