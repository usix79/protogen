[<RequireQualifiedAccess>]
module rec Protogen.FsharpTypesCmd

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
            let fileContent = gen module' locks typesCache
            let fileName =
                if Path.GetExtension(outputFileName) <> ".fs" then  outputFileName + ".g.fs" else outputFileName
            Console.WriteLine($"Writing fsharp types to {fileName}")
            File.WriteAllText(fileName, fileContent)
            Program.checkArgCore "Protogen.fs" args
            |> Option.iter (fun coreFileName ->
                let coreFileText =
                    if (File.Exists coreFileName) then File.ReadAllText(coreFileName) else ""

                let updatedCoreFileText = CoreFsharp.update coreFileText "FsharpTypes" commonsBody
                Console.WriteLine($"Writing common fsharp types and helpers to {coreFileName}")
                File.WriteAllText (coreFileName, updatedCoreFileText)
            )
            Ok () )
    | x -> Error $"expected arguments [-o|--output] outputFile, but {x}"

let Instance = {
    Name = "fsharp-types"
    Description = "generate fsharp types: fsharp-types [-o|--output] outputFile [--update-commons | --update-commons-in commonsFile]"
    Run = Handler
}

let gen (module':Module) (locks:LocksCollection) (typesCache:Types.TypesCache) =

    let txt = StringBuilder()

    let rec genItem ns = function
    | Enum info ->
        line txt $"type {firstName info.Name} ="
        line txt "    | Unknown = 0"
        for symbol in locks.Enum(info.Name).Values do
            line txt $"    | {symbol.Name} = {symbol.Num}"
    | Record info ->
        line txt $"type {firstName info.Name} = {{"
        for field in info.Fields do
            line txt $"    {field.Name} : {typeToString field.Type}"
        line txt $"}}"
        let keys = info.Fields |> List.filter (fun x -> x.IsKey)
        if not keys.IsEmpty then
            line txt $"with"
            recordKeyMembers typesCache txt info.Name keys
    | Union info ->
        line txt $"type {firstName info.Name} ="
        line txt $"    | Unknown"
        for case in info.Cases do
            let fieldsStr =
                case.Fields
                |> List.map (fun field -> $"{field.Name}:{typeToString field.Type}")
                |> String.concat "*"
                |> (fun str -> if str <> "" then " of " + str else str)
            line txt $"    | {firstName case.Name}{fieldsStr}"
        line txt $"with"
        unionKeyMembers locks typesCache txt info

    line txt $"module rec {dottedName module'.Name}"
    line txt "open Protogen.FsharpTypes"
    for item in module'.Items do
        genItem module'.Name item

    txt.ToString()

let rec typeToString (type':Type) =
    match type' with
    | Bool -> "bool"
    | String -> "string"
    | Int -> "int"
    | Long -> "int64"
    | Float -> "float32"
    | Double -> "float"
    | Decimal _ -> "decimal"
    | Bytes -> "byte array"
    | Timestamp -> "System.DateTimeOffset"
    | Duration -> "System.TimeSpan"
    | Guid -> "System.Guid"
    | Optional v -> typeToString v + " option"
    | Array v -> typeToString v + " array"
    | Map v -> $"Map<string,{typeToString v}>"
    | Complex ns -> dottedName ns

let keyParams (typesCache:TypesCache) (keyFields:FieldInfo list) =
    keyFields
    |> List.map (fun info ->
        let pName = info.Name |> firstCharToLower
        match info.Type with
        | Types.IsRecord typesCache _
        | Types.IsUnion typesCache _ -> $"{pName}Key: Key"
        | _ -> $"{pName}': {typeToString info.Type}" )
    |> String.concat ", "

let caseParams (fields:FieldInfo list) =
    if fields.IsEmpty then ""
    else
        fields
        |> List.map (fun info -> $"{info.Name}'")
        |> String.concat ", "
        |> (fun txt -> " (" + txt + ")")

let keyExpression (typesCache:TypesCache) (keyFields:FieldInfo list) =
    keyFields
    |> List.map (fun info ->
        let vName = $"{firstCharToLower info.Name}'"
        match info.Type with
        | String -> $"Key.Value ({vName})"
        | Int | Long | Decimal _ -> $"Key.Value ({vName}.ToString())"
        | Guid -> $"Key.Value ({vName}.ToString())"
        | Types.IsRecord typesCache _
        | Types.IsUnion typesCache _ -> $"Key.Inner {firstCharToLower info.Name}Key"
        | Types.IsEnum typesCache _ -> $"Key.Value ((int {vName}).ToString())"
        | wrong -> failwithf "type not supported as key %A" wrong )
    |> String.concat "; "

let makeKeyArgs (typesCache:TypesCache) (keyFields:FieldInfo list) prefix suffix =
    keyFields
    |> List.map (fun info ->
        match info.Type with
        | Types.IsRecord typesCache _
        | Types.IsUnion typesCache _ -> $"{prefix}{info.Name}{suffix}.Key"
        | _ -> $"{prefix}{info.Name}{suffix}")
    |> String.concat ", "

let recordKeyMembers (typesCache:TypesCache) txt typeName keyFields =
    line txt $"    static member MakeKey ({keyParams typesCache keyFields}) ="
    match keyFields with
    | [] -> failwith "empty key fields is not possible"
    | [_] -> line txt $"        {keyExpression typesCache keyFields}"
    | _ -> line txt $"        Key.Items [{keyExpression typesCache keyFields}]"
    let keyArgs = makeKeyArgs typesCache keyFields "x." ""
    line txt $"    member x.Key = {firstName typeName}.MakeKey ({keyArgs})"

let unionKeyMembers (locks:LocksCollection) (typesCache:TypesCache) txt (info:UnionInfo) =
    line txt "    static member MakeUnknownKey () = Key.Value \"0\""
    for recordInfo, caseLock in locks.Union(info.Name).Cases |> List.zip info.Cases do
        let keyFields = recordInfo.Fields |> List.filter (fun x -> x.IsKey)
        let keyExpression =
            if keyFields.IsEmpty then $"Key.Value \"{caseLock.Num}\""
            else $"Key.Items [Key.Value \"{caseLock.Num}\"; {keyExpression typesCache keyFields}]"
        line txt $"    static member Make{caseLock.Name}Key ({keyParams typesCache keyFields}) = {keyExpression}"

    line txt "    member x.Key ="
    line txt "        match x with"
    line txt $"        | Unknown -> {firstName info.Name}.MakeUnknownKey ()"
    for recordInfo, caseLock in locks.Union(info.Name).Cases |> List.zip info.Cases do
        let keyFields = recordInfo.Fields |> List.filter (fun x -> x.IsKey)
        let keyArgs = makeKeyArgs typesCache keyFields "" "'"
        line txt $"        | {caseLock.Name}{caseParams recordInfo.Fields} -> {firstName info.Name}.Make{caseLock.Name}Key ({keyArgs})"

let commonsBody = """
    type Key =
        | Value of string
        | Items of Key list
        | Inner of Key
"""
