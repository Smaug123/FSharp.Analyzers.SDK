namespace WoofMyriad

open System
open System.Collections.Generic
open System.IO
open System.Runtime.InteropServices
open System.Threading
open FSharp.Analyzers.SDK
open FSharp.Analyzers.SDK.TASTCollecting
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text
open FSharp.Compiler.CodeAnalysis
open Ionide.ProjInfo
open Ionide.ProjInfo.Types
open Microsoft.Extensions.Logging.Abstractions
open Microsoft.Extensions.Logging

module Program =
    let logger = NullLogger.Instance
    let fileName = "/Users/patrick/Documents/GitHub/WoofWare.Myriad/ConsumePlugin/JsonRecord.fs"
    let projectFileName = "/Users/patrick/Documents/GitHub/WoofWare.Myriad/ConsumePlugin/ConsumePlugin.fsproj"
    let cache = Dictionary<string, ProjectOptions>()
    let membersToIgnore = set [ "CompareTo"; "GetHashCode"; "Equals" ]

    let exprTypesToIgnore =
        set [ "Microsoft.FSharp.Core.int"; "Microsoft.FSharp.Core.bool" ]

    type ContentsVisit<'ret> =
        abstract AtEntity : FSharpEntity -> 'ret list -> 'ret
        abstract AtMember : FSharpMemberOrFunctionOrValue -> FSharpMemberOrFunctionOrValue list list -> FSharpExpr -> 'ret
        abstract AtInitAction : FSharpExpr -> 'ret

    type RecordSpec =
        {
            Name : string
            Attributes : FSharpAttribute list
            Fields : (string * FSharpType) list
        }

    let rec getRecords (decl : FSharpImplementationFileDeclaration) : RecordSpec list =
        match decl with
        | FSharpImplementationFileDeclaration.InitAction a -> failwith "unexpected"
        | FSharpImplementationFileDeclaration.Entity (e, d) ->
            if e.IsFSharpRecord then
                {
                    Name = e.FullName
                    Attributes = e.Attributes |> Seq.toList
                    Fields =
                        e.FSharpFields
                        |> Seq.map (fun f ->
                            f.FullName, f.FieldType
                        )
                        |> Seq.toList
                }
                |> List.singleton
            else
                d
                |> List.collect getRecords
        | FSharpImplementationFileDeclaration.MemberOrFunctionOrValue (value, args, body) ->
            if
                not value.IsCompilerGenerated
                || not (Set.contains value.CompiledName membersToIgnore)
                || not body.Type.IsAbbreviation
                || not (Set.contains body.Type.BasicQualifiedName exprTypesToIgnore)
            then
                // Value to treat, but we don't care about values
                []
            else []

    [<EntryPoint>]
    let foo _ : int =
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
        | None -> failwith "no tree"
        | Some typedTree ->

        for r in typedTree.Declarations |> List.collect getRecords do
            printfn "%+A" r

        printfn "Done"
        0

