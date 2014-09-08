#load "NativeNavData.fs"

open FsDrone

let n = ArdroneState.ARDRONE_ACQ_THREAD_ON ||| ArdroneState.ARDRONE_CAMERA_MASK

//let inline (|Mask|_|) mask e  = if int e &&& int mask > 0 then Some mask else None
let inline (|Mask|) m e = int e &&& int m > 0

let test = match n with ArdroneState.ARDRONE_CAMERA_MASK -> true | _ -> false

let test2 = 
    match n with
    | Mask ArdroneState.ARDRONE_COM_LOST_MASK true -> true
    | _ -> false


let test3 = 
    match n with
    | Mask ArdroneState.ARDRONE_CAMERA_MASK true -> true
    | _ -> false

let c1 = ArdroneState.ARDRONE_ALTITUDE_MASK||| ArdroneState.ARDRONE_COMMAND_MASK||| ArdroneState.ARDRONE_CAMERA_MASK||| ArdroneState.ARDRONE_USB_MASK||| ArdroneState.ARDRONE_NAVDATA_DEMO_MASK||| ArdroneState.ARDRONE_NAVDATA_BOOTSTRAP||| ArdroneState.ARDRONE_PIC_VERSION_MASK||| ArdroneState.ARDRONE_ATCODEC_THREAD_ON||| ArdroneState.ARDRONE_NAVDATA_THREAD_ON||| ArdroneState.ARDRONE_VIDEO_THREAD_ON||| ArdroneState.ARDRONE_ACQ_THREAD_ON
let c2 = ArdroneState.ARDRONE_ALTITUDE_MASK||| ArdroneState.ARDRONE_CAMERA_MASK||| ArdroneState.ARDRONE_USB_MASK||| ArdroneState.ARDRONE_NAVDATA_DEMO_MASK||| ArdroneState.ARDRONE_NAVDATA_BOOTSTRAP||| ArdroneState.ARDRONE_PIC_VERSION_MASK||| ArdroneState.ARDRONE_ATCODEC_THREAD_ON||| ArdroneState.ARDRONE_NAVDATA_THREAD_ON||| ArdroneState.ARDRONE_VIDEO_THREAD_ON||| ArdroneState.ARDRONE_ACQ_THREAD_ON

match c1 with Mask ArdroneState.ARDRONE_COMMAND_MASK true -> true | _ -> false
match c2 with Mask ArdroneState.ARDRONE_COMMAND_MASK false -> true | _ -> false
