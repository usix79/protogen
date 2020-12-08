[<RequireQualifiedAccess>]
module Protogen.LockFile

open System.Text
open Types

let complexNameToString (ComplexName ns) = ns |> List.rev |> String.concat "."

let rec typeToString (type':Type) =
    match type' with
    | Bool -> "bool"
    | String -> "string"
    | Int -> "int"
    | Long -> "long"
    | Float -> "float"
    | Double -> "double"
    | Decimal scale -> $"decimal{scale}"
    | Bytes -> "bytes"
    | Timespamp -> "timestamp"
    | Duration -> "duration"
    | Guid -> "guid"
    | Optional v -> typeToString v + " option"
    | Array v -> typeToString v + " array"
    | Map v -> typeToString v + " map"
    | Complex ns -> complexNameToString ns

let line (txt:StringBuilder) l = txt.AppendLine(l) |> ignore

let gen (locks:LockItem list) =
    let txt = StringBuilder()

    let rec f = function
        | EnumLock lock ->
            line txt $"enum {complexNameToString lock.Name}"
            for value' in lock.Values do
                line txt $"    value {value'.Name} = {value'.Num}"
        | MessageLock lock ->
            line txt $"message {complexNameToString lock.Name}"
            for item in lock.LockItems do
                match item with
                | Field lock ->
                    line txt $"    field {lock.Name} {typeToString lock.Type} = {lock.Num}"
                | OneOf (name, unionName, locks) ->
                    line txt $"    oneof {name} {complexNameToString unionName}"
                    for case in locks do
                    line txt $"        case {case.CaseName} = {case.Num}"

    locks |> List.map f |> ignore
    txt.ToString()