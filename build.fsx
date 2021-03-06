// include Fake lib
#I "packages/FAKE/tools"
#r "packages/FAKE/tools/FakeLib.dll"
open Fake
open System
open System.IO
open Fake.AssemblyInfoFile
open Fake.ReleaseNotesHelper

let buildDir nuget = if nuget then "./build_output" else "./build_output/tools"
let release = LoadReleaseNotes "RELEASE_NOTES.md"

let projects = 
    [ ("Elmish.XamarinForms/Elmish.XamarinForms.fsproj", "Elmish.XamarinForms", "F# Functional App Dev Framework", true)
      ("extensions/Maps/Elmish.XamarinForms.Maps.fsproj", "Elmish.XamarinForms.Maps", "Elmish.XamarinForms extension for Xamarin.Forms.Maps", true) 
      ("extensions/SkiaSharp/Elmish.XamarinForms.SkiaSharp.fsproj", "Elmish.XamarinForms.SkiaSharp", "Elmish.XamarinForms extension for SkiaSharp", true)
      ("extensions/OxyPlot/Elmish.XamarinForms.OxyPlot.fsproj", "Elmish.XamarinForms.OxyPlot", "Elmish.XamarinForms extension for OxyPlot", true) 
      ("fscd/fscd.fsproj", "fscd", "F# Compiler Daemon", false)
      ("Elmish.XamarinForms.LiveUpdate/Elmish.XamarinForms.LiveUpdate.fsproj", "Elmish.XamarinForms.LiveUpdate", "F# Functional App Dev Framework Live Update", true) ]

Target "Build" (fun _ ->

    // needed or else 'project.assets.json' not found'
    for (projFile, _project, _summary, _nuget) in projects do
        DotNetCli.Restore (fun p -> { p with Project = projFile })

    for (projFile, _project, _summary, nuget) in projects do
        !! projFile |> MSBuildRelease (buildDir nuget) "Restore" |> Log "LibraryRestore-Output: "

    for (projFile, _project, _summary, nuget) in projects do
        !! projFile |> MSBuildRelease (buildDir nuget) "Build" |> Log "LibraryBuild-Output: "
)

Target "BuildSamples" (fun _ ->

    // needed or else 'project.assets.json' not found'
    DotNetCli.Restore (fun p -> { p with Project = "Elmish.XamarinForms.sln" })

    // restore the apps debug
    !! "Elmish.XamarinForms.sln"
          |> MSBuildDebug null "Restore"
          |> Log "SamplesRestoreDebug-Output: "

    // build the apps debug
    !! "Elmish.XamarinForms.sln"
          |> MSBuildDebug null "Build"
          |> Log "SamplesBuildDebug-Output: "
)

Target "Clean" (fun _ ->
    CleanDir (buildDir true)
)

// Generate assembly info files with the right version & up-to-date information
Target "AssemblyInfo" (fun _ ->

    for (projFile, projName, summary, _nuget) in projects do
        let projFolder = Path.GetDirectoryName(projFile)
        let projDetails = 
          [ Attribute.Title projName
            Attribute.Product projName
            Attribute.Description summary
            Attribute.Version release.AssemblyVersion
            Attribute.FileVersion release.AssemblyVersion ]

        CreateFSharpAssemblyInfo (projFolder @@ "AssemblyInfo.fs") projDetails
)

// Build a NuGet package
Target "LibraryNuGet" (fun _ ->
    for (projFile, _projName, _summary, nuget) in projects do
        if nuget then 
            let projFolder = Path.GetDirectoryName(projFile)
            Paket.Pack(fun p -> 
                { p with
                    OutputPath = buildDir nuget + "/"
                    TemplateFile = projFolder + "/paket.template"
                    Version = release.NugetVersion
                    ReleaseNotes = toLines release.Notes})
)

// Build a NuGet package
Target "TemplatesNuGet" (fun _ ->

    NuGetHelper.NuGetPack (fun p -> 
        { p with
            WorkingDir = "templates"
            OutputPath = buildDir true + "/"
            Version = release.NugetVersion
            ReleaseNotes = toLines release.Notes}) @"templates/Elmish.XamarinForms.Templates.nuspec"
)
let exec exe args =
    let code = Shell.Exec(exe, args) 
    if code <> 0 then failwithf "%s %s failed, error code %d" exe args code

Target "TestTemplatesNuGet" (fun _ ->

    // Globally install the templates from the template nuget package we just built
    DotNetCli.RunCommand id ("new -i " + buildDir true + "/Elmish.XamarinForms.Templates." + release.NugetVersion + ".nupkg")

    let testAppName = "testapp2" + string (abs (hash System.DateTime.Now.Ticks) % 100)
    // Instantiate the template. TODO: additional parameters and variations
    CleanDir testAppName
    DotNetCli.RunCommand id (sprintf "new elmish-forms-app -n %s -lang F#" testAppName)

    let pkgs = Path.GetFullPath(buildDir true)
    // When restoring, using the build_output as a package source to pick up the package we just compiled
    DotNetCli.RunCommand id (sprintf "restore %s/%s/%s.fsproj  --source https://api.nuget.org/v3/index.json --source %s" testAppName testAppName testAppName pkgs)
    
    let slash = if isUnix then "\\" else ""
    for c in ["Debug"; "Release"] do 
        for p in ["Any CPU"; "iPhoneSimulator"] do 
            exec "msbuild" (sprintf "%s/%s.sln /p:Platform=\"%s\" /p:Configuration=%s /p:PackageSources=%s\"https://api.nuget.org/v3/index.json%s;%s%s\"" testAppName testAppName p c  slash slash pkgs slash)

    (* Manual steps without building nupkg
        .\build LibraryNuGet
        dotnet new -i  templates
        rmdir /s /q testapp2
        dotnet new elmish-forms-app -n testapp2 -lang F#
        dotnet restore testapp2/testapp2/testapp2.fsproj -s build_output/
        dotnet new -i  templates && rmdir /s /q testapp2 && dotnet new elmish-forms-app -n testapp2 -lang F# && dotnet restore testapp2/testapp2/testapp2.fsproj && msbuild testapp2/testapp2.Android/testapp2.Android.fsproj /t:RestorePackages && msbuild testapp2/testapp2.Android/testapp2.Android.fsproj
        dotnet new -i  templates && rmdir /s /q testapp2 && dotnet new elmish-forms-app -n testapp2 -lang F# && dotnet restore testapp2/testapp2/testapp2.fsproj && msbuild testapp2/testapp2.iOS/testapp2.iOS.fsproj /t:RestorePackages  && msbuild testapp2/testapp2.iOS/testapp2.iOS.fsproj
        dotnet new -i  templates && rmdir /s /q testapp2 && dotnet new elmish-forms-app -n testapp2 -lang F# --CreateMacProject && dotnet restore testapp2/testapp2/testapp2.fsproj && msbuild testapp2/testapp2.macOS/testapp2.macOS.fsproj /t:RestorePackages  && msbuild testapp2/testapp2.macOS/testapp2.macOS.fsproj
        *)

)


Target "NuGet" DoNothing
Target "Test" DoNothing

"Clean"
  ==> "AssemblyInfo"
  ==> "Build"
  ==> "LibraryNuGet" 
  ==> "TemplatesNuGet" 
  ==> "NuGet"

"Build"
  ==> "BuildSamples"
  ==> "Test"

"NuGet" 
  ==> "TestTemplatesNuGet"
  ==> "Test"


// start build
RunTargetOrDefault "Build"
