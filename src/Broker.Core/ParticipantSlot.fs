namespace Broker.Core

[<Struct>]
type ScriptingClientId = ScriptingClientId of name:string

module ParticipantSlot =

    type Difficulty = int

    type ParticipantKind =
        | Human
        | BuiltInAi of difficulty:Difficulty
        | ProxyAi

    type ParticipantSlot =
        { slotIndex: int
          kind: ParticipantKind
          team: int
          boundClient: ScriptingClientId option }

    type SingleWriterError =
        | SlotAlreadyBound of slot:int * existingOwner:ScriptingClientId
        | SlotNotProxyAi of slot:int

    let private isProxy s =
        match s.kind with
        | ProxyAi -> true
        | _ -> false

    let checkBindings (slots: ParticipantSlot list) : Result<unit, SingleWriterError> =
        // Two checks: (a) any slot with a boundClient must be ProxyAi;
        // (b) no slotIndex appears twice carrying a binding.
        let bindingViolation =
            slots
            |> List.tryPick (fun s ->
                match s.boundClient, s.kind with
                | Some _, k when k <> ProxyAi -> Some (SlotNotProxyAi s.slotIndex)
                | _ -> None)
        match bindingViolation with
        | Some e -> Error e
        | None ->
            // Group by slotIndex; any group with >1 binding is a conflict.
            slots
            |> List.groupBy (fun s -> s.slotIndex)
            |> List.tryPick (fun (idx, bucket) ->
                let bound =
                    bucket
                    |> List.choose (fun s -> s.boundClient)
                match bound with
                | first :: _ :: _ -> Some (SlotAlreadyBound (idx, first))
                | _ -> None)
            |> function Some e -> Error e | None -> Ok ()

    let tryBind
        (slot: int)
        (client: ScriptingClientId)
        (slots: ParticipantSlot list)
        : Result<ParticipantSlot list, SingleWriterError> =
        let target = slots |> List.tryFind (fun s -> s.slotIndex = slot)
        match target with
        | None -> Error (SlotNotProxyAi slot)   // not present → treat as not-bindable
        | Some s when not (isProxy s) -> Error (SlotNotProxyAi slot)
        | Some s ->
            match s.boundClient with
            | Some existing when existing <> client ->
                Error (SlotAlreadyBound (slot, existing))
            | _ ->
                slots
                |> List.map (fun cur ->
                    if cur.slotIndex = slot
                    then { cur with boundClient = Some client }
                    else cur)
                |> Ok

    let unbind (slot: int) (slots: ParticipantSlot list) : ParticipantSlot list =
        slots
        |> List.map (fun s ->
            if s.slotIndex = slot then { s with boundClient = None } else s)
