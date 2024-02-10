namespace WoofMyriad

open System
open System.Collections.Generic
open System.IO
open System.Runtime.InteropServices
open System.Threading
open FSharp.Analyzers.SDK
open FSharp.Analyzers.SDK.TASTCollecting
open FSharp.Compiler.Text
open FSharp.Compiler.CodeAnalysis
open Ionide.ProjInfo
open Ionide.ProjInfo.Types
open Microsoft.Extensions.Logging.Abstractions
open Microsoft.Extensions.Logging
open NUnit.Framework

[<TestFixture>]
module TestProject1 =
    let logger = NullLogger.Instance
    let fileName = "/Users/patrick/Documents/GitHub/WoofWare.Myriad/ConsumePlugin/JsonRecord.fs"
    let projectFileName = "/Users/patrick/Documents/GitHub/WoofWare.Myriad/ConsumePlugin/ConsumePlugin.fsproj"
    let cache = Dictionary<string, ProjectOptions>()

    [<Test>]
    let foo () : unit =
        let toolsPath = Init.init (DirectoryInfo Environment.CurrentDirectory) None

        let loadProject properties projPath =
            let loader = WorkspaceLoader.Create (toolsPath, properties)
            let parsed = loader.LoadProjects [ projPath ] |> Seq.toList

            if parsed.IsEmpty then
                logger.LogError("Failed to load project '{0}'", projPath)
                failwithf "Failed to load project: %+A" projPath

            let fcsPo =
                {
                    ProjectId = None
                    ProjectFileName = projectFileName
                    SourceFiles = [| fileName |]
                    OtherOptions = [||]
                    ReferencedProjects =
                        [|
                        |]
                    IsIncompleteTypeCheckEnvironment = false
                    UseScriptResolutionRules = false
                    LoadTime = DateTime.Now
                    UnresolvedReferences = None
                    OriginalLoadReferences = []
                    Stamp = None
                }

            fcsPo

        let state = ResizeArray<range>()

        let walker =
            { new TypedTreeCollectorBase() with
                override _.WalkCall _ m _ _ _ range =
                    let name = String.Join(".", m.DeclaringEntity.Value.FullName, m.DisplayName)

                    if name = "Microsoft.FSharp.Core.FSharpOption`1.Value" then
                        state.Add range
            }

        let sourceText = File.ReadAllText fileName |> SourceText.ofString
        let properties =
            [
                "RuntimeIdentifier", RuntimeInformation.RuntimeIdentifier
                "Configuration", "Release"
            ]
        let fsharpOptions =
            loadProject properties projectFileName

        let fcs = Utils.createFCS None
        let checkProjectResults = fcs.ParseAndCheckProject(fsharpOptions) |> Async.RunSynchronously

        let ctx : CliContext =
            match Utils.typeCheckFile fcs logger fsharpOptions fileName (Utils.SourceOfSource.SourceText sourceText) with
            | Error e -> failwithf "Typecheck failed: %+A" e
            | Ok results -> Utils.createContext checkProjectResults fileName sourceText results

        match ctx.TypedTree with
        | None -> ()
        | Some typedTree -> walkTast walker typedTree

