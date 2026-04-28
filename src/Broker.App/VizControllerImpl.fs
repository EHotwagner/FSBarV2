namespace Broker.App

open System
open Broker.Core
open Broker.Tui
open Broker.Viz

module VizControllerImpl =

    type LiveVizController(snapshots: IObservable<Snapshot.GameStateSnapshot>) =
        let mutable handle : VizHost.Handle option = None
        let mutable lastError : string option = None
        let lock' = obj ()

        member this.Close() =
            lock lock' (fun () ->
                match handle with
                | Some h ->
                    try
                        (h :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
                    with _ -> ()
                    handle <- None
                | None -> ())

        interface TickLoop.VizController with
            member this.Toggle() =
                lock lock' (fun () ->
                    match handle with
                    | Some _ -> this.Close()
                    | None ->
                        match VizHost.probe () with
                        | Error reason ->
                            lastError <- Some reason
                        | Ok () ->
                            try
                                let h =
                                    (VizHost.open_ snapshots)
                                        .GetAwaiter()
                                        .GetResult()
                                handle <- Some h
                                lastError <- None
                            with ex ->
                                lastError <- Some ex.Message)
            member this.Status() =
                match handle, lastError with
                | _, Some r ->
                    Some (sprintf "2D visualization unavailable: %s" r)
                | Some _, None -> None
                | None,   None -> None
