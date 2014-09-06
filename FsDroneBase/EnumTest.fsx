#load "NativeNavData.fs"

open FsDrone

let n = ArdroneState.ARDRONE_ACQ_THREAD_ON ||| ArdroneState.ARDRONE_CAMERA_MASK

let inline (|Mask|_|) mask e  = if int e &&& int mask > 0 then Some mask else None

let test = match n with ArdroneState.ARDRONE_CAMERA_MASK -> true | _ -> false

let test2 = 
    match n with
    | Mask ArdroneState.ARDRONE_COM_LOST_MASK _ -> true
    | _ -> false


let test3 = 
    match n with
    | Mask ArdroneState.ARDRONE_CAMERA_MASK _ -> true
    | _ -> false