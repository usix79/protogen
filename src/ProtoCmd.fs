[<RequireQualifiedAccess>]
module rec Protogen.ProtoCmd

open System
open System.Text
open System.IO
open Types
open Codegen

let Handler module' locks typesCache = function
    | "-o"::outputFileName::args
    | "--output"::outputFileName::args ->
        Program.checkLock module' locks typesCache
        |> Result.bind(fun _ ->
            let fileContent = gen module' locks
            let fileName =
                if Path.GetExtension(outputFileName) <> ".proto" then outputFileName + ".proto" else outputFileName
            Console.WriteLine($"Writing .proto definition to {fileName}")
            File.WriteAllText(fileName, fileContent)
            Ok () )
    | x -> Error $"expected arguments [-o|--output] outputFile, but {x}"


let Instance = {
    Name = "proto"
    Description = "generate protobuf description: proto [-o|--output] outputFile"
    Run = Handler
}

let gen (module':Module) (locks:LockItem list) =

    let enumLocksCache =
        locks |> List.choose(function EnumLock item -> Some(item.Name, item) | _ -> None) |> Map.ofList
    let messageLockCache =
        locks |> List.choose(function MessageLock item -> Some(item.Name, item) | _ -> None) |> Map.ofList

    let txt = StringBuilder()

    let rec genItem ns = function
    | Enum info ->
        let name = firstName info.Name
        line txt $"enum {name} {{"
        line txt $"    {name}Unknown = 0;"
        for symbol in enumLocksCache.[info.Name].Values do
            line txt $"    {name}{symbol.Name} = {symbol.Num};"
        line txt $"}}"
    | Record info -> genRecord (firstName info.Name) info
    | Union info ->
        for case in info.Cases do
            let needRecord =
                match messageLockCache.[case.Name].LockItems with
                | Types.EmptyCase -> false
                | Types.SingleParamCase _ -> false
                | Types.MultiParamCase -> true

            if needRecord then
                let recordName = (firstName info.Name) + "__" + (firstName case.Name)
                genRecord recordName case
    and genRecord recordName info =
        line txt $"message {recordName} {{"
        for item in messageLockCache.[info.Name].LockItems do
            match item with
            | Field info ->
                match info.Type with
                | Optional v ->
                    line txt $"    oneof {firstCharToUpper info.Name} {{{typeToString v} {firstCharToUpper info.Name}Value = {info.Num};}}"
                | _ ->
                    line txt $"    {typeToString info.Type} {firstCharToUpper info.Name} = {info.Num};"
            | OneOf (name,unionName,fields) ->
                line txt $"    oneof {firstCharToUpper name} {{"
                for fieldLock in fields do
                    let fieldMessage = messageLockCache.[Types.mergeName unionName fieldLock.CaseName]
                    let fieldTypeName =
                        match fieldMessage.LockItems with
                        | Types.EmptyCase -> "bool" // empty case would be bool
                        | Types.SingleParamCase fieldLock -> $"{typeToString fieldLock.Type}"
                        | Types.MultiParamCase -> $"{dottedName unionName}__{fieldLock.CaseName}"
                    line txt $"        {fieldTypeName} {firstCharToUpper name}{fieldLock.CaseName} = {fieldLock.Num};"
                line txt $"    }}"
        line txt $"}}"

    line txt """syntax = "proto3";"""
    line txt $"package {dottedName module'.Name};"
    line txt $"option csharp_namespace = \"ProtoClasses.{dottedName module'.Name}\";";
    for reference in references messageLockCache module' do
        if reference <> module'.Name then
            line txt $"import \"{dottedName reference}\";"

    module'.Items |> List.iter (genItem module'.Name)
    txt.ToString()

let rec typeToString (type':Type) =
    match type' with
    | Bool -> "bool"
    | String -> "string"
    | Int -> "int32"
    | Long -> "int64"
    | Float -> "float"
    | Double -> "double"
    | Decimal _ -> "int64"
    | Bytes -> "bytes"
    | Timestamp -> "google.protobuf.Timestamp"
    | Duration -> "google.protobuf.Duration"
    | Guid -> "bytes"
    | Optional v -> typeToString v
    | Array v -> "repeated " + (typeToString v)
    | Map v -> $"map<string,{typeToString v}>"
    | Complex ns -> dottedName ns

let references (messageLockCache : Map<ComplexName,MessageLock>) (module':Module) =
    let set = Collections.Generic.HashSet<ComplexName>()

    let rec typeReference = function
        | Timestamp -> Some <| ComplexName ["google/protobuf/timestamp.proto"]
        | Duration -> Some <| ComplexName ["google/protobuf/duration.proto"]
        | Complex ns -> Some <| Types.extractNamespace ns
        | Optional v
        | Map v
        | Array v -> typeReference v
        | _ -> None

    let rec f = function
        | Enum _ -> ()
        | Record info -> fRecord info
        | Union info ->
            info.Cases |> List.iter fRecord
    and fRecord info =
        messageLockCache.[info.Name].LockItems
        |> List.choose (function
            | Field x -> typeReference x.Type
            | OneOf (_,unionName,_) -> Types.extractNamespace unionName |> Some )
        |> List.iter (fun r -> set.Add(r) |> ignore)

    module'.Items |> List.iter f

    seq {for item in set do item}