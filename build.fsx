#r @"packages/FAKE/tools/FakeLib.dll"
#r "System.Xml.Linq"

#load "paket-files/fsharp/FAKE/modules/Octokit/Octokit.fsx"

open Fake
open Fake.Git
open Fake.AppVeyor
open System
open System.IO
open System.Text
open System.Xml.Linq
open Octokit

let outputDir = currentDirectory @@ "Output"
let newsPostDir = currentDirectory @@ "docs" @@ "_posts"
let srcDir = currentDirectory @@ "src"
let isAppVeyor = buildServer = AppVeyor
let changeLogPath = currentDirectory @@ "src" @@ "Gallifrey.UI.Modern" @@ "ChangeLog.xml"
let branchName = match isAppVeyor with
                 | true-> AppVeyorEnvironment.RepoBranch
                 | _ -> getBranchName currentDirectory
                 
let isPR = match isAppVeyor with
           | true-> not(String.IsNullOrWhiteSpace AppVeyorEnvironment.PullRequestNumber)
           | _ -> false
                 
let isStable = branchName = "master"
let isBeta = (isStable || branchName = "release")
let isAlpha = (isBeta || branchName = "develop")

let githubApiKey = environVar "github_api_key"

printfn "Running On Branch: %s" branchName
printfn "PR Number: %s" AppVeyorEnvironment.PullRequestNumber
printfn "IsPR: %b" isPR
printfn "isStable: %b" isStable
printfn "isBeta: %b" isBeta
printfn "isAlpha: %b" isAlpha

let baseVersion = match isAppVeyor with
                  | true -> AppVeyorEnvironment.BuildVersion.Substring(0, AppVeyorEnvironment.BuildVersion.LastIndexOf("."))
                  | _ -> "0.0.0"

let buildNumber = match isAppVeyor with
                  | true -> AppVeyorEnvironment.BuildNumber
                  | _ -> "0"

let versionNumber = match isStable with
                    | true -> sprintf "%s.0" baseVersion                        
                    | _ -> sprintf "%s.%s" baseVersion buildNumber

Target "Clean" (fun _ -> 
    printfn "Clean & Ensure Output Directory"
    CleanDir outputDir
)

Target "VersionUpdate" (fun _ ->
    printfn "Update Assembly Info Versions"
    BulkReplaceAssemblyInfoVersions "src/" (fun f -> { f with AssemblyVersion = versionNumber; AssemblyInformationalVersion = versionNumber; AssemblyFileVersion = versionNumber })
    
    printfn "Update Change Log Versions"
    let changeLog = XDocument.Load(changeLogPath)

    let versionLogs = changeLog
                      |> fun changelog -> changelog.Descendants(XName.Get("Version", "https://releases.gallifreyapp.co.uk/ChangeLog"))
                      |> Seq.filter(fun x -> x.Attribute(XName.Get("Number")).Value = "0.0.0.0" || x.Attribute(XName.Get("Number")).Value = versionNumber)
    
    if versionLogs |> Seq.isEmpty then failwithf "No change log for version 0.0.0.0 or %s" versionNumber

    versionLogs
    |> Seq.head
    |> fun versionLog -> versionLog.Attribute(XName.Get("Number")).Value <- versionNumber    

    changeLog.Save(changeLogPath)

    printfn "Update Click-Once Settings Versions"
    Directory.GetFiles(srcDir, "*proj", SearchOption.AllDirectories)
    |> Seq.map(fun path -> path, XDocument.Load(path))
    |> Seq.filter(fun (_, (document:XDocument)) -> document.Descendants(XName.Get("OutputType", "http://schemas.microsoft.com/developer/msbuild/2003")) |> Seq.head |> fun x -> x.Value = "WinExe")
    |> Seq.iter(fun (path, (document:XDocument)) -> document.Descendants(XName.Get("MinimumRequiredVersion","http://schemas.microsoft.com/developer/msbuild/2003")) |> Seq.head |> fun x -> x.Value <- versionNumber
                                                    document.Descendants(XName.Get("ApplicationVersion","http://schemas.microsoft.com/developer/msbuild/2003")) |> Seq.head |> fun x -> x.Value <- versionNumber
                                                    document.Save(path)
               )
)

