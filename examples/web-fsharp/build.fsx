#r "paket:
nuget Fake.Core.Target prerelease
nuget Fake.DotNet.Cli
nuget Fake.IO.FileSystem"
#load "./.fake/build.fsx/intellisense.fsx"

open System.IO
open Fake.Core
open Fake.DotNet
open Fake.IO


Target.initEnvironment ()

let protogenDirectory = "../../"
let protogenDll = "../../src/bin/Debug/net5.0/Protogen.dll"
let pgenFile = "domain.pgen"
let protoClassesDir = "./ProtoClasses/"
let protoFile = protoClassesDir + "Domain.proto"
let fsharpTypesFile = "./server/Domain.fs"
let fsharpConvertersFile = "./server/DomainConverters.fs"

let runTool cmd args workingDir =
    let arguments = args |> String.split ' ' |> Arguments.OfArgs
    RawCommand (cmd, arguments)
    |> CreateProcess.fromCommand
    |> CreateProcess.withWorkingDirectory workingDir
    |> CreateProcess.ensureExitCode
    |> Proc.run
    |> ignore

let dotnetWithArgs (args:string list) cmd workingDir =
    let argsString = System.String.Join (" ", args)
    let result =
        DotNet.exec (DotNet.Options.withWorkingDirectory workingDir) cmd argsString
    if result.ExitCode <> 0 then failwithf "'dotnet %s %s' failed in %s" cmd argsString workingDir

let dotnet = dotnetWithArgs []

Target.create "Clean" (fun _ ->
    Shell.cleanDirs [protoClassesDir + "obj"; protoClassesDir + "bin"]
    Directory.GetFiles(protoClassesDir, "*.proto")
    |> File.deleteAll
)

Target.create "Gen" (fun _ ->
    dotnet "build" protogenDirectory
    dotnetWithArgs [pgenFile; "lock"] protogenDll __SOURCE_DIRECTORY__
    dotnetWithArgs [pgenFile; "proto"; "-o"; protoFile] protogenDll __SOURCE_DIRECTORY__
    dotnetWithArgs [pgenFile; "fsharp-types"; "-o"; fsharpTypesFile] protogenDll __SOURCE_DIRECTORY__
    dotnetWithArgs [pgenFile; "fsharp-converters"; "-o"; fsharpConvertersFile] protogenDll __SOURCE_DIRECTORY__
    dotnetWithArgs [pgenFile; "proto"; "-o"; protoFile] protogenDll __SOURCE_DIRECTORY__
)

Target.create "Build" (fun _ ->
    dotnet "build" __SOURCE_DIRECTORY__
    dotnet "fable build client" __SOURCE_DIRECTORY__
)

Target.create "Run" (fun _ ->
    [
        async {dotnet "watch --project ./server/ run" __SOURCE_DIRECTORY__}
        async {dotnet "fable watch client" __SOURCE_DIRECTORY__}
    ]
    |> Async.Parallel
    |> Async.RunSynchronously
    |> ignore
)

open Fake.Core.TargetOperators

"Clean"
    ==> "Gen"
    ==> "Build"

Target.runOrDefaultWithArguments "Build"