//provides basic connectivity to the drone
//including the ability to send commnads and receive telemetry data
namespace FsDrone
open Extensions
open System
open System.Net.Sockets
open System.Net
open System.Text
open System.IO
open IOUtils

module private Services =

    let droneHost     = "192.168.1.1"
    let controlPort   = 5559
    let videoPort     = 5555
    let commandPort   = 5556
    let telemetryPort = 5554 //navdata

    open FsDrone.CommandUtils

    let createSender token (skt:Socket) endpoint (monitor:Agent<MonitorMsg>) =
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
                        let atCommand = toATCommand buffer.TextWriter !seqNum msg
                        skt.SendTo(buffer.ByteArray,buffer.Length, SocketFlags.None, endpoint) |> ignore
                    with ex -> 
                        logEx ex
                        ex |> CommandPortError |> monitor.Post
            }),
            token)

    let startReceiver token (skt:Socket) endpoint fTelemetryProcessor fError =
        //
        let buffer = ReadBuffer(4096)
        let seqNum = ref 0u
        Async.Start (
            async {
                while true do 
                    try
                        buffer.Reset()
                        let! read = skt.AsyncReceiveFrom(buffer.ByteArray,endpoint)
                        buffer.PrepareForRead read
                        seqNum := fTelemetryProcessor fError buffer !seqNum 
                    with ex ->
                        logEx ex
                        fError (UnhandledException ex) },
            token)


type DroneConnection(monitorAgent:Agent<MonitorMsg>) = 
    let droneAddr = IPAddress.Parse(Services.droneHost)
    let rcvEndpt:EndPoint ref = ref (IPEndPoint(droneAddr,Services.telemetryPort) :> EndPoint)
    let sndEndpt = IPEndPoint(droneAddr,Services.commandPort)
    let sndSocket = new Socket(SocketType.Dgram, ProtocolType.Udp)
    let rcvSocket = new Socket(SocketType.Stream,ProtocolType.Tcp)

    let connect() =
        try
            rcvSocket.Connect(!rcvEndpt)
        with ex ->
            logEx ex
            sndSocket.Dispose()
            rcvSocket.Dispose()
            monitorAgent.Post(ConnectionError ex)
            raise ex

    do connect()

    let cts = new System.Threading.CancellationTokenSource()
    let telemetryObserver,fPost = Observable.createObservableAgent<Telemetry> cts.Token
    let fTelemetry = Parsing.processTelemeteryData fPost
    let sender   = Services.createSender cts.Token sndSocket sndEndpt monitorAgent
    let receiver = Services.startReceiver cts.Token rcvSocket rcvEndpt fTelemetry

    member x.Telemetry = telemetryObserver
    member x.Cmds = sender

    interface IDisposable with 
        member x.Dispose() = 
            cts.Cancel()
            sndSocket.Close(); sndSocket.Dispose()
            rcvSocket.Close(); rcvSocket.Dispose()




