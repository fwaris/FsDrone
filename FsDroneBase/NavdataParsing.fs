//parse binary data structures received from the drone
namespace FsDrone
open FsDrone
open IOUtils
open System.IO
open System
open Extensions

module Parsing =
    let magic_no1 = 0x55667788u
    let magic_no2 = 0x55667789u

    [<Literal>]
    let CSFlyState = 3us
    let csArray = 
        let csMap = 
            [
                0us, Default
                1us, Init
                2us, Landed
                3us, Flying FlyingState.Ok
                4us, Hovering
                5us, Test
                6us, TakingOff
                7us, GoingToFix
                8us, Landing
                9us, Looping
            ] |> Map.ofList
        let maxVal = (Map.toList >> List.map fst >> List.max >> int) csMap
        let xs = Array.create (maxVal + 1) Default
        csMap |> Map.iter (fun k v -> xs.[int k] <- v)
        xs

    let inline readSat (rdr:BinaryReader) =
        let sat = rdr.ReadByte()
        let cn0 = rdr.ReadByte()
        {SatNum = sat; Cn0 = cn0}

    let checkSum (buf:ReadBuffer) =
        let xs = buf.ByteArray
        let len = buf.Length
        let cks = uint16 NavdataOption.Checksum
        let pred (xs,i) = BitConverter.ToUInt16(xs,i) = cks
        let idx = Array.reverseFind (len-2) pred xs
        if idx = -1 then
            false
        else
            let mutable calculatedSum = 0u
            for i in 0 .. idx-1 do calculatedSum <- calculatedSum + Operators.uint32 xs.[i]
            let givenSum = BitConverter.ToUInt32(xs,idx+4)
            calculatedSum = givenSum

    let gpsEpoch = DateTime(1980,1,6, 0,0,0, DateTimeKind.Utc)

    let inline processGPSOption (rdr:BinaryReader) fPost =
        let lat         = rdr.ReadDouble()
        let lon         = rdr.ReadDouble()
        let elevation   = rdr.ReadDouble()
        let hdop        = rdr.ReadDouble()
        let data_avlbl  = rdr.ReadInt32()
        let zeroVldted  = rdr.ReadInt32()
        let wptVldted   = rdr.ReadInt32()
        let _lat        = rdr.ReadDouble()
        let _lon        = rdr.ReadDouble()
        let latFused    = rdr.ReadDouble()
        let lonFused    = rdr.ReadDouble()
        let gpsState    = rdr.ReadUInt32()
        let xTraj       = rdr.ReadSingle()
        let xRef        = rdr.ReadSingle()
        let yTraj       = rdr.ReadSingle()
        let yRef        = rdr.ReadSingle()
        let thetaP      = rdr.ReadSingle()
        let phiP        = rdr.ReadSingle()
        let thetaI      = rdr.ReadSingle()
        let phiI        = rdr.ReadSingle()
        let thetaD      = rdr.ReadSingle()
        let phiD        = rdr.ReadSingle()
        let vdop        = rdr.ReadDouble()
        let pdop        = rdr.ReadDouble()
        let speed       = rdr.ReadSingle()
        let lastFrameTS = rdr.ReadUInt32()
        let degree      = rdr.ReadSingle()
        let degreeMag   = rdr.ReadSingle()
        let ehpe        = rdr.ReadSingle()
        let ehve        = rdr.ReadSingle()
        let c_n0        = rdr.ReadSingle() // signal-to-noise, avg of 4 best sats
        let numSatsAcqrd= rdr.ReadUInt32()
        let channels    = [for _ in 1 .. 12 -> readSat rdr]
        let gpsPlugged  = rdr.ReadInt32()
        let ephemStts   = rdr.ReadUInt32()
        let vxTraj      = rdr.ReadSingle()
        let vyTraj      = rdr.ReadSingle()
        let frmwrStts   = rdr.ReadUInt32()
        fPost
            (GPS
                { 
                    IsGPSPlugged   = gpsPlugged > 0
                    Fix            = gpsState > 0u
                    Lat            = lat
                    Lon            = lon
                    Alt            = elevation
                    Heading        = float degree
                    Speed          = float speed
                    NumSats        = int numSatsAcqrd
                    SatStrength    = c_n0
                    SatChannels    = channels
                }
            )

    let processDemoOption (rdr:BinaryReader) fPost =
        let flyState     = rdr.ReadUInt16()
        let controlState = rdr.ReadUInt16()
        let batteryLevel = rdr.ReadUInt32()
        let pitch = rdr.ReadSingle()
        let roll  = rdr.ReadSingle()
        let yaw   = rdr.ReadSingle()
        let alt = rdr.ReadInt32()
        let vx = rdr.ReadSingle()
        let vy = rdr.ReadSingle()
        let vz = rdr.ReadSingle()
        let frameIdx = rdr.ReadUInt32()
        fPost
            (NavState 
                (match controlState with 
                | CSFlyState -> Flying (LanguagePrimitives.EnumOfValue flyState) 
                | x -> csArray.[int x]))
        fPost
            (FlightSummary 
                {
                    Velocity        = {Vx=vx; Vy=vy; Vz=vz}
                    RPY             = {Roll=roll; Pitch=pitch; Yaw=yaw}
                    BatteryLevel    = float batteryLevel
                    Altitude        = float alt
                })

    let inline processOption (rdr:BinaryReader) fPost fError = function
        | NavdataOption.Demo -> processDemoOption rdr fPost
        | NavdataOption.GPS  -> processGPSOption rdr fPost
        | o ->  fError (UnhandledOption o)

    let processOptions (rdr:BinaryReader) fPost fError =
        let startPos        = rdr.BaseStream.Position
        let optionTag       = LanguagePrimitives.EnumOfValue (rdr.ReadUInt16())
        let optionSz        = rdr.ReadUInt16()
        let nextOptionPos   = startPos + int64 optionSz
        //
        let rec loop nextOptionPos option = 
            match option with
            | NavdataOption.Checksum -> ()
            | opt -> 
                processOption rdr fPost fError opt
                rdr.BaseStream.Position <- nextOptionPos //set to read next option; some options are skipped so need to reposition
                let optionTag       = LanguagePrimitives.EnumOfValue (rdr.ReadUInt16())
                let optionSz        = rdr.ReadUInt16()
                let nextOptionPos   = nextOptionPos + int64 optionSz
                loop nextOptionPos optionTag
        //
        loop nextOptionPos optionTag

    let headersize = 16

    let processTelemeteryData fPost fError (buff:ReadBuffer) prevSeqNum   =
        let rdr = buff.Reader
        if buff.Length < headersize then 
            fError TooFewBytes //should have received atleast the header bits
            prevSeqNum
        else
            let hdr = rdr.ReadUInt32()
            if hdr <> magic_no1 && hdr <> magic_no2 then 
                log (sprintf "magic not matched, got %d" hdr)
                fError Parse
                prevSeqNum
            else
                let droneState = enum<ArdroneState> (rdr.ReadInt32())
                let seqNum     = rdr.ReadUInt32()
                let visionFlag = rdr.ReadInt32()
                if seqNum <= prevSeqNum then 
                    fError MessageSeq
                else
                    if checkSum(buff) then
                        fPost (DroneState droneState)
                        processOptions rdr fPost fError
                    else
                        fError Checksum
                seqNum

 
