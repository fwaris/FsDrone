namespace FsDrone
open Extensions
open System
open System.Net.Sockets
open System.Net
open System.Text

module Controller =

    let droneHost     = "192.168.1.1"
    let controllPort  = 5559
    let videoPort     = 5555
    let commandPort   = 5556
    let telemetryPort = 5554 //navdata

    let openUdp (host:string) port = 
        let client = new UdpClient()
        client.Connect(host,port)
        client

    let openTcp (host:string) port =
        let client = new TcpClient()
        client.Connect(host,port)
        client
    
    let _takeoff = 0b00010001010101000000000100000000
    let _land    = 0b00010001010101000000000000000000
    let _emrgncy = 0b00010001010101000000000010000000

    let toATCommand seq_no = function
        | Land          -> sprintf "AT*REF%d,%d\r" seq_no _land
        | Takeoff       -> sprintf "AT*REF%d,%d\r" seq_no _takeoff
        | Emergency     -> sprintf "AT*REF%d,%d\r" seq_no _emrgncy
        | Hover         -> sprintf "AT*PCMD=%d,%d,%d,%d,%d,%d\r" seq_no 0 0 0 0 0
        | Progress (p)  -> sprintf "AT*PCMD_MAG=%d,%d,%f,%f,%f,%f,%f,%f\r" seq_no 1 p.Roll p.Pitch p.Lift p.Yaw p.Psi p.PsiAccuracy
        | Flattrim      -> sprintf "AT*FTRIM=%d,\r" seq_no
        | Calibrate i   -> sprintf "AT*CALIB=%d,%d,\r" seq_no i
        | Config (k,v)  -> sprintf """AT*CONFIG=%d,"%s","%s"\r""" seq_no k v
        | Watchdog      -> sprintf "AT*COMWDG=%d\r" seq_no

    let openSender token (client:UdpClient)  =
        let seqNum = ref 0
        Agent.Start(
            (fun inbox -> 
            async {
                while true do
                    let! msg = inbox.Receive()
                    seqNum := !seqNum + 1
                    let atCommand = toATCommand !seqNum msg
                    let bytes = atCommand |> Encoding.ASCII.GetBytes
                    client.Send(bytes,bytes.Length) |> ignore
            }),
            token)

type DroneConnection = 
    abstract Cmds:Agent<Command>
    abstract Emergency:Agent<Command>
    abstract Telemtery:IObservable<Telemetery>