#if INTERACTIVE
#else
namespace FsDrone
#endif
#nowarn "9"
open System
open System.Runtime.InteropServices
open System.IO
open Extensions
open NativeNavData

//flying substate (minor state)
type Fs = 
    | Ok                = 0us
    | LostAlt           = 1us
    | LostAlt_GoDown    = 2us
    | Alt_OutZone       = 3us
    | CombinedYaw       = 4us
    | Brake             = 5us
    | NoVision          = 6us

//controls state: major states
type Cs =
    | Default
    | Init
    | Landed
    | Flying of Fs
    | Hovering
    | Test
    | TakingOff
    | GoingToFix
    | Landing
    | Looping

type  Velocity      = {Vx:float32; Vy:float32; Vz:float32}
type  RPY           = {Roll:float32; Pitch:float32; Yaw:float32}
type  FlightSummary = {Velocity:Velocity; RPY:RPY; Altitude:float; BatteryLevel:float}
type  Magneto       = {Mx:int; My:int; Mz:int}

type  GPS  = 
    { 
        Fix            : bool
        Time           : DateTime
        Lat            : float
        Lon            : float
        Alt            : float 
        Heading        : float
        Speed          : float
        NumSats        : int
        Accuracy_Speed : float32
        Accuracy_Pos   : float
        Accuracy_Time  : float32
    }

type Errors = MessageSeq | Parse | UnhandledOption of NavdataOption

type Telemetry =
    | State         of Cs
    | FlightSummary of FlightSummary
    | Magneto       of Magneto
    | GPS           of GPS
    | Error         of Errors

module DataStructures = 
    //credits to ruslan balanukin
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
            

(* navdata gps structure
//Navdata gps packet
typedef double float64_t;               //TODO: Fix this nicely, but this is only used here
typedef float float32_t;               //TODO: Fix this nicely, but this is only used here
typedef struct _navdata_gps_t {
  uint16_t      tag;                    /*!< Navdata block ('option') identifier */
  uint16_t      size;                   /*!< set this to the size of this structure */
  float64_t     lat;                    /*!< Latitude */
  float64_t     lon;                    /*!< Longitude */
  float64_t     elevation;              /*!< Elevation */
  float64_t     hdop;                   /*!< hdop */
  int32_t       data_available;         /*!< When there is data available */
  uint8_t       unk_0[8];
  float64_t     lat0;                   /*!< Latitude ??? */
  float64_t     lon0;                   /*!< Longitude ??? */
  float64_t     lat_fuse;               /*!< Latitude fused */
  float64_t     lon_fuse;               /*!< Longitude fused */
  uint32_t      gps_state;              /*!< State of the GPS, still need to figure out */
  uint8_t       unk_1[40];
  float64_t     vdop;                   /*!< vdop */
  float64_t     pdop;                   /*!< pdop */
  float32_t     speed;                  /*!< speed */
  uint32_t      last_frame_timestamp;   /*!< Timestamp from the last frame */
  float32_t     degree;                 /*!< Degree */
  float32_t     degree_mag;             /*!< Degree of the magnetic */
  uint8_t       unk_2[16];
  struct{
    uint8_t     sat;
    uint8_t     cn0;
  }channels[12];
  int32_t       gps_plugged;            /*!< When the gps is plugged */
  uint8_t       unk_3[108];
  float64_t     gps_time;               /*!< The gps time of week */
  uint16_t      week;                   /*!< The gps week */
  uint8_t       gps_fix;                /*!< The gps fix */
  uint8_t       num_sattelites;         /*!< Number of sattelites */
  uint8_t       unk_4[24];
  float64_t     ned_vel_c0;             /*!< NED velocity */
  float64_t     ned_vel_c1;             /*!< NED velocity */
  float64_t     ned_vel_c2;             /*!< NED velocity */
  float64_t     pos_accur_c0;           /*!< Position accuracy */
  float64_t     pos_accur_c1;           /*!< Position accuracy */
  float64_t     pos_accur_c2;           /*!< Position accuracy */
  float32_t     speed_acur;             /*!< Speed accuracy */
  float32_t     time_acur;              /*!< Time accuracy */
  uint8_t       unk_5[72];
  float32_t     temprature;
  float32_t     pressure;
} __attribute__ ((packed)) navdata_gps_t;
*)   

module Parsing =
    open DataStructures
    open IOUtils
    let magic_no1 = 0x55667788u
    let magic_no2 = 0x55667789u

    [<Literal>]
    let CSFlyState = 3us
    let csMap = 
        [
            0us, Default
            1us, Init
            2us, Landed
            3us, Flying Fs.Ok
            4us, Hovering
            5us, Test
            6us, TakingOff
            7us, GoingToFix
            8us, Landing
            9us, Looping
        ] |> Map.ofList
 

    let inline calcChecksum (buf:byte[]) position = 
        let mutable s = 0u
        for i in 0 .. position do  s <- s + Operators.uint32 buf.[i]
        s

    let inline demoOption (rdr:BinaryReader) length =
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
        rdr.BaseStream.Skip (rdr.BaseStream.Position - int64 length) //skip rest of the navdata option
        [
            (State 
                        (match controlState with 
                        | CSFlyState -> Flying (LanguagePrimitives.EnumOfValue flyState) 
                        | x -> csMap.[x]))
            (FlightSummary 
                {
                    Velocity        = {Vx=vx; Vy=vy; Vz=vz}
                    RPY             = {Roll=roll; Pitch=pitch; Yaw=yaw}
                    BatteryLevel    = float batteryLevel
                    Altitude        = float alt
                })
        ]

    let inline mapOption (rdr:BinaryReader) length = function
        | NavdataOption.Demo -> demoOption rdr length
        | NavdataOption.GPS  -> processGPSOption rdr length
        | o -> [(Error (UnhandledOption o))]

    let inline processOptions (rdr:BinaryReader) buf fPost =
        let rec loop len = function
            | NavdataOption.Checksum -> 
                let expectedSum = calcChecksum buf (int rdr.BaseStream.Position - 4)
                let cks = rdr.ReadUInt32()
                if cks <> expectedSum then 
                    failwith "invalid checksum"
            | opt -> 
                mapOption rdr optionSz fPost opt
                let optionTag = rdr.ReadUInt16()
                let optionSz  = rdr.ReadUInt16()

        let mutable currentNavOption = NavdataOption.Uknown
        while currentNavOption <> NavdataOption.Checksum do
            currentNavOption <- LanguagePrimitives.EnumOfValue optionTag

    let inline processTelemeteryData (buff:ReadBuffer) prevSeqNum fPost =
        let rdr = buff.Reader
        let hdr = rdr.ReadUInt32()
        if hdr <> magic_no1 then 
            log "magic not matched"
            fPost (Error Parse)
            prevSeqNum
        else
            let droneState = rdr.ReadUInt32()
            let seqNum     = rdr.ReadUInt32()
            let visionFlag = rdr.ReadInt32()
            if seqNum <= prevSeqNum then 
                fPost (Error MessageSeq)
                seqNum
            else
                processOptions rdr buff.ByteArray fPost
                seqNum

 
