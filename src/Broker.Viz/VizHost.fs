namespace Broker.Viz

open System
open System.Threading.Tasks
open Broker.Core

module VizHost =

    let probe () : Result<unit, string> =
        if OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD() then
            let display = Environment.GetEnvironmentVariable "DISPLAY"
            let wayland = Environment.GetEnvironmentVariable "WAYLAND_DISPLAY"
            if String.IsNullOrEmpty display && String.IsNullOrEmpty wayland then
                Error "no graphical display detected (DISPLAY/WAYLAND_DISPLAY unset)"
            else
                Ok ()
        elif OperatingSystem.IsMacOS() || OperatingSystem.IsWindows() then
            Ok ()
        else
            Error "no graphical display (unsupported platform)"

    type Handle =
        inherit IAsyncDisposable
        abstract IsOpen : bool

    type private SubjectScene() =
        let observers = ResizeArray<IObserver<SkiaViewer.Scene>>()
        let lock' = obj ()
        member _.Push(scene: SkiaViewer.Scene) =
            let snap =
                lock lock' (fun () -> observers.ToArray())
            for o in snap do
                try o.OnNext(scene) with _ -> ()
        member _.Complete() =
            let snap =
                lock lock' (fun () -> observers.ToArray())
            for o in snap do
                try o.OnCompleted() with _ -> ()
        interface IObservable<SkiaViewer.Scene> with
            member _.Subscribe(observer: IObserver<SkiaViewer.Scene>) =
                lock lock' (fun () -> observers.Add observer)
                { new IDisposable with
                    member _.Dispose() =
                        lock lock' (fun () -> observers.Remove observer |> ignore) }

    type private OpenHandle
        (
            viewerHandle: SkiaViewer.ViewerHandle,
            snapshotSub: IDisposable,
            sceneSubject: SubjectScene
        ) =
        let mutable closed = 0
        interface Handle with
            member _.IsOpen = closed = 0
            member _.DisposeAsync() : ValueTask =
                if System.Threading.Interlocked.Exchange(&closed, 1) = 0 then
                    try snapshotSub.Dispose() with _ -> ()
                    try sceneSubject.Complete() with _ -> ()
                    try (viewerHandle :> IDisposable).Dispose() with _ -> ()
                ValueTask.CompletedTask

    let open_ (snapshots: IObservable<Snapshot.GameStateSnapshot>) : Task<Handle> =
        task {
            match probe () with
            | Error reason ->
                return raise (InvalidOperationException(sprintf "viz unavailable: %s" reason))
            | Ok () ->
                let sceneSubject = SubjectScene()
                let snapshotSub =
                    snapshots.Subscribe(
                        { new IObserver<Snapshot.GameStateSnapshot> with
                            member _.OnNext(snap) =
                                try
                                    let scene = SceneBuilder.build snap
                                    sceneSubject.Push(SceneBuilder.toSkiaScene scene)
                                with _ -> ()
                            member _.OnError(_) = ()
                            member _.OnCompleted() = sceneSubject.Complete() })
                let cfg : SkiaViewer.ViewerConfig =
                    { Title = "FSBar Broker — 2D viz"
                      Width = 1024
                      Height = 768
                      TargetFps = 30
                      ClearColor = SkiaSharp.SKColor(15uy, 18uy, 24uy, 255uy)
                      PreferredBackend = None }
                let viewerHandle, _inputs =
                    SkiaViewer.Viewer.run cfg (sceneSubject :> IObservable<SkiaViewer.Scene>)
                let handle = OpenHandle(viewerHandle, snapshotSub, sceneSubject) :> Handle
                return handle
        }