Target "Build" (fun _ ->
    let buildMode = getBuildParamOrDefault "buildMode" "Release"
    let setParams defaults = { defaults with Verbosity = Some(Quiet)
                                             Targets = ["Clean,Rebuild,publish"]
                                             Properties = [
                                                            "Optimize", "True"
                                                            "DebugSymbols", "True"
                                                            "Configuration", buildMode
                                                          ]
                             }

    build setParams (srcDir @@ "Gallifrey.sln")
)

Target "Package" (fun _ ->
    let moveArtifacts releaseType = 
        let source = srcDir @@ (sprintf "Gallifrey.UI.Modern.%s" releaseType) @@ "bin" @@ "Release" @@ "app.publish"
        let destination = outputDir @@ releaseType
        Directory.Move(source, destination)
        CreateZip destination (outputDir @@ (sprintf "%s.zip" releaseType)) "" DefaultZipLevel false (Directory.GetFiles(destination, "*.*", SearchOption.AllDirectories))

    if isAlpha then
        moveArtifacts "Alpha"

    if isBeta then
        moveArtifacts "Beta"
        File.Copy(outputDir @@ "Beta" @@ "setup.exe", outputDir @@ "beta-setup.exe")

    if isStable then
        moveArtifacts "Stable"
        File.Copy(outputDir @@ "Stable" @@ "setup.exe", outputDir @@ "stable-setup.exe")
)

Target "Publish-Artifacts" (fun _ ->
    PushArtifacts (Directory.GetFiles(outputDir, "*.zip", SearchOption.TopDirectoryOnly))
    PushArtifacts (Directory.GetFiles(outputDir, "*.exe", SearchOption.TopDirectoryOnly))
)