(* reference c structs
// https://github.com/felixge/node-ar-drone
//credits to many github projects, especially ruslan balanukin and ar drone autonomy

type ushort = UInt16
type uint32 = UInt32
type uint8  = UInt8

[<StructLayout(LayoutKind.Sequential, Pack=1, CharSet = CharSet.Ansi)>]
type matrix33_t =
    struct
        val  m11:float32
        val  m12:float32
        val  m13:float32
        val  m21:float32
        val  m22:float32
        val  m23:float32
        val  m31:float32
        val  m32:float32
        val  m33:float32
    end

[<StructLayout(LayoutKind.Sequential, Pack=1, CharSet = CharSet.Ansi)>]
type vector31_t =
    struct
        val x:float32
        val y:float32
        val z:float32
    end

//basic telemetery data
[<StructLayout(LayoutKind.Sequential, Pack=1, CharSet = CharSet.Ansi)>]
type navdata_demo =
    struct
        val tag :ushort
        val size :ushort
        val ctrl_state :uint8 // flying state (landed, flying, hovering, etc.) defined in CTRL_STATES and FLYING_STATES enum.
        val vbat_flying_percentage :uint8 // battery voltage filtered (mV)
        val theta :float32 // UAV's pitch in milli-degrees
        val phi :float32 // UAV's roll in milli-degrees
        val psi :float32 // UAV's yaw in milli-degrees
        val altitude :int // UAV's altitude in centimeters
        val vx :float32 // UAV's estimated linear velocity
        val vy :float32 // UAV's estimated linear velocity
        val vz :float32 // UAV's estimated linear velocity
        val num_frames :uint8 // streamed frame index - Not used -> To integrate in video stage.
        val detection_camera_rot :matrix33_t // Deprecated! Don't use!
        val detection_camera_trans : vector31_t// Deprecated! Don't use!
        val detection_tag_index :uint8 // Deprecated! Don't use!
        val detection_camera_type :uint8 // Type of tag searched in detection
        val drone_camera_rot :matrix33_t// Deprecated! Don't use!
        val drone_camera_trans :vector31_t // Deprecated! Don't use!
    end
            
*)

(* navdata gps structure
// see https://github.com/AutonomyLab/ardrone_autonomy/blob/gps/msg/navdata_gps.msg
Header  header
uint16 tag
uint16 size
float64 latitude
float64 longitude
float64 elevation
float64 hdop
uint32   data_available
bool zero_validated 
bool wpt_validated 
float64 lat0 
float64 long0 
float64 lat_fused 
float64 long_fused 
uint32 gps_state 
float32 X_traj 
float32 X_ref 
float32 Y_traj 
float32 Y_ref 
float32 theta_p 
float32 phi_p 
float32 theta_i 
float32 phi_i 
float32 theta_d 
float32 phi_d 
float64 vdop
float64 pdop
float32 speed
uint32  lastFrameTimestamp
float32 degree
float32 degree_magnetic
float32 ehpe 
float32 ehve 
float32 c_n0  # Signal to noise ratio (average of the four best satellites)
uint32  nbsat # Number of acquired satellites
navdata_gps_channel[12] channels
bool is_gps_plugged
uint32 ephemerisStatus
float32 vx_traj 
float32 vy_traj 
uint32 firmwareStatus
*)   

