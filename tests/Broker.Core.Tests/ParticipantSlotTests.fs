module Broker.Core.Tests.ParticipantSlotTests

open Expecto
open Broker.Core
open Broker.Core.ParticipantSlot

let private proxy slotIdx team boundClient : ParticipantSlot =
    { slotIndex = slotIdx
      kind = ProxyAi
      team = team
      boundClient = boundClient }

let private human slotIdx team : ParticipantSlot =
    { slotIndex = slotIdx
      kind = Human
      team = team
      boundClient = None }

[<Tests>]
let participantSlotTests =
    testList "ParticipantSlot single-writer rule" [
        test "checkBindings_empty_is_Ok" {
            Expect.equal (checkBindings []) (Ok ()) "empty slot list violates nothing"
        }

        test "checkBindings_single_binding_is_Ok" {
            let slots = [ proxy 0 1 (Some (ScriptingClientId "alice"))
                          proxy 1 2 None ]
            Expect.equal (checkBindings slots) (Ok ()) "one bound proxy + one free is fine"
        }

        test "checkBindings_two_clients_to_same_slot_index_is_caught" {
            // FR-009 / Invariant 1: distinct slots may not share a binding;
            // a single slot can only carry one boundClient. Same client
            // appearing twice in the list is itself a SlotAlreadyBound
            // violation when the slot index repeats.
            let slots = [ proxy 0 1 (Some (ScriptingClientId "alice"))
                          proxy 0 1 (Some (ScriptingClientId "bob")) ]
            match checkBindings slots with
            | Error (SlotAlreadyBound (0, ScriptingClientId "alice")) -> ()
            | other -> failtestf "expected SlotAlreadyBound on slot 0; got %A" other
        }

        test "checkBindings_human_slot_with_boundClient_is_caught" {
            // boundClient is only meaningful when kind = ProxyAi.
            let slots = [ { human 0 1 with boundClient = Some (ScriptingClientId "alice") } ]
            match checkBindings slots with
            | Error (SlotNotProxyAi 0) -> ()
            | other -> failtestf "expected SlotNotProxyAi 0; got %A" other
        }

        test "tryBind_to_empty_proxy_slot_succeeds" {
            let slots = [ proxy 0 1 None ]
            match tryBind 0 (ScriptingClientId "alice") slots with
            | Ok updated ->
                let bound = updated |> List.head
                Expect.equal bound.boundClient (Some (ScriptingClientId "alice")) "slot 0 now bound to alice"
            | Error e -> failtestf "first bind should succeed; got %A" e
        }

        test "tryBind_re-bind_to_already_bound_slot_fails" {
            // The single-writer rule: a slot already bound to another
            // client must not silently change owner.
            let slots = [ proxy 0 1 (Some (ScriptingClientId "alice")) ]
            match tryBind 0 (ScriptingClientId "bob") slots with
            | Error (SlotAlreadyBound (0, ScriptingClientId "alice")) -> ()
            | other -> failtestf "expected SlotAlreadyBound; got %A" other
        }

        test "tryBind_human_slot_fails_with_SlotNotProxyAi" {
            let slots = [ human 0 1 ]
            match tryBind 0 (ScriptingClientId "alice") slots with
            | Error (SlotNotProxyAi 0) -> ()
            | other -> failtestf "expected SlotNotProxyAi; got %A" other
        }

        test "unbind_after_bind_clears_boundClient" {
            let slots = [ proxy 0 1 (Some (ScriptingClientId "alice")) ]
            let after = unbind 0 slots
            Expect.equal (after |> List.head).boundClient None "boundClient is None after unbind"
        }
    ]
