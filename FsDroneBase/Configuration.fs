namespace FsDrone
open System
open System.IO
open Extensions
open System.Text
open FsDrone

module Configuration =

    let rec private scanName (str:BinaryReader) (sb:StringBuilder) fPost =
        match str.ReadChar() with
        | '='                           -> scanValue str (sb.ToString()) (new StringBuilder()) fPost
        | '\n'                          -> scanName str (new StringBuilder()) fPost
        | c when Char.IsWhiteSpace(c)   -> scanName str sb fPost
        | c                             -> 
            sb.Append(c) |> ignore
            scanName str sb fPost
    //
    and private scanValueStart (str:BinaryReader) name (sb:StringBuilder) fPost = 
        match str.ReadChar() with
        | c when Char.IsWhiteSpace(c)   -> scanValueStart str name sb fPost
        | c                             -> 
            sb.Append(c) |> ignore
            scanValue str name sb fPost
    //
    and private scanValue str name sb fPost =
        match str.ReadChar() with
        | '\n'                          -> 
            fPost {Name=name; Value=sb.ToString()}
            scanName str (new StringBuilder()) fPost
        | '='                           -> scanName str (new StringBuilder()) fPost
        | c                             -> 
            sb.Append(c) |> ignore
            scanValue str name sb fPost
    
    let scanConfig str fPost = scanName str (new StringBuilder()) fPost