Target "Publish-Release" (fun _ ->    
    let releasesRepo = outputDir @@ "Releases"

    //Hide process tracing so the access token doesn't show
    
    cloneSingleBranch outputDir "https://github.com/BlythMeister/Gallifrey.Releases.git" "master" "Releases"
    
    enableProcessTracing <- false
    directRunGitCommandAndFail currentDirectory (sprintf "remote set-url origin https://%s:x-oauth-basic@github.com/BlythMeister/Gallifrey.git" githubApiKey)
    directRunGitCommandAndFail releasesRepo (sprintf "remote set-url origin https://%s:x-oauth-basic@github.com/BlythMeister/Gallifrey.Releases.git" githubApiKey)
    directRunGitCommandAndFail releasesRepo "config --global user.email \"publish@gallifreyapp.co.uk\""
    directRunGitCommandAndFail releasesRepo "config --global user.name \"Gallifrey Auto Publish\""
    enableProcessTracing <- true    

    let publishRelease (releaseType:string) =         
        let destinationRoot = releasesRepo @@ "download" @@ "modern" @@ (releaseType.ToLower())
        let destinationAppFilesRoot = destinationRoot @@ "Application Files"
        let destinationAppFiles = destinationAppFilesRoot @@ (sprintf "Gallifrey.UI.Modern.%s_%s" releaseType (versionNumber.Replace(".","_")))

        if not(Directory.Exists destinationAppFiles) then
            ensureDirectory destinationRoot       
            
            match isStable, releaseType with
            | true, "Alpha" 
            | true, "Beta" -> CleanDir destinationAppFilesRoot
                              StageAll releasesRepo
                              Commit.Commit releasesRepo (sprintf "Clean %s - Due to Stable Release" releaseType)
            | _, _ -> ensureDirectory destinationAppFilesRoot
        
            printfn "Publish Relase Type: %s" releaseType
            let sourceRoot = outputDir @@ releaseType

            let sourceAppFiles = sourceRoot @@ "Application Files" @@ (sprintf "Gallifrey.UI.Modern.%s_%s" releaseType (versionNumber.Replace(".","_")))
            File.Copy(sourceRoot @@ (sprintf "Gallifrey.UI.Modern.%s.application" releaseType), destinationRoot @@ (sprintf "Gallifrey.UI.Modern.%s.application" releaseType), true)
            Directory.Move(sourceAppFiles, destinationAppFiles)
            
            StageAll releasesRepo
            Commit.Commit releasesRepo (sprintf "Publish %s - %s" releaseType versionNumber)
            true
        else
            printfn "Already have %s version %s published" releaseType versionNumber
            false
                           
    let publishedAlpha = if isAlpha then publishRelease "Alpha" else false
    let publishedBeta = if isBeta then publishRelease "Beta" else false
    let publishedStable = if isStable then publishRelease "Stable" else false

    if publishedAlpha || publishedBeta || publishedStable then
        pushBranch releasesRepo "origin" "master"

        let releaseVersion = if isStable then baseVersion else versionNumber

        if isStable || isBeta then 
            let versionLog = XDocument.Load(changeLogPath)
                             |> fun changelog -> changelog.Descendants(XName.Get("Version", "https://releases.gallifreyapp.co.uk/ChangeLog"))
                             |> Seq.filter(fun x -> x.Attribute(XName.Get("Number")).Value.StartsWith(releaseVersion))
                             |> Seq.head

            let features = versionLog.Descendants(XName.Get("Feature", "https://releases.gallifreyapp.co.uk/ChangeLog")) |> Seq.map(fun x -> sprintf "* %s" x.Value) |> Seq.toList
            let bugs = versionLog.Descendants(XName.Get("Bug", "https://releases.gallifreyapp.co.uk/ChangeLog")) |> Seq.map(fun x -> sprintf "* %s" x.Value) |> Seq.toList
            let others = versionLog.Descendants(XName.Get("Other", "https://releases.gallifreyapp.co.uk/ChangeLog")) |> Seq.map(fun x -> sprintf "* %s" x.Value) |> Seq.toList

            let releaseNotes = [
                                if features |> List.isEmpty |> not then yield ["# Features"; ""]
                                yield features
                                if features |> List.isEmpty |> not then yield [""]
                                if bugs |> List.isEmpty |> not then yield ["# Bugs"; ""]
                                yield bugs
                                if bugs |> List.isEmpty |> not then yield [""]
                                if others |> List.isEmpty |> not then yield ["# Others"; ""]
                                yield others
                                if others |> List.isEmpty |> not then yield [""]
                                if not(isStable) then yield ["*NB: This is a cumulative change log for next release*"]
                               ]
                               |> List.concat
            
            if isStable then
                let currentDate = DateTime.UtcNow
                let releasesNewsFile = newsPostDir @@ sprintf "%s-release-%s.md" (currentDate.ToString("yyyy-MM-dd")) (releaseVersion.Replace(".", "-"))
                
                new StringBuilder()
                |> fun x -> x.AppendLine("---")
                |> fun x -> x.AppendLine(sprintf "title: Released Version %s" releaseVersion)
                |> fun x -> x.AppendLine(sprintf "date: %s +0000" (currentDate.ToString("yyyy-MM-dd HH:mm")))
                |> fun x -> x.AppendLine(sprintf "excerpt: We have now released version %s." releaseVersion)
                |> fun x -> x.AppendLine("show: true")
                |> fun x -> x.AppendLine("---")
                |> fun x -> x.AppendLine("")
                |> fun x -> x.AppendLine(sprintf "We have now released version %s." releaseVersion)
                |> fun x -> x.AppendLine("This version contains the following changes:")
                |> fun x -> x.AppendLine("")
                |> fun x -> x.AppendLine(String.Join("\n", releaseNotes))
                |> fun x -> x.AppendLine("")
                |> fun x -> x.AppendLine("To download the latest version of the app head to https://www.gallifreyapp.co.uk/downloads/stable")
                |> fun x -> File.WriteAllText(releasesNewsFile, x.ToString())

                checkoutBranch currentDirectory branchName
                StageFile currentDirectory releasesNewsFile |> ignore
                Commit.Commit currentDirectory (sprintf "Create news for stable release - %s" releaseVersion)    
                pushBranch currentDirectory "origin" branchName

            tag currentDirectory releaseVersion
            pushTag currentDirectory "origin" releaseVersion

            createClientWithToken githubApiKey
            |> createDraft "BlythMeister" "Gallifrey" releaseVersion (not(isStable)) releaseNotes
            |> fun x -> if isBeta then uploadFile (outputDir @@ "beta-setup.exe") x else x
            |> fun x -> if isStable then uploadFile (outputDir @@ "stable-setup.exe") x else x
            |> releaseDraft
            |> Async.RunSynchronously
)

Target "Default" DoNothing

"Clean"
    ==> "VersionUpdate"
    ==> "Build"
    ==> "Package"
    =?> ("Publish-Artifacts", isAppVeyor)
    =?> ("Publish-Release", isAppVeyor && not(isPR) && (isAlpha || isBeta || isStable))
    ==> "Default"

RunParameterTargetOrDefault "target" "Default"
